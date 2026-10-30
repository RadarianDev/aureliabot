using Discord;
using Discord.WebSocket;

public class AlertService
{
    private readonly DiscordSocketClient _client;
    private readonly DatabaseService _data;

    public AlertService(DiscordSocketClient client, DatabaseService data)
    {
        _client = client;
        _data = data;
    }

    public async Task PostAsync(string message)
    {
        Console.WriteLine($"[Alert] {message}");
        try
        {
            if (_data.GetChannel(ChannelRole.Gm) is { } channelId && _client.GetChannel(channelId) is IMessageChannel channel)
                await channel.SendMessageAsync(message.Length > 2000 ? message[..1997] + "..." : message, allowedMentions: AllowedMentions.None);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Alert] Could not post to #gm: {ex.Message}");
        }
    }

    public async Task PostAllAsync(IEnumerable<string> messages)
    {
        foreach (var message in messages)
            await PostAsync(message);
    }
}
