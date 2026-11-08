using Discord;
using Discord.WebSocket;

public class AssemblyPublisher
{
    private readonly DiscordSocketClient _client;
    private readonly DatabaseService _data;
    private readonly ConstantsStore _constants;
    private readonly AlertService _alerts;

    public AssemblyPublisher(DiscordSocketClient client, DatabaseService data, ConstantsStore constants, AlertService alerts)
    {
        _client = client;
        _data = data;
        _constants = constants;
        _alerts = alerts;
    }

    public static string WithdrawButtonId(long readingId) => $"bill:withdraw:{readingId}";

    public static string ReadingVoteButtonId(VoteChoice choice, long readingId) => $"vote:reading:{choice}:{readingId}";

    public static string MotionVoteButtonId(VoteChoice choice, long motionId) => $"vote:nc:{choice}:{motionId}";

    public static string CosignButtonId(long motionId) => $"nc:cosign:{motionId}";

    public static string LeanName(Lean lean) => lean switch
    {
        Lean.For => Texts.Get("assembly.lean.for"),
        Lean.Against => Texts.Get("assembly.lean.against"),
        _ => Texts.Get("assembly.lean.neutral"),
    };

    public static string KindName(ReadingKind kind) => kind switch
    {
        ReadingKind.Government => Texts.Get("assembly.kind.government"),
        ReadingKind.Override => Texts.Get("assembly.kind.override"),
        _ => Texts.Get("assembly.kind.private"),
    };

    public static string ChoiceName(VoteChoice choice) => choice switch
    {
        VoteChoice.Yes => Texts.Get("assembly.choice.yes"),
        VoteChoice.No => Texts.Get("assembly.choice.no"),
        _ => Texts.Get("assembly.choice.abstain"),
    };

    public static MessageComponent VoteButtons(Func<VoteChoice, string> id, bool disabled = false) =>
        new ComponentBuilder()
            .WithButton(ChoiceName(VoteChoice.Yes), id(VoteChoice.Yes), ButtonStyle.Success, disabled: disabled)
            .WithButton(ChoiceName(VoteChoice.No), id(VoteChoice.No), ButtonStyle.Danger, disabled: disabled)
            .WithButton(ChoiceName(VoteChoice.Abstain), id(VoteChoice.Abstain), ButtonStyle.Secondary, disabled: disabled)
            .Build();

    public static MessageComponent ReadingButtons(long readingId) => VoteButtons(c => ReadingVoteButtonId(c, readingId));

    public static MessageComponent MotionVoteButtons(long motionId) => VoteButtons(c => MotionVoteButtonId(c, motionId));

    public static MessageComponent CosignButton(long motionId) =>
        new ComponentBuilder().WithButton(Texts.Get("noConfidence.cosignButton"), CosignButtonId(motionId), ButtonStyle.Danger).Build();

    private static MessageComponent NoButtons => new ComponentBuilder().Build();

    public async Task PublishAsync(ResolutionReport report, string source)
    {
        foreach (var cancelled in report.ReadingsCancelled)
            await Safely(() => PublishCancelledAsync(cancelled, source));

        foreach (var result in report.VoteResults.Where(r => r.Subject == VoteSubject.Reading))
            await Safely(() => PublishReadingResultAsync(result, source));

        if (report.WhipCounts.Count > 0)
            await Safely(() => PostAsync(ChannelRole.Assembly, string.Join("\n", report.WhipCounts.Select(WhipCountLine)), source));

        foreach (var law in report.Laws)
            await Safely(() => PostAsync(ChannelRole.News, LawPost(law), source));

        foreach (var motion in report.NoConfidenceMotions)
            await Safely(() => PublishMotionOutcomeAsync(motion, source));

        if (report.SnapElectionCalled)
        {
            var state = _data.GetGameState();
            await Safely(() => PostAsync(ChannelRole.News, Texts.Get("noConfidence.dissolved",
                ("season", GameCalendar.Label(state.PreElectionSeason, state.PreElectionYear))), source));
        }

        foreach (var opened in report.ReadingsOpened)
            await Safely(() => PublishOpenedAsync(opened, source));
    }

