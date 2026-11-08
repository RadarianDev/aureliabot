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
        var issue = Texts.Get("campaign.describe.issue", ("issue", data.GetIssue(action.IssueId)?.Title ?? action.IssueId));
        var stance = action.Stance?.DisplayName() ?? Texts.Get("campaign.describe.noStance");
        var province = Display.Province(config, action.ProvinceId);
        return action.Kind switch
        {
            ActionKind.Campaign => action.OffProgramme
                ? Texts.Get("campaign.describe.campaignOff", ("stance", stance), ("issue", issue), ("province", province))
                : Texts.Get("campaign.describe.campaign", ("stance", stance), ("issue", issue), ("province", province)),
            ActionKind.Attack => Texts.Get("campaign.describe.attack",
                ("target", action.TargetFactionId is { } target ? Display.Faction(data, target) : Texts.Get("campaign.describe.nobody")),
                ("issue", issue), ("province", province)),
            _ => Texts.Get("campaign.describe.poll", ("issue", issue)),
        };
    }

    public static string Resolves(GameState state) =>
        Texts.Get("campaign.resolves", ("when", Display.NextResolution(state)));

    public static int ActionsAllowed(GameConstants constants, GameState state) => constants.ActionsAllowed(state);

    [SlashCommand("campaign", "Campaign on an issue in one province")]
    [RequireFaction]
    public async Task CampaignAsync(
        [Summary("issue", "The open issue to campaign on"), Autocomplete(typeof(CampaignIssueAutocomplete))] string issue,
        [Summary("province", "Where to campaign; defaults to your home province"), Autocomplete(typeof(ProvinceAutocomplete))] string? province = null,
        [Summary("stance", "Your stance; defaults to your party's programme")] StanceChoice? stance = null)
    {
        if (OpenIssueAutocomplete.Find(_data, issue) is not { } target)
        {
            await RespondAsync(NoOpenIssue(issue), ephemeral: true, allowedMentions: AllowedMentions.None);
            return;
        }
        if (!TryProvince(province, out var provinceId))
        {
            await RespondAsync(Texts.Get("campaign.noSuchProvince", ("province", province)), ephemeral: true, allowedMentions: AllowedMentions.None);
            return;
        }

        var faction = _data.GetFaction(Context.User.Id)!;
        var programme = _data.GetProgrammeStance(faction.UserId, target.Id);
        var decision = ActionPlanner.DecideCampaignStance(programme, stance is { } s ? (Stance)s : null);
        if (decision.Outcome != CampaignStanceOutcome.Ok)
        {
            await RespondAsync(
                faction.IsIndependent
                    ? Texts.Get("campaign.needStanceIndependent", ("issue", target.Title))
                    : Texts.Get("campaign.needStanceSilent", ("issue", target.Title)),
                ephemeral: true);
            return;
        }

        var where = provinceId ?? faction.HomeProvinceId;
        if (SlotRefusal(faction.UserId, ActionKind.Campaign, target.Id, where) is { } refusal)
        {
            await RespondAsync(refusal, ephemeral: true, allowedMentions: AllowedMentions.None);
            return;
        }

        var chosen = decision.Stance!.Value;
        if (!decision.OffProgramme)
        {
            await RespondAsync(Submit(faction.UserId, ActionKind.Campaign, target.Id, stance: chosen, provinceId: provinceId),
                ephemeral: true, allowedMentions: AllowedMentions.None);
            return;
        }

        var userId = faction.UserId;
        var issueId = target.Id;
        await RespondAsync(
            Texts.Get("campaign.offProgrammeConfirm",
                ("programme", decision.ProgrammeStance!.Value.DisplayName()), ("issue", target.Title),
                ("stance", chosen.DisplayName()), ("when", Display.NextResolution(_data.GetGameState()))),
            components: _confirmations.Create(userId,
                () => Task.FromResult(Submit(userId, ActionKind.Campaign, issueId, stance: chosen, provinceId: provinceId)),
                Texts.Get("campaign.offProgrammeButton")),
            ephemeral: true,
            allowedMentions: AllowedMentions.None);
    }

    [SlashCommand("attack", "Attack a rival's stance on an issue in one province")]
    [RequireFaction]
    public async Task AttackAsync(
        [Summary("faction", "The rival to attack"), Autocomplete(typeof(RivalAutocomplete))] string faction,
        [Summary("issue", "The issue where the rival holds a stance"), Autocomplete(typeof(RivalStanceIssueAutocomplete))] string issue,
        [Summary("province", "Where to attack; defaults to your home province"), Autocomplete(typeof(ProvinceAutocomplete))] string? province = null)
    {
        if (FactionAutocomplete.Find(_data, faction) is not { } rival)
        {
            await RespondAsync(Texts.Get("campaign.noSuchFaction"), ephemeral: true);
            return;
        }
        if (OpenIssueAutocomplete.Find(_data, issue) is not { } target)
        {
            await RespondAsync(NoOpenIssue(issue), ephemeral: true, allowedMentions: AllowedMentions.None);
            return;
        }
        if (!TryProvince(province, out var provinceId))
        {
            await RespondAsync(Texts.Get("campaign.noSuchProvince", ("province", province)), ephemeral: true, allowedMentions: AllowedMentions.None);
            return;
        }

        var check = _data.CheckAttack(Context.User.Id, rival.UserId, target.Id);
        if (check.Refusal is not null)
        {
            await RespondAsync(Texts.Get("campaign.attackRefusal",
                    ("rival", Display.Faction(rival)), ("issue", target.Title), ("reason", check.Refusal)),
                ephemeral: true, allowedMentions: AllowedMentions.None);
            return;
        }

        await RespondAsync(Submit(Context.User.Id, ActionKind.Attack, target.Id, provinceId: provinceId, targetFactionId: rival.UserId),
            ephemeral: true, allowedMentions: AllowedMentions.None);
    }

    [SlashCommand("poll", "Poll an issue across the kingdom and in every province")]
    [RequireFaction]
    public async Task PollAsync(
        [Summary("issue", "The open issue to poll"), Autocomplete(typeof(OpenIssueAutocomplete))] string issue)
    {
        if (OpenIssueAutocomplete.Find(_data, issue) is not { } target)
        {
            await RespondAsync(NoOpenIssue(issue), ephemeral: true, allowedMentions: AllowedMentions.None);
            return;
        }

        await RespondAsync(Submit(Context.User.Id, ActionKind.Poll, target.Id), ephemeral: true, allowedMentions: AllowedMentions.None);
    }

    [ComponentInteraction("action:cancel:*", ignoreGroupNames: true)]
    [RequireFaction]
    public async Task CancelAsync(string rawId)
    {
        if (!long.TryParse(rawId, out var actionId) || _data.GetPendingAction(actionId) is not { } action
            || action.FactionId != Context.User.Id)
        {
            await RespondAsync(Texts.Get("campaign.noLongerPending"), ephemeral: true);
            return;
        }

        var description = Describe(_data, _constants.Config, action);
        var cancelled = _data.CancelAction(actionId, Context.User.Id);
        await RespondAsync(
            cancelled
                ? Texts.Get("campaign.cancelled", ("action", description), ("when", Display.NextResolution(_data.GetGameState())))
                : Texts.Get("campaign.noLongerPending"),
            ephemeral: true,
            allowedMentions: AllowedMentions.None);
    }

    private bool TryProvince(string? text, out string? provinceId)
    {
        provinceId = null;
        if (text is null) return true;
        if (ProvinceAutocomplete.Find(_constants.Config, text) is not { } found) return false;
        provinceId = found.Id;
        return true;
    }

    private int MaxPending() => ActionsAllowed(_constants.Current, _data.GetGameState());

    private string? SlotRefusal(ulong factionId, ActionKind kind, string issueId, string? provinceId)
    {
        var pending = _data.GetPendingActions(factionId);
        var slot = ActionPlanner.SlotFor(pending, kind, issueId, provinceId, MaxPending(), _data.GetGameState().IsPreElection);
        return slot.Allowed ? null : TooMany(pending);
    }

    private string TooMany(IReadOnlyList<PendingAction> pending) =>
        Texts.Get("campaign.tooMany", ("count", pending.Count),
            ("actions", string.Join("; ", pending.Select(a => Describe(_data, _constants.Config, a)))));

    private string Submit(ulong factionId, ActionKind kind, string issueId, Stance? stance = null, string? provinceId = null, ulong? targetFactionId = null)
    {
        var config = _constants.Config;
        var max = MaxPending();
        var result = _data.SubmitAction(factionId, kind, issueId, max, stance, provinceId, targetFactionId);
        var state = _data.GetGameState();
        switch (result.Outcome)
        {
            case SubmitActionOutcome.TooMany:
                return TooMany(result.Pending);
            case SubmitActionOutcome.NotActive:
                return FactionGate.Retired;
            case SubmitActionOutcome.IssueNotOpen:
                return Texts.Get("campaign.issueNotOpen");
            case SubmitActionOutcome.StanceRequired:
                return Texts.Get("campaign.stanceRequired");
            case SubmitActionOutcome.UnknownProvince:
                return Texts.Get("campaign.provinceNotInPlay");
            case SubmitActionOutcome.AttackRefused:
                return Texts.Get("campaign.attackRefused", ("reason", result.Refusal));
        }

        var action = result.Action!;
        var lines = new List<string> { Texts.Get("campaign.recorded", ("action", Describe(_data, config, action))) };
        if (result.Replaced is { } replaced)
            lines.Add(Texts.Get("campaign.replaces", ("action", Describe(_data, config, replaced))));
        if (action.OffProgramme)
            lines.Add(Texts.Get("campaign.loggedOffProgramme"));
        lines.Add(kind == ActionKind.Poll
            ? Texts.Get("campaign.pollTiming", ("when", Display.NextResolution(state)))
            : Resolves(state));
        if (kind != ActionKind.Poll)
            lines.Add(Texts.Get("campaign.postedWithResolution"));
        lines.Add(Texts.Get("campaign.pendingCount", ("count", result.Pending.Count), ("max", max)));
        return string.Join("\n", lines);
    }

    private static string NoOpenIssue(string text) => Texts.Get("campaign.noOpenIssue", ("issue", text));
}
