using Xunit;

namespace AureliaBot.Tests;

public class StandingsTests : IDisposable
{
    private readonly DatabaseFixture _fixture = new();
    private DatabaseService Db => _fixture.Db;

    public void Dispose() => _fixture.Dispose();

    private static GameConfig Config => DatabaseFixture.Config;

    private void Resolve() =>
        Db.Resolve(Config, new ScriptedRandomSource([], Enumerable.Repeat(0.5, 20)), 1, DateTimeOffset.UtcNow);

    [Fact]
    public void BeforeAnyResolution_ThereIsNoMark_AndNoChangeIsShown()
    {
        Db.StartGame(Config, DateTimeOffset.UtcNow);
        Db.CreateFaction(1, "A", "aurenten", 20);

        Assert.Empty(Db.GetPopularityMarks());
        Assert.Equal("", Standings.Change(20, null));
    }

    [Fact]
    public void AResolution_MarksEveryActiveFactionsPopularityAsItStarted()
    {
        Db.StartGame(Config, DateTimeOffset.UtcNow);
        Db.CreateFaction(1, "A", "aurenten", 20);

        Resolve();

        Assert.Equal(20, Db.GetPopularityMarks()[1]);
        Assert.NotEqual(20, Db.GetFaction(1)!.Popularity);
    }

    [Fact]
    public void Change_ShowsTheDirectionAndSize_OrSteady()
    {
        Assert.Equal(" ▲1.5", Standings.Change(11.5, 10));
        Assert.Equal(" ▼0.3", Standings.Change(9.7, 10));
        Assert.Equal(" =", Standings.Change(10.01, 10));
    }

    [Fact]
    public void Lines_RankFactionsByPopularity()
    {
        Db.StartGame(Config, DateTimeOffset.UtcNow);
        Db.CreateFaction(1, "Low", "aurenten", 5);
        Db.CreateFaction(2, "High", "aurenten", 30);

        var lines = Standings.Lines(Db, Config);

        Assert.StartsWith("1. <@2> (High)", lines[0]);
        Assert.StartsWith("2. <@1> (Low)", lines[1]);
    }

    [Fact]
    public void ProvinceLeaders_NameOneFactionForEveryProvince()
    {
        Db.StartGame(Config, DateTimeOffset.UtcNow);
        Db.CreateFaction(1, "A", "aurenten", 5);
        Db.CreateFaction(2, "B", "aurenten", 30);

        var leaders = Standings.ProvinceLeaders(Db, Config);

        Assert.Equal(Config.Provinces.Count, leaders.Count);
        Assert.All(leaders, l => Assert.Contains("<@2>", l));
    }
}