    private async Task Safely(Func<Task> post)
    {
        try
        {
            await post();
        }
        catch (Exception ex)
        {
            await _alerts.PostAsync(Texts.Get("assembly.alert.postFailed", ("error", ex.Message)));
        }
    }

    private async Task PublishOpenedAsync(ReadingOpenedReport opened, string source)
    {
        if (_data.GetReading(opened.ReadingId) is not { } reading) return;
        var message = await PostAsync(ChannelRole.Assembly, ReadingPost(reading), source, ReadingButtons(reading.Id));
        if (message is not null) _data.SetReadingMessage(reading.Id, message.Id);
    }

    private async Task PublishCancelledAsync(ReadingCancelledReport cancelled, string source)
    {
        if (_data.GetReading(cancelled.ReadingId) is { } reading)
        {
            await ModifyAsync(reading.MessageId, ReadingPost(reading), NoButtons);
            await ModifyAsync(reading.AnnounceMessageId, AnnouncePost(reading), null);
        }
        await PostAsync(ChannelRole.Assembly, Texts.Get("assembly.cancelled", ("bill", cancelled.Title), ("reason", cancelled.Reason)), source);
    }

    private async Task PublishReadingResultAsync(VoteResultReport result, string source)
    {
        var reading = _data.GetReading(result.SubjectId);
        if (reading is not null)
            await ModifyAsync(reading.MessageId, ReadingPost(reading), NoButtons);
        var state = _data.GetGameState();
        var rests = reading is not null && _data.GetBill(reading.BillId)?.CooldownUntil is { } until
            ? GameCalendar.Add(state.Season, state.Year, (int)(until - state.ResolutionNumber))
            : GameCalendar.Add(state.Season, state.Year, _constants.Current.RejectedCooldownSeasons);
        var chart = VoteChart.Render(_data, result.YesSeats, result.NoSeats);
        try
        {
            await PostAsync(ChannelRole.Assembly, result.ReturnedBySenate
                ? Texts.Get("assembly.result.returned", ("bill", result.Title), ("yes", result.YesSeats), ("no", result.NoSeats), ("abstain", result.AbstainSeats),
                    ("percent", Display.Number(_constants.Current.Senate.OverrideShare * 100)))
                : result.Passed
                    ? Texts.Get("assembly.result.passed", ("bill", result.Title), ("yes", result.YesSeats), ("no", result.NoSeats), ("abstain", result.AbstainSeats))
                    : Texts.Get("assembly.result.rejected", ("bill", result.Title), ("yes", result.YesSeats), ("no", result.NoSeats), ("abstain", result.AbstainSeats),
                        ("season", GameCalendar.Label(rests.Season, rests.Year))), source, chart: chart);
        }
        finally
        {
            VoteChart.Discard(chart);
        }

        if (result.Senate is not { } senate) return;
        var senateChart = VoteChart.RenderSenate(_data, senate.YesSeats, senate.NoSeats);
        try
        {
            await PostAsync(ChannelRole.Assembly,
                Texts.Get("assembly.result.senate", ("yes", senate.YesSeats), ("no", senate.NoSeats), ("abstain", senate.AbstainSeats)), source, chart: senateChart);
        }
        finally
        {
            VoteChart.Discard(senateChart);
        }
    }

    private async Task PublishMotionOutcomeAsync(NoConfidenceReport outcome, string source)
    {
        if (_data.GetMotion(outcome.MotionId) is { } motion)
            await ModifyAsync(motion.MessageId, MotionPost(motion), NoButtons);
        var tabler = Display.Faction(_data, outcome.TablerId);
        var text = outcome.Outcome switch
        {
            NoConfidenceStatus.Lapsed => Texts.Get("noConfidence.result.lapsed", ("tabler", tabler)),
            NoConfidenceStatus.Cancelled => Texts.Get("noConfidence.result.cancelled", ("tabler", tabler)),
            NoConfidenceStatus.Passed => Texts.Get("noConfidence.result.passed", ("tabler", tabler), ("yes", outcome.YesSeats), ("no", outcome.NoSeats), ("filled", outcome.FilledSeats)),
            _ => Texts.Get("noConfidence.result.failed", ("tabler", tabler), ("yes", outcome.YesSeats), ("no", outcome.NoSeats), ("filled", outcome.FilledSeats)),
        };
        var chart = outcome.Outcome is NoConfidenceStatus.Lapsed or NoConfidenceStatus.Cancelled
            ? null
            : VoteChart.Render(_data, outcome.YesSeats, outcome.NoSeats);
        try
        {
            await PostAsync(ChannelRole.Assembly, text, source, chart: chart);
        }
        finally
        {
            VoteChart.Discard(chart);
        }
    }

