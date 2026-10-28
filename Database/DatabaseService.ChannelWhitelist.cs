using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

public partial class DatabaseService
{
    public bool AddWhitelistedChannel(ulong guildId, ulong channelId, ulong addedByUserId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO whitelisted_channels (guild_id, channel_id, added_by_user_id, added_at)
            VALUES ($guild, $channel, $user, $addedAt)
            ON CONFLICT(guild_id, channel_id) DO NOTHING;";
        cmd.Parameters.AddWithValue("$guild", guildId.ToString());
        cmd.Parameters.AddWithValue("$channel", channelId.ToString());
        cmd.Parameters.AddWithValue("$user", addedByUserId.ToString());
        cmd.Parameters.AddWithValue("$addedAt", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        return cmd.ExecuteNonQuery() > 0;
    }

    public bool RemoveWhitelistedChannel(ulong guildId, ulong channelId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM whitelisted_channels WHERE guild_id = $guild AND channel_id = $channel;";
        cmd.Parameters.AddWithValue("$guild", guildId.ToString());
        cmd.Parameters.AddWithValue("$channel", channelId.ToString());
        return cmd.ExecuteNonQuery() > 0;
    }

    public List<ulong> GetWhitelistedChannels(ulong guildId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT channel_id FROM whitelisted_channels WHERE guild_id = $guild ORDER BY added_at;";
        cmd.Parameters.AddWithValue("$guild", guildId.ToString());
        var list = new List<ulong>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(ulong.Parse(r.GetString(0)));
        return list;
    }

    public bool IsChannelAllowed(ulong guildId, ulong channelId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var total = conn.CreateCommand();
        total.CommandText = "SELECT COUNT(*) FROM whitelisted_channels WHERE guild_id = $guild;";
        total.Parameters.AddWithValue("$guild", guildId.ToString());
        if (Convert.ToInt64(total.ExecuteScalar()) == 0) return true;

        var match = conn.CreateCommand();
        match.CommandText = "SELECT COUNT(*) FROM whitelisted_channels WHERE guild_id = $guild AND channel_id = $channel;";
        match.Parameters.AddWithValue("$guild", guildId.ToString());
        match.Parameters.AddWithValue("$channel", channelId.ToString());
        return Convert.ToInt64(match.ExecuteScalar()) > 0;
    }
}
