using Discord;
using Discord.WebSocket;

public class CrisisPublisher
{
    private readonly DiscordSocketClient _client;
    private readonly DatabaseService _data;
    private readonly ConstantsStore _constants;
    private readonly FactionChannelService _factionChannels;
    private readonly AlertService _alerts;

    public CrisisPublisher(
        DiscordSocketClient client, DatabaseService data, ConstantsStore constants, FactionChannelService factionChannels, AlertService alerts)
    {
        _client = client;
        _data = data;
        _constants = constants;
        _factionChannels = factionChannels;
        _alerts = alerts;
    }

    public async Task PublishAsync(ResolutionReport report, string source)
    {
        foreach (var outcome in report.CrisisOutcomes)
            await Safely(() => PostAsync(OutcomePost(outcome), source));

        foreach (var posted in report.NewCrises)
            await Safely(() => PublishNewCrisisAsync(posted.CrisisId, source));
    }

    private async Task PublishNewCrisisAsync(long crisisId, string source)
    {
        if (_data.GetCrisis(crisisId) is not { } crisis) return;
        var state = _data.GetGameState();
        var message = await PostAsync(CrisisPost(crisis, state, _constants.Config, IssueTitle(_data, crisis.IssueId), Briefing.ForCrisis(_data, crisis)), source);
        if (message is not null) _data.SetCrisisMessageId(crisis.Id, message.Id);

        if (_data.ResponsibleMinister(crisis.Ministry) is { } minister)
            await _factionChannels.SendAsync(minister, Texts.Get("crisis.private.awaits",
                ("crisis", crisis.Title), ("ministry", crisis.Ministry.DisplayName()), ("due", GovernmentPublisher.Deadline(state, crisis.DueResolution))));
    }

    public async Task RefreshPostAsync(long crisisId)
    {
        if (_data.GetCrisis(crisisId) is not { MessageId: { } messageId } crisis) return;
        var post = CrisisPost(crisis, _data.GetGameState(), _constants.Config, IssueTitle(_data, crisis.IssueId), Briefing.ForCrisis(_data, crisis));
        if (crisis.Status == CrisisStatus.Cancelled) post = Texts.Get("gmCrisis.post.withdrawn") + "\n" + post;
        await Safely(() => EditAsync(messageId, post));
    }

    public async Task WithdrawAsync(Crisis crisis, string? message, string source)
    {
        await RefreshPostAsync(crisis.Id);
        var note = string.IsNullOrWhiteSpace(message)
            ? Texts.Get("gmCrisis.news.withdrawn", ("crisis", crisis.Title))
            : Texts.Get("gmCrisis.news.withdrawnMessage", ("crisis", crisis.Title), ("message", message.Trim().ReplaceLineEndings("\n> ")));
        await Safely(() => PostAsync(note, source, ChannelRole.Notices));
    }

    public async Task NotifyAmendedAsync(long crisisId)
    {
        if (_data.GetCrisis(crisisId) is not { Status: CrisisStatus.Live } crisis
            || _data.ResponsibleMinister(crisis.Ministry) is not { } minister) return;
        await Safely(() => _factionChannels.SendAsync(minister, Texts.Get("gmCrisis.private.amended", ("crisis", crisis.Title))));
    }

    private async Task EditAsync(ulong messageId, string text)
    {
        if (_data.GetChannel(ChannelRole.News) is not { } channelId || _client.GetChannel(channelId) is not IMessageChannel channel
            || await channel.GetMessageAsync(messageId) is not IUserMessage post) return;
        await post.ModifyAsync(p =>
        {
            p.Content = Display.Truncate(text);
            p.AllowedMentions = AllowedMentions.None;
        });
    }

    public static string IssueTitle(DatabaseService data, string issueId) => data.GetIssue(issueId)?.Title ?? issueId;

    public static string AnswerLine(int number, CrisisAnswer answer) =>
        Texts.Get("crisis.answerLine", ("number", number), ("label", answer.Label), ("lean", AssemblyPublisher.LeanName(answer.Lean)),
            ("price", answer.Price == 0 ? Texts.Get("crisis.free") : Display.Crowns(answer.Price)));

    public static string MetaLine(Crisis crisis, GameConfig config, string issueTitle) =>
        Texts.Get("crisis.post.meta", ("ministry", crisis.Ministry.DisplayName()), ("province", Display.Province(config, crisis.ProvinceId)), ("issue", issueTitle));

