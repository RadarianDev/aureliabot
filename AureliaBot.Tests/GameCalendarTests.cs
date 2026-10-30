using Xunit;

namespace AureliaBot.Tests;

public class GameCalendarTests : IDisposable
{
    private readonly DatabaseFixture _fixture = new();
    private static GameConstants Constants => DatabaseFixture.Constants;
    private static TimeZoneInfo Zone => Constants.Zone;

    public void Dispose() => _fixture.Dispose();

    private static DateTimeOffset Local(int year, int month, int day, int hour, int minute = 0)
    {
        var local = new DateTime(year, month, day, hour, minute, 0);
        return new DateTimeOffset(local, Zone.GetUtcOffset(local));
    }

    private static void AssertLocal(DateTimeOffset expectedLocal, DateTimeOffset actual)
    {
        Assert.Equal(expectedLocal.UtcDateTime, actual.UtcDateTime);
        var local = TimeZoneInfo.ConvertTime(actual, Zone);
        Assert.Equal(expectedLocal.DateTime, local.DateTime);
    }

    private GameState StateAt(Season season, int year, Season preElectionSeason, int preElectionYear) =>
        _fixture.Db.GetGameState() with
        {
            Season = season,
            Year = year,
            PreElectionSeason = preElectionSeason,
            PreElectionYear = preElectionYear,
        };

    [Fact]
    public void Constants_AreTheOnesTheseTestsAssume()
    {
        Assert.Equal("Europe/Prague", Constants.TimeZone);
        Assert.Equal(new TimeOnly(8, 0), Constants.ResolutionTimeOfDay);
        Assert.Equal(12, Constants.MinHoursBetweenResolutions);
        Assert.Equal(3, Constants.PreElectionDays);
        Assert.Equal(16, Constants.SeasonsPerTerm);
    }

    [Fact]
    public void FirstTerm_PreElectionIsSpring1854()
    {
        Assert.Equal((Season.Spring, 1854), GameCalendar.PreElectionOfTermStarting(Season.Summer, 1850, Constants));
    }

    [Fact]
    public void NextTerm_StartsAfterThePreElectionAndRunsSixteenSeasons()
    {
        var (preSeason, preYear) = GameCalendar.PreElectionOfTermStarting(Season.Summer, 1850, Constants);
        var (startSeason, startYear) = GameCalendar.Next(preSeason, preYear);
        Assert.Equal((Season.Summer, 1854), (startSeason, startYear));
        var nextPre = GameCalendar.PreElectionOfTermStarting(startSeason, startYear, Constants);
        Assert.Equal((Season.Spring, 1858), nextPre);
        Assert.Equal(16, GameCalendar.SeasonsBetween(startSeason, startYear, nextPre.Season, nextPre.Year) + 1);
    }

    [Fact]
    public void SnapElection_NewTermRunsSixteenSeasonsFromTheElection()
    {
        var current = (Season: Season.Autumn, Year: 1851);
        var snapPre = GameCalendar.Next(current.Season, current.Year);
        Assert.Equal((Season.Winter, 1851), snapPre);

        var state = StateAt(snapPre.Season, snapPre.Year, snapPre.Season, snapPre.Year);
        Assert.True(state.IsPreElection);
        Assert.Equal(3, GameCalendar.SeasonLengthDays(state, Constants));

        var termStart = GameCalendar.Next(snapPre.Season, snapPre.Year);
        Assert.Equal((Season.Spring, 1852), termStart);
        var nextPre = GameCalendar.PreElectionOfTermStarting(termStart.Season, termStart.Year, Constants);
        Assert.Equal((Season.Winter, 1855), nextPre);
        Assert.Equal(15, GameCalendar.SeasonsBetween(termStart.Season, termStart.Year, nextPre.Season, nextPre.Year));
    }

    [Fact]
    public void SeasonLength_IsOneDayOutsideThePreElection()
    {
        var state = StateAt(Season.Summer, 1850, Season.Spring, 1854);
        Assert.False(state.IsPreElection);
        Assert.Equal(1, GameCalendar.SeasonLengthDays(state, Constants));
    }

    [Fact]
    public void SeasonLength_IsThreeDaysInThePreElection()
    {
        var state = StateAt(Season.Spring, 1854, Season.Spring, 1854);
        Assert.Equal(3, GameCalendar.SeasonLengthDays(state, Constants));
    }

    [Fact]
    public void SeasonLength_OverrideWins()
    {
        var ordinary = StateAt(Season.Summer, 1850, Season.Spring, 1854) with { SeasonLengthDays = 5 };
        Assert.Equal(5, GameCalendar.SeasonLengthDays(ordinary, Constants));
        var preElection = StateAt(Season.Spring, 1854, Season.Spring, 1854) with { SeasonLengthDays = 1 };
        Assert.Equal(1, GameCalendar.SeasonLengthDays(preElection, Constants));
    }

    [Fact]
    public void Next_RollsTheYearAfterWinter()
    {
        Assert.Equal((Season.Spring, 1851), GameCalendar.Next(Season.Winter, 1850));
        Assert.Equal((Season.Spring, 1851), GameCalendar.Add(Season.Winter, 1850, 1));
        Assert.Equal((Season.Winter, 1850), GameCalendar.Add(Season.Spring, 1851, -1));
    }

