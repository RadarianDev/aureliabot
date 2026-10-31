using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Data.Sqlite;

public enum NavyReaction { Pleased, Indifferent, Insulted }

public sealed record FiscalYear(int Year, double Economy, double TaxRate, decimal Budget, long ComputedResolution);

public sealed record FiscalOpening(
    int Year, decimal Budget, IReadOnlyDictionary<Ministry, decimal> Amounts, decimal ToReserve, bool Submitted,
    double NavyShare, double LastNavyShare, NavyReaction Reaction);

public sealed record AllocationWindow(string? Refusal, int Year, decimal Budget, IReadOnlyDictionary<Ministry, decimal> Prefill);

public sealed record AllocationPreview(
    string? Refusal, int Year, decimal Budget, IReadOnlyDictionary<Ministry, decimal> Amounts, decimal ToReserve,
    double NavyShare, double LastNavyShare, NavyReaction Reaction, bool Replaced = false);

public sealed record GrantResult(string? Refusal, Ministry Ministry, decimal Amount, decimal Balance, decimal Reserve);

public partial class DatabaseService
{
    public static IReadOnlyList<Ministry> Purses { get; } = Enum.GetValues<Ministry>();

    public FiscalYear? GetFiscalYear(int year)
    {
        using var conn = Open();
        return GetFiscalYearTx(conn, null, year);
    }

    public Dictionary<Ministry, decimal> GetAllocation(int year)
    {
        using var conn = Open();
        return GetAllocationTx(conn, null, year);
    }

    public bool IsAllocationSubmitted(int year)
    {
        using var conn = Open();
        return IsAllocationSubmittedTx(conn, null, year);
    }

    public double GrowthRate(GameConfig config)
    {
        using var conn = Open();
        return GrowthRateTx(conn, null, config);
    }

    public AllocationWindow OpenAllocation(ulong factionId)
    {
        using var conn = Open();
        var state = GetGameStateTx(conn, null);
        var year = state.Year + 1;
        if (AllocationRefusalTx(conn, null, state, factionId) is { } refusal)
            return new AllocationWindow(refusal, year, 0, new Dictionary<Ministry, decimal>());

        var budget = GetBudgetTx(conn, null, year);
        var submitted = GetAllocationTx(conn, null, year);
        var prefill = submitted.Count > 0 ? submitted : GetAllocationTx(conn, null, state.Year);
        return new AllocationWindow(null, year, budget, Purses.ToDictionary(m => m, m => prefill.GetValueOrDefault(m)));
    }

    public AllocationPreview PreviewAllocation(ulong factionId, IReadOnlyDictionary<Ministry, decimal> amounts)
    {
        using var conn = Open();
        return PreviewAllocationTx(conn, null, factionId, amounts);
    }

    public AllocationPreview SubmitAllocation(ulong factionId, IReadOnlyDictionary<Ministry, decimal> amounts)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        var preview = PreviewAllocationTx(conn, tx, factionId, amounts);
        if (preview.Refusal is not null) return preview;

