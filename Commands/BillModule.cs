using Discord;
using Discord.Interactions;

[Group("bill", "Bills before the Assembly")]
public class BillModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly DatabaseService _data;
    private readonly ConstantsStore _constants;
    private readonly AssemblyPublisher _assembly;

    public BillModule(DatabaseService data, ConstantsStore constants, AssemblyPublisher assembly)
    {
        _data = data;
        _constants = constants;
        _assembly = assembly;
    }

    [SlashCommand("propose", "Schedule an available bill for a vote next turn")]
    [RequireFaction]
    public async Task ProposeAsync(
        [Summary("bill", "An available bill whose issue has no bill before the Assembly"), Autocomplete(typeof(ProposableBillAutocomplete))] string bill)
    {
        var target = ProposableBillAutocomplete.Find(_data, bill);
        var c = _constants.Current;
        var result = _data.ProposeBill(Context.User.Id, target?.Id ?? bill.Trim(), c);
        if (result.Outcome != ProposeBillOutcome.Proposed)
        {
            await RespondAsync(Refusal(result, bill), ephemeral: true, allowedMentions: AllowedMentions.None);
            return;
        }

        var reading = result.Reading!;
        var state = _data.GetGameState();
        var lines = new List<string>
        {
            reading.Kind == ReadingKind.Government
                ? Texts.Get("bill.proposedGovernment", ("bill", result.Bill!.Title), ("when", Display.NextResolution(state)))
                : Texts.Get("bill.proposedPrivate", ("bill", result.Bill!.Title), ("when", Display.NextResolution(state))),
            Texts.Get("bill.withdrawHint"),
        };
        await RespondAsync(string.Join("\n", lines),
            components: new ComponentBuilder().WithButton(Texts.Get("bill.withdrawButton"), AssemblyPublisher.WithdrawButtonId(reading.Id), ButtonStyle.Secondary).Build(),
            ephemeral: true, allowedMentions: AllowedMentions.None);

        if (await _assembly.AnnounceAsync(reading) is null)
            await _assembly.AlertAsync(Texts.Get("bill.alertNoAssembly", ("bill", result.Bill.Title)));
    }

    [ComponentInteraction("bill:withdraw:*", ignoreGroupNames: true)]
    [RequireFaction]
    public async Task WithdrawAsync(string rawId)
    {
        if (!long.TryParse(rawId, out var readingId))
        {
            await RespondAsync(Texts.Get("bill.withdraw.notFound"), ephemeral: true);
            return;
        }

        var result = _data.WithdrawReading(readingId, Context.User.Id);
        var title = result.Bill?.Title ?? "";
        var reply = result.Outcome switch
        {
            WithdrawOutcome.Withdrawn => result.Reading!.Kind == ReadingKind.Government
                ? Texts.Get("bill.withdraw.doneGovernment", ("bill", title))
                : Texts.Get("bill.withdraw.donePrivate", ("bill", title)),
            WithdrawOutcome.NotYours => Texts.Get("bill.withdraw.notYours", ("bill", title)),
            WithdrawOutcome.NotScheduled => Texts.Get("bill.withdraw.tooLate", ("bill", title)),
            _ => Texts.Get("bill.withdraw.notFound"),
        };
        await RespondAsync(reply, ephemeral: true, allowedMentions: AllowedMentions.None);
        if (result.Outcome == WithdrawOutcome.Withdrawn)
            await _assembly.RefreshReadingAsync(readingId);
    }

    private string Refusal(ProposeBillResult result, string typed)
    {
        var title = result.Bill?.Title ?? typed;
        var c = _constants.Current;
        var state = _data.GetGameState();
        return result.Outcome switch
        {
            ProposeBillOutcome.NotActive => FactionGate.Retired,
            ProposeBillOutcome.PreElection or ProposeBillOutcome.BeforePreElection => TimingRefusal(state, c, result.Outcome),
            ProposeBillOutcome.NoSuchBill => Texts.Get("bill.refuse.noSuchBill", ("bill", typed)),
            ProposeBillOutcome.NotAvailable => result.Bill!.Status == BillStatus.Cooldown && result.Bill.CooldownUntil is { } until
                ? Texts.Get("bill.refuse.cooldown", ("bill", title), ("season", CooldownSeason(state, until)))
                : Texts.Get("bill.refuse.notAvailable", ("bill", title)),
            ProposeBillOutcome.IssueNotOpen => Texts.Get("bill.refuse.issueNotOpen", ("bill", title)),
            ProposeBillOutcome.IssueBusy => Texts.Get("bill.refuse.issueBusy", ("bill", title)),
            ProposeBillOutcome.Independent => Texts.Get("bill.refuse.independent"),
            ProposeBillOutcome.MinisterCannot => Texts.Get("bill.refuse.minister"),
            ProposeBillOutcome.GovernmentBackbencher => Texts.Get("bill.refuse.backbencher"),
            ProposeBillOutcome.ChancellorshipVacant => Texts.Get("bill.refuse.vacant"),
            ProposeBillOutcome.GovernmentLimit => Texts.Get("bill.refuse.governmentLimit", ("count", c.Government.BillsPerSeason),
                ("when", Display.NextResolution(state))),
            ProposeBillOutcome.OppositionActionUsed => Texts.Get("bill.refuse.actionUsed", ("when", Display.NextResolution(state))),
            ProposeBillOutcome.PrivateBillCapReached => Texts.Get("bill.refuse.yearCap", ("count", c.Opposition.PrivateBillsPerYear),
                ("year", state.Year + 1)),
            _ => Texts.Get("bill.refuse.notAvailable", ("bill", title)),
        };
    }

    private static string CooldownSeason(GameState state, long until)
    {
        var (season, year) = GameCalendar.Add(state.Season, state.Year, (int)(until - state.ResolutionNumber));
        return GameCalendar.Label(season, year);
    }

    public static string TimingRefusal(GameState state, GameConstants c, ProposeBillOutcome timing)
    {
        var (season, year) = GameCalendar.Next(state.PreElectionSeason, state.PreElectionYear);
        DateTimeOffset? opens = state.NextResolutionAt is not { } next
            ? null
            : timing == ProposeBillOutcome.PreElection
                ? next
                : GameCalendar.NextResolutionAt(next, c, c.PreElectionDays);
        var label = GameCalendar.Label(season, year);
        var reason = timing == ProposeBillOutcome.PreElection
            ? Texts.Get("bill.refuse.preElection")
            : Texts.Get("bill.refuse.beforePreElection");
        return opens is { } at
            ? Texts.Get("bill.refuse.afterElectionAt", ("reason", reason), ("season", label), ("when", Display.Timestamp(at, 'R')))
            : Texts.Get("bill.refuse.afterElection", ("reason", reason), ("season", label));
    }
}
