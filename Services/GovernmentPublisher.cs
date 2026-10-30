using Discord;
using Discord.WebSocket;

public class GovernmentPublisher
{
    private readonly DiscordSocketClient _client;
    private readonly DatabaseService _data;
    private readonly CabinetSyncService _cabinetSync;
    private readonly FactionChannelService _factionChannels;
    private readonly AlertService _alerts;
    private readonly ConstantsStore _constants;

    public GovernmentPublisher(
        DiscordSocketClient client, DatabaseService data, CabinetSyncService cabinetSync, FactionChannelService factionChannels, AlertService alerts,
        ConstantsStore constants)
    {
        _constants = constants;
        _client = client;
        _data = data;
        _cabinetSync = cabinetSync;
        _factionChannels = factionChannels;
        _alerts = alerts;
    }

    public async Task PublishAsync(ResolutionReport report, string source)
    {
        foreach (var vote in report.CabinetVotes)
        {
            await RefreshNominationAsync(vote.CabinetId);
            var chancellor = Who(vote.ChancellorId);
            await PostAsync(ChannelRole.Assembly, vote.Approved
                ? Texts.Get("cabinet.result.approved", ("chancellor", chancellor), ("yes", vote.YesSeats), ("no", vote.NoSeats), ("abstain", vote.AbstainSeats))
                : Texts.Get("cabinet.result.rejected", ("chancellor", chancellor), ("yes", vote.YesSeats), ("no", vote.NoSeats), ("abstain", vote.AbstainSeats)),
                source);
            if (vote.Approved && _data.GetCabinet(vote.CabinetId) is { } cabinet)
                await ProclaimAsync(Texts.Get("cabinet.proclaim.approved", ("cabinet", CabinetLines(cabinet))), source);
        }
        if (report.CabinetVotes.Any(v => v.Approved))
            await _cabinetSync.SyncAsync();

        foreach (var outcome in report.Questions)
        {
            if (outcome.Outcome == QuestionOutcomeKind.Unanswered)
                await PostAsync(ChannelRole.News, UnansweredPost(outcome), source);
            if (_data.GetQuestion(outcome.QuestionId) is { } question)
                await RefreshQuestionAsync(question);
        }
    }

    private string UnansweredPost(QuestionReport outcome)
    {
        var config = _constants.Config;
        var effects = new List<Effect>();
        if (outcome.MinisterId is { } minister) effects.Add(new Effect($"popularity.{minister}", outcome.MinisterChange));
        effects.Add(new Effect($"popularity.{outcome.AskerId}", outcome.AskerChange));
        return Publisher.FormatEffectPost(
            Texts.Get("questions.news.unansweredHeading", ("ministry", outcome.Ministry.DisplayName())),
            [],
            Texts.Get("questions.news.unanswered", ("minister", Who(outcome.MinisterId)), ("ministry", outcome.Ministry.DisplayName()), ("asker", Who(outcome.AskerId))),
            EffectSummary.Lines(effects, Publisher.Names(_data, config), config.Constants.Display));
    }

    public async Task<IUserMessage?> ProclaimAsync(string text, string source) =>
        await PostAsync(ChannelRole.News, Texts.Get("cabinet.proclamation", ("text", text)), source);

    public async Task PostNominationAsync(Cabinet nomination, string source)
    {
        var message = await PostAsync(ChannelRole.Assembly, NominationText(nomination), source, VoteButtons(nomination.Id, false));
        if (message is not null) _data.SetCabinetMessageId(nomination.Id, message.Id);
        foreach (var (ministry, nominee) in nomination.Posts)
            if (nominee is { } id)
                await _factionChannels.SendAsync(id, Texts.Get("cabinet.private.nominated",
                    ("chancellor", Who(nomination.ChancellorId)), ("ministry", ministry.DisplayName()), ("due", Deadline(_data.GetGameState(), nomination.DueResolution))));
    }

    public async Task RefreshNominationAsync(long cabinetId)
    {
        if (_data.GetCabinet(cabinetId) is not { } cabinet || _data.GetCabinetMessageId(cabinetId) is not { } messageId) return;
        var open = cabinet is { Kind: CabinetKind.Nominated, Status: CabinetStatus.Voting };
        await EditAsync(ChannelRole.Assembly, messageId, NominationText(cabinet), VoteButtons(cabinetId, !open));
    }