    private string WhipCountLine(WhipCountReport score)
    {
        var faction = Display.Faction(_data, score.FactionId);
        var member = Display.Faction(_data, score.MinisterId);
        var predicted = ChoiceName(score.Predicted);
        if (score.Actual is null or VoteChoice.Abstain)
            return Texts.Get("whipCount.score.void", ("faction", faction), ("member", member), ("choice", predicted), ("bill", score.Title));
        var change = Display.Signed(score.PopularityChange);
        return score.Actual == score.Predicted
            ? Texts.Get("whipCount.score.right", ("faction", faction), ("member", member), ("choice", predicted), ("bill", score.Title), ("change", change))
            : Texts.Get("whipCount.score.wrong", ("faction", faction), ("member", member), ("choice", predicted), ("bill", score.Title), ("change", change));
    }

    public string LawPost(LawReport law)
    {
        var config = _constants.Config;
        return Publisher.FormatEffectPost(
            Texts.Get("law.heading", ("bill", law.Title)),
            [Texts.Get("law.preamble", ("yes", law.YesSeats), ("no", law.NoSeats), ("sponsor", Display.Faction(_data, law.SponsorFactionId)))],
            law.PassText,
            EffectSummary.Lines(law.Effects, Publisher.Names(_data, config), config.Constants.Display));
    }

    public string AnnouncePost(Reading reading)
    {
        var bill = _data.GetBill(reading.BillId);
        var title = bill?.Title ?? reading.BillId;
        var issue = bill is null ? reading.BillId : _data.GetIssue(bill.IssueId)?.Title ?? bill.IssueId;
        var lean = bill is null ? "" : LeanName(bill.Lean);
        var sponsor = Display.Faction(_data, reading.SponsorId);
        return reading.Status switch
        {
            ReadingStatus.Scheduled => Texts.Get("assembly.announce", ("bill", title), ("issue", issue), ("lean", lean),
                ("kind", KindName(reading.Kind)), ("sponsor", sponsor), ("when", Display.NextResolution(_data.GetGameState()))),
            ReadingStatus.Cancelled => Texts.Get("assembly.announceCancelled", ("bill", title), ("issue", issue), ("lean", lean),
                ("kind", KindName(reading.Kind)), ("sponsor", sponsor)),
            _ => Texts.Get("assembly.announceOpened", ("bill", title), ("issue", issue), ("lean", lean),
                ("kind", KindName(reading.Kind)), ("sponsor", sponsor)),
        };
    }

    public string ReadingPost(Reading reading)
    {
        var bill = _data.GetBill(reading.BillId);
        var lines = new List<string>
        {
            Texts.Get("assembly.reading.heading", ("bill", bill?.Title ?? reading.BillId)),
        };
        if (bill is not null)
        {
            lines.Add(Texts.Get("assembly.reading.motion", ("motion", bill.Motion)));
            lines.Add(Texts.Get("assembly.reading.meta", ("issue", _data.GetIssue(bill.IssueId)?.Title ?? bill.IssueId),
                ("lean", LeanName(bill.Lean)), ("kind", KindName(reading.Kind)), ("sponsor", Display.Faction(_data, reading.SponsorId))));
        }

        switch (reading.Status)
        {
            case ReadingStatus.Open:
                if (reading.Kind == ReadingKind.Override)
                    lines.Add(Texts.Get("assembly.reading.overrideNote", ("percent", Display.Number(_constants.Current.Senate.OverrideShare * 100))));
                lines.Add(Texts.Get("assembly.reading.closes", ("when", Display.NextResolution(_data.GetGameState()))));
                lines.Add(Texts.Get("assembly.secret"));
                break;
            case ReadingStatus.Passed or ReadingStatus.Rejected or ReadingStatus.ReturnedBySenate:
                (string, object?)[] seats = [("yes", reading.YesSeats ?? 0), ("no", reading.NoSeats ?? 0), ("abstain", reading.AbstainSeats ?? 0)];
                lines.Add(reading.Status switch
                {
                    ReadingStatus.Passed => Texts.Get("assembly.reading.passed", seats),
                    ReadingStatus.Rejected => Texts.Get("assembly.reading.rejected", seats),
                    _ => Texts.Get("assembly.reading.returned", seats),
                });
                if (reading.SenateYesSeats is { } senateYes)
                    lines.Add(Texts.Get("assembly.reading.senate", ("yes", senateYes), ("no", reading.SenateNoSeats ?? 0), ("abstain", reading.SenateAbstainSeats ?? 0)));
                lines.AddRange(VoterLines(VoteSubject.Reading, reading.Id));
                break;
            case ReadingStatus.Cancelled:
                lines.Add(Texts.Get("assembly.reading.cancelled"));
                break;
        }
        return string.Join("\n", lines);
    }

