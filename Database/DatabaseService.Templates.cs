using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

public record TemplateRow(
    string Id, TemplateKind Kind, TemplateStatus Status, string Yaml, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, int TimesPosted);

public sealed record TemplateSaveResult(bool Replaced, TemplateStatus? PreviousStatus, TemplateStatus Status);

public partial class DatabaseService
{
    private const string SelectTemplateColumns = "id, kind, status, yaml, created_at, updated_at, times_posted";

    private static TemplateRow ReadTemplate(SqliteDataReader r) => new(
        r.GetString(0),
        Enum.Parse<TemplateKind>(r.GetString(1)),
        Enum.Parse<TemplateStatus>(r.GetString(2)),
        r.GetString(3),
        DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(4)),
        DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(5)),
        r.GetInt32(6));

    public TemplateRow? GetTemplate(string id)
    {
        using var conn = Open();
        return GetTemplateTx(conn, null, id);
    }

    private static TemplateRow? GetTemplateTx(SqliteConnection conn, SqliteTransaction? tx, string id)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT {SelectTemplateColumns} FROM templates WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadTemplate(r) : null;
    }

    public List<TemplateRow> GetTemplates(TemplateKind? kind = null, TemplateStatus? status = null)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT {SelectTemplateColumns} FROM templates
            WHERE ($kind IS NULL OR kind = $kind) AND ($status IS NULL OR status = $status)
            ORDER BY kind, id;";
        cmd.Parameters.AddWithValue("$kind", (object?)kind?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$status", (object?)status?.ToString() ?? DBNull.Value);
        var list = new List<TemplateRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(ReadTemplate(r));
        return list;
    }

    public TemplateSaveResult SaveTemplate(string id, TemplateKind kind, string yaml)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        var existing = GetTemplateTx(conn, tx, id);
        var now = Now();
        if (existing is null)
        {
            Exec(conn, tx, @"
                INSERT INTO templates (id, kind, status, yaml, created_at, updated_at, times_posted)
                VALUES ($id, $kind, 'Draft', $yaml, $now, $now, 0);",
                ("$id", id), ("$kind", kind.ToString()), ("$yaml", yaml), ("$now", now));
        }
        else
        {
            Exec(conn, tx, "UPDATE templates SET kind = $kind, status = 'Draft', yaml = $yaml, updated_at = $now WHERE id = $id;",
                ("$id", id), ("$kind", kind.ToString()), ("$yaml", yaml), ("$now", now));
        }
        LogTx(conn, tx, LogKinds.StateChange, "content", $"{(existing is null ? "Imported" : "Replaced")} {kind} template {id}.");
        tx.Commit();
        return new TemplateSaveResult(existing is not null, existing?.Status, TemplateStatus.Draft);
    }

    public void SetTemplateStatus(string id, TemplateStatus status)
    {
        using var conn = Open();
        Exec(conn, null, "UPDATE templates SET status = $status, updated_at = $now WHERE id = $id;",
            ("$id", id), ("$status", status.ToString()), ("$now", Now()));
        LogTx(conn, null, LogKinds.StateChange, "content", $"Template {id} is now {status}.");
    }

    public bool DeleteTemplate(string id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM templates WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        var deleted = cmd.ExecuteNonQuery() > 0;
        if (deleted) LogTx(conn, null, LogKinds.StateChange, "content", $"Deleted template {id}.");
        return deleted;
    }

    private static void MarkTemplatePostedTx(SqliteConnection conn, SqliteTransaction? tx, string id) =>
        Exec(conn, tx, "UPDATE templates SET times_posted = times_posted + 1 WHERE id = $id;", ("$id", id));

    private static Template? ParseTemplateTx(SqliteConnection conn, SqliteTransaction? tx, string id)
    {
        var row = GetTemplateTx(conn, tx, id);
        return row is null ? null : TemplateCodec.Parse(row.Yaml, id).Template;
    }

    public Template? GetParsedTemplate(string id)
    {
        using var conn = Open();
        return ParseTemplateTx(conn, null, id);
    }

    public EffectContext BuildEffectContext(GameConfig config, bool allowHere = false, bool allowCrisis = false)
    {
        var templates = GetTemplates();
        HashSet<string> Ids(TemplateKind kind) =>
            templates.Where(t => t.Kind == kind && t.Status != TemplateStatus.Archived).Select(t => t.Id).ToHashSet();

        var issueTemplates = Ids(TemplateKind.Issue);
        var issues = new HashSet<string>(issueTemplates);
        foreach (var issue in GetAllIssues()) issues.Add(issue.Id);

        return new EffectContext(
            config.Provinces.Select(p => p.Id).ToHashSet(),
            config.Conditions.Select(c => c.Id).ToHashSet(),
            issues,
            issueTemplates,
            Ids(TemplateKind.Bill),
            Ids(TemplateKind.Project),
            Ids(TemplateKind.Crisis),
            GetAllFactions().Select(f => f.UserId.ToString()).ToHashSet())
        {
            AllowHere = allowHere,
            AllowCrisis = allowCrisis,
            EventTemplates = Ids(TemplateKind.Event),
        };
    }
}
