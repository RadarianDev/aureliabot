using Discord;
using Discord.Interactions;

public enum CabinetMinistryChoice
{
    Treasury,
    War,
    Interior,
    [ChoiceDisplay("Commerce and Labor")] CommerceLabor,
}

public static class CabinetMinistryChoiceExtensions
{
    public static Ministry ToMinistry(this CabinetMinistryChoice choice) => Enum.Parse<Ministry>(choice.ToString());
}

[Group("ministry", "Ministers' commands")]
public class MinistryModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly DatabaseService _data;
    private readonly Publisher _publisher;
    private readonly GovernmentPublisher _government;
    private readonly CabinetSyncService _cabinetSync;
    private readonly Confirmations _confirmations;
    private readonly AlertService _alerts;
    private readonly ProjectPublisher _projects;
    private readonly ConstantsStore _constants;

    public MinistryModule(
        DatabaseService data, Publisher publisher, GovernmentPublisher government, CabinetSyncService cabinetSync,
        Confirmations confirmations, AlertService alerts, ProjectPublisher projects, ConstantsStore constants)
    {
        _projects = projects;
        _constants = constants;
        _data = data;
        _publisher = publisher;
        _government = government;
        _cabinetSync = cabinetSync;
        _confirmations = confirmations;
        _alerts = alerts;
    }

    [SlashCommand("resign", "Resign from the cabinet at once")]
    [RequireFaction]
    public async Task ResignAsync()
    {
        var userId = Context.User.Id;
        if (_data.GetSittingCabinet() is not { } sitting || !sitting.Holders.Contains(userId))
        {
            await RespondAsync(Texts.Get("ministry.resign.notInCabinet"), ephemeral: true);
            return;
        }

        var office = OfficeName(sitting, userId);
        var nomination = _data.GetOpenNomination();
        var cancels = nomination is not null && (nomination.ChancellorId == userId || nomination.Posts.Values.Contains(userId));
        var warning = Texts.Get("ministry.resign.confirm", ("office", office))
                      + (cancels ? "\n" + Texts.Get("ministry.resign.confirmCancels") : "");
        await RespondAsync(warning, components: _confirmations.Create(userId, () => ResignConfirmedAsync(userId), Texts.Get("ministry.resign.button")),
            ephemeral: true);
    }

    private async Task<string> ResignConfirmedAsync(ulong userId)
    {
        var result = _data.Resign(userId);
        if (result.Refusal is not null) return result.Refusal;

        var faction = Display.Faction(_data, userId);
        var office = result.WasChancellor ? Texts.Get("ministry.office.chancellor") : result.Ministry!.Value.DisplayName();
        await _publisher.PostAsync(ChannelRole.News, result.WasChancellor
            ? Texts.Get("ministry.resign.newsChancellor", ("faction", faction))
            : Texts.Get("ministry.resign.newsMinister", ("faction", faction), ("ministry", office)), "ministry resign");
        await _government.CloseNominationsAsync(result.Cancelled);
        await _alerts.PostAsync(result.WasChancellor
            ? Texts.Get("ministry.resign.alertChancellor", ("faction", faction), ("cancelled", result.Cancelled.Count))
            : Texts.Get("ministry.resign.alertMinister", ("faction", faction), ("ministry", office), ("cancelled", result.Cancelled.Count)));
        await _cabinetSync.SyncAsync();
        return Texts.Get("ministry.resign.done", ("office", office));
    }

    [SlashCommand("status", "Privately show a ministry's purse, recent ledger and open questions")]
    [RequireFaction]
    public async Task StatusAsync([Summary("ministry", "The ministry; defaults to your own")] CabinetMinistryChoice? ministry = null)
    {
        var userId = Context.User.Id;
        var sitting = _data.GetSittingCabinet();
        var own = sitting?.Posts.FirstOrDefault(p => p.Value == userId) is { Value: not null } held ? held.Key : (Ministry?)null;
        var isChancellor = sitting?.ChancellorId == userId;

        IReadOnlyList<Ministry> shown;
        if (ministry is { } chosen)
        {
            var target = chosen.ToMinistry();
            if (!isChancellor && own != target)
            {
                await RespondAsync(Texts.Get("ministry.status.notYours", ("ministry", target.DisplayName())), ephemeral: true);
                return;
            }
            shown = [target];
        }
        else if (own is { } mine) shown = [mine];
        else if (isChancellor) shown = GameEnumExtensions.CabinetMinistries;
        else
        {
            await RespondAsync(Texts.Get("ministry.status.notMinister"), ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);
        var state = _data.GetGameState();
        var text = string.Join("\n\n", shown.Select(m => MinistryStatus(sitting, m, state)));
        foreach (var chunk in Publisher.Chunk(text))
            await FollowupAsync(chunk, ephemeral: true, allowedMentions: AllowedMentions.None);
    }

    private string MinistryStatus(Cabinet? sitting, Ministry ministry, GameState state)
    {
        var holder = sitting?.HolderOf(ministry) is { } id ? Display.Faction(_data, id) : Texts.Get("cabinet.vacant");
        var lines = new List<string>
        {
            Texts.Get("ministry.status.heading", ("ministry", ministry.DisplayName()), ("holder", holder)),
            Texts.Get("ministry.status.purse", ("balance", Display.Crowns(_data.GetPurseBalance(ministry)))),
        };

        var ledger = _data.GetLedger(Enum.Parse<LedgerAccount>(ministry.ToString()))
            .Where(e => e.PublicFromResolution > state.ResolutionNumber)
            .ToList();
        lines.Add(ledger.Count == 0
            ? Texts.Get("ministry.status.noPrivateLedger")
            : Texts.Get("ministry.status.privateLedger") + "\n" + string.Join("\n", ledger.Select(e =>
                Texts.Get("ministry.status.ledgerLine", ("amount", Display.Crowns(e.Amount)), ("label", e.Label)))));

        var questions = _data.GetOpenQuestions(ministry);
        lines.Add(questions.Count == 0
            ? Texts.Get("ministry.status.noQuestions")
            : Texts.Get("ministry.status.questions") + "\n" + string.Join("\n", questions.Select(q =>
                Texts.Get("ministry.status.questionLine", ("asker", Display.Faction(_data, q.AskerId)),
                    ("text", Display.Truncate(q.Text, 200)), ("due", GovernmentPublisher.Deadline(state, q.DueResolution))))));
        return string.Join("\n", lines);
    }

    [SlashCommand("project", "Start a ministerial project in a province")]
    [RequireFaction]
    public async Task ProjectAsync(
        [Summary("project", "The ministerial project to start"), Autocomplete(typeof(MinisterialProjectAutocomplete))] string project,
        [Summary("province", "Where to build it"), Autocomplete(typeof(ProvinceAutocomplete))] string province)
    {
        var config = _constants.Config;
        if (ProvinceAutocomplete.Find(config, province) is not { } where)
        {
            await RespondAsync(Texts.Get("projects.refusal.unknownProvince", ("province", province)), ephemeral: true);
            return;
        }

        var templateId = MinisterialProjectAutocomplete.Find(_data, project)?.Id ?? project.Trim();
        var result = _data.StartMinisterialProject(Context.User.Id, templateId, where.Id, config);
        if (result.Refusal is not null || result.Project is not { } started)
        {
            await RespondAsync(result.Refusal ?? Texts.Get("projects.refusal.noProject"), ephemeral: true);
            return;
        }

        var state = _data.GetGameState();
        await RespondAsync(Texts.Get("projects.started", ("project", started.Title), ("province", where.Name),
            ("due", GovernmentPublisher.Deadline(state, started.FirstPaymentResolution)), ("pace", Display.Crowns(started.Pace)),
            ("ministry", started.Purse.DisplayName())), ephemeral: true);
        await _projects.PublishStartedAsync(started.Id, "ministry project");
    }

    [SlashCommand("fund", "Set a project's spending per season")]
    [RequireFaction]
    public async Task FundAsync(
        [Summary("project", "The project to fund"), Autocomplete(typeof(ProjectAutocomplete))] string project,
        [Summary("amount", "Crowns a season, from 0 up"), MinValue(0)] double amount)
    {
        if (ProjectAutocomplete.Find(_data, project) is not { } live)
        {
            await RespondAsync(Texts.Get("projects.refusal.noProject"), ephemeral: true);
            return;
        }

        var config = _constants.Config;
        var result = _data.FundProject(Context.User.Id, live.Id, (decimal)amount, config);
        if (result.Refusal is not null || result.Project is not { } funded || result.Forecast is not { } forecast)
        {
            await RespondAsync(result.Refusal ?? Texts.Get("projects.refusal.noProject"), ephemeral: true);
            return;
        }

        await RespondAsync(FundReply(funded, forecast, result.Balance, config, _data.GetGameState()), ephemeral: true);
    }

    public static string FundReply(LiveProject project, ProjectForecast forecast, decimal balance, GameConfig config, GameState state)
    {
        var ministry = project.Purse.DisplayName();
        var spending = Display.Crowns(forecast.Spending);
        var lines = new List<string>();
        if (forecast.Spending <= 0)
            lines.Add(Texts.Get("projects.fund.paused", ("project", project.Title), ("ministry", ministry)));
        else if (forecast.Excess > 0)
            lines.Add(Texts.Get("projects.fund.above", ("project", project.Title), ("spending", spending), ("ministry", ministry),
                ("excess", Display.Crowns(forecast.Excess))));
        else if (forecast.Spending < forecast.Pace)
            lines.Add(Texts.Get("projects.fund.below", ("project", project.Title), ("spending", spending), ("ministry", ministry),
                ("pace", Display.Crowns(forecast.Pace))));
        else
            lines.Add(Texts.Get("projects.fund.atPace", ("project", project.Title), ("spending", spending), ("ministry", ministry)));

        if (forecast.FinishTurn is { } finish)
        {
            var when = GameCalendar.Label(finish.Season, finish.Year);
            var work = Display.Crowns(forecast.WorkPerSeason);
            lines.Add(forecast.Excess > 0
                ? Texts.Get("projects.fund.graft", ("share", Display.Number((double)config.Constants.Project.Graft * 100)), ("work", work), ("finish", when))
                : Texts.Get("projects.fund.noGraft", ("work", work), ("finish", when)));
        }

        var tail = new List<string>();
        if (project.BonusDueResolution is { } due)
        {
            var deadline = ProjectPublisher.TurnLabel(state, due);
            tail.Add(forecast.BeatsBonus == true
                ? Texts.Get("projects.fund.beats", ("deadline", deadline))
                : Texts.Get("projects.fund.misses", ("deadline", deadline)));
        }
        tail.Add(Texts.Get("projects.fund.balance", ("balance", Display.Crowns(balance))));
        lines.Add(string.Join(" ", tail));
        return string.Join("\n", lines);
    }

    private static string OfficeName(Cabinet sitting, ulong userId) =>
        sitting.ChancellorId == userId
            ? Texts.Get("ministry.office.chancellor")
            : sitting.Posts.First(p => p.Value == userId).Key.DisplayName();
}
