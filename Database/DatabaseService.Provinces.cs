using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

public record Province(string Id, string Name, double Population, double Discontent);

public sealed class OpinionSnapshot
{
    private readonly Dictionary<string, Issue> _issues;
    private readonly Dictionary<(string Issue, string Province), double> _provincial = [];
    private readonly Dictionary<string, double> _national = [];

    public OpinionSnapshot(
        IReadOnlyList<Province> provinces,
        IReadOnlyDictionary<string, IReadOnlyList<ConditionDefinition>> conditionsByProvince,
        IEnumerable<Issue> issues)
    {
        Provinces = provinces;
        _issues = issues.ToDictionary(i => i.Id);
        foreach (var issue in _issues.Values)
        {
            foreach (var province in provinces)
            {
                var conditions = conditionsByProvince.TryGetValue(province.Id, out var list) ? list : [];
                _provincial[(issue.Id, province.Id)] = Formulas.ProvincialOpinion(issue.Opinion, conditions, issue.Id);
            }
            _national[issue.Id] = Formulas.NationalOpinion(
                provinces.Select(p => (p.Population, _provincial[(issue.Id, p.Id)])));
        }
    }

    public IReadOnlyList<Province> Provinces { get; }

    public Issue? Issue(string issueId) => _issues.GetValueOrDefault(issueId);

    public double National(string issueId) => _national.GetValueOrDefault(issueId);

    public double Provincial(string issueId, string provinceId) => _provincial.GetValueOrDefault((issueId, provinceId));

    public double Salience(string issueId) => _issues.TryGetValue(issueId, out var issue) ? issue.Salience : 0;
}

public partial class DatabaseService
{
    private const string SelectProvinceColumns = "id, name, population, discontent";

    private static Province ReadProvince(SqliteDataReader r) =>
        new(r.GetString(0), r.GetString(1), r.GetDouble(2), r.GetDouble(3));

    public List<Province> GetProvinces()
    {
        using var conn = Open();
        return GetProvincesTx(conn, null);
    }

    private static List<Province> GetProvincesTx(SqliteConnection conn, SqliteTransaction? tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT {SelectProvinceColumns} FROM provinces ORDER BY population DESC, id;";
        var list = new List<Province>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(ReadProvince(r));
        return list;
    }

    public Province? GetProvince(string id) => GetProvinces().FirstOrDefault(p => p.Id == id);

    public Dictionary<string, List<string>> GetProvinceConditions()
    {
        using var conn = Open();
        return GetProvinceConditionsTx(conn, null);
    }

    private static Dictionary<string, List<string>> GetProvinceConditionsTx(SqliteConnection conn, SqliteTransaction? tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT province_id, condition_id FROM province_conditions ORDER BY province_id, rowid;";
        var map = new Dictionary<string, List<string>>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var province = r.GetString(0);
            if (!map.TryGetValue(province, out var list)) map[province] = list = [];
            list.Add(r.GetString(1));
        }
        return map;
    }

    private static bool SetConditionTx(SqliteConnection conn, SqliteTransaction tx, GameState state, string provinceId, string conditionId, bool present)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = present
            ? @"INSERT INTO province_conditions (province_id, condition_id, since_season, since_year)
                VALUES ($province, $condition, $season, $year) ON CONFLICT DO NOTHING;"
            : "DELETE FROM province_conditions WHERE province_id = $province AND condition_id = $condition;";
        cmd.Parameters.AddWithValue("$province", provinceId);
        cmd.Parameters.AddWithValue("$condition", conditionId);
        cmd.Parameters.AddWithValue("$season", state.Season.ToString());
        cmd.Parameters.AddWithValue("$year", state.Year);
        return cmd.ExecuteNonQuery() > 0;
    }

    private static void SetDiscontentTx(SqliteConnection conn, SqliteTransaction tx, string provinceId, double discontent) =>
        Exec(conn, tx, "UPDATE provinces SET discontent = $value WHERE id = $id;", ("$id", provinceId), ("$value", discontent));

    public OpinionSnapshot LoadOpinionSnapshot(GameConfig config)
    {
        using var conn = Open();
        return LoadOpinionSnapshotTx(conn, null, config);
    }

    private static OpinionSnapshot LoadOpinionSnapshotTx(SqliteConnection conn, SqliteTransaction? tx, GameConfig config)
    {
        var provinces = GetProvincesTx(conn, tx);
        Dictionary<string, List<string>> conditionIds;
        if (provinces.Count == 0)
        {
            provinces = config.Provinces.Select(p => new Province(p.Id, p.Name, p.Population, config.Constants.Discontent.Start)).ToList();
            conditionIds = config.Provinces.ToDictionary(p => p.Id, p => p.Conditions.ToList());
        }
        else
        {
            conditionIds = GetProvinceConditionsTx(conn, tx);
        }

        var conditions = conditionIds.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<ConditionDefinition>)kv.Value.Select(config.Condition).OfType<ConditionDefinition>().ToList());
        return new OpinionSnapshot(provinces, conditions, QueryIssuesTx(conn, tx, "status = 'Open'"));
    }
}
