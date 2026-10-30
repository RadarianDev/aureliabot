using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

public record Issue(
    string Id,
    string TemplateId,
    string Title,
    string ForText,
    string AgainstText,
    double Opinion,
    double Salience,
    long? SalienceRaisedResolution,
    IssueStatus Status);

public partial class DatabaseService
{
    private const string SelectIssueColumns =
        "id, template_id, title, for_text, against_text, opinion, salience, salience_raised_resolution, status";

    private static Issue ReadIssue(SqliteDataReader r) => new(
        r.GetString(0),
        r.GetString(1),
        r.GetString(2),
        r.GetString(3),
        r.GetString(4),
        r.GetDouble(5),
        r.GetDouble(6),
        r.IsDBNull(7) ? null : r.GetInt64(7),
        Enum.Parse<IssueStatus>(r.GetString(8)));

    public Issue? GetIssue(string issueId)
    {
        using var conn = Open();
        return GetIssueTx(conn, null, issueId);
    }

    private static Issue? GetIssueTx(SqliteConnection conn, SqliteTransaction? tx, string issueId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT {SelectIssueColumns} FROM issues WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", issueId);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadIssue(r) : null;
    }

    public List<Issue> GetOpenIssues()
    {
        using var conn = Open();
        return QueryIssuesTx(conn, null, "status = 'Open'");
    }

    public List<Issue> GetAllIssues()
    {
        using var conn = Open();
        return QueryIssuesTx(conn, null, "1 = 1");
    }

    private static List<Issue> QueryIssuesTx(SqliteConnection conn, SqliteTransaction? tx, string where)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT {SelectIssueColumns} FROM issues WHERE {where} ORDER BY opened_resolution, title;";
        var list = new List<Issue>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(ReadIssue(r));
        return list;
    }

    public void OpenIssue(string id, string templateId, string title, string forText, string againstText, double opinion, double salience)
    {
        using var conn = Open();
        var state = GetGameStateTx(conn, null);
        Exec(conn, null, @"
            INSERT INTO issues (id, template_id, title, for_text, against_text, opinion, salience, status, opened_resolution)
            VALUES ($id, $template, $title, $for, $against, $opinion, $salience, 'Open', $resolution)
            ON CONFLICT(id) DO UPDATE SET status = 'Open', closed_resolution = NULL;",
            ("$id", id), ("$template", templateId), ("$title", title), ("$for", forText), ("$against", againstText),
            ("$opinion", opinion), ("$salience", salience), ("$resolution", state.ResolutionNumber));
    }

    public Stance? GetLatestLoggedStance(ulong factionId, string issueId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT stance FROM stance_log WHERE faction_id = $faction AND issue_id = $issue ORDER BY id DESC LIMIT 1;";
        cmd.Parameters.AddWithValue("$faction", factionId.ToString());
        cmd.Parameters.AddWithValue("$issue", issueId);
        return cmd.ExecuteScalar() is long stance ? (Stance)stance : null;
    }

    public List<(string IssueId, Stance Stance, StanceSource Source, bool OffProgramme, Season Season, int Year)> GetLatestStances(ulong factionId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT issue_id, stance, source, off_programme, season, year FROM stance_log
            WHERE id IN (SELECT MAX(id) FROM stance_log WHERE faction_id = $faction GROUP BY issue_id)
            ORDER BY id DESC;";
        cmd.Parameters.AddWithValue("$faction", factionId.ToString());
        var list = new List<(string, Stance, StanceSource, bool, Season, int)>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add((r.GetString(0), (Stance)r.GetInt32(1), Enum.Parse<StanceSource>(r.GetString(2)), r.GetInt64(3) != 0,
                Enum.Parse<Season>(r.GetString(4)), r.GetInt32(5)));
        return list;
    }
}