    public static string CrisisPost(Crisis crisis, GameState state, GameConfig config, string issueTitle, IEnumerable<string> briefing)
    {
        var lines = new List<string>
        {
            Texts.Get("crisis.post.heading", ("crisis", crisis.Title)),
            MetaLine(crisis, config, issueTitle),
            Texts.Get("crisis.post.due", ("due", GovernmentPublisher.Deadline(state, crisis.DueResolution))),
        };
        lines.AddRange(briefing);
        lines.Add("");
        lines.AddRange(crisis.Answers.Select((answer, index) => AnswerLine(index + 1, answer)));
        return string.Join("\n", lines);
    }

    public string OutcomePost(CrisisOutcomeReport outcome)
    {
        var config = _constants.Config;
        var ministry = outcome.Ministry.DisplayName();
        var minister = outcome.ResponsibleFactionId is { } id ? Display.Faction(_data, id) : null;
        var preamble = new List<string>();
        var effects = outcome.Effects.ToList();

        if (outcome.Outcome == CrisisOutcome.Default)
        {
            preamble.Add(minister is null
                ? Texts.Get("crisis.outcome.nobody", ("ministry", ministry))
                : Texts.Get("crisis.outcome.noAnswer", ("ministry", ministry), ("minister", minister), ("change", Display.Signed(outcome.PopularityChange))));
        }
        else
        {
            preamble.Add(Texts.Get("crisis.outcome.chose", ("minister", minister ?? Texts.Get("cabinet.vacant")), ("ministry", ministry), ("answer", outcome.AnswerLabel ?? "")));
            if (!string.IsNullOrWhiteSpace(outcome.ResponseText))
                preamble.Add(Texts.Get("crisis.outcome.response", ("text", outcome.ResponseText.ReplaceLineEndings("\n> "))));
            if (outcome.Outcome == CrisisOutcome.Complicated)
                preamble.Add(Texts.Get("crisis.outcome.couldNotPay"));
            if (outcome.ResponsibleFactionId is { } responder && outcome.PopularityChange != 0)
                effects.Add(new Effect($"popularity.{responder}", outcome.PopularityChange));
        }

        var post = Publisher.FormatEffectPost(outcome.Title, preamble, string.Join("\n\n", outcome.Texts),
            EffectSummary.Lines(effects, Publisher.Names(_data, config), config.Constants.Display), Briefing.ForEffects(_data, outcome.Effects));
        var condemnations = outcome.Condemnations.Select(score => CondemnationLine(score, outcome)).ToList();
        return condemnations.Count == 0 ? post : post + "\n\n" + string.Join("\n", condemnations);
    }

    private string CondemnationLine(CondemnationScore score, CrisisOutcomeReport outcome)
    {
        var faction = Display.Faction(_data, score.FactionId);
        var side = AssemblyPublisher.LeanName(score.Side);
        if (score.PopularityChange > 0) return Texts.Get("condemn.score.right", ("faction", faction), ("change", Display.Signed(score.PopularityChange)));
        if (score.PopularityChange < 0) return Texts.Get("condemn.score.wrong", ("faction", faction), ("change", Display.Signed(score.PopularityChange)));
        var ministerLean = outcome.Outcome == CrisisOutcome.Default || _data.GetCrisis(outcome.CrisisId)?.Chosen is not { } chosen ? (Lean?)null : chosen.Lean;
        return ministerLean == score.Side
            ? Texts.Get("condemn.score.withMinister", ("faction", faction))
            : Texts.Get("condemn.score.nothing", ("faction", faction), ("side", side));
    }

    private async Task Safely(Func<Task> post)
    {
        try
        {
            await post();
        }
        catch (Exception ex)
        {
            await _alerts.PostAsync(Texts.Get("crisis.alert.postFailed", ("error", ex.Message)));
        }
    }

    private async Task<IUserMessage?> PostAsync(string text, string source, ChannelRole role = ChannelRole.News)
    {
        if (_data.GetPostingChannel(role) is not { } channelId || _client.GetChannel(channelId) is not IMessageChannel channel)
        {
            Console.WriteLine($"[CrisisPublisher] No {role} channel is set; skipped a post from {source}.");
            return null;
        }

        IUserMessage? first = null;
        foreach (var chunk in Publisher.Chunk(text))
        {
            var message = await channel.SendMessageAsync(chunk, allowedMentions: AllowedMentions.None);
            _data.LogPost(channelId, message.Id, source);
            first ??= message;
        }
        return first;
    }
}
