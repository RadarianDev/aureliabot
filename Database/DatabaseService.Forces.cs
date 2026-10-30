using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Data.Sqlite;

public record Unit(long Id, UnitType Type, string Name, int Sides, string ProvinceId);

public record PendingOrder(
    long Id, UnitType? UnitType, long? UnitId, OrderKind Kind, string? ProvinceId, string? Name, decimal Cost, ulong? OrderedBy,
    long ResolutionNumber, int? Sides);

public sealed record OrderResult(string? Refusal, PendingOrder? Order = null, decimal Balance = 0, bool Replaced = false, bool Cancelled = false);

public sealed record DisbandResult(string? Refusal, Unit? Unit = null, int CancelledOrders = 0);

public partial class DatabaseService
{
    public const string LawlessCondition = "lawless";
    public const int UnitNameLimit = 60;

    private const string SelectUnitColumns = "id, type, name, sides, province_id";

    private const string SelectPendingOrderColumns =
        "o.id, o.unit_type, o.unit_id, o.kind, o.province_id, COALESCE(u.name, o.name), o.cost, o.ordered_by, o.resolution_number, u.sides";

    private const string PendingOrderSource = "pending_orders o LEFT JOIN units u ON u.id = o.unit_id";

    private static Unit ReadUnit(SqliteDataReader r) =>
        new(r.GetInt64(0), Enum.Parse<UnitType>(r.GetString(1)), r.GetString(2), r.GetInt32(3), r.GetString(4));

    private static PendingOrder ReadPendingOrder(SqliteDataReader r) => new(
        r.GetInt64(0),
        r.IsDBNull(1) ? null : Enum.Parse<UnitType>(r.GetString(1)),
        r.IsDBNull(2) ? null : r.GetInt64(2),
        Enum.Parse<OrderKind>(r.GetString(3)),
        r.IsDBNull(4) ? null : r.GetString(4),
        r.IsDBNull(5) ? null : r.GetString(5),
        decimal.Parse(r.GetString(6), CultureInfo.InvariantCulture),
        r.IsDBNull(7) ? null : ulong.Parse(r.GetString(7)),
        r.GetInt64(8),
        r.IsDBNull(9) ? null : r.GetInt32(9));

    public static Ministry MinistryOf(UnitType type) => type == UnitType.Division ? Ministry.War : Ministry.Interior;

    public static int CrackdownDieSides(double discontent) =>
        Math.Max(2, (int)Math.Round(2 * discontent, MidpointRounding.AwayFromZero));

    public List<Unit> GetUnits(UnitType? type = null)
    {
        using var conn = Open();
        return GetUnitsTx(conn, null, type);
    }

    public Unit? GetUnit(long id)
    {
        using var conn = Open();
        return GetUnitTx(conn, null, id);
    }

