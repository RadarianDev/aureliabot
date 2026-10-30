using System.Globalization;
using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using Microsoft.Data.Sqlite;

public sealed record SnapshotInfo(string Path, DateTimeOffset TakenAt, Season Season, int Year);

public sealed record UndoResult(SnapshotInfo Restored, IReadOnlyList<LogEntry> LostCommands, int PostsDeleted, int PostsNotDeleted);

public sealed record RestorePlan(SnapshotInfo Restored, IReadOnlyList<LogEntry> LostCommands, IReadOnlyList<LogEntry> Posts);

public partial class SnapshotService
{
    public const int Keep = 30;
    private const string TimestampFormat = "yyyy-MM-dd-HHmmss";

    private readonly DiscordSocketClient? _client;
    private readonly DatabaseService _data;
    private readonly string _folder;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _gate = new();

    public SnapshotService(DiscordSocketClient client, DatabaseService data)
        : this(client, data, DefaultFolder(data), () => DateTimeOffset.UtcNow)
    {
    }

    internal SnapshotService(DiscordSocketClient? client, DatabaseService data, string folder, Func<DateTimeOffset>? clock = null)
    {
        _client = client;
        _data = data;
        _folder = folder;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public DiscordSocketClient? Client => _client;

    public string Folder => _folder;

    public SnapshotInfo Backup()
    {
        lock (_gate)
        {
            Directory.CreateDirectory(_folder);
            var state = _data.GetGameState();
            var takenAt = TruncateToSecond(_clock().ToUniversalTime());
            var path = PathFor(takenAt, state.Season, state.Year);
            while (File.Exists(path))
            {
                takenAt = takenAt.AddSeconds(1);
                path = PathFor(takenAt, state.Season, state.Year);
            }

            using (var live = new SqliteConnection(_data.ConnectionString))
            using (var target = new SqliteConnection(UnpooledConnectionString(path)))
            {
                live.Open();
                target.Open();
                live.BackupDatabase(target);
            }

            Prune();
            return new SnapshotInfo(path, takenAt, state.Season, state.Year);
        }
    }

    public IReadOnlyList<SnapshotInfo> List()
    {
        if (!Directory.Exists(_folder))
            return [];
        return Directory.EnumerateFiles(_folder, "*.db")
            .Select(Parse)
            .OfType<SnapshotInfo>()
            .OrderByDescending(s => System.IO.Path.GetFileName(s.Path), StringComparer.Ordinal)
            .ToList();
    }

    public RestorePlan? RestoreLatest()
    {
        lock (_gate)
        {
            var latest = List().FirstOrDefault();
            if (latest is null)
                return null;

            var liveState = _data.GetGameState();
            var lostCommands = _data.GetLogEntries(LogKinds.Command, latest.TakenAt);
            var posts = _data.GetLogEntriesForResolution(LogKinds.Post, liveState.ResolutionNumber)
                .Where(p => p.At >= latest.TakenAt)
                .ToList();

            SqliteConnection.ClearAllPools();
            using (var source = new SqliteConnection(UnpooledConnectionString(latest.Path)))
            using (var live = new SqliteConnection(_data.ConnectionString))
            {
                source.Open();
                live.Open();
                source.BackupDatabase(live);
            }
            SqliteConnection.ClearAllPools();

            File.Delete(latest.Path);
            _data.SaveGameState(_data.GetGameState() with { Paused = true });
            return new RestorePlan(latest, lostCommands, posts);
        }
    }

    public async Task<UndoResult?> UndoAsync()
    {
        var plan = RestoreLatest();
        if (plan is null)
            return null;

        var deleted = 0;
        var notDeleted = 0;
        foreach (var post in plan.Posts)
        {
            if (await TryDeletePostAsync(post.Payload))
                deleted++;
            else
                notDeleted++;
        }
        return new UndoResult(plan.Restored, plan.LostCommands, deleted, notDeleted);
    }

    private async Task<bool> TryDeletePostAsync(string payload)
    {
        if (_client is null || !TryParsePost(payload, out var channelId, out var messageId))
            return false;
        try
        {
            if (_client.GetChannel(channelId) is not IMessageChannel channel)
                return false;
            await channel.DeleteMessageAsync(messageId);
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SnapshotService] Deleting post {payload} failed: {ex.Message}");
            return false;
        }
    }

    internal static bool TryParsePost(string payload, out ulong channelId, out ulong messageId)
    {
        messageId = 0;
        var parts = payload.Split(':');
        channelId = 0;
        return parts.Length == 2
            && ulong.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out channelId)
            && ulong.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out messageId);
    }

    private void Prune()
    {
        foreach (var stale in List().Skip(Keep))
        {
            try
            {
                File.Delete(stale.Path);
            }
            catch (IOException ex)
            {
                Console.Error.WriteLine($"[SnapshotService] Pruning {stale.Path} failed: {ex.Message}");
            }
        }
    }

    private string PathFor(DateTimeOffset takenAt, Season season, int year) =>
        System.IO.Path.Combine(_folder, FileName(takenAt, season, year));

    internal static string FileName(DateTimeOffset takenAt, Season season, int year) =>
        $"{takenAt.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture)}-{season}{year.ToString(CultureInfo.InvariantCulture)}.db";

    internal static SnapshotInfo? Parse(string path)
    {
        var match = FileNamePattern().Match(System.IO.Path.GetFileName(path));
        if (!match.Success
            || !DateTime.TryParseExact(match.Groups[1].Value, TimestampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var takenAt)
            || !Enum.TryParse<Season>(match.Groups[2].Value, out var season)
            || !int.TryParse(match.Groups[3].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var year))
            return null;
        return new SnapshotInfo(path, new DateTimeOffset(takenAt, TimeSpan.Zero), season, year);
    }

    [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2}-\d{6})-([A-Za-z]+)(\d+)\.db$")]
    private static partial Regex FileNamePattern();

    private static DateTimeOffset TruncateToSecond(DateTimeOffset value) =>
        DateTimeOffset.FromUnixTimeSeconds(value.ToUnixTimeSeconds());

    private static string UnpooledConnectionString(string path) =>
        new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();

    private static string DefaultFolder(DatabaseService data)
    {
        var dataSource = new SqliteConnectionStringBuilder(data.ConnectionString).DataSource;
        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(dataSource)) ?? Directory.GetCurrentDirectory();
        return System.IO.Path.Combine(directory, "snapshots");
    }
}
