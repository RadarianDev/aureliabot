using Microsoft.Data.Sqlite;

public sealed record ProjectSite(long Id, string TemplateId, Ministry Purse, long CreatedResolution);

public partial class DatabaseService
{
    public List<ProjectSite> GetWaitingSites()
    {
        using var conn = Open();
        return WaitingSitesTx(conn, null);
    }

    public List<ProjectTemplate> GetPlaceableProjectTemplates()
    {
        var waiting = GetWaitingSites().Select(s => s.TemplateId).ToHashSet();
        return GetTemplates(TemplateKind.Project, TemplateStatus.Ready)
            .Select(row => TemplateCodec.Parse(row.Yaml, row.Id).Template)
            .OfType<ProjectTemplate>()
            .Where(t => t.Ministerial || waiting.Contains(t.Id))
            .OrderBy(t => t.Title)
            .ToList();
    }

    private static List<ProjectSite> WaitingSitesTx(SqliteConnection conn, SqliteTransaction? tx, string? templateId = null)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT id, template_id, purse, created_resolution FROM project_sites WHERE status = 'Waiting'"
            + (templateId is null ? "" : " AND template_id = $template") + " ORDER BY id;";
        if (templateId is not null) cmd.Parameters.AddWithValue("$template", templateId);
        var list = new List<ProjectSite>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new ProjectSite(r.GetInt64(0), r.GetString(1), Enum.Parse<Ministry>(r.GetString(2)), r.GetInt64(3)));
        return list;
    }

    private static string? AwaitSiteTx(SqliteConnection conn, SqliteTransaction tx, ProjectTemplate template)
    {
        if (RepeatRefusalTx(conn, tx, template, null) is { } refusal) return refusal;
        if (WaitingSitesTx(conn, tx, template.Id).Count > 0)
            return Texts.Get("projects.warning.alreadyWaiting", ("project", template.Title));
        Exec(conn, tx, @"
            INSERT INTO project_sites (template_id, purse, created_resolution)
            VALUES ($template, $purse, $resolution);",
            ("$template", template.Id), ("$purse", template.Purse.ToString()),
            ("$resolution", GetGameStateTx(conn, tx).ResolutionNumber));
        return null;
    }

    private static void PlaceSiteTx(SqliteConnection conn, SqliteTransaction tx, string templateId)
    {
        if (WaitingSitesTx(conn, tx, templateId).FirstOrDefault() is { } site)
            Exec(conn, tx, "UPDATE project_sites SET status = 'Placed' WHERE id = $id;", ("$id", site.Id));
    }

    private static string? RepeatRefusalTx(SqliteConnection conn, SqliteTransaction tx, ProjectTemplate template, string? province)
    {
        const string built = "('Active', 'Stalled', 'Finished')";
        switch (template.Repeat)
        {
            case ProjectRepeat.Once when QueryProjectsTx(conn, tx, $"template_id = $template AND status IN {built}",
                ("$template", template.Id)).Count > 0:
                return Texts.Get("projects.warning.builtOnce", ("project", template.Title));
            case ProjectRepeat.PerProvince when province is not null && QueryProjectsTx(conn, tx,
                $"template_id = $template AND province_id = $province AND status IN {built}",
                ("$template", template.Id), ("$province", province)).Count > 0:
                return Texts.Get("projects.warning.builtHere", ("project", template.Title));
            default:
                return null;
        }
    }
}
