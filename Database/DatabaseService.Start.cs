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

    private static double GrowthRateTx(SqliteConnection conn, SqliteTransaction tx, GameConfig config)
    {
        var c = config.Constants;
        var provinces = GetProvincesTx(conn, tx);
        var conditions = GetProvinceConditionsTx(conn, tx);
        var conditionGrowth = conditions.Values.SelectMany(ids => ids).Sum(id => config.Condition(id)?.Growth ?? 0);
        var totalPopulation = provinces.Sum(p => p.Population);
        var drag = totalPopulation <= 0 ? 0 : provinces.Sum(p =>
            p.Population / totalPopulation * Math.Max(0, p.Discontent - c.Discontent.Rest));
        return c.Economy.Growth + conditionGrowth - c.Discontent.GrowthDrag * drag;
    }

    private static decimal ComputeBudgetTx(SqliteConnection conn, SqliteTransaction tx, GameConfig config, int fiscalYear)
    {
        var state = GetGameStateTx(conn, tx);
        var economy = state.Economy * (1 + GrowthRateTx(conn, tx, config));
        SetStateTx(conn, tx, "economy", economy);
        var budget = Math.Round((decimal)(economy * state.TaxRate), 2, MidpointRounding.AwayFromZero);
        Exec(conn, tx, @"
            INSERT INTO fiscal_years (year, economy, tax_rate, budget, computed_resolution) VALUES ($year, $economy, $tax, $budget, $resolution)
            ON CONFLICT(year) DO UPDATE SET economy = excluded.economy, tax_rate = excluded.tax_rate, budget = excluded.budget,
                computed_resolution = excluded.computed_resolution;",
            ("$year", fiscalYear), ("$economy", economy), ("$tax", state.TaxRate), ("$budget", Money(budget)), ("$resolution", state.ResolutionNumber));
        return budget;
    }

    private static decimal GetBudgetTx(SqliteConnection conn, SqliteTransaction tx, int fiscalYear)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT budget FROM fiscal_years WHERE year = $year;";
        cmd.Parameters.AddWithValue("$year", fiscalYear);
        return cmd.ExecuteScalar() is string text ? decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture) : 0m;
    }

    private static void OpenFiscalYearTx(
        SqliteConnection conn, SqliteTransaction tx, GameConfig config, int fiscalYear, IReadOnlyDictionary<Ministry, double> shares, ResolutionReport report)
    {
        var c = config.Constants;
        var budget = GetBudgetTx(conn, tx, fiscalYear);

        foreach (var ministry in Enum.GetValues<Ministry>().Where(m => m != Ministry.Navy))
        {
            var leftover = GetBalanceTx(conn, tx, (LedgerAccount)(int)ministry);
            if (leftover > 0)
            {
                PostLedgerTx(conn, tx, (LedgerAccount)(int)ministry, -leftover, LedgerKind.CarryOver, Texts.Get("data.ledger.unspentToReserve", ("year", fiscalYear - 1)), "fiscal", fiscalYear.ToString(), true);
                PostLedgerTx(conn, tx, LedgerAccount.Reserve, leftover, LedgerKind.CarryOver, Texts.Get("data.ledger.unspentFrom", ("year", fiscalYear - 1), ("ministry", ministry.DisplayName())), "fiscal", fiscalYear.ToString(), true);
            }
        }

        var reserve = GetBalanceTx(conn, tx, LedgerAccount.Reserve);
        PostLedgerTx(conn, tx, LedgerAccount.Reserve, reserve * (decimal)c.Reserve.Interest, LedgerKind.Interest, Texts.Get("data.ledger.interest", ("year", fiscalYear)), "fiscal", fiscalYear.ToString(), true);

        var allocated = 0m;
        foreach (var ministry in Enum.GetValues<Ministry>())
        {
            var amount = Math.Round(budget * (decimal)shares.GetValueOrDefault(ministry), 2, MidpointRounding.AwayFromZero);
            allocated += amount;
            Exec(conn, tx, "UPDATE purses SET allocated_this_year = $amount WHERE ministry = $ministry;",
                ("$amount", Money(amount)), ("$ministry", ministry.ToString()));
            Exec(conn, tx, @"
                INSERT INTO allocations (year, ministry, amount, submitted_by, submitted_at) VALUES ($year, $ministry, $amount, NULL, $now)
                ON CONFLICT(year, ministry) DO NOTHING;",
                ("$year", fiscalYear), ("$ministry", ministry.ToString()), ("$amount", Money(amount)), ("$now", Now()));
            PostLedgerTx(conn, tx, (LedgerAccount)(int)ministry, amount, LedgerKind.Allocation, Texts.Get("data.ledger.allocation", ("year", fiscalYear)), "fiscal", fiscalYear.ToString(), true);
            if (ministry == Ministry.Navy)
                PostLedgerTx(conn, tx, LedgerAccount.Navy, -amount, LedgerKind.Allocation, Texts.Get("data.ledger.navyContribution"), "fiscal", fiscalYear.ToString(), true);
        }
        if (budget - allocated > 0)
            PostLedgerTx(conn, tx, LedgerAccount.Reserve, budget - allocated, LedgerKind.Allocation, Texts.Get("data.ledger.unallocated", ("year", fiscalYear)), "fiscal", fiscalYear.ToString(), true);

        PayUpkeepTx(conn, tx, config, fiscalYear, report);

        var state = GetGameStateTx(conn, tx);
        var navyShare = shares.GetValueOrDefault(Ministry.Navy);
        SetStateTx(conn, tx, "favour", Math.Clamp(state.Favour + (navyShare - state.LastNavyShare) * c.Favour.NavyFactor, 0, 100));
        SetStateTx(conn, tx, "last_navy_share", navyShare);
    }

    private static void PayUpkeepTx(SqliteConnection conn, SqliteTransaction tx, GameConfig config, int fiscalYear, ResolutionReport report)
    {
        foreach (var type in Enum.GetValues<UnitType>())
        {
            var units = GetUnitsTx(conn, tx, type);
            if (units.Count == 0) continue;
            var constants = config.Constants.UnitsOf(type);
            var ministry = type.MinistryOf();
            var due = constants.Upkeep * units.Count;
            var balance = GetBalanceTx(conn, tx, (LedgerAccount)(int)ministry);
            PostLedgerTx(conn, tx, (LedgerAccount)(int)ministry, -due, LedgerKind.Upkeep,
                Texts.Get("data.ledger.upkeep", ("year", fiscalYear), ("count", units.Count), ("type", type.ToString().ToLowerInvariant())), "fiscal", fiscalYear.ToString(), true);
            if (balance >= due) continue;

            foreach (var unit in units)
                Exec(conn, tx, "UPDATE units SET sides = MAX($base, sides - $step) WHERE id = $id;",
                    ("$base", constants.BaseSides), ("$step", constants.SidesStep), ("$id", unit.Id));
            report.Alerts.Add(Texts.Get("alerts.upkeepShort", ("ministry", ministry.DisplayName()), ("due", Display.Crowns(due)), ("type", type.ToString().ToLowerInvariant())));
        }
    }
}
