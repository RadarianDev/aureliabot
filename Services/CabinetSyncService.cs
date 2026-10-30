using Discord.WebSocket;

public class CabinetSyncService
{
    private readonly DiscordSocketClient _client;
    private readonly DatabaseService _data;
    private readonly AlertService _alerts;

    public CabinetSyncService(DiscordSocketClient client, DatabaseService data, AlertService alerts)
    {
        _client = client;
        _data = data;
        _alerts = alerts;
    }

    public Task<IReadOnlyList<string>> SyncAsync() => throw new NotImplementedException();
}
