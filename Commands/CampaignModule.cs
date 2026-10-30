using Discord;
using Discord.Interactions;

public enum StanceChoice
{
    [ChoiceDisplay("Strongly For")] StronglyFor = 2,
    [ChoiceDisplay("For")] For = 1,
    [ChoiceDisplay("Against")] Against = -1,
    [ChoiceDisplay("Strongly Against")] StronglyAgainst = -2,
}

public class CampaignModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly DatabaseService _data;
    private readonly ConstantsStore _constants;
    private readonly Confirmations _confirmations;

    public CampaignModule(DatabaseService data, ConstantsStore constants, Confirmations confirmations)
    {
        _data = data;
        _constants = constants;
        _confirmations = confirmations;
    }

    public static string Describe(DatabaseService data, GameConfig config, PendingAction action)
    {
        var issue = $"*{data.GetIssue(action.IssueId)?.Title ?? action.IssueId}*";
        return action.Kind switch
        {
            ActionKind.Campaign =>
                $"a campaign {action.Stance?.DisplayName() ?? "with no stance"} on {issue}{(action.OffProgramme ? " (off-programme)" : "")}",
            ActionKind.Attack =>
                $"an attack on {(action.TargetFactionId is { } target ? Display.Faction(data, target) : "nobody")}'s stance on {issue}",
            _ => $"a poll on {issue} {(action.ProvinceId is null ? "nationally" : $"in {Display.Province(config, action.ProvinceId)}")}",
        };
    }

    public static string Resolves(GameState state) =>
        $"It resolves at the next resolution, {Display.NextResolution(state)}, simultaneously with everyone else's actions; " +
        "results are revealed then.";

    [SlashCommand("campaign", "Campaign on an issue nationally")]
    [RequireFaction]
    public async Task CampaignAsync(
        [Summary("issue", "The open issue to campaign on"), Autocomplete(typeof(CampaignIssueAutocomplete))] string issue,
        [Summary("stance", "Your stance; defaults to your party's programme")] StanceChoice? stance = null)
    {
        if (OpenIssueAutocomplete.Find(_data, issue) is not { } target)
        {
            await RespondAsync(NoOpenIssue(issue), ephemeral: true, allowedMentions: AllowedMentions.None);
            return;
        }

        var faction = _data.GetFaction(Context.User.Id)!;
        var programme = _data.GetProgrammeStance(faction.UserId, target.Id);
        var decision = ActionPlanner.DecideCampaignStance(programme, stance is { } s ? (Stance)s : null);
        if (decision.Outcome != CampaignStanceOutcome.Ok)
        {
            await RespondAsync(
                faction.IsIndependent
                    ? $"As an independent you must give a stance to campaign on *{target.Title}*."
                    : $"Your party's programme takes no stance on *{target.Title}*, so you must give one. It will not count as off-programme.",
                ephemeral: true);
            return;
        }

        if (SlotRefusal(faction.UserId, ActionKind.Campaign) is { } refusal)
        {
            await RespondAsync(refusal, ephemeral: true, allowedMentions: AllowedMentions.None);
            return;
        }

        var chosen = decision.Stance!.Value;
        if (!decision.OffProgramme)
        {
            await RespondAsync(Submit(faction.UserId, ActionKind.Campaign, target.Id, stance: chosen), ephemeral: true, allowedMentions: AllowedMentions.None);
            return;
        }

        var userId = faction.UserId;
        var issueId = target.Id;
        await RespondAsync(
            $"Your party's programme is **{decision.ProgrammeStance!.Value.DisplayName()}** on *{target.Title}*. Campaigning " +
            $"**{chosen.DisplayName()}** goes **off-programme**, and will be logged publicly as off-programme in #campaign-log " +
            $"at the next resolution, {Display.NextResolution(_data.GetGameState())}. Confirm?",
            components: _confirmations.Create(userId,
                () => Task.FromResult(Submit(userId, ActionKind.Campaign, issueId, stance: chosen)),
                "Campaign off-programme"),
            ephemeral: true,
            allowedMentions: AllowedMentions.None);
    }

    [SlashCommand("attack", "Attack a rival's stance on an issue nationally")]
    [RequireFaction]
    public async Task AttackAsync(
        [Summary("faction", "The rival to attack"), Autocomplete(typeof(RivalAutocomplete))] string faction,
        [Summary("issue", "The issue where the rival holds a stance"), Autocomplete(typeof(RivalStanceIssueAutocomplete))] string issue)
    {
        if (FactionAutocomplete.Find(_data, faction) is not { } rival)
        {
            await RespondAsync("There is no such faction. Pick a rival from the list.", ephemeral: true);
            return;
        }
        if (OpenIssueAutocomplete.Find(_data, issue) is not { } target)
        {
            await RespondAsync(NoOpenIssue(issue), ephemeral: true, allowedMentions: AllowedMentions.None);
            return;
        }

        var check = _data.CheckAttack(Context.User.Id, rival.UserId, target.Id);
        if (check.Refusal is not null)
        {
            await RespondAsync($"You cannot attack {Display.Faction(rival)} on *{target.Title}*: {check.Refusal}.",
                ephemeral: true, allowedMentions: AllowedMentions.None);
            return;
        }

        await RespondAsync(Submit(Context.User.Id, ActionKind.Attack, target.Id, targetFactionId: rival.UserId),
            ephemeral: true, allowedMentions: AllowedMentions.None);
    }

    [SlashCommand("poll", "Poll an issue, nationally or in one province")]
    [RequireFaction]
    public async Task PollAsync(
        [Summary("issue", "The open issue to poll"), Autocomplete(typeof(OpenIssueAutocomplete))] string issue,
        [Summary("province", "Poll one province instead of the nation"), Autocomplete(typeof(ProvinceAutocomplete))] string? province = null)
    {
        if (OpenIssueAutocomplete.Find(_data, issue) is not { } target)
        {
            await RespondAsync(NoOpenIssue(issue), ephemeral: true, allowedMentions: AllowedMentions.None);
            return;
        }

        string? provinceId = null;
        if (province is not null)
        {
            if (ProvinceAutocomplete.Find(_constants.Config, province) is not { } found)
            {
                await RespondAsync($"There is no province called \"{province}\".", ephemeral: true, allowedMentions: AllowedMentions.None);
                return;
            }
            provinceId = found.Id;
        }

        await RespondAsync(Submit(Context.User.Id, ActionKind.Poll, target.Id, provinceId: provinceId),
            ephemeral: true, allowedMentions: AllowedMentions.None);
    }

    [ComponentInteraction("action:cancel:*", ignoreGroupNames: true)]
    [RequireFaction]
    public async Task CancelAsync(string rawId)
    {
        if (!long.TryParse(rawId, out var actionId) || _data.GetPendingAction(actionId) is not { } action
            || action.FactionId != Context.User.Id)
        {
            await RespondAsync("That action is no longer pending; it was already cancelled, replaced or resolved.", ephemeral: true);
            return;
        }

        var description = Describe(_data, _constants.Config, action);
        var cancelled = _data.CancelAction(actionId, Context.User.Id);
        await RespondAsync(
            cancelled
                ? $"Cancelled {description}. You have a free action slot until the next resolution, {Display.NextResolution(_data.GetGameState())}."
                : "That action is no longer pending; it was already cancelled, replaced or resolved.",
            ephemeral: true,
            allowedMentions: AllowedMentions.None);
    }

    private string? SlotRefusal(ulong factionId, ActionKind kind)
    {
        var pending = _data.GetPendingActions(factionId);
        var slot = ActionPlanner.SlotFor(pending, kind, _constants.Current.Campaign.ActionsPerTurn);
        return slot.Allowed ? null : TooMany(pending);
    }

    private string TooMany(IReadOnlyList<PendingAction> pending) =>
        $"You already have {pending.Count} pending actions, the most you can have, each of a different kind: " +
        $"{string.Join("; ", pending.Select(a => Describe(_data, _constants.Config, a)))}. " +
        "Cancel one in `/me` to free a slot, or submit an action of a kind already pending to replace it.";

    private string Submit(ulong factionId, ActionKind kind, string issueId, Stance? stance = null, string? provinceId = null, ulong? targetFactionId = null)
    {
        var config = _constants.Config;
        var result = _data.SubmitAction(factionId, kind, issueId, config.Constants.Campaign.ActionsPerTurn, stance, provinceId, targetFactionId);
        var state = _data.GetGameState();
        switch (result.Outcome)
        {
            case SubmitActionOutcome.TooMany:
                return TooMany(result.Pending);
            case SubmitActionOutcome.NotActive:
                return FactionGate.Retired;
            case SubmitActionOutcome.IssueNotOpen:
                return "That issue is no longer open.";
            case SubmitActionOutcome.StanceRequired:
                return "You must give a stance on this issue.";
            case SubmitActionOutcome.UnknownProvince:
                return "That province is not in play yet.";
            case SubmitActionOutcome.AttackRefused:
                return $"The attack was refused: {result.Refusal}.";
        }

        var action = result.Action!;
        var lines = new List<string> { $"Recorded {Describe(_data, config, action)}." };
        if (result.Replaced is { } replaced)
            lines.Add($"This replaces your pending {Describe(_data, config, replaced)}, which is cancelled.");
        if (action.OffProgramme)
            lines.Add("It will be logged publicly as **off-programme** in #campaign-log.");
        lines.Add(kind == ActionKind.Poll
            ? $"The poll is taken at the next resolution, {Display.NextResolution(state)}, against the mood at its start, and the answer comes to your private channel in words."
            : Resolves(state));
        if (kind != ActionKind.Poll)
            lines.Add("Campaigns and attacks are posted to #campaign-log with the resolution.");
        lines.Add($"Pending actions: {result.Pending.Count} of {config.Constants.Campaign.ActionsPerTurn}.");
        return string.Join("\n", lines);
    }

    private static string NoOpenIssue(string text) => $"There is no open issue \"{text}\". Pick one from the list.";
}