    [Theory]
    [InlineData(Season.Spring, 1850, 0, Season.Spring, 1850)]
    [InlineData(Season.Spring, 1850, 3, Season.Winter, 1850)]
    [InlineData(Season.Autumn, 1850, 6, Season.Spring, 1852)]
    [InlineData(Season.Summer, 1852, -2, Season.Winter, 1851)]
    [InlineData(Season.Spring, 1850, -9, Season.Winter, 1847)]
    [InlineData(Season.Winter, 1853, -15, Season.Spring, 1850)]
    public void Add_And_SeasonsBetween_RoundTrip(Season season, int year, int seasons, Season expectedSeason, int expectedYear)
    {
        var result = GameCalendar.Add(season, year, seasons);
        Assert.Equal((expectedSeason, expectedYear), result);
        Assert.Equal(seasons, GameCalendar.SeasonsBetween(season, year, result.Season, result.Year));
        Assert.Equal(-seasons, GameCalendar.SeasonsBetween(result.Season, result.Year, season, year));
        Assert.Equal((season, year), GameCalendar.Add(result.Season, result.Year, -seasons));
    }

    [Fact]
    public void ResolutionForTurn_CountsFromTheCurrentTurn()
    {
        var state = StateAt(Season.Autumn, 1851, Season.Spring, 1854) with { ResolutionNumber = 6 };
        Assert.Equal(6, GameCalendar.ResolutionForTurn(state, Season.Autumn, 1851));
        Assert.Equal(7, GameCalendar.ResolutionForTurn(state, Season.Winter, 1851));
        Assert.Equal(9, GameCalendar.ResolutionForTurn(state, Season.Summer, 1852));
        Assert.Equal(5, GameCalendar.ResolutionForTurn(state, Season.Summer, 1851));
    }

    [Fact]
    public void NextResolution_FromJustBeforeEight_IsTheNextDay()
    {
        var next = GameCalendar.NextResolutionAt(Local(2026, 6, 10, 7), Constants, 1);
        AssertLocal(Local(2026, 6, 11, 8), next);
    }

    [Fact]
    public void NextResolution_FromTheEvening_IsTheNextMorning()
    {
        var next = GameCalendar.NextResolutionAt(Local(2026, 6, 10, 19), Constants, 1);
        AssertLocal(Local(2026, 6, 11, 8), next);
    }

    [Fact]
    public void NextResolution_FromEight_IsTheNextDay()
    {
        var next = GameCalendar.NextResolutionAt(Local(2026, 6, 10, 8), Constants, 1);
        AssertLocal(Local(2026, 6, 11, 8), next);
    }

    [Fact]
    public void NextResolution_AcceptsAnyOffsetForFrom()
    {
        var next = GameCalendar.NextResolutionAt(Local(2026, 6, 10, 19).ToUniversalTime(), Constants, 1);
        AssertLocal(Local(2026, 6, 11, 8), next);
    }

    [Fact]
    public void NextResolution_AcrossSpringDst_StaysAtEight()
    {
        var from = Local(2026, 3, 28, 8);
        var next = GameCalendar.NextResolutionAt(from, Constants, 1);
        AssertLocal(Local(2026, 3, 29, 8), next);
        Assert.Equal(TimeSpan.FromHours(23), next - from);
        Assert.Equal(TimeSpan.FromHours(2), next.Offset);
    }

    [Fact]
    public void NextResolution_AcrossAutumnDst_StaysAtEight()
    {
        var from = Local(2026, 10, 24, 8);
        var next = GameCalendar.NextResolutionAt(from, Constants, 1);
        AssertLocal(Local(2026, 10, 25, 8), next);
        Assert.Equal(TimeSpan.FromHours(25), next - from);
        Assert.Equal(TimeSpan.FromHours(1), next.Offset);
    }

    [Fact]
    public void PreElection_LastsThreeDaysWithTwoCampaignDays()
    {
        var from = Local(2026, 6, 10, 8);
        var next = GameCalendar.NextResolutionAt(from, Constants, 3);
        AssertLocal(Local(2026, 6, 13, 8), next);

        var campaignDays = GameCalendar.CampaignDays(next, Constants, 3);
        Assert.Equal(2, campaignDays.Count);
        AssertLocal(Local(2026, 6, 11, 8), campaignDays[0]);
        AssertLocal(Local(2026, 6, 12, 8), campaignDays[1]);
    }

    [Fact]
    public void PreElection_AcrossDst_KeepsEveryDayAtEight()
    {
        var next = GameCalendar.NextResolutionAt(Local(2026, 3, 27, 8), Constants, 3);
        AssertLocal(Local(2026, 3, 30, 8), next);

        var campaignDays = GameCalendar.CampaignDays(next, Constants, 3);
        AssertLocal(Local(2026, 3, 28, 8), campaignDays[0]);
        AssertLocal(Local(2026, 3, 29, 8), campaignDays[1]);
    }

    [Fact]
    public void CampaignDays_AreEmptyForAOneDaySeason()
    {
        var next = GameCalendar.NextResolutionAt(Local(2026, 6, 10, 8), Constants, 1);
        Assert.Empty(GameCalendar.CampaignDays(next, Constants, 1));
    }

    [Fact]
    public void NextResolution_InTheSpringGap_MovesForwardByTheGap()
    {
        var constants = Constants with { ResolutionTime = "02:30" };
        var next = GameCalendar.NextResolutionAt(Local(2026, 3, 28, 2, 30), constants, 1);
        Assert.Equal(new DateTimeOffset(2026, 3, 29, 1, 30, 0, TimeSpan.Zero), next.ToUniversalTime());
        Assert.Equal(new DateTime(2026, 3, 29, 3, 30, 0), TimeZoneInfo.ConvertTime(next, Zone).DateTime);
    }

    [Fact]
    public void NextResolution_InTheAutumnOverlap_TakesTheFirstOccurrence()
    {
        var constants = Constants with { ResolutionTime = "02:30" };
        var next = GameCalendar.NextResolutionAt(Local(2026, 10, 24, 2, 30), constants, 1);
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero), next.ToUniversalTime());
    }
}
