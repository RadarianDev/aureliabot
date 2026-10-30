using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

public record LogEntry(long Id, DateTimeOffset At, long ResolutionNumber, string Kind, string Source, ulong? FactionId, string Payload, bool Public);

public static class LogKinds
{
    public const string Command = "Command";
    public const string Post = "Post";
    public const string StateChange = "StateChange";
    public const string Resolution = "Resolution";
    public const string GmOverride = "GmOverride";
    public const string LostCommand = "LostCommand";
    public const string Resubmitted = "Resubmitted";
}

public partial class DatabaseService
{
    private const string SelectLogEntryColumns = "id, at, resolution_number, kind, source, faction_id, payload, public_flag";

    private static LogEntry ReadLogEntry(SqliteDataReader r) => new(
        r.GetInt64(0),
        DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(1)),
        r.GetInt64(2),
        r.GetString(3),
        r.GetString(4),
        r.IsDBNull(5) ? null : ulong.Parse(r.GetString(5)),
        r.GetString(6),
        r.GetInt64(7) != 0);

    public void Log(string kind, string source, string payload, ulong? factionId = null, bool isPublic = false)
    {
        using var conn = Open();
        LogTx(conn, null, kind, source, payload, factionId, isPublic);
    }

    private static void LogTx(SqliteConnection conn, SqliteTransaction? tx, string kind, string source, string payload, ulong? factionId = null, bool isPublic = false) =>
        Exec(conn, tx, @"
            INSERT INTO log_entries (at, resolution_number, kind, source, faction_id, payload, public_flag)
            VALUES ($at, (SELECT resolution_number FROM game_state WHERE id = 1), $kind, $source, $faction, $payload, $public);",
            ("$at", Now()),
            ("$kind", kind),
            ("$source", source),
            ("$faction", factionId?.ToString()),
            ("$payload", payload),
            ("$public", isPublic ? 1 : 0));

    public LogEntry LogAndRead(string kind, string source, string payload, ulong? factionId = null)
    {
        using var conn = Open();
        LogTx(conn, null, kind, source, payload, factionId);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectLogEntryColumns} FROM log_entries WHERE id = last_insert_rowid();";
        using var r = cmd.ExecuteReader();
        r.Read();
        return ReadLogEntry(r);
    }

    public void DeleteLogEntries(string kind, string payload)
    {
        using var conn = Open();
        Exec(conn, null, "DELETE FROM log_entries WHERE kind = $kind AND payload = $payload;", ("$kind", kind), ("$payload", payload));
    }

    public LogEntry? GetLogEntry(long id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectLogEntryColumns} FROM log_entries WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadLogEntry(r) : null;
    }

    public void LogPost(ulong channelId, ulong messageId, string source) =>
        Log(LogKinds.Post, source, $"{channelId}:{messageId}");

    public List<LogEntry> GetLogEntries(string kind, DateTimeOffset since)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectLogEntryColumns} FROM log_entries WHERE kind = $kind AND at >= $since ORDER BY id;";
        cmd.Parameters.AddWithValue("$kind", kind);
        cmd.Parameters.AddWithValue("$since", since.ToUnixTimeSeconds());
        var list = new List<LogEntry>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(ReadLogEntry(r));
        return list;
    }

    public List<LogEntry> GetLogEntriesForResolution(string kind, long resolutionNumber)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectLogEntryColumns} FROM log_entries WHERE kind = $kind AND resolution_number = $resolution ORDER BY id;";
        cmd.Parameters.AddWithValue("$kind", kind);
        cmd.Parameters.AddWithValue("$resolution", resolutionNumber);
        var list = new List<LogEntry>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(ReadLogEntry(r));
        return list;
    }
}
