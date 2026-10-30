public partial class DatabaseService
{
    public bool TryMarkReminderSent(ulong factionId, long resolutionNumber)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO reminders_sent (faction_id, resolution_number) VALUES ($faction, $resolution)
            ON CONFLICT(faction_id, resolution_number) DO NOTHING;";
        cmd.Parameters.AddWithValue("$faction", factionId.ToString());
        cmd.Parameters.AddWithValue("$resolution", resolutionNumber);
        return cmd.ExecuteNonQuery() > 0;
    }
}
