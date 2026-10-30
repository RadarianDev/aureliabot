using Discord.WebSocket;
using Xunit;

namespace AureliaBot.Tests;

public class TurnTrackerTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly DatabaseFixture _fixture = new();
    private readonly DiscordSocketClient _client = new();
    private DatabaseService Db => _fixture.Db;

    public void Dispose()
    {
        _client.Dispose();
        _fixture.Dispose();
    }

    private GameState State(Season season, int year, bool preElection, bool paused) =>
        Db.GetGameState() with
        {
            Season = season,
            Year = year,
            PreElectionSeason = preElection ? season : Season.Winter,
            PreElectionYear = preElection ? year : year + 5,
            Paused = paused,
        };

    [Fact]
    public void NameFor_Normal()
    {
        Assert.Equal("Summer 1851", TurnTrackerService.NameFor(State(Season.Summer, 1851, false, false)));
    }

    [Fact]
    public void NameFor_PreElection()
    {
        Assert.Equal("Spring 1854 · Pre-election", TurnTrackerService.NameFor(State(Season.Spring, 1854, true, false)));
    }

    [Fact]
    public void NameFor_Paused()
    {
        Assert.Equal("Summer 1851 · Paused", TurnTrackerService.NameFor(State(Season.Summer, 1851, false, true)));
    }

    [Fact]
    public void NameFor_PreElectionAndPaused()
    {
        Assert.Equal("Spring 1854 · Pre-election · Paused", TurnTrackerService.NameFor(State(Season.Spring, 1854, true, true)));
    }

    [Fact]
    public void RateLimiter_AllowsTwoThenWaitsUntilTenMinutesAfterTheFirst()
    {
        var limiter = new RenameRateLimiter();

        Assert.Equal(TimeSpan.Zero, limiter.DelayBeforeNext(T0));
        limiter.Record(T0);
        Assert.Equal(TimeSpan.Zero, limiter.DelayBeforeNext(T0.AddMinutes(1)));
        limiter.Record(T0.AddMinutes(1));

        Assert.Equal(TimeSpan.FromMinutes(8), limiter.DelayBeforeNext(T0.AddMinutes(2)));
        Assert.Equal(TimeSpan.FromSeconds(1), limiter.DelayBeforeNext(T0.AddMinutes(10).AddSeconds(-1)));
        Assert.Equal(TimeSpan.Zero, limiter.DelayBeforeNext(T0.AddMinutes(10)));

        limiter.Record(T0.AddMinutes(10));
        Assert.Equal(TimeSpan.FromMinutes(1), limiter.DelayBeforeNext(T0.AddMinutes(10)));
        Assert.Equal(2, limiter.Recent.Count);
    }

    [Fact]
    public void RateLimiter_IgnoresRenamesOlderThanTheWindow()
    {
        var recent = new[] { T0, T0.AddMinutes(1) };
        Assert.Equal(TimeSpan.Zero, RenameRateLimiter.DelayBeforeNext(recent, T0.AddMinutes(30)));
    }

    [Fact]
    public void Request_LatestNameWins()
    {
        var tracker = new TurnTrackerService(_client, Db);

        tracker.Request("Spring 1850");
        tracker.Request("Summer 1850");
        tracker.Request("Summer 1850 · Paused");

        Assert.Equal("Summer 1850 · Paused", tracker.PendingName);
    }

    [Fact]
    public void RequestCurrent_UsesTheGameState()
    {
        var tracker = new TurnTrackerService(_client, Db);
        Db.SaveGameState(State(Season.Autumn, 1852, false, true));

        tracker.RequestCurrent();

        Assert.Equal("Autumn 1852 · Paused", tracker.PendingName);
    }

    [Fact]
    public async Task RunAsync_WithoutAChannel_ConsumesTheRequestAndKeepsRunning()
    {
        var tracker = new TurnTrackerService(_client, Db);
        using var cts = new CancellationTokenSource();
        var loop = tracker.RunAsync(cts.Token);

        tracker.Request("Summer 1851");
        await WaitUntil(() => tracker.PendingName is null);
        tracker.Request("Autumn 1851");
        await WaitUntil(() => tracker.PendingName is null);

        Assert.False(loop.IsCompleted);
        cts.Cancel();
        await loop.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the turn tracker loop.");
            await Task.Delay(10);
        }
    }
}
