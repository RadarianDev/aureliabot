using Discord.WebSocket;

public class CabinetSyncService
{
    private readonly DiscordSocketClient _client;
    private readonly DatabaseService _data;

    public CabinetSyncService(DiscordSocketClient client, DatabaseService data)
    {
        _client = client;
        _data = data;
    }

    public DiscordSocketClient Client => _client;

    public DatabaseService Data => _data;
}
