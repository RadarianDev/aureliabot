using System.Text.Json;

public sealed record ClearedDiscordIds(int Parties, int Factions);

public partial class DatabaseService
{
    public ClearedDiscordIds ClearDiscordIds(IEnumerable<ulong> roleIds, IEnumerable<ulong> channelIds)
    {
        var roles = JsonSerializer.Serialize(roleIds.Select(id => id.ToString()).ToList());
        var channels = JsonSerializer.Serialize(channelIds.Select(id => id.ToString()).ToList());

        using var conn = Open();
        using var tx = conn.BeginTransaction();
        var parties = ExecCount(conn, tx, @"
            UPDATE parties SET
                role_id = CASE WHEN role_id IN (SELECT value FROM json_each($roles)) THEN NULL ELSE role_id END,
                channel_id = CASE WHEN channel_id IN (SELECT value FROM json_each($channels)) THEN NULL ELSE channel_id END,
                foreign_channel_id = CASE WHEN foreign_channel_id IN (SELECT value FROM json_each($channels)) THEN NULL ELSE foreign_channel_id END
            WHERE role_id IN (SELECT value FROM json_each($roles)) OR channel_id IN (SELECT value FROM json_each($channels))
                OR foreign_channel_id IN (SELECT value FROM json_each($channels));",
            ("$roles", roles),
            ("$channels", channels));
        var factions = ExecCount(conn, tx,
            "UPDATE factions SET private_channel_id = NULL WHERE private_channel_id IN (SELECT value FROM json_each($channels));",
            ("$channels", channels));
        tx.Commit();
        return new ClearedDiscordIds(parties, factions);
    }
}
