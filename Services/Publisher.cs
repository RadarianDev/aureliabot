using Discord;
using Discord.WebSocket;

public class Publisher
{
    private readonly DiscordSocketClient _client;
    private readonly DatabaseService _data;

    public Publisher(DiscordSocketClient client, DatabaseService data)
    {
        _client = client;
        _data = data;
    }

    public async Task PublishAsync(ResolutionReport report)
    {
        var news = new List<string>();
        if (report.CampaignDay)
        {
            news.Add($"## Dispatches from the hustings\n{GameCalendar.Label(report.FromSeason, report.FromYear)}");
        }
        else
        {
            news.Add($"## {GameCalendar.Label(report.ToSeason, report.ToYear)}");
            if (report.PreElectionOpened)
                news.Add("The hustings open: this season ends in an election.");
            if (_data.GetGameState().NextResolutionAt is { } next)
                news.Add($"The season closes <t:{next.ToUnixTimeSeconds()}:F>.");
        }
        news.AddRange(report.News);

        await PostAsync(ChannelRole.News, string.Join("\n", news), $"resolution {report.ResolutionNumber}");
        await UpdateDashboardAsync();
    }

    public const string DashboardMessageKey = "dashboard_message_id";

    public Embed BuildDashboard()
    {
        var state = _data.GetGameState();
        var parties = _data.GetAllParties();
        var factions = _data.GetActiveFactions();

        var calendar = $"{state.Label}{(state.IsPreElection ? " · **Pre-election**" : "")}{(state.Paused ? " · Paused" : "")}\n" +
                       $"Next resolution: {Display.NextResolution(state)}\n" +
                       $"Next election: end of {GameCalendar.Label(state.PreElectionSeason, state.PreElectionYear)}";

        var seatLines = parties
            .Select(p => (Party: p, Members: factions.Where(f => f.PartyId == p.Id).ToList()))
            .OrderByDescending(x => x.Members.Sum(m => m.Seats))
            .ThenBy(x => x.Party.Id)
            .Select(x => $"{Display.Party(x.Party)}: {x.Members.Sum(m => m.Seats)} seats · {x.Members.Count} members")
            .ToList();
        var independents = factions.Where(f => f.IsIndependent).ToList();
        if (independents.Count > 0)
            seatLines.Add($"Independents: {independents.Sum(f => f.Seats)} seats · {independents.Count} factions");

        var openIssues = _data.GetOpenIssues();

        return new EmbedBuilder()
            .WithTitle($"The Kingdom of Aurelia · {state.Label}")
            .WithColor(new Color(0xB8, 0x86, 0x0B))
            .AddField("Calendar", calendar)
            .AddField($"The Assembly · {state.Seats} seats", seatLines.Count == 0 ? "No parties yet." : Display.Truncate(string.Join("\n", seatLines), 1024))
            .AddField("Open issues", openIssues.Count == 0 ? "None yet." : Display.Truncate(string.Join("\n", openIssues.Select(i => i.Title)), 1024))
            .WithFooter("Updated after every resolution")
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
