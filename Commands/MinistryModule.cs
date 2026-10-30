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

    public MinistryModule(
        DatabaseService data, Publisher publisher, GovernmentPublisher government, CabinetSyncService cabinetSync,
        Confirmations confirmations, AlertService alerts)
    {
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

    private static string OfficeName(Cabinet sitting, ulong userId) =>
        sitting.ChancellorId == userId
            ? Texts.Get("ministry.office.chancellor")
            : sitting.Posts.First(p => p.Value == userId).Key.DisplayName();
}
