using Discord;
using Discord.WebSocket;

public class FactionChannelService
{
    private readonly DiscordSocketClient _client;
    private readonly DatabaseService _data;
    private readonly AlertService _alerts;

    public FactionChannelService(DiscordSocketClient client, DatabaseService data, AlertService alerts)
    {
        _client = client;
        _data = data;
        _alerts = alerts;
    }

    public Task<ulong?> EnsureChannelAsync(ulong factionId) => throw new NotImplementedException();

    public Task<IReadOnlyList<string>> SyncAllAsync() => throw new NotImplementedException();

    public Task<bool> SendAsync(ulong factionId, string text, MessageComponent? components = null, Embed? embed = null) =>
        throw new NotImplementedException();
}
