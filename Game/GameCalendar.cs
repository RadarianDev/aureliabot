public static class GameCalendar
{
    public static (Season Season, int Year) Next(Season season, int year) =>
        season == Season.Winter ? (Season.Spring, year + 1) : (season.Next(), year);

    public static string Label(Season season, int year) => $"{season} {year}";

    public static (Season Season, int Year) Add(Season season, int year, int seasons) =>
        throw new NotImplementedException();

    public static int SeasonsBetween(Season fromSeason, int fromYear, Season toSeason, int toYear) =>
        throw new NotImplementedException();

    public static (Season Season, int Year) PreElectionOfTermStarting(Season termStartSeason, int termStartYear, GameConstants constants) =>
        throw new NotImplementedException();

    public static int SeasonLengthDays(GameState state, GameConstants constants) =>
        throw new NotImplementedException();

    public static DateTimeOffset NextResolutionAt(DateTimeOffset from, GameConstants constants, int seasonLengthDays) =>
        throw new NotImplementedException();

    public static IReadOnlyList<DateTimeOffset> CampaignDays(DateTimeOffset nextResolutionAt, GameConstants constants, int seasonLengthDays) =>
        throw new NotImplementedException();

    public static long ResolutionForTurn(GameState state, Season season, int year) =>
        throw new NotImplementedException();
}
