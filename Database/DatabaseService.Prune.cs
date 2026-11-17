public enum InPlayKind { BillBeforeTheAssembly, LiveCrisis, LiveProject }

public sealed record InPlay(string TemplateId, InPlayKind Kind);

public partial class DatabaseService
{
    public List<(string Key, string Value)> GetSettingsWithPrefix(string prefix)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT key, value FROM settings WHERE substr(key, 1, length($prefix)) = $prefix ORDER BY key;";
        cmd.Parameters.AddWithValue("$prefix", prefix);
        var list = new List<(string, string)>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add((r.GetString(0), r.GetString(1)));
        return list;
    }

    public int CancelQueuedEvents(string templateId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE events SET status = 'Cancelled' WHERE template_id = $template AND status = 'Queued';";
        cmd.Parameters.AddWithValue("$template", templateId);
        return cmd.ExecuteNonQuery();
    }

    public bool WithdrawIdleBill(string billId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE bills SET status = 'Withdrawn' WHERE template_id = $id AND status IN ('Available', 'Cooldown');";
        cmd.Parameters.AddWithValue("$id", billId);
        return cmd.ExecuteNonQuery() > 0;
    }

    public List<InPlay> StillInPlay(string templateId)
    {
        var found = new List<InPlay>();
        if (GetBills().Any(b => b.TemplateId == templateId && b.Status == BillStatus.Voting))
            found.Add(new InPlay(templateId, InPlayKind.BillBeforeTheAssembly));
        if (GetCrises(CrisisStatus.Live).Any(c => c.TemplateId == templateId))
            found.Add(new InPlay(templateId, InPlayKind.LiveCrisis));
        using var conn = Open();
        if (LiveProjectOfTemplateTx(conn, null, templateId) is not null)
            found.Add(new InPlay(templateId, InPlayKind.LiveProject));
        return found;
    }
}
