using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

public partial class DatabaseService
{
    public Dictionary<string, double> GetRegionalPopularity(ulong factionId, PopularityConstants range)
    {
        using var conn = Open();
        return GetRegionalPopularityTx(conn, null, factionId, range);
    }

    private static Dictionary<string, double> GetRegionalPopularityTx(
        SqliteConnection conn, SqliteTransaction? tx, ulong factionId, PopularityConstants range)
    {
        if (GetFactionTx(conn, tx, factionId) is not { } faction) return [];
        var deviations = GetDeviationsTx(conn, tx, factionId);
        return GetProvincesTx(conn, tx).ToDictionary(
            p => p.Id,
            p => Formulas.RegionalPopularity(faction.Popularity, deviations.GetValueOrDefault(p.Id), range));
    }

    private static Dictionary<string, double> GetDeviationsTx(SqliteConnection conn, SqliteTransaction? tx, ulong factionId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT province_id, deviation FROM regional_popularity WHERE faction_id = $faction;";
        cmd.Parameters.AddWithValue("$faction", factionId.ToString());
        var result = new Dictionary<string, double>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            result[r.GetString(0)] = r.GetDouble(1);
        return result;
    }

    private static Dictionary<ulong, Dictionary<string, double>> GetAllDeviationsTx(SqliteConnection conn, SqliteTransaction? tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT faction_id, province_id, deviation FROM regional_popularity;";
        var result = new Dictionary<ulong, Dictionary<string, double>>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var factionId = ulong.Parse(r.GetString(0));
            if (!result.TryGetValue(factionId, out var deviations))
                result[factionId] = deviations = [];
            deviations[r.GetString(1)] = r.GetDouble(2);
        }
        return result;
    }

    private static double AddRegionalPopularityTx(
        SqliteConnection conn, SqliteTransaction tx, ulong factionId, string provinceId, double delta, PopularityConstants range)
    {
        if (GetFactionTx(conn, tx, factionId) is not { } faction) return 0;
        var populations = GetProvincesTx(conn, tx).ToDictionary(p => p.Id, p => p.Population);
        var shift = Formulas.ShiftInProvince(faction.Popularity, GetDeviationsTx(conn, tx, factionId), populations, provinceId, delta, range);
        if (shift.RegionalChange == 0) return 0;

        Exec(conn, tx, "UPDATE factions SET popularity = $popularity WHERE user_id = $user;",
            ("$user", factionId.ToString()), ("$popularity", faction.Popularity + shift.NationalChange));
        foreach (var (province, deviation) in shift.Deviations)
            Exec(conn, tx, @"
                INSERT INTO regional_popularity (faction_id, province_id, deviation) VALUES ($faction, $province, $deviation)
                ON CONFLICT (faction_id, province_id) DO UPDATE SET deviation = excluded.deviation;",
                ("$faction", factionId.ToString()), ("$province", province), ("$deviation", deviation));
        return shift.RegionalChange;
    }

    private static void SpreadRegionalPopularityTx(SqliteConnection conn, SqliteTransaction tx, PopularityConstants popularity) =>
        Exec(conn, tx, "UPDATE regional_popularity SET deviation = deviation * $keep;", ("$keep", 1 - popularity.Spread));
}
