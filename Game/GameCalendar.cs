public static class GameCalendar
{
    private const int SeasonsPerYear = 4;

    public static (Season Season, int Year) Next(Season season, int year) =>
        season == Season.Winter ? (Season.Spring, year + 1) : (season.Next(), year);

    public static string Label(Season season, int year) => $"{season} {year}";

    public static (Season Season, int Year) Add(Season season, int year, int seasons)
    {
        var index = IndexOf(season, year) + seasons;
        return ((Season)Mod(index, SeasonsPerYear), (int)FloorDiv(index, SeasonsPerYear));
    }

    public static int SeasonsBetween(Season fromSeason, int fromYear, Season toSeason, int toYear) =>
        (int)(IndexOf(toSeason, toYear) - IndexOf(fromSeason, fromYear));

    public static (Season Season, int Year) PreElectionOfTermStarting(Season termStartSeason, int termStartYear, GameConstants constants) =>
        Add(termStartSeason, termStartYear, constants.SeasonsPerTerm - 1);

    public static int SeasonLengthDays(GameState state, GameConstants constants) =>
        state.SeasonLengthDays ?? (state.IsPreElection ? constants.PreElectionDays : 1);

    public static DateTimeOffset NextResolutionAt(DateTimeOffset from, GameConstants constants, int seasonLengthDays)
    {
        var zone = constants.Zone;
        var time = constants.ResolutionTimeOfDay;
        var earliest = from.AddHours(constants.MinHoursBetweenResolutions);
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(earliest, zone).DateTime);
        while (AtLocalTime(date, time, zone) < earliest)
            date = date.AddDays(1);
        return AtLocalTime(date.AddDays(Math.Max(seasonLengthDays, 1) - 1), time, zone);
    }

    public static IReadOnlyList<DateTimeOffset> CampaignDays(DateTimeOffset nextResolutionAt, GameConstants constants, int seasonLengthDays)
    {
        var zone = constants.Zone;
        var time = constants.ResolutionTimeOfDay;
        var resolutionDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nextResolutionAt, zone).DateTime);
        return Enumerable.Range(1, Math.Max(seasonLengthDays - 1, 0))
            .Reverse()
            .Select(daysBefore => AtLocalTime(resolutionDate.AddDays(-daysBefore), time, zone))
            .ToList();
    }

    public static long ResolutionForTurn(GameState state, Season season, int year) =>
        state.ResolutionNumber + SeasonsBetween(state.Season, state.Year, season, year);

    private static DateTimeOffset AtLocalTime(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(time);
        var offset = zone.IsInvalidTime(local)
            ? zone.GetUtcOffset(local.AddDays(-1))
            : zone.IsAmbiguousTime(local)
                ? zone.GetAmbiguousTimeOffsets(local).Max()
                : zone.GetUtcOffset(local);
        return TimeZoneInfo.ConvertTime(new DateTimeOffset(local, offset), zone);
    }

    private static long IndexOf(Season season, int year) => (long)year * SeasonsPerYear + (int)season;

    private static long Mod(long value, long divisor) => ((value % divisor) + divisor) % divisor;

    private static long FloorDiv(long value, long divisor) => (value - Mod(value, divisor)) / divisor;
}