    private static List<Unit> GetUnitsTx(SqliteConnection conn, SqliteTransaction? tx, UnitType? type)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT {SelectUnitColumns} FROM units WHERE ($type IS NULL OR type = $type) ORDER BY type, id;";
        cmd.Parameters.AddWithValue("$type", (object?)type?.ToString() ?? DBNull.Value);
        var list = new List<Unit>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(ReadUnit(r));
        return list;
    }

    private static Unit? GetUnitTx(SqliteConnection conn, SqliteTransaction? tx, long id)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT {SelectUnitColumns} FROM units WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadUnit(r) : null;
    }

    private static long InsertUnitTx(SqliteConnection conn, SqliteTransaction tx, UnitType type, string name, int sides, string provinceId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO units (type, name, sides, province_id, raised_at) VALUES ($type, $name, $sides, $province, $now)
            RETURNING id;";
        cmd.Parameters.AddWithValue("$type", type.ToString());
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$sides", sides);
        cmd.Parameters.AddWithValue("$province", provinceId);
        cmd.Parameters.AddWithValue("$now", Now());
        return (long)cmd.ExecuteScalar()!;
    }

    public static string Ordinal(int n) => (n % 100) switch
    {
        11 or 12 or 13 => Texts.Get("data.ordinal.th", ("n", n)),
        _ => (n % 10) switch
        {
            1 => Texts.Get("data.ordinal.st", ("n", n)),
            2 => Texts.Get("data.ordinal.nd", ("n", n)),
            3 => Texts.Get("data.ordinal.rd", ("n", n)),
            _ => Texts.Get("data.ordinal.th", ("n", n)),
        },
    };

    public List<PendingOrder> GetPendingOrders(Ministry ministry)
    {
        using var conn = Open();
        return GetPendingOrdersTx(conn, null).Where(o => OrderMinistry(o) == ministry).ToList();
    }

    public List<PendingOrder> GetPendingOrders()
    {
        using var conn = Open();
        return GetPendingOrdersTx(conn, null);
    }

    public static Ministry OrderMinistry(PendingOrder order) =>
        order.UnitType is { } type ? MinistryOf(type) : Ministry.Interior;

    private static List<PendingOrder> GetPendingOrdersTx(SqliteConnection conn, SqliteTransaction? tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT {SelectPendingOrderColumns} FROM {PendingOrderSource} ORDER BY o.id;";
        var list = new List<PendingOrder>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(ReadPendingOrder(r));
        return list;
    }

    private static PendingOrder? GetPendingOrderTx(SqliteConnection conn, SqliteTransaction? tx, long id) =>
        GetPendingOrdersTx(conn, tx).FirstOrDefault(o => o.Id == id);

    private static long InsertOrderTx(
        SqliteConnection conn, SqliteTransaction tx, GameState state, UnitType? type, long? unitId, OrderKind kind, string? provinceId,
        string? name, decimal cost, ulong factionId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO pending_orders (unit_type, unit_id, kind, province_id, name, cost, ordered_by, ordered_at, resolution_number)
            VALUES ($type, $unit, $kind, $province, $name, $cost, $by, $now, $resolution)
            RETURNING id;";
        cmd.Parameters.AddWithValue("$type", (object?)type?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$unit", (object?)unitId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$kind", kind.ToString());
        cmd.Parameters.AddWithValue("$province", (object?)provinceId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$name", (object?)name ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$cost", Money(cost));
        cmd.Parameters.AddWithValue("$by", factionId.ToString());
        cmd.Parameters.AddWithValue("$now", Now());
        cmd.Parameters.AddWithValue("$resolution", state.ResolutionNumber);
        return (long)cmd.ExecuteScalar()!;
    }

    public string? ForcesRefusal(Ministry ministry, ulong factionId)
    {
        using var conn = Open();
        return ForcesRefusalTx(conn, null, ministry, factionId);
    }

    private static string? ForcesRefusalTx(SqliteConnection conn, SqliteTransaction? tx, Ministry ministry, ulong factionId)
    {
        if (!GetGameStateTx(conn, tx).Started) return Texts.Get("forces.refusal.notStarted");
        var responsible = ResponsibleMinisterTx(conn, tx, ministry);
        if (responsible == factionId) return null;
        return Texts.Get("forces.refusal.notMinister",
            ("ministry", ministry.DisplayName()),
            ("holder", responsible is { } holder ? $"<@{holder}>" : Texts.Get("cabinet.vacant")));
    }

    private static string? AffordTx(SqliteConnection conn, SqliteTransaction tx, Ministry ministry, decimal cost, out decimal balance)
    {
        balance = GetBalanceTx(conn, tx, (LedgerAccount)(int)ministry);
        return balance >= cost
            ? null
            : Texts.Get("forces.refusal.cannotPay", ("ministry", ministry.DisplayName()),
                ("cost", Display.Crowns(cost)), ("balance", Display.Crowns(balance)));
    }

    private static HashSet<string> TakenUnitNamesTx(SqliteConnection conn, SqliteTransaction? tx, UnitType type)
    {
        var taken = GetUnitsTx(conn, tx, type).Select(u => u.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var order in GetPendingOrdersTx(conn, tx).Where(o => o.Kind == OrderKind.Raise && o.UnitType == type && o.Name is not null))
            taken.Add(order.Name!);
        return taken;
    }

    public OrderResult OrderRaise(UnitType type, ulong factionId, string name, string provinceId, GameConfig config)
    {
        name = name.Trim();
        var ministry = MinistryOf(type);
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        if (ForcesRefusalTx(conn, tx, ministry, factionId) is { } refusal) return new OrderResult(refusal);
        if (name.Length == 0) return new OrderResult(Texts.Get("forces.refusal.noName"));
        if (name.Length > UnitNameLimit) return new OrderResult(Texts.Get("forces.refusal.longName", ("limit", UnitNameLimit)));
        if (config.Province(provinceId) is null) return new OrderResult(Texts.Get("forces.refusal.noProvince", ("province", provinceId)));
        if (TakenUnitNamesTx(conn, tx, type).Contains(name)) return new OrderResult(Texts.Get("forces.refusal.nameTaken", ("name", name)));

        var cost = config.Constants.UnitsOf(type).Raise;
        if (AffordTx(conn, tx, ministry, cost, out _) is { } cannot) return new OrderResult(cannot);

        var state = GetGameStateTx(conn, tx);
        var id = InsertOrderTx(conn, tx, state, type, null, OrderKind.Raise, provinceId, name, cost, factionId);
        PostLedgerTx(conn, tx, (LedgerAccount)(int)ministry, -cost, LedgerKind.Purchase,
            Texts.Get("forces.ledger.raise", ("name", name)), "order", id.ToString());
        LogTx(conn, tx, LogKinds.StateChange, "forces raise", $"{factionId} ordered {type} '{name}' raised in {provinceId}.", factionId);
        return Commit(conn, tx, ministry, id);
    }

    public OrderResult OrderTrain(UnitType type, ulong factionId, long unitId, GameConfig config)
    {
        var ministry = MinistryOf(type);
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        if (ForcesRefusalTx(conn, tx, ministry, factionId) is { } refusal) return new OrderResult(refusal);
        if (GetUnitTx(conn, tx, unitId) is not { } unit || unit.Type != type) return new OrderResult(Texts.Get("forces.refusal.noUnit"));

        var constants = config.Constants.UnitsOf(type);
        if (constants.NextTrainCost(unit.Sides) is not { } cost)
            return new OrderResult(Texts.Get("forces.refusal.maxSides", ("name", unit.Name), ("sides", unit.Sides)));
        if (GetPendingOrdersTx(conn, tx).Any(o => o.UnitId == unitId && o.Kind == OrderKind.Train))
            return new OrderResult(Texts.Get("forces.refusal.trainPending", ("name", unit.Name)));
        if (AffordTx(conn, tx, ministry, cost, out _) is { } cannot) return new OrderResult(cannot);

        var state = GetGameStateTx(conn, tx);
        var id = InsertOrderTx(conn, tx, state, type, unitId, OrderKind.Train, null, null, cost, factionId);
        PostLedgerTx(conn, tx, (LedgerAccount)(int)ministry, -cost, LedgerKind.Purchase,
            Texts.Get("forces.ledger.train", ("name", unit.Name), ("sides", Math.Min(constants.MaxSides, unit.Sides + constants.SidesStep))),
            "order", id.ToString());
        LogTx(conn, tx, LogKinds.StateChange, "forces train", $"{factionId} ordered unit {unitId} trained.", factionId);
        return Commit(conn, tx, ministry, id);
    }

    public OrderResult OrderStation(UnitType type, ulong factionId, long unitId, string provinceId, GameConfig config)
    {
        var ministry = MinistryOf(type);
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        if (ForcesRefusalTx(conn, tx, ministry, factionId) is { } refusal) return new OrderResult(refusal);
        if (GetUnitTx(conn, tx, unitId) is not { } unit || unit.Type != type) return new OrderResult(Texts.Get("forces.refusal.noUnit"));
        if (config.Province(provinceId) is null) return new OrderResult(Texts.Get("forces.refusal.noProvince", ("province", provinceId)));

        var pending = GetPendingOrdersTx(conn, tx).FirstOrDefault(o => o.UnitId == unitId && o.Kind == OrderKind.Station);
        if (unit.ProvinceId == provinceId)
        {
            if (pending is null)
                return new OrderResult(Texts.Get("forces.refusal.alreadyThere", ("name", unit.Name), ("province", config.Province(provinceId)!.Name)));
            Exec(conn, tx, "DELETE FROM pending_orders WHERE id = $id;", ("$id", pending.Id));
            LogTx(conn, tx, LogKinds.StateChange, "forces station", $"{factionId} cancelled the stationing of unit {unitId}.", factionId);
            tx.Commit();
            return new OrderResult(null, pending, Cancelled: true);
        }

        if (pending is not null)
        {
            Exec(conn, tx, "UPDATE pending_orders SET province_id = $province, ordered_by = $by, ordered_at = $now WHERE id = $id;",
                ("$id", pending.Id), ("$province", provinceId), ("$by", factionId.ToString()), ("$now", Now()));
            LogTx(conn, tx, LogKinds.StateChange, "forces station", $"{factionId} re-stationed unit {unitId} to {provinceId}.", factionId);
            return Commit(conn, tx, ministry, pending.Id) with { Replaced = true };
        }

        var id = InsertOrderTx(conn, tx, GetGameStateTx(conn, tx), type, unitId, OrderKind.Station, provinceId, null, 0, factionId);
        LogTx(conn, tx, LogKinds.StateChange, "forces station", $"{factionId} ordered unit {unitId} to {provinceId}.", factionId);
        return Commit(conn, tx, ministry, id);
    }

    public OrderResult OrderCrackdown(ulong factionId, string provinceId, GameConfig config)
    {
        const Ministry ministry = Ministry.Interior;
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        if (ForcesRefusalTx(conn, tx, ministry, factionId) is { } refusal) return new OrderResult(refusal);
        if (config.Province(provinceId) is not { } province) return new OrderResult(Texts.Get("forces.refusal.noProvince", ("province", provinceId)));
        if (!GetUnitsTx(conn, tx, UnitType.Gendarmerie).Any(u => u.ProvinceId == provinceId))
            return new OrderResult(Texts.Get("crackdown.refusal.noGendarmerie", ("province", province.Name)));
        if (GetPendingOrdersTx(conn, tx).Any(o => o.Kind == OrderKind.Crackdown && o.ProvinceId == provinceId))
            return new OrderResult(Texts.Get("crackdown.refusal.already", ("province", province.Name), ("when", Display.NextResolution(GetGameStateTx(conn, tx)))));

        var cost = config.Constants.Crackdown.Cost;
        if (AffordTx(conn, tx, ministry, cost, out _) is { } cannot) return new OrderResult(cannot);

        var id = InsertOrderTx(conn, tx, GetGameStateTx(conn, tx), null, null, OrderKind.Crackdown, provinceId, null, cost, factionId);
        PostLedgerTx(conn, tx, LedgerAccount.Interior, -cost, LedgerKind.Crackdown,
            Texts.Get("crackdown.ledger", ("province", province.Name)), "order", id.ToString());
        LogTx(conn, tx, LogKinds.StateChange, "crackdown", $"{factionId} ordered a crackdown in {provinceId}.", factionId);
        return Commit(conn, tx, ministry, id);
    }

    private static OrderResult Commit(SqliteConnection conn, SqliteTransaction tx, Ministry ministry, long orderId)
    {
        var order = GetPendingOrderTx(conn, tx, orderId);
        var balance = GetBalanceTx(conn, tx, (LedgerAccount)(int)ministry);
        tx.Commit();
        return new OrderResult(null, order, balance);
    }

    public DisbandResult Disband(UnitType type, ulong factionId, long unitId)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        if (ForcesRefusalTx(conn, tx, MinistryOf(type), factionId) is { } refusal) return new DisbandResult(refusal);
        if (GetUnitTx(conn, tx, unitId) is not { } unit || unit.Type != type) return new DisbandResult(Texts.Get("forces.refusal.noUnit"));

        var cancelled = GetPendingOrdersTx(conn, tx).Count(o => o.UnitId == unitId);
        Exec(conn, tx, "DELETE FROM pending_orders WHERE unit_id = $id;", ("$id", unitId));
        Exec(conn, tx, "DELETE FROM units WHERE id = $id;", ("$id", unitId));
        LogTx(conn, tx, LogKinds.StateChange, "forces disband", $"{factionId} disbanded {type} '{unit.Name}'.", factionId);
        tx.Commit();
        return new DisbandResult(null, unit, cancelled);
    }

    private static string GeneratedUnitNameTx(SqliteConnection conn, SqliteTransaction tx, GameConfig config, UnitType type, string provinceId)
    {
        var taken = TakenUnitNamesTx(conn, tx, type);
        var province = config.Province(provinceId)?.Name ?? provinceId;
        if (type == UnitType.Gendarmerie)
        {
            var plain = Texts.Get("data.unit.gendarmerie", ("province", province));
            if (!taken.Contains(plain)) return plain;
        }

        for (var n = type == UnitType.Division ? GetUnitsTx(conn, tx, type).Count + 1 : 2; ; n++)
        {
            var name = type == UnitType.Division
                ? Texts.Get("data.unit.division", ("ordinal", Ordinal(n)), ("province", province))
                : Texts.Get("forces.name.gendarmerieNumbered", ("ordinal", Ordinal(n)), ("province", province));
            if (!taken.Contains(name)) return name;
        }
    }

    private static string? RaiseUnitsTx(SqliteConnection conn, SqliteTransaction tx, GameConfig config, UnitType type, string provinceId, int count)
    {
        var sides = config.Constants.UnitsOf(type).BaseSides;
        for (var i = 0; i < count; i++)
            InsertUnitTx(conn, tx, type, GeneratedUnitNameTx(conn, tx, config, type, provinceId), sides, provinceId);
        return null;
    }

    private static string? RemoveUnitsTx(SqliteConnection conn, SqliteTransaction tx, UnitType type, string provinceId, int count)
    {
        var weakest = GetUnitsTx(conn, tx, type)
            .Where(u => u.ProvinceId == provinceId)
            .OrderBy(u => u.Sides)
            .ThenByDescending(u => u.Id)
            .Take(count)
            .ToList();
        if (count > 0 && weakest.Count == 0) return Texts.Get("forces.effect.noneToRemove", ("province", provinceId));
        foreach (var unit in weakest)
        {
            Exec(conn, tx, "DELETE FROM pending_orders WHERE unit_id = $id;", ("$id", unit.Id));
            Exec(conn, tx, "DELETE FROM units WHERE id = $id;", ("$id", unit.Id));
        }
        return null;
    }

    internal ResolutionReport ApplyOrders(GameConfig config, IRandomSource random)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        var state = GetGameStateTx(conn, tx);
        var report = new ResolutionReport { ResolutionNumber = state.ResolutionNumber + 1, FromSeason = state.Season, FromYear = state.Year };
        ApplyOrdersTx(conn, tx, state, config, random, report);
        tx.Commit();
        return report;
    }

    private static void ApplyOrdersTx(SqliteConnection conn, SqliteTransaction tx, GameState state, GameConfig config, IRandomSource random, ResolutionReport report)
    {
        var orders = GetPendingOrdersTx(conn, tx);
        if (orders.Count == 0) return;

        var done = new Dictionary<ulong, List<string>>();
        void Tell(PendingOrder order, string line)
        {
            if (order.OrderedBy is not { } by) return;
            if (!done.TryGetValue(by, out var lines)) done[by] = lines = [];
            lines.Add(line);
        }
        string ProvinceName(string? id) => id is null ? "" : config.Province(id)?.Name ?? id;

        foreach (var order in orders.Where(o => o.Kind == OrderKind.Raise && o.UnitType is not null && o.ProvinceId is not null))
        {
            var units = config.Constants.UnitsOf(order.UnitType!.Value);
            var name = order.Name ?? GeneratedUnitNameTx(conn, tx, config, order.UnitType.Value, order.ProvinceId!);
            InsertUnitTx(conn, tx, order.UnitType.Value, name, units.BaseSides, order.ProvinceId!);
            Tell(order, Texts.Get("forces.private.raised", ("name", name), ("sides", units.BaseSides), ("province", ProvinceName(order.ProvinceId))));
        }

        foreach (var order in orders.Where(o => o.Kind == OrderKind.Train && o.UnitId is not null))
        {
            if (GetUnitTx(conn, tx, order.UnitId!.Value) is not { } unit) continue;
            var units = config.Constants.UnitsOf(unit.Type);
            var sides = Math.Min(units.MaxSides, unit.Sides + units.SidesStep);
            Exec(conn, tx, "UPDATE units SET sides = $sides WHERE id = $id;", ("$id", unit.Id), ("$sides", sides));
            Tell(order, Texts.Get("forces.private.trained", ("name", unit.Name), ("sides", sides)));
        }

        foreach (var order in orders.Where(o => o.Kind == OrderKind.Station && o.UnitId is not null && o.ProvinceId is not null))
        {
            if (GetUnitTx(conn, tx, order.UnitId!.Value) is not { } unit) continue;
            Exec(conn, tx, "UPDATE units SET province_id = $province WHERE id = $id;", ("$id", unit.Id), ("$province", order.ProvinceId));
            Tell(order, Texts.Get("forces.private.stationed", ("name", unit.Name), ("province", ProvinceName(order.ProvinceId))));
        }

        var c = config.Constants;
        foreach (var order in orders.Where(o => o.Kind == OrderKind.Crackdown && o.ProvinceId is not null))
        {
            if (GetProvincesTx(conn, tx).FirstOrDefault(p => p.Id == order.ProvinceId) is not { } province) continue;
            var total = GetUnitsTx(conn, tx, UnitType.Gendarmerie)
                .Where(u => u.ProvinceId == province.Id)
                .Sum(u => random.RollDie(u.Sides));
            var dieSides = CrackdownDieSides(province.Discontent);
            var dieRoll = random.RollDie(dieSides);
            var success = total >= dieRoll;
            var name = ProvinceName(province.Id);

            var effects = new List<Effect>();
            if (success)
            {
                var discontent = Math.Clamp(province.Discontent - c.Crackdown.Reduction, c.Discontent.Min, c.Discontent.Max);
                SetDiscontentTx(conn, tx, province.Id, discontent);
                effects.Add(new Effect($"discontent.{province.Id}", discontent - province.Discontent));
                if (GetProvinceConditionsTx(conn, tx).GetValueOrDefault(province.Id, []).Contains(LawlessCondition))
                {
                    SetConditionTx(conn, tx, state, province.Id, LawlessCondition, false);
                    effects.Add(new Effect($"condition.{province.Id}.{LawlessCondition}", -1));
                }
                Tell(order, Texts.Get("forces.private.crackdownSucceeded", ("province", name), ("total", total), ("roll", dieRoll), ("sides", dieSides)));
            }
            else
            {
                var discontent = Math.Clamp(province.Discontent + c.Crackdown.Backlash, c.Discontent.Min, c.Discontent.Max);
                SetDiscontentTx(conn, tx, province.Id, discontent);
                effects.Add(new Effect($"discontent.{province.Id}", discontent - province.Discontent));
                var minister = ResponsibleMinisterTx(conn, tx, Ministry.Interior);
                if (minister is { } faction && GetFactionTx(conn, tx, faction) is { } before
                    && AddPopularityTx(conn, tx, faction, -c.Crackdown.FailurePopularity, c.Popularity) is { } after)
                    effects.Add(new Effect($"popularity.{faction}", after - before.Popularity));
                report.Alerts.Add(Texts.Get("crackdown.alert.failed", ("province", name), ("total", total), ("roll", dieRoll), ("sides", dieSides),
                    ("minister", minister is { } m ? $"<@{m}>" : Texts.Get("cabinet.vacant"))));
                Tell(order, Texts.Get("forces.private.crackdownFailed", ("province", name), ("total", total), ("roll", dieRoll), ("sides", dieSides)));
            }

            report.Crackdowns.Add(new CrackdownReport(province.Id, success, total, dieSides, dieRoll, effects));
        }

        foreach (var order in orders)
            Exec(conn, tx, "DELETE FROM pending_orders WHERE id = $id;", ("$id", order.Id));

        foreach (var (faction, lines) in done)
            report.AddPrivate(faction, Texts.Get("forces.private.header") + "\n" + string.Join("\n", lines));
    }
}
