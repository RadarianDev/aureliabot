using System;
using System.Collections.Generic;
using System.Linq;

public enum CampaignStanceOutcome { Ok, StanceRequired, SilentRefused }

public sealed record CampaignStanceDecision(CampaignStanceOutcome Outcome, Stance? Stance, bool OffProgramme, Stance? ProgrammeStance);

public sealed record ActionSlot(bool Allowed, PendingAction? Replaces);

public sealed record AttackTarget(Stance? Stance, string? Refusal);

public sealed record RegionalDelta(ulong FactionId, string ProvinceId, double Change);

internal sealed record ActionStartState(
    long ResolutionNumber,
    bool Crowding,
    Func<string, bool> IssueOpen,
    Func<string, string, double> ProvincialOpinion,
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
    IReadOnlyList<RegionalDelta> PopularityDeltas,
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

    public static bool SameSlot(PendingAction action, ActionKind kind, string issueId, string? provinceId) =>
        action.Kind == kind && (kind == ActionKind.Poll ? action.IssueId == issueId : action.ProvinceId == provinceId);

    public static ActionSlot SlotFor(
        IReadOnlyList<PendingAction> pending, ActionKind kind, string issueId, string? provinceId, int maxPending, bool campaignsStack = false)
    {
        var stacks = campaignsStack && kind == ActionKind.Campaign;
        var same = stacks ? null : pending.FirstOrDefault(a => SameSlot(a, kind, issueId, provinceId));
        if (same is not null) return new ActionSlot(true, same);
        return new ActionSlot(pending.Count < maxPending, null);
    }

    public static int SameSideCampaigners(IEnumerable<PendingAction> campaigns, string issueId, string provinceId, Stance stance) =>
        campaigns
            .Where(c => c.IssueId == issueId && c.ProvinceId == provinceId && c.Stance is { } s && s.Sign() == stance.Sign())
            .Select(c => c.FactionId)
            .Distinct()
            .Count();

    public static int OwnRepeats(IEnumerable<PendingAction> campaigns, PendingAction action) =>
        campaigns.Count(c => c.FactionId == action.FactionId && c.IssueId == action.IssueId && c.ProvinceId == action.ProvinceId);

    public static int Crowd(IReadOnlyList<PendingAction> campaigns, PendingAction action, Stance stance) =>
        SameSideCampaigners(campaigns, action.IssueId, action.ProvinceId!, stance) * Math.Max(1, OwnRepeats(campaigns, action));

    public static ActionPlan Plan(IEnumerable<PendingAction> actions, ActionStartState start, ActionScoring scoring)
    {
        var ordered = actions.OrderBy(a => a.Id).ToList();
        var campaigns = ordered
            .Where(a => a.Kind == ActionKind.Campaign && a.ProvinceId is not null
                        && a.Stance is { } s && s != Stance.Silent && start.IssueOpen(a.IssueId))
            .ToList();

        var outcomes = new List<ActionOutcome>();
        foreach (var action in ordered)
            outcomes.Add(action.Kind switch
            {
                ActionKind.Campaign => PlanCampaign(action, campaigns, start, scoring),
                ActionKind.Attack => PlanAttack(action, start, scoring),
                _ => PlanPoll(action, start, scoring),
            });

        var deltas = new Dictionary<(ulong FactionId, string ProvinceId), double>();
        foreach (var outcome in outcomes.Where(o => !o.Fizzled && o.Action.ProvinceId is not null))
        {
            var province = outcome.Action.ProvinceId!;
            Add(deltas, outcome.Action.FactionId, province, outcome.ActorChange);
            if (outcome.Action.TargetFactionId is { } target) Add(deltas, target, province, outcome.TargetChange);
        }

        var raises = campaigns
            .Select(c => c.IssueId)
            .Distinct()
            .Where(issueId => start.SalienceRaisedResolution(issueId) != start.ResolutionNumber)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        var regional = deltas
            .Select(kv => new RegionalDelta(kv.Key.FactionId, kv.Key.ProvinceId, kv.Value))
            .OrderBy(d => d.FactionId)
            .ThenBy(d => d.ProvinceId, StringComparer.Ordinal)
            .ToList();
        return new ActionPlan(outcomes, regional, campaigns, raises);
    }

    private static ActionOutcome PlanCampaign(PendingAction action, IReadOnlyList<PendingAction> campaigns, ActionStartState start, ActionScoring scoring)
    {
        if (!start.IssueOpen(action.IssueId))
            return Fizzle(action, Texts.Get("data.action.fizzledClosed"), Texts.Get("data.action.campaignClosed"));
        if (action.Stance is not { } stance || stance == Stance.Silent)
            return Fizzle(action, Texts.Get("data.action.fizzledNoStance"), Texts.Get("data.action.campaignNoStance"));
        if (action.ProvinceId is not { } province)
            return Fizzle(action, Texts.Get("data.action.fizzledNoProvince"), Texts.Get("data.action.campaignNoProvince"));

        var m = start.Crowding ? Crowd(campaigns, action, stance) : 1;
        var gain = scoring.CampaignGain(stance, start.ProvincialOpinion(action.IssueId, province), start.Salience(action.IssueId), m);
        return new ActionOutcome(action, false, gain, 0, null,
            Texts.Get("data.action.campaigned", ("stance", stance.DisplayName()), ("change", Display.Signed(gain))), null);
    }

    private static ActionOutcome PlanAttack(PendingAction action, ActionStartState start, ActionScoring scoring)
    {
        if (!start.IssueOpen(action.IssueId))
            return Fizzle(action, Texts.Get("data.action.fizzledClosed"), Texts.Get("data.action.attackClosed"));
        if (action.ProvinceId is not { } province)
            return Fizzle(action, Texts.Get("data.action.fizzledNoProvince"), Texts.Get("data.action.attackNoProvince"));
        var target = start.AttackTargetOf(action);
        if (target.Refusal is not null || target.Stance is not { } stance || stance == Stance.Silent)
            return Fizzle(action, Texts.Get("data.action.fizzledBecause", ("reason", target.Refusal ?? Texts.Get("data.action.targetNoStanceShort"))),
                Texts.Get("data.action.attackFizzled", ("reason", target.Refusal ?? Texts.Get("data.attack.targetNoStance"))));

        var result = scoring.Attack(stance, start.ProvincialOpinion(action.IssueId, province), start.Salience(action.IssueId));
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

    private static void Add(Dictionary<(ulong, string), double> deltas, ulong factionId, string provinceId, double change)
    {
        if (change == 0) return;
        deltas[(factionId, provinceId)] = deltas.GetValueOrDefault((factionId, provinceId)) + change;
    }
}
