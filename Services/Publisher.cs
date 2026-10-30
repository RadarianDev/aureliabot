using Discord;
using Discord.WebSocket;

public class Publisher
{
    private readonly DiscordSocketClient _client;
    private readonly DatabaseService _data;
    private readonly ConstantsStore _constants;
    private readonly FactionChannelService _factionChannels;
    private readonly ElectionPublisher _elections;

    public Publisher(DiscordSocketClient client, DatabaseService data, ConstantsStore constants, FactionChannelService factionChannels, ElectionPublisher elections)
    {
        _client = client;
        _data = data;
        _constants = constants;
        _factionChannels = factionChannels;
        _elections = elections;
    }

    public async Task PublishAsync(ResolutionReport report)
    {
        var source = $"resolution {report.ResolutionNumber}";
        var actionLines = ActionLines(report).ToList();

        if (report.CampaignDay)
        {
            var dispatch = new List<string> { Texts.Get("news.dispatches.header", ("season", GameCalendar.Label(report.FromSeason, report.FromYear))) };
            dispatch.Add(actionLines.Count == 0 ? Texts.Get("news.dispatches.quiet") : string.Join("\n", actionLines));
            await PostAsync(ChannelRole.News, string.Join("\n", dispatch), source);
        }
        else
        {
            var news = new List<string> { Texts.Get("news.season.header", ("season", GameCalendar.Label(report.ToSeason, report.ToYear))) };
            if (report.PreElectionOpened)
                news.Add(Texts.Get("news.season.hustingsOpen"));
            if (_data.GetGameState().NextResolutionAt is { } next)
                news.Add(Texts.Get("news.season.closes", ("when", Display.When(next))));
            news.AddRange(report.News);
            await PostAsync(ChannelRole.News, string.Join("\n", news), source);

            if (report.Election is { } election)
                await _elections.PublishAsync(election, source);

            await PostEventsAsync(report, source);

            if (actionLines.Count > 0)
                await PostAsync(ChannelRole.CampaignLog,
                    Texts.Get("news.campaignLog.header", ("season", GameCalendar.Label(report.FromSeason, report.FromYear))) + "\n" + string.Join("\n", actionLines), source);
        }

        foreach (var (factionId, messages) in report.PrivateMessages)
        {
            try
            {
                await _factionChannels.SendAsync(factionId, string.Join("\n\n", messages));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Publisher] Private report to {factionId} failed: {ex.Message}");
            }
        }

