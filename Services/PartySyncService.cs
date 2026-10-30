using Discord.WebSocket;

public class PartySyncService
{
    private readonly DiscordSocketClient _client;
    private readonly DatabaseService _data;

    public PartySyncService(DiscordSocketClient client, DatabaseService data)
    {
        _client = client;
        _data = data;
    }

    public DiscordSocketClient Client => _client;

    public DatabaseService Data => _data;
}
