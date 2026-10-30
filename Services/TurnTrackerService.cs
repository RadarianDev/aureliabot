using Discord.WebSocket;

public class TurnTrackerService
{
    private readonly DiscordSocketClient _client;
    private readonly DatabaseService _data;

    public TurnTrackerService(DiscordSocketClient client, DatabaseService data)
    {
        _client = client;
        _data = data;
    }

    public static string NameFor(GameState state) => throw new NotImplementedException();

    public void Request(string name) => throw new NotImplementedException();

    public void RequestCurrent() => Request(NameFor(_data.GetGameState()));

    public Task RunAsync(CancellationToken cancellationToken) => throw new NotImplementedException();

    public DiscordSocketClient Client => _client;
}