    public async Task CloseNominationsAsync(IEnumerable<CancelledNomination> cancelled)
    {
        foreach (var nomination in cancelled)
            if (nomination.MessageId is not null)
                await RefreshNominationAsync(nomination.Id);
    }

    public async Task PostQuestionAsync(Question question, string source)
    {
        var message = await PostAsync(ChannelRole.Assembly, QuestionText(question), source, AnswerButton(question.Id, false));
        if (message is not null) _data.SetQuestionMessageId(question.Id, message.Id);
        if (question.MinisterAtAsk is { } minister)
            await _factionChannels.SendAsync(minister, Texts.Get("questions.private.asked",
                ("asker", Who(question.AskerId)), ("ministry", question.Ministry.DisplayName()), ("text", Quoted(question.Text)),
                ("due", Deadline(_data.GetGameState(), question.DueResolution))), AnswerButton(question.Id, false));
    }

    public async Task PostAnswerAsync(Question question, string source)
    {
        await RefreshQuestionAsync(question);
        var text = Texts.Get("questions.answerPost", ("minister", Who(question.AnswererId)), ("ministry", question.Ministry.DisplayName()), ("text", question.AnswerText ?? ""));
        await PostAsync(ChannelRole.Assembly, text, source, reference: question.MessageId is { } id ? new MessageReference(id, failIfNotExists: false) : null);
    }

    public async Task RefreshQuestionAsync(Question question)
    {
        if (question.MessageId is not { } messageId) return;
        var current = _data.GetQuestion(question.Id) ?? question;
        await EditAsync(ChannelRole.Assembly, messageId, QuestionText(current), AnswerButton(current.Id, current.Status != QuestionStatus.Open));
    }

    public string NominationText(Cabinet cabinet)
    {
        var votes = _data.GetVotes(VoteSubject.Cabinet, cabinet.Id);
        var tally = VoteCounting.Tally(votes, _data.GetVotingSeats());
        var lines = new List<string>
        {
            Texts.Get("cabinet.post.heading", ("chancellor", Who(cabinet.ChancellorId))),
            CabinetLines(cabinet),
            Texts.Get("cabinet.post.tally", ("yes", tally.YesSeats), ("no", tally.NoSeats), ("abstain", tally.AbstainSeats)),
        };
        if (votes.Count > 0)
            lines.Add(Texts.Get("cabinet.post.voters", ("votes", string.Join(", ", votes.Select(v => Texts.Get("cabinet.post.voter", ("faction", Who(v.Key)), ("choice", ChoiceName(v.Value))))))));
        lines.Add(cabinet switch
        {
            { Kind: CabinetKind.Nominated, Status: CabinetStatus.Voting } => Texts.Get("cabinet.post.due", ("due", Deadline(_data.GetGameState(), cabinet.DueResolution))),
            { Kind: CabinetKind.Sitting } => Texts.Get("cabinet.post.approved"),
            { Status: CabinetStatus.Rejected } => Texts.Get("cabinet.post.rejected"),
            _ => Texts.Get("cabinet.post.withdrawn"),
        });
        return string.Join("\n", lines);
    }

    public string CabinetLines(Cabinet cabinet) =>
        string.Join("\n", new[] { Texts.Get("cabinet.line.chancellor", ("holder", Who(cabinet.ChancellorId))) }
            .Concat(GameEnumExtensions.CabinetMinistries.Select(m =>
                Texts.Get("cabinet.line.ministry", ("ministry", m.DisplayName()), ("holder", Who(cabinet.HolderOf(m)))))));

    public string QuestionText(Question question)
    {
        var status = question.Status switch
        {
            QuestionStatus.Open => Texts.Get("questions.post.open", ("minister", Who(_data.ResponsibleMinister(question.Ministry))),
                ("due", Deadline(_data.GetGameState(), question.DueResolution))),
            QuestionStatus.Answered => Texts.Get("questions.post.answered"),
            QuestionStatus.Unanswered => Texts.Get("questions.post.unanswered"),
            _ => Texts.Get("questions.post.dropped"),
        };
        return Texts.Get("questions.post.body",
            ("asker", Who(question.AskerId)), ("ministry", question.Ministry.DisplayName()), ("text", Quoted(question.Text)), ("status", status));
    }