    public string MotionPost(NoConfidenceMotion motion)
    {
        var c = _constants.Current;
        var cabinet = _data.GetSittingCabinet();
        var lines = new List<string>
        {
            Texts.Get("noConfidence.post.heading"),
            Texts.Get("noConfidence.post.tabled", ("tabler", Display.Faction(_data, motion.TablerId)),
                ("chancellor", cabinet?.ChancellorId is { } chancellor ? Display.Faction(_data, chancellor) : Texts.Get("noConfidence.post.noChancellor"))),
        };
        var support = _data.GetMotionSupport(motion.Id, c);
        var signers = string.Join(", ", support.Signers.Select(id => Display.Faction(_data, id)));
        switch (motion.Status)
        {
            case NoConfidenceStatus.Gathering:
                lines.Add(Texts.Get("noConfidence.post.support", ("supporters", support.Supporters), ("threshold", support.Threshold)));
                lines.Add(Texts.Get("noConfidence.post.signers", ("signers", signers)));
                lines.Add(Texts.Get("noConfidence.post.lapses", ("when", LapseTime(motion))));
                break;
            case NoConfidenceStatus.Voting:
                lines.Add(Texts.Get("noConfidence.post.signers", ("signers", signers)));
                lines.Add(Texts.Get("noConfidence.post.voting", ("filled", _data.GetVotingSeats().Values.Sum()), ("when", DueTime(motion.DueResolution))));
                lines.Add(Texts.Get("assembly.secret"));
                break;
            case NoConfidenceStatus.Passed:
                lines.Add(Texts.Get("noConfidence.post.passed", ("yes", motion.YesSeats ?? 0), ("no", motion.NoSeats ?? 0)));
                lines.AddRange(VoterLines(VoteSubject.NoConfidence, motion.Id));
                break;
            case NoConfidenceStatus.Failed:
                lines.Add(Texts.Get("noConfidence.post.failed", ("yes", motion.YesSeats ?? 0), ("no", motion.NoSeats ?? 0)));
                lines.AddRange(VoterLines(VoteSubject.NoConfidence, motion.Id));
                break;
            case NoConfidenceStatus.Lapsed:
                lines.Add(Texts.Get("noConfidence.post.lapsed", ("supporters", support.Supporters), ("threshold", support.Threshold)));
                break;
            default:
                lines.Add(Texts.Get("noConfidence.post.cancelled"));
                break;
        }
        return string.Join("\n", lines);
    }

    public MessageComponent? MotionButtons(NoConfidenceMotion motion) => motion.Status switch
    {
        NoConfidenceStatus.Gathering => CosignButton(motion.Id),
        NoConfidenceStatus.Voting => MotionVoteButtons(motion.Id),
        _ => NoButtons,
    };

    private string LapseTime(NoConfidenceMotion motion) => DueTime(motion.GatherUntilResolution);

    private string DueTime(long? resolution)
    {
        var state = _data.GetGameState();
        if (resolution is not { } due || state.NextResolutionAt is not { } next) return Texts.Get("display.notScheduled");
        return due <= state.ResolutionNumber + 1 ? Display.When(next) : Texts.Get("assembly.afterNext", ("when", Display.When(next)));
    }

