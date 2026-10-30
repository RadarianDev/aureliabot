using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

public record Unit(long Id, UnitType Type, string Name, int Sides, string ProvinceId);

public partial class DatabaseService
{
    private const string SelectUnitColumns = "id, type, name, sides, province_id";

    private static Unit ReadUnit(SqliteDataReader r) =>
        new(r.GetInt64(0), Enum.Parse<UnitType>(r.GetString(1)), r.GetString(2), r.GetInt32(3), r.GetString(4));

    public List<Unit> GetUnits(UnitType? type = null)
    {
        using var conn = Open();
        return GetUnitsTx(conn, null, type);
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
}

public partial class DatabaseService
{
    private static string? RaiseUnitsTx(SqliteConnection conn, SqliteTransaction tx, GameConfig config, UnitType type, string provinceId, int count) =>
        Texts.Get("data.effect.forcesNotBuilt");

    private static string? RemoveUnitsTx(SqliteConnection conn, SqliteTransaction tx, UnitType type, string provinceId, int count) =>
        Texts.Get("data.effect.forcesNotBuilt");

    private static void ApplyOrdersTx(SqliteConnection conn, SqliteTransaction tx, GameState state, GameConfig config, IRandomSource random, ResolutionReport report)
    {
    }
}
