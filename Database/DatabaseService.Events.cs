using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

public record GameEvent(
    long Id,
    string TemplateId,
    string Title,
    string Text,
    string Effects,
    EventStatus Status,
    bool FromSchedule,
    long FireAtResolution,
    long? FiredResolution)
{
    public IReadOnlyList<Effect> ParsedEffects => EffectParser.Parse(Effects);
}

public partial class DatabaseService
{
    private const string SelectEventColumns =
        "id, template_id, title, text, effects, status, from_schedule, fire_at_resolution, fired_resolution";

    private static GameEvent ReadEvent(SqliteDataReader r) => new(
        r.GetInt64(0),
        r.GetString(1),
        r.GetString(2),
        r.GetString(3),
        r.GetString(4),
        Enum.Parse<EventStatus>(r.GetString(5)),
        r.GetInt64(6) != 0,
        r.GetInt64(7),
        r.IsDBNull(8) ? null : r.GetInt64(8));

    public long QueueEvent(EventTemplate template, long fireAtResolution, bool fromSchedule)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        var id = QueueEventTx(conn, tx, template, fireAtResolution, fromSchedule);
        tx.Commit();
        return id;
    }

    private static long QueueEventTx(SqliteConnection conn, SqliteTransaction tx, EventTemplate template, long fireAtResolution, bool fromSchedule)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO events (template_id, title, text, effects, status, from_schedule, queued_at, fire_at_resolution)
            VALUES ($template, $title, $text, $effects, 'Queued', $fromSchedule, $now, $fireAt)
            RETURNING id;";
        cmd.Parameters.AddWithValue("$template", template.Id);
        cmd.Parameters.AddWithValue("$title", template.Title);
        cmd.Parameters.AddWithValue("$text", template.Text);
        cmd.Parameters.AddWithValue("$effects", EffectParser.Format(template.Effects));
        cmd.Parameters.AddWithValue("$fromSchedule", fromSchedule ? 1 : 0);
        cmd.Parameters.AddWithValue("$now", Now());
        cmd.Parameters.AddWithValue("$fireAt", fireAtResolution);
        var id = (long)cmd.ExecuteScalar()!;
        MarkTemplatePostedTx(conn, tx, template.Id);
        LogTx(conn, tx, LogKinds.StateChange, "event queue", $"Queued event {id} from {template.Id} for resolution {fireAtResolution}.");
        return id;
    }

    public GameEvent? GetEvent(long id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectEventColumns} FROM events WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadEvent(r) : null;
    }

    public List<GameEvent> GetQueuedEvents()
    {
        using var conn = Open();
        return QueryEventsTx(conn, null, "status = 'Queued'");
    }

    public List<GameEvent> GetFiredEvents(int limit = 25)
    {
        using var conn = Open();
        return QueryEventsTx(conn, null, "status = 'Fired'", $"fired_resolution DESC, id DESC LIMIT {limit}");
    }

    private static List<GameEvent> QueryEventsTx(
        SqliteConnection conn, SqliteTransaction? tx, string where, string orderBy = "fire_at_resolution, id", params (string Name, object? Value)[] parameters)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT {SelectEventColumns} FROM events WHERE {where} ORDER BY {orderBy};";
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        var list = new List<GameEvent>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(ReadEvent(r));
        return list;
    }

    public bool CancelEvent(long id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE events SET status = 'Cancelled' WHERE id = $id AND status = 'Queued';";
        cmd.Parameters.AddWithValue("$id", id);
        var cancelled = cmd.ExecuteNonQuery() > 0;
        if (cancelled) LogTx(conn, null, LogKinds.StateChange, "gm event cancel", $"Cancelled event {id}.");
        return cancelled;
    }

    public int CancelScheduledEvents(string templateId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE events SET status = 'Cancelled' WHERE template_id = $template AND from_schedule = 1 AND status = 'Queued';";
        cmd.Parameters.AddWithValue("$template", templateId);
        return cmd.ExecuteNonQuery();
    }

    public bool HasScheduledEventFired(string templateId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM events WHERE template_id = $template AND from_schedule = 1 AND status = 'Fired';";
        cmd.Parameters.AddWithValue("$template", templateId);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    public long ScheduleEvent(EventTemplate template, long fireAtResolution)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        Exec(conn, tx, "UPDATE events SET status = 'Cancelled' WHERE template_id = $template AND from_schedule = 1 AND status = 'Queued';",
            ("$template", template.Id));
        var id = QueueEventTx(conn, tx, template, fireAtResolution, fromSchedule: true);
        tx.Commit();
        return id;
    }

    public bool UpdateQueuedEvent(long id, string title, string text, IReadOnlyList<Effect> effects)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE events SET title = $title, text = $text, effects = $effects WHERE id = $id AND status = 'Queued';";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$title", title);
        cmd.Parameters.AddWithValue("$text", text);
        cmd.Parameters.AddWithValue("$effects", EffectParser.Format(effects));
        var updated = cmd.ExecuteNonQuery() > 0;
        if (updated) LogTx(conn, null, LogKinds.GmOverride, "content edit live", $"Edited queued event {id}.");
        return updated;
    }
}
