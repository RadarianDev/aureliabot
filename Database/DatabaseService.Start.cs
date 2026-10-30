using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

public sealed record StartResult(string? Refusal, ResolutionReport? Report);

public partial class DatabaseService
{
    public StartResult StartGame(GameConfig config, DateTimeOffset now)
    {
        var c = config.Constants;
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        var state = GetGameStateTx(conn, tx);
        if (state.Started) return new StartResult(Texts.Get("data.start.alreadyStarted"), null);

        Exec(conn, tx, "DELETE FROM province_conditions;");
        Exec(conn, tx, "DELETE FROM provinces;");
        foreach (var province in config.Provinces)
        {
            Exec(conn, tx, "INSERT INTO provinces (id, name, population, discontent) VALUES ($id, $name, $population, $discontent);",
                ("$id", province.Id), ("$name", province.Name), ("$population", province.Population), ("$discontent", c.Discontent.Start));
            foreach (var condition in province.Conditions)
                Exec(conn, tx, @"
                    INSERT INTO province_conditions (province_id, condition_id, since_season, since_year)
                    VALUES ($province, $condition, $season, $year);",
                    ("$province", province.Id), ("$condition", condition), ("$season", c.Start.Season.ToString()), ("$year", c.Start.Year));
        }

        var started = state with
        {
            Started = true,
            Season = c.Start.Season,
            Year = c.Start.Year,
            TermNumber = 0,
            TermStartSeason = c.Start.Season,
            TermStartYear = c.Start.Year,
            PreElectionSeason = c.Start.Season,
            PreElectionYear = c.Start.Year,
            ResolutionNumber = 0,
            CampaignDaysRun = 0,
            LastResolutionAt = now,
            SeasonLengthDays = null,
            Paused = false,
            Seats = c.Seats,
            TaxRate = c.TaxRate,
            Economy = c.Economy.Start,
            Favour = c.Favour.Start,
            FavourBaseline = c.Favour.Baseline,
            LastNavyShare = c.DefaultAllocation[Ministry.Navy],
            Threat = c.Threat.Start,
            Reserve = 0,
            RngSeed = null,
            NoConfidenceCooldownUntil = null,
        };
        started = started with { NextResolutionAt = GameCalendar.NextResolutionAt(now, c, GameCalendar.SeasonLengthDays(started, c)) };
        SaveGameStateTx(conn, tx, started);

        Exec(conn, tx, "DELETE FROM units;");
        foreach (var (provinceId, count) in c.StartingForces.Divisions)
            for (var i = 0; i < count; i++)
            {
                var number = GetUnitsTx(conn, tx, UnitType.Division).Count + 1;
                InsertUnitTx(conn, tx, UnitType.Division,
                    Texts.Get("data.unit.division", ("ordinal", Ordinal(number)), ("province", config.Province(provinceId)!.Name)), c.Division.BaseSides, provinceId);
            }
        foreach (var provinceId in c.StartingForces.Gendarmerie)
            InsertUnitTx(conn, tx, UnitType.Gendarmerie,
                Texts.Get("data.unit.gendarmerie", ("province", config.Province(provinceId)!.Name)), c.Gendarmerie.BaseSides, provinceId);

        var report = new ResolutionReport
        {
            ResolutionNumber = 0,
            FromSeason = c.Start.Season,
            FromYear = c.Start.Year,
            ToSeason = c.Start.Season,
            ToYear = c.Start.Year,
            PreElectionOpened = true,
        };

        PostLedgerTx(conn, tx, LedgerAccount.Reserve, c.Reserve.Start, LedgerKind.CarryOver, Texts.Get("data.ledger.openingReserve"), "start", null, publicNow: true);
        ComputeBudgetTx(conn, tx, config, c.Start.Year);
        OpenFiscalYearTx(conn, tx, config, c.Start.Year, c.DefaultAllocation, report);

        FireDueEventsTx(conn, tx, config, report);
        LogTx(conn, tx, LogKinds.StateChange, "gm start", $"Started the game in {GameCalendar.Label(c.Start.Season, c.Start.Year)}.");
        tx.Commit();
        return new StartResult(null, report);
    }
}
