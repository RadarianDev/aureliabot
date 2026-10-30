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

        if (state.IsPreElection)
            DropPendingJoinRequestsTx(conn, tx);

        state = AdvanceSeasonTx(conn, tx, state, constants, report, now);

        SaveGameStateTx(conn, tx, state with { RngSeed = null });
        LogTx(conn, tx, LogKinds.Resolution, "resolution", $"Resolved {GameCalendar.Label(report.FromSeason, report.FromYear)} with seed {seed}.");
        tx.Commit();
        return report;
    }

    public ResolutionReport ResolveCampaignDay(GameConstants constants, IRandomSource random, int seed, DateTimeOffset now)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();

        var state = GetGameStateTx(conn, tx);
        var report = new ResolutionReport
        {
            ResolutionNumber = state.ResolutionNumber,
            FromSeason = state.Season,
            FromYear = state.Year,
            ToSeason = state.Season,
            ToYear = state.Year,
            Seed = seed,
            CampaignDay = true,
        };

        SaveGameStateTx(conn, tx, state with { CampaignDaysRun = state.CampaignDaysRun + 1, RngSeed = null });
        LogTx(conn, tx, LogKinds.Resolution, "campaign-day", $"Campaign day {state.CampaignDaysRun + 1} of {state.Label} with seed {seed}.");
        tx.Commit();
        return report;
    }

    private static GameState AdvanceSeasonTx(SqliteConnection conn, SqliteTransaction tx, GameState state, GameConstants constants, ResolutionReport report, DateTimeOffset now)
    {
        var (season, year) = GameCalendar.Next(state.Season, state.Year);
        report.ToSeason = season;
        report.ToYear = year;
        var advanced = state with
        {
            Season = season,
            Year = year,
            ResolutionNumber = state.ResolutionNumber + 1,
            CampaignDaysRun = 0,
            SeasonLengthDays = null,
            LastResolutionAt = now,
        };
        report.PreElectionOpened = advanced.IsPreElection;
        return advanced with
        {
            NextResolutionAt = GameCalendar.NextResolutionAt(now, constants, GameCalendar.SeasonLengthDays(advanced, constants)),
        };
    }
}