    private static string Quoted(string text) => text.ReplaceLineEndings("\n> ");

    public static string CabinetVoteButtonId(VoteChoice choice, long cabinetId) => $"vote:cabinet:{choice.ToString().ToLowerInvariant()}:{cabinetId}";

    public static string AnswerButtonId(long questionId) => $"question:answer:{questionId}";

    public static MessageComponent VoteButtons(long cabinetId, bool disabled) =>
        new ComponentBuilder()
            .WithButton(Texts.Get("cabinet.button.yes"), CabinetVoteButtonId(VoteChoice.Yes, cabinetId), ButtonStyle.Success, disabled: disabled)
            .WithButton(Texts.Get("cabinet.button.no"), CabinetVoteButtonId(VoteChoice.No, cabinetId), ButtonStyle.Danger, disabled: disabled)
            .WithButton(Texts.Get("cabinet.button.abstain"), CabinetVoteButtonId(VoteChoice.Abstain, cabinetId), ButtonStyle.Secondary, disabled: disabled)
            .Build();

    public static MessageComponent AnswerButton(long questionId, bool disabled) =>
        new ComponentBuilder()
            .WithButton(Texts.Get("questions.button.answer"), AnswerButtonId(questionId), ButtonStyle.Primary, disabled: disabled)
            .Build();

    public static string ChoiceName(VoteChoice choice) => choice switch
    {
        VoteChoice.Yes => Texts.Get("cabinet.choice.yes"),
        VoteChoice.No => Texts.Get("cabinet.choice.no"),
        _ => Texts.Get("cabinet.choice.abstain"),
    };

    public static string Deadline(GameState state, long? dueResolution)
    {
        if (dueResolution is not { } due) return Texts.Get("cabinet.noDeadline");
        var turns = (int)Math.Max(0, due - state.ResolutionNumber - 1);
        var (season, year) = GameCalendar.Add(state.Season, state.Year, turns);
        return turns == 0
            ? Texts.Get("cabinet.deadlineNext", ("season", GameCalendar.Label(season, year)), ("when", Display.NextResolution(state)))
            : Texts.Get("cabinet.deadline", ("season", GameCalendar.Label(season, year)));
    }

    private string Who(ulong? factionId) =>
        factionId is { } id ? Display.Faction(_data, id) : Texts.Get("cabinet.vacant");

    private async Task<IUserMessage?> PostAsync(ChannelRole role, string text, string source, MessageComponent? components = null, MessageReference? reference = null)
    {
        if (_data.GetChannel(role) is not { } channelId || _client.GetChannel(channelId) is not IMessageChannel channel)
        {
            Console.WriteLine($"[GovernmentPublisher] No {role} channel is set; skipped a post from {source}.");
            return null;
        }
        try
        {
            IUserMessage? first = null;
            foreach (var chunk in Publisher.Chunk(text))
            {
                var message = await channel.SendMessageAsync(chunk,
                    components: first is null ? components : null,
                    messageReference: first is null ? reference : null,
                    allowedMentions: AllowedMentions.None);
                _data.LogPost(channelId, message.Id, source);
                first ??= message;
            }
            return first;
        }
        catch (Exception ex)
        {
            await _alerts.PostAsync(Texts.Get("cabinet.alert.postFailed", ("source", source), ("error", ex.Message)));
            return null;
        }
    }

    private async Task EditAsync(ChannelRole role, ulong messageId, string text, MessageComponent components)
    {
        try
        {
            if (_data.GetChannel(role) is not { } channelId || _client.GetChannel(channelId) is not IMessageChannel channel
                || await channel.GetMessageAsync(messageId) is not IUserMessage post) return;
            await post.ModifyAsync(p =>
            {
                p.Content = Display.Truncate(text);
                p.Components = components;
                p.AllowedMentions = AllowedMentions.None;
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GovernmentPublisher] Could not edit message {messageId}: {ex.Message}");
        }
    }
}
