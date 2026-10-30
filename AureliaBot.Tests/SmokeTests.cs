using Xunit;

namespace AureliaBot.Tests;

public class SmokeTests : IDisposable
{
    private readonly DatabaseFixture _fixture = new();
    private DatabaseService Db => _fixture.Db;

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void FreshDatabase_HasDefaultGameState()
    {
        var state = Db.GetGameState();
        Assert.False(state.Started);
        Assert.Equal(Season.Spring, state.Season);
        Assert.Equal(1850, state.Year);
        Assert.Equal(0, state.ResolutionNumber);
    }

    [Fact]
    public void Resolve_AdvancesTheCalendar()
    {
        var report = Db.Resolve(DatabaseFixture.Constants, new ScriptedRandomSource(), 7, DateTimeOffset.UtcNow);

        Assert.Equal(1, report.ResolutionNumber);
        Assert.Equal(Season.Summer, report.ToSeason);
        var state = Db.GetGameState();
        Assert.Equal(Season.Summer, state.Season);
        Assert.Equal(1, state.ResolutionNumber);
    }

    [Fact]
    public void Resolve_RollsTheYearAfterWinter()
    {
        Db.SaveGameState(Db.GetGameState() with { Season = Season.Winter });
        Db.Resolve(DatabaseFixture.Constants, new ScriptedRandomSource(), 7, DateTimeOffset.UtcNow);
        var state = Db.GetGameState();
        Assert.Equal(Season.Spring, state.Season);
        Assert.Equal(1851, state.Year);
    }

    [Fact]
    public void Config_LoadsAndValidates()
    {
        var config = DatabaseFixture.Config;
        Assert.Equal(180, config.Constants.Seats);
        Assert.Equal(10, config.Provinces.Count);
        Assert.Equal(0.15, config.Constants.DefaultAllocation[Ministry.Navy]);
        Assert.Equal(2, config.Constants.StartingForces.Divisions["aurenten"]);
        Assert.Equal(0.2, config.Condition("lawless")!.BiasOn("poor_relief"));
        Assert.Equal(3, config.Constants.Division.TrainCosts.Count);
        Assert.Equal(new TimeOnly(8, 0), config.Constants.ResolutionTimeOfDay);
        Assert.Empty(ConstantsStore.Validate(config));
    }

    [Fact]
    public void Channels_RoundTrip()
    {
        Db.SetChannel(ChannelRole.News, 123UL);
        Assert.Equal(123UL, Db.GetChannel(ChannelRole.News));
        Assert.Null(Db.GetChannel(ChannelRole.Gm));
    }
}
