using Discord.WebSocket;

public sealed record SnapshotInfo(string Path, DateTimeOffset TakenAt, Season Season, int Year);

public sealed record UndoResult(SnapshotInfo Restored, IReadOnlyList<LogEntry> LostCommands, int PostsDeleted, int PostsNotDeleted);

public class SnapshotService
{
    private readonly DiscordSocketClient _client;
    private readonly DatabaseService _data;

    public SnapshotService(DiscordSocketClient client, DatabaseService data)
    {
        _client = client;
        _data = data;
    }

    public SnapshotInfo Backup() => throw new NotImplementedException();

    public IReadOnlyList<SnapshotInfo> List() => throw new NotImplementedException();

    public Task<UndoResult?> UndoAsync() => throw new NotImplementedException();

    public DiscordSocketClient Client => _client;
}
