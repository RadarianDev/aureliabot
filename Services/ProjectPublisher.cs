using Discord.WebSocket;

public class ProjectPublisher
{
    private readonly DiscordSocketClient _client;
    private readonly DatabaseService _data;

    public ProjectPublisher(DiscordSocketClient client, DatabaseService data)
    {
        _client = client;
        _data = data;
    }

    public Task PublishAsync(ResolutionReport report, string source) => Task.CompletedTask;
}
