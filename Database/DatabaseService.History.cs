using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

public sealed record HistoryTurn(long Resolution, Season Season, int Year);

public sealed record History(IReadOnlyList<HistoryTurn> Turns, IReadOnlyDictionary<string, IReadOnlyDictionary<long, double>> Series)
{
    public IReadOnlyDictionary<long, double> Of(string series) =>
        Series.TryGetValue(series, out var values) ? values : new Dictionary<long, double>();
}

public static class HistorySeries
{
    public const string Economy = "economy";
    public const string Budget = "budget";
    public const string Reserve = "reserve";
    public const string Favour = "favour";
    public const string Threat = "threat";

    public static string Purse(Ministry ministry) => $"purse.{ministry}";

    public static string Popularity(ulong factionId) => $"popularity.{factionId}";

    public static string Discontent(string provinceId) => $"discontent.{provinceId}";
}

public partial class DatabaseService
{
    public History GetHistory()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT resolution_number, season, year, series, value FROM stat_history ORDER BY resolution_number;";
        var turns = new Dictionary<long, HistoryTurn>();
        var series = new Dictionary<string, Dictionary<long, double>>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var resolution = r.GetInt64(0);
            turns.TryAdd(resolution, new HistoryTurn(resolution, Enum.Parse<Season>(r.GetString(1)), r.GetInt32(2)));
            var name = r.GetString(3);
            if (!series.TryGetValue(name, out var values)) series[name] = values = [];
            values[resolution] = r.GetDouble(4);
        }
        return new History(
            turns.Values.OrderBy(t => t.Resolution).ToList(),
            series.ToDictionary(kv => kv.Key, kv => (IReadOnlyDictionary<long, double>)kv.Value));
    }

    private static void RecordHistoryTx(SqliteConnection conn, SqliteTransaction tx)
    {
        var state = GetGameStateTx(conn, tx);
        var values = new Dictionary<string, double>
        {
            [HistorySeries.Economy] = state.Economy,
            [HistorySeries.Reserve] = (double)GetBalanceTx(conn, tx, LedgerAccount.Reserve),
            [HistorySeries.Favour] = state.Favour,
            [HistorySeries.Threat] = state.Threat,
        };
        if (GetFiscalYearTx(conn, tx, state.Year) is { } fiscal) values[HistorySeries.Budget] = (double)fiscal.Budget;
        foreach (var ministry in GameEnumExtensions.CabinetMinistries)
            values[HistorySeries.Purse(ministry)] = (double)GetBalanceTx(conn, tx, (LedgerAccount)(int)ministry);
        foreach (var faction in QueryFactionsTx(conn, tx, "status = 'Active'"))
            values[HistorySeries.Popularity(faction.UserId)] = faction.Popularity;
        foreach (var province in GetProvincesTx(conn, tx))
            values[HistorySeries.Discontent(province.Id)] = province.Discontent;

        Exec(conn, tx, "DELETE FROM stat_history WHERE resolution_number = $resolution;", ("$resolution", state.ResolutionNumber));
        foreach (var (series, value) in values)
            Exec(conn, tx, @"
                INSERT INTO stat_history (resolution_number, season, year, series, value)
                VALUES ($resolution, $season, $year, $series, $value);",
                ("$resolution", state.ResolutionNumber), ("$season", state.Season.ToString()), ("$year", state.Year),
                ("$series", series), ("$value", value));
    }
}

public partial class DatabaseService
{
    public void RecordHistoryIfMissing()
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        var state = GetGameStateTx(conn, tx);
        if (!state.Started) return;
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM stat_history WHERE resolution_number = $resolution;";
        cmd.Parameters.AddWithValue("$resolution", state.ResolutionNumber);
        if ((long)cmd.ExecuteScalar()! > 0) return;
        RecordHistoryTx(conn, tx);
        tx.Commit();
    }
}
