using Discord.WebSocket;

public class GovernmentPublisher
{
    private readonly DiscordSocketClient _client;
    private readonly DatabaseService _data;

    public GovernmentPublisher(DiscordSocketClient client, DatabaseService data)
    {
        _client = client;
        _data = data;
    }

    public Task PublishAsync(ResolutionReport report, string source) => Task.CompletedTask;
}
