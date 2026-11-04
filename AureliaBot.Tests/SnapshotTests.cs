using Microsoft.Data.Sqlite;
using Xunit;

namespace AureliaBot.Tests;

public class SnapshotTests : IDisposable
{
    private readonly DatabaseFixture _fixture = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"aureliabot-snapshots-{Guid.NewGuid():N}");
    private DateTimeOffset _now = DateTimeOffset.UtcNow.AddDays(-1);
    private readonly SnapshotService _snapshots;

    public SnapshotTests()
    {
        _snapshots = new SnapshotService(null, Db, _folder, () => _now);
    }

    private DatabaseService Db => _fixture.Db;

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
        }
        catch (IOException)
        {
        }
        _fixture.Dispose();
    }

    private SnapshotInfo BackupAndAdvanceClock()
    {
        var info = _snapshots.Backup();
        _now = _now.AddMinutes(1);
        return info;
    }

    private void InsertLog(string kind, DateTimeOffset at, long resolutionNumber, string payload)
    {
        using var conn = new SqliteConnection(_fixture.ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO log_entries (at, resolution_number, kind, source, faction_id, payload, public_flag)
            VALUES ($at, $resolution, $kind, 'test', NULL, $payload, 0);";
        cmd.Parameters.AddWithValue("$at", at.ToUnixTimeSeconds());
        cmd.Parameters.AddWithValue("$resolution", resolutionNumber);
        cmd.Parameters.AddWithValue("$kind", kind);
        cmd.Parameters.AddWithValue("$payload", payload);
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void Backup_CreatesANamedFile()
    {
        Db.SaveGameState(Db.GetGameState() with { Season = Season.Autumn, Year = 1852 });
        var info = _snapshots.Backup();

        Assert.True(File.Exists(info.Path));
        Assert.Equal(Season.Autumn, info.Season);
        Assert.Equal(1852, info.Year);
        Assert.EndsWith("-Autumn1852.db", info.Path);
        Assert.Equal(info, SnapshotService.Parse(info.Path));
        Assert.Equal(info, Assert.Single(_snapshots.List()));
    }

    [Fact]
    public void Backup_InTheSameSecond_GetsAUniqueName()
    {
        var first = _snapshots.Backup();
        var second = _snapshots.Backup();

        Assert.NotEqual(first.Path, second.Path);
        Assert.Equal(2, _snapshots.List().Count);
    }

    [Fact]
    public void Backup_PrunesToTheNewestThirty()
    {
        var taken = Enumerable.Range(0, SnapshotService.Keep + 3).Select(_ => BackupAndAdvanceClock()).ToList();

        var kept = _snapshots.List();
        Assert.Equal(SnapshotService.Keep, kept.Count);
        Assert.Equal(taken[^1].Path, kept[0].Path);
        Assert.Equal(taken[3].Path, kept[^1].Path);
        Assert.False(File.Exists(taken[0].Path));
        Assert.Equal(SnapshotService.Keep, Directory.GetFiles(_folder).Length);
    }

    [Fact]
    public void RestoringASnapshotFromAnOlderVersion_AddsTheTablesItLacks()
    {
        Db.CreateFaction(1, "A", "aurenten", 10);
        var snapshot = BackupAndAdvanceClock();
        using (var old = new SqliteConnection($"Data Source={snapshot.Path};Pooling=False"))
        {
            old.Open();
            using var cmd = old.CreateCommand();
            cmd.CommandText = "DROP TABLE regional_popularity;";
            cmd.ExecuteNonQuery();
        }

        Assert.NotNull(_snapshots.RestoreLatest());

        Assert.Empty(Db.GetRegionalPopularity(1, DatabaseFixture.Constants.Popularity));
    }

    [Fact]
    public void RestoreLatest_BringsBackTheStateAndPauses()
    {
        Db.SaveGameState(Db.GetGameState() with { Year = 1851, Threat = 12, ResolutionNumber = 4, RngSeed = 99 });
        var snapshot = BackupAndAdvanceClock();
        Db.SaveGameState(Db.GetGameState() with { Season = Season.Winter, Year = 1860, Threat = 70, ResolutionNumber = 5, RngSeed = 5 });

        var plan = _snapshots.RestoreLatest();

        Assert.NotNull(plan);
        Assert.Equal(snapshot, plan.Restored);
        var state = Db.GetGameState();
        Assert.Equal(Season.Spring, state.Season);
        Assert.Equal(1851, state.Year);
        Assert.Equal(12, state.Threat);
        Assert.Equal(4, state.ResolutionNumber);
        Assert.Equal(99, state.RngSeed);
        Assert.True(state.Paused);
        Assert.False(File.Exists(snapshot.Path));
        Assert.Empty(_snapshots.List());
    }

    [Fact]
    public void RestoreLatest_TwiceGoesOneSnapshotFurtherBack()
    {
        Db.SaveGameState(Db.GetGameState() with { Year = 1851 });
        var older = BackupAndAdvanceClock();
        Db.SaveGameState(Db.GetGameState() with { Year = 1852 });
        var newer = BackupAndAdvanceClock();
        Db.SaveGameState(Db.GetGameState() with { Year = 1853 });

        Assert.Equal(newer, _snapshots.RestoreLatest()!.Restored);
        Assert.Equal(1852, Db.GetGameState().Year);

        Assert.Equal(older, _snapshots.RestoreLatest()!.Restored);
        Assert.Equal(1851, Db.GetGameState().Year);
        Assert.True(Db.GetGameState().Paused);

        Assert.Null(_snapshots.RestoreLatest());
    }

    [Fact]
    public void RestoreLatest_ListsCommandsAndPostsMadeSinceTheSnapshot()
    {
        Db.SaveGameState(Db.GetGameState() with { ResolutionNumber = 2 });
        InsertLog(LogKinds.Command, _now.AddMinutes(-5), 2, "before");
        InsertLog(LogKinds.Post, _now.AddMinutes(-5), 2, "1:10");
        var snapshot = BackupAndAdvanceClock();
        Db.SaveGameState(Db.GetGameState() with { ResolutionNumber = 3 });
        InsertLog(LogKinds.Post, snapshot.TakenAt.AddSeconds(10), 3, "1:11");
        InsertLog(LogKinds.Command, snapshot.TakenAt.AddSeconds(20), 3, "after one");
        InsertLog(LogKinds.Post, snapshot.TakenAt.AddSeconds(30), 3, "2:12");
        InsertLog(LogKinds.Command, snapshot.TakenAt.AddSeconds(40), 3, "after two");

        var plan = _snapshots.RestoreLatest()!;

        Assert.Equal(["after one", "after two"], plan.LostCommands.Select(c => c.Payload));
        Assert.Equal(["1:11", "2:12"], plan.Posts.Select(p => p.Payload));
        Assert.DoesNotContain(Db.GetLogEntries(LogKinds.Command, snapshot.TakenAt), c => c.Payload.StartsWith("after"));
    }

    [Fact]
    public void RestoreLatest_WithNoSnapshots_ReturnsNull()
    {
        Assert.Null(_snapshots.RestoreLatest());
        Assert.False(Db.GetGameState().Paused);
    }

    [Fact]
    public async Task UndoAsync_WithoutAClient_CountsPostsAsNotDeleted()
    {
        var snapshot = BackupAndAdvanceClock();
        InsertLog(LogKinds.Post, snapshot.TakenAt.AddSeconds(5), Db.GetGameState().ResolutionNumber, "1:2");
        InsertLog(LogKinds.Command, snapshot.TakenAt.AddSeconds(6), Db.GetGameState().ResolutionNumber, "cmd");

        var result = await _snapshots.UndoAsync();

        Assert.NotNull(result);
        Assert.Equal(snapshot, result.Restored);
        Assert.Equal(0, result.PostsDeleted);
        Assert.Equal(1, result.PostsNotDeleted);
        Assert.Single(result.LostCommands);
    }

    [Fact]
    public void TryParsePost_ReadsChannelAndMessage()
    {
        Assert.True(SnapshotService.TryParsePost("123:456", out var channel, out var message));
        Assert.Equal(123UL, channel);
        Assert.Equal(456UL, message);
        Assert.False(SnapshotService.TryParsePost("garbage", out _, out _));
    }
}