        var replaced = IsAllocationSubmittedTx(conn, tx, preview.Year);
        Exec(conn, tx, "DELETE FROM allocations WHERE year = $year;", ("$year", preview.Year));
        foreach (var (ministry, amount) in preview.Amounts)
            Exec(conn, tx, @"
                INSERT INTO allocations (year, ministry, amount, submitted_by, submitted_at)
                VALUES ($year, $ministry, $amount, $by, $now);",
                ("$year", preview.Year), ("$ministry", ministry.ToString()), ("$amount", Money(amount)),
                ("$by", factionId.ToString()), ("$now", Now()));
        LogTx(conn, tx, LogKinds.StateChange, "treasury allocate",
            $"Allocation for {preview.Year} submitted by {factionId}: {string.Join(", ", preview.Amounts.Select(a => $"{a.Key} {Money(a.Value)}"))}.");
        tx.Commit();
        return preview with { Replaced = replaced };
    }

    public GrantResult Grant(ulong factionId, Ministry ministry, decimal amount)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        var state = GetGameStateTx(conn, tx);
        var reserve = GetBalanceTx(conn, tx, LedgerAccount.Reserve);
        GrantResult Refuse(string refusal) => new(refusal, ministry, amount, 0, reserve);

        if (!state.Started) return Refuse(Texts.Get("treasury.refusal.notStarted"));
        if (ResponsibleMinisterTx(conn, tx, Ministry.Treasury) != factionId) return Refuse(Texts.Get("treasury.refusal.notTreasury"));
        if (ministry == Ministry.Navy) return Refuse(Texts.Get("treasury.grant.refusal.navy"));
        amount = Math.Round(amount, 2, MidpointRounding.AwayFromZero);
        if (amount <= 0) return Refuse(Texts.Get("treasury.grant.refusal.amount"));
        if (amount > reserve) return Refuse(Texts.Get("treasury.grant.refusal.tooMuch", ("amount", Display.Crowns(amount)), ("reserve", Display.Crowns(reserve))));

        PostLedgerTx(conn, tx, LedgerAccount.Reserve, -amount, LedgerKind.Grant,
            Texts.Get("treasury.ledger.grantTo", ("ministry", ministry.DisplayName())), "grant", ministry.ToString());
        PostLedgerTx(conn, tx, (LedgerAccount)(int)ministry, amount, LedgerKind.Grant,
            Texts.Get("treasury.ledger.grantFrom"), "grant", ministry.ToString());
        LogTx(conn, tx, LogKinds.StateChange, "treasury grant", $"{factionId} granted {Money(amount)} from the reserve to {ministry}.");
        var result = new GrantResult(null, ministry, amount, GetBalanceTx(conn, tx, (LedgerAccount)(int)ministry), GetBalanceTx(conn, tx, LedgerAccount.Reserve));
        tx.Commit();
        return result;
    }

    public static NavyReaction ReactionTo(double navyShare, double lastNavyShare) =>
        navyShare > lastNavyShare ? NavyReaction.Pleased : navyShare < lastNavyShare ? NavyReaction.Insulted : NavyReaction.Indifferent;

    public static double NavyShareOf(decimal budget, decimal navy, double lastNavyShare)
    {
        if (budget <= 0) return 0;
        return Math.Round(budget * (decimal)lastNavyShare, 2, MidpointRounding.AwayFromZero) == navy ? lastNavyShare : (double)(navy / budget);
    }

    private static void FiscalStepTx(SqliteConnection conn, SqliteTransaction tx, GameState state, GameConfig config, ResolutionReport report)
    {
        if (state.Season == Season.Autumn) BudgetStepTx(conn, tx, state, config, report);
        else if (state.Season == Season.Winter) OpeningStepTx(conn, tx, state, config, report);
    }

    private static void BudgetStepTx(SqliteConnection conn, SqliteTransaction tx, GameState state, GameConfig config, ResolutionReport report)
    {
        var year = state.Year + 1;
        var budget = ComputeBudgetTx(conn, tx, config, year);
        var cabinet = GetSittingCabinetTx(conn, tx);
        var allocator = ResponsibleMinisterTx(conn, tx, Ministry.Treasury);

        if (cabinet?.HolderOf(Ministry.Treasury) is null)
            report.Alerts.Add(allocator is null
                ? Texts.Get("fiscal.alert.noTreasuryNobody", ("year", year), ("budget", Display.Crowns(budget)))
                : Texts.Get("fiscal.alert.noTreasury", ("year", year), ("budget", Display.Crowns(budget))));
        if (allocator is { } minister)
            report.AddPrivate(minister, Texts.Get("fiscal.private.budget", ("year", year), ("budget", Display.Crowns(budget))));
    }

    private static void OpeningStepTx(SqliteConnection conn, SqliteTransaction tx, GameState state, GameConfig config, ResolutionReport report)
    {
        var year = state.Year + 1;
        if (GetFiscalYearTx(conn, tx, year) is null) ComputeBudgetTx(conn, tx, config, year);
        var budget = GetBudgetTx(conn, tx, year);

        var submitted = IsAllocationSubmittedTx(conn, tx, year);
        var amounts = submitted
            ? GetAllocationTx(conn, tx, year)
            : AllocationFromShares(budget, PreviousSharesTx(conn, tx, config, state.Year));
        var opening = OpenFiscalYearTx(conn, tx, config, year, amounts, submitted, report);

        var purses = string.Join(", ", Purses.Where(m => m != Ministry.Navy).Select(m =>
            Texts.Get("fiscal.news.purse", ("ministry", m.DisplayName()), ("amount", Display.Crowns(opening.Amounts[m])))));
        report.News.Add(Texts.Get("fiscal.news.opening", ("year", year), ("budget", Display.Crowns(opening.Budget)), ("purses", purses),
            ("navy", Display.Crowns(opening.Amounts[Ministry.Navy])), ("reserve", Display.Crowns(opening.ToReserve))));
        if (!submitted) report.News.Add(Texts.Get("fiscal.news.repeated"));
        if (opening.Reaction == NavyReaction.Pleased) report.News.Add(Texts.Get("fiscal.news.pleased"));
        if (opening.Reaction == NavyReaction.Insulted) report.News.Add(Texts.Get("fiscal.news.insulted"));
    }

    private static Dictionary<Ministry, double> PreviousSharesTx(SqliteConnection conn, SqliteTransaction tx, GameConfig config, int previousYear)
    {
        var previous = GetAllocationTx(conn, tx, previousYear);
        var previousBudget = GetBudgetTx(conn, tx, previousYear);
        if (previous.Count == 0 || previousBudget <= 0) return new Dictionary<Ministry, double>(config.Constants.DefaultAllocation);

        var shares = Purses.ToDictionary(m => m, m => (double)(previous.GetValueOrDefault(m) / previousBudget));
        shares[Ministry.Navy] = GetGameStateTx(conn, tx).LastNavyShare;
        return shares;
    }

    private static Dictionary<Ministry, decimal> AllocationFromShares(decimal budget, IReadOnlyDictionary<Ministry, double> shares) =>
        Purses.ToDictionary(m => m, m => Math.Round(budget * (decimal)shares.GetValueOrDefault(m), 2,
            m == Ministry.Navy ? MidpointRounding.AwayFromZero : MidpointRounding.ToZero));

    private static Dictionary<Ministry, decimal> FitToBudget(IReadOnlyDictionary<Ministry, decimal> amounts, decimal budget)
    {
        var fitted = Purses.ToDictionary(m => m, m => Math.Max(0, amounts.GetValueOrDefault(m)));
        var total = fitted.Values.Sum();
        if (total <= budget) return fitted;
        var scale = total == 0 ? 0 : budget / total;
        return fitted.ToDictionary(p => p.Key, p => Math.Round(p.Value * scale, 2, MidpointRounding.ToZero));
    }

    private AllocationPreview PreviewAllocationTx(SqliteConnection conn, SqliteTransaction? tx, ulong factionId, IReadOnlyDictionary<Ministry, decimal> amounts)
    {
        var state = GetGameStateTx(conn, tx);
        var year = state.Year + 1;
        var budget = GetBudgetTx(conn, tx, year);
        var rounded = Purses.ToDictionary(m => m, m => Math.Round(amounts.GetValueOrDefault(m), 2, MidpointRounding.AwayFromZero));
        AllocationPreview Refuse(string refusal) => new(refusal, year, budget, rounded, 0, 0, state.LastNavyShare, NavyReaction.Indifferent);

        if (AllocationRefusalTx(conn, tx, state, factionId) is { } refusal) return Refuse(refusal);
        if (rounded.FirstOrDefault(p => p.Value < 0) is { Value: < 0 } negative)
            return Refuse(Texts.Get("treasury.refusal.negative", ("ministry", negative.Key.DisplayName())));
        var total = rounded.Values.Sum();
        if (total > budget)
            return Refuse(Texts.Get("treasury.refusal.overBudget", ("total", Display.Crowns(total)), ("budget", Display.Crowns(budget))));

        var navyShare = NavyShareOf(budget, rounded[Ministry.Navy], state.LastNavyShare);
        return new AllocationPreview(null, year, budget, rounded, budget - total, navyShare, state.LastNavyShare, ReactionTo(navyShare, state.LastNavyShare));
    }

    private static string? AllocationRefusalTx(SqliteConnection conn, SqliteTransaction? tx, GameState state, ulong factionId)
    {
        if (!state.Started) return Texts.Get("treasury.refusal.notStarted");
        if (ResponsibleMinisterTx(conn, tx, Ministry.Treasury) != factionId) return Texts.Get("treasury.refusal.notTreasury");
        if (state.Season != Season.Winter)
        {
            var winter = GameCalendar.Label(Season.Winter, state.Year);
            return state.Season == Season.Autumn
                ? Texts.Get("treasury.refusal.closedSoon", ("winter", winter), ("when", Display.NextResolution(state)))
                : Texts.Get("treasury.refusal.closed", ("winter", winter));
        }
        if (GetFiscalYearTx(conn, tx, state.Year + 1) is null)
            return Texts.Get("treasury.refusal.noBudget", ("year", state.Year + 1));
        return null;
    }

    private static FiscalYear? GetFiscalYearTx(SqliteConnection conn, SqliteTransaction? tx, int year)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT year, economy, tax_rate, budget, computed_resolution FROM fiscal_years WHERE year = $year;";
        cmd.Parameters.AddWithValue("$year", year);
        using var r = cmd.ExecuteReader();
        return r.Read()
            ? new FiscalYear(r.GetInt32(0), r.GetDouble(1), r.GetDouble(2), decimal.Parse(r.GetString(3), CultureInfo.InvariantCulture), r.GetInt64(4))
            : null;
    }

    private static Dictionary<Ministry, decimal> GetAllocationTx(SqliteConnection conn, SqliteTransaction? tx, int year)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT ministry, amount FROM allocations WHERE year = $year;";
        cmd.Parameters.AddWithValue("$year", year);
        var map = new Dictionary<Ministry, decimal>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            map[Enum.Parse<Ministry>(r.GetString(0))] = decimal.Parse(r.GetString(1), CultureInfo.InvariantCulture);
        return map;
    }

    private static bool IsAllocationSubmittedTx(SqliteConnection conn, SqliteTransaction? tx, int year)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM allocations WHERE year = $year AND submitted_by IS NOT NULL;";
        cmd.Parameters.AddWithValue("$year", year);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    private static double GrowthRateTx(SqliteConnection conn, SqliteTransaction? tx, GameConfig config)
    {
        var c = config.Constants;
        var provinces = GetProvincesTx(conn, tx);
        var conditions = GetProvinceConditionsTx(conn, tx);
        var totalPopulation = provinces.Sum(p => p.Population);
        var conditionGrowth = totalPopulation <= 0 ? 0 : provinces.Sum(p =>
            p.Population / totalPopulation * conditions.GetValueOrDefault(p.Id, []).Sum(id => config.Condition(id)?.Growth ?? 0));
        var drag = AverageUnrestTx(conn, tx) ?? UnrestTx(conn, tx, config);
        return c.Economy.Growth + conditionGrowth - c.Discontent.GrowthDrag * drag;
    }

    private static double UnrestTx(SqliteConnection conn, SqliteTransaction? tx, GameConfig config)
    {
        var provinces = GetProvincesTx(conn, tx);
        var totalPopulation = provinces.Sum(p => p.Population);
        return totalPopulation <= 0 ? 0 : provinces.Sum(p =>
            p.Population / totalPopulation * Math.Max(0, p.Discontent - config.Constants.Discontent.Rest));
    }

    private static void SampleUnrestTx(SqliteConnection conn, SqliteTransaction tx, GameState state, GameConfig config) =>
        Exec(conn, tx, "INSERT OR REPLACE INTO unrest_samples (resolution_number, unrest) VALUES ($resolution, $unrest);",
            ("$resolution", state.ResolutionNumber), ("$unrest", UnrestTx(conn, tx, config)));

    private static double? AverageUnrestTx(SqliteConnection conn, SqliteTransaction? tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT AVG(unrest) FROM unrest_samples;";
        return cmd.ExecuteScalar() is double average ? average : null;
    }

    private static decimal ComputeBudgetTx(SqliteConnection conn, SqliteTransaction tx, GameConfig config, int fiscalYear)
    {
        var state = GetGameStateTx(conn, tx);
        var economy = state.Economy * (1 + GrowthRateTx(conn, tx, config));
        SetStateTx(conn, tx, "economy", economy);
        Exec(conn, tx, "DELETE FROM unrest_samples;");
        var budget = Math.Round((decimal)(economy * state.TaxRate), 2, MidpointRounding.AwayFromZero);
        Exec(conn, tx, @"
            INSERT INTO fiscal_years (year, economy, tax_rate, budget, computed_resolution) VALUES ($year, $economy, $tax, $budget, $resolution)
            ON CONFLICT(year) DO UPDATE SET economy = excluded.economy, tax_rate = excluded.tax_rate, budget = excluded.budget,
                computed_resolution = excluded.computed_resolution;",
            ("$year", fiscalYear), ("$economy", economy), ("$tax", state.TaxRate), ("$budget", Money(budget)), ("$resolution", state.ResolutionNumber));
        return budget;
    }

    private static decimal GetBudgetTx(SqliteConnection conn, SqliteTransaction? tx, int fiscalYear) =>
        GetFiscalYearTx(conn, tx, fiscalYear)?.Budget ?? 0m;

    private static FiscalOpening OpenFiscalYearTx(
        SqliteConnection conn, SqliteTransaction tx, GameConfig config, int fiscalYear, IReadOnlyDictionary<Ministry, double> shares, ResolutionReport report) =>
        OpenFiscalYearTx(conn, tx, config, fiscalYear, AllocationFromShares(GetBudgetTx(conn, tx, fiscalYear), shares), false, report);

    private static FiscalOpening OpenFiscalYearTx(
        SqliteConnection conn, SqliteTransaction tx, GameConfig config, int fiscalYear, IReadOnlyDictionary<Ministry, decimal> requested, bool submitted,
        ResolutionReport report)
    {
        var c = config.Constants;
        var budget = GetBudgetTx(conn, tx, fiscalYear);
        var source = fiscalYear.ToString(CultureInfo.InvariantCulture);

        foreach (var ministry in Purses.Where(m => m != Ministry.Navy))
        {
            var leftover = GetBalanceTx(conn, tx, (LedgerAccount)(int)ministry);
            if (leftover > 0)
            {
                PostLedgerTx(conn, tx, (LedgerAccount)(int)ministry, -leftover, LedgerKind.CarryOver, Texts.Get("data.ledger.unspentToReserve", ("year", fiscalYear - 1)), "fiscal", source, true);
                PostLedgerTx(conn, tx, LedgerAccount.Reserve, leftover, LedgerKind.CarryOver, Texts.Get("data.ledger.unspentFrom", ("year", fiscalYear - 1), ("ministry", ministry.DisplayName())), "fiscal", source, true);
            }
        }

        var reserve = GetBalanceTx(conn, tx, LedgerAccount.Reserve);
        PostLedgerTx(conn, tx, LedgerAccount.Reserve, reserve * (decimal)c.Reserve.Interest, LedgerKind.Interest, Texts.Get("data.ledger.interest", ("year", fiscalYear)), "fiscal", source, true);

        var amounts = FitToBudget(requested, budget);
        foreach (var (ministry, amount) in amounts)
        {
            Exec(conn, tx, "UPDATE purses SET allocated_this_year = $amount WHERE ministry = $ministry;",
                ("$amount", Money(amount)), ("$ministry", ministry.ToString()));
            Exec(conn, tx, @"
                INSERT INTO allocations (year, ministry, amount, submitted_by, submitted_at) VALUES ($year, $ministry, $amount, NULL, $now)
                ON CONFLICT(year, ministry) DO UPDATE SET amount = excluded.amount;",
                ("$year", fiscalYear), ("$ministry", ministry.ToString()), ("$amount", Money(amount)), ("$now", Now()));
            PostLedgerTx(conn, tx, (LedgerAccount)(int)ministry, amount, LedgerKind.Allocation, Texts.Get("data.ledger.allocation", ("year", fiscalYear)), "fiscal", source, true);
            if (ministry == Ministry.Navy)
                PostLedgerTx(conn, tx, LedgerAccount.Navy, -amount, LedgerKind.Allocation, Texts.Get("data.ledger.navyContribution"), "fiscal", source, true);
        }
        var toReserve = budget - amounts.Values.Sum();
        if (toReserve > 0)
            PostLedgerTx(conn, tx, LedgerAccount.Reserve, toReserve, LedgerKind.Allocation, Texts.Get("data.ledger.unallocated", ("year", fiscalYear)), "fiscal", source, true);

        PayUpkeepTx(conn, tx, config, fiscalYear, report);

        var state = GetGameStateTx(conn, tx);
        var navyShare = NavyShareOf(budget, amounts[Ministry.Navy], state.LastNavyShare);
        if (navyShare != state.LastNavyShare)
        {
            var shift = (navyShare - state.LastNavyShare) * c.Favour.NavyFactor;
            SetStateTx(conn, tx, "favour", Math.Clamp(state.Favour + shift, 0, 100));
            SetStateTx(conn, tx, "favour_baseline", Math.Clamp(state.FavourBaseline + shift, 0, 100));
            SetStateTx(conn, tx, "last_navy_share", navyShare);
        }
        return new FiscalOpening(fiscalYear, budget, amounts, Math.Max(0, toReserve), submitted, navyShare, state.LastNavyShare,
            ReactionTo(navyShare, state.LastNavyShare));
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
                Texts.Get("data.ledger.upkeep", ("year", fiscalYear), ("count", units.Count), ("type", type.ToString().ToLowerInvariant())), "fiscal", fiscalYear.ToString(CultureInfo.InvariantCulture), true);
            if (balance >= due) continue;

            foreach (var unit in units)
                Exec(conn, tx, "UPDATE units SET sides = MAX($base, sides - $step) WHERE id = $id;",
                    ("$base", constants.BaseSides), ("$step", constants.SidesStep), ("$id", unit.Id));
            report.Alerts.Add(Texts.Get("alerts.upkeepShort", ("ministry", ministry.DisplayName()), ("due", Display.Crowns(due)), ("type", type.ToString().ToLowerInvariant())));
        }
    }
}
