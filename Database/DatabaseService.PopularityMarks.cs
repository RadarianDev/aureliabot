using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

public partial class DatabaseService
{
    public Dictionary<ulong, double> GetPopularityMarks()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT faction_id, popularity FROM popularity_marks;";
        var result = new Dictionary<ulong, double>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            result[ulong.Parse(r.GetString(0))] = r.GetDouble(1);
        return result;
    }

    public Dictionary<ulong, Dictionary<string, double>> GetAllRegionalPopularity(PopularityConstants range)
    {
        using var conn = Open();
        var deviations = GetAllDeviationsTx(conn, null);
        var provinces = GetProvincesTx(conn, null);
        return QueryFactionsTx(conn, null, "status = 'Active'").ToDictionary(
            f => f.UserId,
            f => provinces.ToDictionary(
                p => p.Id,
                p => Formulas.RegionalPopularity(f.Popularity, deviations.GetValueOrDefault(f.UserId)?.GetValueOrDefault(p.Id) ?? 0, range)));
    }

    private static void MarkPopularityTx(SqliteConnection conn, SqliteTransaction tx)
    {
        Exec(conn, tx, "DELETE FROM popularity_marks;");
        Exec(conn, tx, "INSERT INTO popularity_marks (faction_id, popularity) SELECT user_id, popularity FROM factions WHERE status = 'Active';");
    }
}
