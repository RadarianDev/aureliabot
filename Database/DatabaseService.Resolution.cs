using System;
using Microsoft.Data.Sqlite;

public partial class DatabaseService
{
    public ResolutionReport Resolve(GameConstants constants, IRandomSource random, int seed, DateTimeOffset now)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();

        var state = GetGameStateTx(conn, tx);
        var report = new ResolutionReport
        {
            ResolutionNumber = state.ResolutionNumber + 1,
            FromSeason = state.Season,
            FromYear = state.Year,
            Seed = seed,
        };

        state = AdvanceSeasonTx(conn, tx, state, report, now);

        SaveGameStateTx(conn, tx, state with { RngSeed = null });
        tx.Commit();
        return report;
    }

    private static GameState AdvanceSeasonTx(SqliteConnection conn, SqliteTransaction tx, GameState state, ResolutionReport report, DateTimeOffset now)
    {
        var (season, year) = GameCalendar.Next(state.Season, state.Year);
        report.ToSeason = season;
        report.ToYear = year;
        return state with
        {
            Season = season,
            Year = year,
            ResolutionNumber = state.ResolutionNumber + 1,
            CampaignDaysRun = 0,
            SeasonLengthDays = null,
            LastResolutionAt = now,
        };
    }
}
