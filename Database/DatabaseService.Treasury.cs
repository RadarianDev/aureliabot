using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

public record LedgerEntry(
    long Id, LedgerAccount Account, decimal Amount, LedgerKind Kind, string Label, string? SourceType, string? SourceId,
    DateTimeOffset MadeAt, long ResolutionNumber, Season Season, int Year, long PublicFromResolution);

public partial class DatabaseService
{
    private const string SelectLedgerColumns =
        "id, account, amount, kind, label, source_type, source_id, made_at, resolution_number, season, year, public_from_resolution";

    private static LedgerEntry ReadLedgerEntry(SqliteDataReader r) => new(
        r.GetInt64(0),
        Enum.Parse<LedgerAccount>(r.GetString(1)),
        r.GetDecimal(2),
        Enum.Parse<LedgerKind>(r.GetString(3)),
        r.GetString(4),
        r.IsDBNull(5) ? null : r.GetString(5),
        r.IsDBNull(6) ? null : r.GetString(6),
        DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(7)),
        r.GetInt64(8),
        Enum.Parse<Season>(r.GetString(9)),
        r.GetInt32(10),
        r.GetInt64(11));

    public decimal GetPurseBalance(Ministry ministry)
    {
        using var conn = Open();
        return GetBalanceTx(conn, null, (LedgerAccount)(int)ministry);
    }

    public Dictionary<Ministry, decimal> GetPurseBalances()
    {
        using var conn = Open();
        var map = new Dictionary<Ministry, decimal>();
        foreach (var ministry in Enum.GetValues<Ministry>())
            map[ministry] = GetBalanceTx(conn, null, (LedgerAccount)(int)ministry);
        return map;
    }

    private static decimal GetBalanceTx(SqliteConnection conn, SqliteTransaction? tx, LedgerAccount account)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = account == LedgerAccount.Reserve
            ? "SELECT reserve FROM game_state WHERE id = 1;"
            : "SELECT balance FROM purses WHERE ministry = $ministry;";
        cmd.Parameters.AddWithValue("$ministry", account.ToString());
        return cmd.ExecuteScalar() is { } value ? decimal.Parse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!, System.Globalization.CultureInfo.InvariantCulture) : 0m;
    }

    private static decimal PostLedgerTx(
        SqliteConnection conn, SqliteTransaction tx, LedgerAccount account, decimal amount, LedgerKind kind, string label,
        string? sourceType, string? sourceId, bool publicNow = false)
    {
        var balance = GetBalanceTx(conn, tx, account);
        var applied = Math.Round(Math.Max(amount, -balance), 2, MidpointRounding.AwayFromZero);
        if (applied == 0) return 0;
        var updated = Money(balance + applied);

        if (account == LedgerAccount.Reserve)
            Exec(conn, tx, "UPDATE game_state SET reserve = $value WHERE id = 1;", ("$value", updated));
        else
            Exec(conn, tx, "UPDATE purses SET balance = $value WHERE ministry = $ministry;",
                ("$value", updated), ("$ministry", account.ToString()));

        var state = GetGameStateTx(conn, tx);
        Exec(conn, tx, @"
            INSERT INTO ledger (account, amount, kind, label, source_type, source_id, made_at, resolution_number, season, year, public_from_resolution)
            VALUES ($account, $amount, $kind, $label, $sourceType, $sourceId, $now, $resolution, $season, $year, $publicFrom);",
            ("$account", account.ToString()),
            ("$amount", Money(applied)),
            ("$kind", kind.ToString()),
            ("$label", label),
            ("$sourceType", sourceType),
            ("$sourceId", sourceId),
            ("$now", Now()),
            ("$resolution", state.ResolutionNumber),
            ("$season", state.Season.ToString()),
            ("$year", state.Year),
            ("$publicFrom", publicNow ? state.ResolutionNumber : state.ResolutionNumber + 1));
        return applied;
    }

    public List<LedgerEntry> GetLedger(LedgerAccount? account = null, int? year = null)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT {SelectLedgerColumns} FROM ledger
            WHERE ($account IS NULL OR account = $account) AND ($year IS NULL OR year = $year)
            ORDER BY id;";
        cmd.Parameters.AddWithValue("$account", (object?)account?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$year", (object?)year ?? DBNull.Value);
        var list = new List<LedgerEntry>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(ReadLedgerEntry(r));
        return list;
    }
}