    private IEnumerable<string> VoterLines(VoteSubject subject, long subjectId)
    {
        var votes = _data.GetVotes(subject, subjectId);
        var followers = _data.GetPartyFollowers(subject, subjectId);
        foreach (var choice in new[] { VoteChoice.Yes, VoteChoice.No, VoteChoice.Abstain })
        {
            var voters = votes.Where(v => v.Value == choice).Select(v => Display.Faction(_data, v.Key)).ToList();
            if (voters.Count > 0)
                yield return Texts.Get("assembly.voters", ("choice", ChoiceName(choice)), ("voters", string.Join(", ", voters)));
            var following = followers.Where(v => v.Value == choice).Select(v => Display.Faction(_data, v.Key)).ToList();
            if (following.Count > 0)
                yield return Texts.Get("assembly.followers", ("choice", ChoiceName(choice)), ("voters", string.Join(", ", following)));
        }
    }

    public async Task<IUserMessage?> AnnounceAsync(Reading reading)
    {
        var message = await PostAsync(ChannelRole.Assembly, AnnouncePost(reading), "bill propose");
        if (message is not null) _data.SetReadingAnnounceMessage(reading.Id, message.Id);
        return message;
    }

    public async Task RefreshReadingAsync(long readingId)
    {
        if (_data.GetReading(readingId) is not { } reading) return;
        await ModifyAsync(reading.AnnounceMessageId, AnnouncePost(reading), null);
        await ModifyAsync(reading.MessageId, ReadingPost(reading), reading.Status == ReadingStatus.Open ? ReadingButtons(reading.Id) : NoButtons);
    }

    public async Task PostMotionAsync(long motionId)
    {
        if (_data.GetMotion(motionId) is not { } motion) return;
        var message = await PostAsync(ChannelRole.Assembly, MotionPost(motion), "noconfidence", MotionButtons(motion));
        if (message is not null) _data.SetMotionMessage(motion.Id, message.Id);
    }

    public async Task RefreshMotionAsync(long motionId)
    {
        if (_data.GetMotion(motionId) is not { } motion) return;
        if (motion.MessageId is null)
        {
            await PostMotionAsync(motionId);
            return;
        }
        await ModifyAsync(motion.MessageId, MotionPost(motion), MotionButtons(motion));
    }

    public Task AlertAsync(string message) => _alerts.PostAsync(message);

    private async Task<IUserMessage?> PostAsync(ChannelRole role, string text, string source, MessageComponent? components = null, string? chart = null)
    {
        if (_data.GetChannel(role) is not { } channelId || _client.GetChannel(channelId) is not IMessageChannel channel)
        {
            Console.WriteLine($"[AssemblyPublisher] No {role} channel is set; skipped a post from {source}.");
            return null;
        }

        IUserMessage? first = null;
        try
        {
            var chunks = Publisher.Chunk(text).ToList();
            foreach (var chunk in chunks)
            {
                var message = chart is not null && chunk == chunks[^1]
                    ? await channel.SendFileAsync(chart, chunk, components: first is null ? components : null, allowedMentions: AllowedMentions.None)
                    : await channel.SendMessageAsync(chunk, components: first is null ? components : null, allowedMentions: AllowedMentions.None);
                _data.LogPost(channelId, message.Id, source);
                first ??= message;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AssemblyPublisher] Posting to {role} failed: {ex}");
            await _alerts.PostAsync(Texts.Get("assembly.alert.postFailed", ("error", ex.Message)));
        }
        return first;
    }

    private async Task ModifyAsync(ulong? messageId, string text, MessageComponent? components)
    {
        if (messageId is not { } id || _data.GetChannel(ChannelRole.Assembly) is not { } channelId
            || _client.GetChannel(channelId) is not IMessageChannel channel) return;
        try
        {
            if (await channel.GetMessageAsync(id) is not IUserMessage post) return;
            await post.ModifyAsync(p =>
            {
                p.Content = Display.Truncate(text);
                if (components is not null) p.Components = components;
                p.AllowedMentions = AllowedMentions.None;
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AssemblyPublisher] Could not update message {id}: {ex.Message}");
        }
    }
}
