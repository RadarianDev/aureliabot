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