        await UpdateDashboardAsync();
    }

    public async Task PostEventsAsync(ResolutionReport report, string source)
    {
        if (report.FiredEvents.Count == 0) return;
        var names = Names(_data, _constants.Config);
        foreach (var fired in report.FiredEvents)
            await PostAsync(ChannelRole.News, EventPost(fired, names), source);
    }

    public string EventPost(FiredEventReport fired, EffectNames names) =>
        FormatEvent(fired.Title, fired.Text, EffectSummary.Lines(fired.Effects, names, _constants.Current.Display));

    public static string FormatEvent(string title, string text, IReadOnlyList<string> effectLines)
    {
        var story = string.Join("\n\n", text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(paragraph => Texts.Get("news.event.story", ("text", paragraph))));
        var post = Texts.Get("news.event.post", ("title", title), ("story", story));
        if (effectLines.Count == 0) return post;
        return post + "\n\n" + Texts.Get("news.event.effectsHeader") + "\n"
               + string.Join("\n", effectLines.Select(line => Texts.Get("news.event.effectLine", ("line", line))));
    }

    private IEnumerable<string> ActionLines(ResolutionReport report)
    {
        var titles = _data.GetAllIssues().ToDictionary(i => i.Id, i => i.Title);
        foreach (var action in report.Actions)
        {
            var actor = Display.Faction(_data, action.FactionId);
            var issue = titles.GetValueOrDefault(action.IssueId, action.IssueId);
            switch (action.Kind)
            {
                case ActionKind.Campaign:
                    var stance = action.Stance?.DisplayName() ?? Texts.Get("news.action.noStance");
                    yield return Texts.Get("news.action.campaign", ("actor", actor), ("stance", stance), ("issue", issue),
                        ("offProgramme", action.OffProgramme ? Texts.Get("news.action.offProgramme") : ""),
                        ("change", Display.Signed(action.ActorPopularityChange)));
                    break;
                case ActionKind.Attack when action.TargetFactionId is { } targetId:
                    var target = Display.Faction(_data, targetId);
                    yield return action.AttackSucceeded
                        ? Texts.Get("news.action.attackLanded", ("actor", actor), ("target", target), ("issue", issue), ("change", Display.Signed(action.TargetPopularityChange)))
                        : Texts.Get("news.action.attackBackfired", ("actor", actor), ("target", target), ("issue", issue), ("change", Display.Signed(action.ActorPopularityChange)));
                    break;
            }
        }
    }

    public static EffectNames Names(DatabaseService data, GameConfig config)
    {
        var issues = data.GetAllIssues().ToDictionary(i => i.Id, i => i.Title);
        var templateTitles = new Dictionary<string, string>();
        foreach (var row in data.GetTemplates())
        {
            try
            {
                if (TemplateCodec.Parse(row.Yaml, row.Id).Template is { } template)
                    templateTitles[row.Id] = template.Title;
            }
            catch (Exception)
            {
                templateTitles[row.Id] = row.Id;
            }
        }

        string Template(string id) => templateTitles.GetValueOrDefault(id, id);
        string Issue(string id) => issues.TryGetValue(id, out var title) ? title : Template(id);

        return new EffectNames(
            Province: id => config.Province(id)?.Name ?? id,
            Issue: Issue,
            Condition: id => config.Condition(id)?.Name ?? id,
            Faction: id => ulong.TryParse(id, out var userId) ? Display.Faction(data, userId) : id,
            IssueTemplate: Template,
            BillTemplate: Template,
            ProjectTemplate: Template,
            CrisisTemplate: Template);
    }

    public const string DashboardMessageKey = "dashboard_message_id";

    public Embed BuildDashboard()
    {
        var state = _data.GetGameState();
        var parties = _data.GetAllParties();
        var factions = _data.GetActiveFactions();

        var calendar = Texts.Get("dashboard.calendar",
            ("label", state.Label),
            ("preElection", state.IsPreElection ? Texts.Get("dashboard.preElectionTag") : ""),
            ("paused", state.Paused ? Texts.Get("dashboard.pausedTag") : ""),
            ("nextResolution", Display.NextResolution(state)),
            ("nextElection", GameCalendar.Label(state.PreElectionSeason, state.PreElectionYear)));

        var seatLines = parties
            .Select(p => (Party: p, Members: factions.Where(f => f.PartyId == p.Id).ToList()))
            .OrderByDescending(x => x.Members.Sum(m => m.Seats))
            .ThenBy(x => x.Party.Id)
            .Select(x => Texts.Get("dashboard.partyLine", ("party", Display.Party(x.Party)), ("seats", x.Members.Sum(m => m.Seats)), ("members", x.Members.Count)))
            .ToList();
        var independents = factions.Where(f => f.IsIndependent).ToList();
        if (independents.Count > 0)
            seatLines.Add(Texts.Get("dashboard.independentsLine", ("seats", independents.Sum(f => f.Seats)), ("factions", independents.Count)));

        var openIssues = _data.GetOpenIssues();

        return new EmbedBuilder()
            .WithTitle(Texts.Get("dashboard.title", ("label", state.Label)))
            .WithColor(new Color(0xB8, 0x86, 0x0B))
            .AddField(Texts.Get("dashboard.calendarField"), calendar)
            .AddField(Texts.Get("dashboard.assemblyField", ("seats", state.Seats)), seatLines.Count == 0 ? Texts.Get("dashboard.noParties") : Display.Truncate(string.Join("\n", seatLines), 1024))
            .AddField(Texts.Get("dashboard.issuesField"), openIssues.Count == 0 ? Texts.Get("dashboard.noIssues") : Display.Truncate(string.Join("\n", openIssues.Select(i => i.Title)), 1024))
            .WithFooter(Texts.Get("dashboard.footer"))
            .WithCurrentTimestamp()
            .Build();
    }

    public async Task UpdateDashboardAsync()
    {
        try
        {
            if (_data.GetChannel(ChannelRole.Dashboard) is not { } channelId || _client.GetChannel(channelId) is not ITextChannel channel)
                return;
            var embed = BuildDashboard();

            if (ulong.TryParse(_data.GetSetting(DashboardMessageKey), out var messageId)
                && await channel.GetMessageAsync(messageId) is IUserMessage existing
                && existing.Author.Id == _client.CurrentUser.Id)
            {
                await existing.ModifyAsync(p => { p.Embed = embed; p.Content = ""; });
                if (!existing.IsPinned) await existing.PinAsync();
                return;
            }

            var message = await channel.SendMessageAsync(embed: embed);
            _data.SetSetting(DashboardMessageKey, message.Id.ToString());
            await message.PinAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Publisher] Dashboard update failed: {ex.Message}");
        }
    }

    public async Task<IUserMessage?> PostAsync(ChannelRole role, string text, string source, Embed? embed = null, MessageComponent? components = null)
    {
        if (_data.GetChannel(role) is not { } channelId || _client.GetChannel(channelId) is not IMessageChannel channel)
        {
            Console.WriteLine($"[Publisher] No {role} channel is set; skipped a post from {source}.");
            return null;
        }

        IUserMessage? first = null;
        foreach (var chunk in Chunk(text))
        {
            var message = await channel.SendMessageAsync(
                chunk,
                embed: first is null ? embed : null,
                components: first is null ? components : null,
                allowedMentions: AllowedMentions.None);
            _data.LogPost(channelId, message.Id, source);
            first ??= message;
        }
        return first;
    }

    public static IEnumerable<string> Chunk(string text, int limit = 2000)
    {
        if (text.Length <= limit)
        {
            yield return text;
            yield break;
        }

        var current = new System.Text.StringBuilder();
        foreach (var line in text.Split('\n'))
        {
            if (current.Length > 0 && current.Length + line.Length + 1 > limit)
            {
                yield return current.ToString();
                current.Clear();
            }
            var remaining = line;
            while (remaining.Length > limit)
            {
                yield return remaining[..limit];
                remaining = remaining[limit..];
            }
            if (current.Length > 0) current.Append('\n');
            current.Append(remaining);
        }
        if (current.Length > 0) yield return current.ToString();
    }
}
