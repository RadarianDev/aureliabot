using System;
using System.Collections.Generic;
using System.Linq;

public enum CampaignStanceOutcome { Ok, StanceRequired, SilentRefused }

public sealed record CampaignStanceDecision(CampaignStanceOutcome Outcome, Stance? Stance, bool OffProgramme, Stance? ProgrammeStance);

public sealed record ActionSlot(bool Allowed, PendingAction? Replaces);

public sealed record AttackTarget(Stance? Stance, string? Refusal);

internal sealed record ActionStartState(
    long ResolutionNumber,
    bool Crowding,
    Func<string, bool> IssueOpen,
    Func<string, double> NationalOpinion,
    Func<string, double> Salience,
    Func<string, long?> SalienceRaisedResolution,
    Func<PendingAction, AttackTarget> AttackTargetOf);

internal sealed record ActionScoring(
    Func<Stance, double, double, int, double> CampaignGain,
    Func<Stance, double, double, AttackResult> Attack,
    Func<PendingAction, string> Poll);

internal sealed record ActionOutcome(
    PendingAction Action,
    bool Fizzled,
    double ActorChange,
    double TargetChange,
    Stance? TargetStance,
    string Result,
    string? PrivateMessage,
    bool Succeeded = true);

internal sealed record ActionPlan(
    IReadOnlyList<ActionOutcome> Outcomes,
    IReadOnlyDictionary<ulong, double> PopularityDeltas,
    IReadOnlyList<PendingAction> StanceLogs,
    IReadOnlyList<string> SalienceRaises);

internal sealed record ActionPreview(ActionPlan Plan, OpinionSnapshot Snapshot);

internal static class ActionPlanner
{
    public static CampaignStanceDecision DecideCampaignStance(Stance? programmeStance, Stance? given)
    {
        var programme = programmeStance is { } p && p != Stance.Silent ? p : (Stance?)null;
        if (given == Stance.Silent) return new CampaignStanceDecision(CampaignStanceOutcome.SilentRefused, null, false, programme);
        if (given is null)
            return programme is null
                ? new CampaignStanceDecision(CampaignStanceOutcome.StanceRequired, null, false, null)
                : new CampaignStanceDecision(CampaignStanceOutcome.Ok, programme, false, programme);
        return new CampaignStanceDecision(CampaignStanceOutcome.Ok, given, programme is not null && programme != given, programme);
    }

    public static ActionSlot SlotFor(IReadOnlyList<PendingAction> pending, ActionKind kind, int maxPending)
    {
        var sameKind = pending.FirstOrDefault(a => a.Kind == kind);
        if (sameKind is not null) return new ActionSlot(true, sameKind);
        return new ActionSlot(pending.Count < maxPending, null);
    }

    public static int SameSideCampaigners(IEnumerable<PendingAction> campaigns, string issueId, Stance stance) =>
        campaigns.Count(c => c.IssueId == issueId && c.Stance is { } s && s.Sign() == stance.Sign());

    public static ActionPlan Plan(IEnumerable<PendingAction> actions, ActionStartState start, ActionScoring scoring)
    {
        var ordered = actions.OrderBy(a => a.Id).ToList();
        var campaigns = ordered
            .Where(a => a.Kind == ActionKind.Campaign && a.Stance is { } s && s != Stance.Silent && start.IssueOpen(a.IssueId))
            .ToList();

        var outcomes = new List<ActionOutcome>();
        foreach (var action in ordered)
            outcomes.Add(action.Kind switch
            {
                ActionKind.Campaign => PlanCampaign(action, campaigns, start, scoring),
                ActionKind.Attack => PlanAttack(action, start, scoring),
                _ => PlanPoll(action, start, scoring),
            });

        var deltas = new Dictionary<ulong, double>();
        foreach (var outcome in outcomes.Where(o => !o.Fizzled))
        {
            Add(deltas, outcome.Action.FactionId, outcome.ActorChange);
            if (outcome.Action.TargetFactionId is { } target) Add(deltas, target, outcome.TargetChange);
        }

        var raises = campaigns
            .Select(c => c.IssueId)
            .Distinct()
            .Where(issueId => start.SalienceRaisedResolution(issueId) != start.ResolutionNumber)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        return new ActionPlan(outcomes, deltas, campaigns, raises);
    }

    private static ActionOutcome PlanCampaign(PendingAction action, IReadOnlyList<PendingAction> campaigns, ActionStartState start, ActionScoring scoring)
    {
        if (!start.IssueOpen(action.IssueId))
            return Fizzle(action, Texts.Get("data.action.fizzledClosed"), Texts.Get("data.action.campaignClosed"));
        if (action.Stance is not { } stance || stance == Stance.Silent)
            return Fizzle(action, Texts.Get("data.action.fizzledNoStance"), Texts.Get("data.action.campaignNoStance"));

        var m = start.Crowding ? SameSideCampaigners(campaigns, action.IssueId, stance) : 1;
        var gain = scoring.CampaignGain(stance, start.NationalOpinion(action.IssueId), start.Salience(action.IssueId), m);
        return new ActionOutcome(action, false, gain, 0, null,
            Texts.Get("data.action.campaigned", ("stance", stance.DisplayName()), ("change", Display.Signed(gain))), null);
    }

    private static ActionOutcome PlanAttack(PendingAction action, ActionStartState start, ActionScoring scoring)
    {
        if (!start.IssueOpen(action.IssueId))
            return Fizzle(action, Texts.Get("data.action.fizzledClosed"), Texts.Get("data.action.attackClosed"));
        var target = start.AttackTargetOf(action);
        if (target.Refusal is not null || target.Stance is not { } stance || stance == Stance.Silent)
            return Fizzle(action, Texts.Get("data.action.fizzledBecause", ("reason", target.Refusal ?? Texts.Get("data.action.targetNoStanceShort"))),
                Texts.Get("data.action.attackFizzled", ("reason", target.Refusal ?? Texts.Get("data.attack.targetNoStance"))));

        var result = scoring.Attack(stance, start.NationalOpinion(action.IssueId), start.Salience(action.IssueId));
        var text = result.Succeeded
            ? Texts.Get("data.action.attackSucceeded", ("stance", stance.DisplayName()), ("change", Display.Signed(result.TargetChange)))
            : Texts.Get("data.action.attackBackfired", ("stance", stance.DisplayName()), ("change", Display.Signed(result.AttackerChange)));
        return new ActionOutcome(action, false, result.AttackerChange, result.TargetChange, stance, text, null, result.Succeeded);
    }

    private static ActionOutcome PlanPoll(PendingAction action, ActionStartState start, ActionScoring scoring)
    {
        if (!start.IssueOpen(action.IssueId))
            return Fizzle(action, Texts.Get("data.action.fizzledClosed"), Texts.Get("data.action.pollClosed"));
        var line = scoring.Poll(action);
        return new ActionOutcome(action, false, 0, 0, null, Texts.Get("data.action.pollDelivered"), line);
    }

    private static ActionOutcome Fizzle(PendingAction action, string result, string message) =>
        new(action, true, 0, 0, null, result, message);

    private static void Add(Dictionary<ulong, double> deltas, ulong factionId, double change)
    {
        if (change == 0) return;
        deltas[factionId] = deltas.GetValueOrDefault(factionId) + change;
    }
}
