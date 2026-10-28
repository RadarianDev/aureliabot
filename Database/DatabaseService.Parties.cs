using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

public record Party(long Id, string Name, string Colour, ulong? EmojiId);

public partial class DatabaseService
{
    private const string SelectPartyColumns = "id, name, colour, emoji_id";

    private static Party ReadParty(SqliteDataReader r) => new(
        r.GetInt64(0),
        r.GetString(1),
        r.GetString(2),
        r.IsDBNull(3) ? null : ulong.Parse(r.GetString(3)));

    public long? CreateParty(string name, string colour, ulong? emojiId = null)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO parties (name, colour, emoji_id)
            VALUES ($name, $colour, $emoji)
            ON CONFLICT(name) DO NOTHING
            RETURNING id;";
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$colour", colour);
        cmd.Parameters.AddWithValue("$emoji", (object?)emojiId?.ToString() ?? DBNull.Value);
        return cmd.ExecuteScalar() is long id ? id : null;
    }

    public bool DeleteParty(long partyId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM parties WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", partyId);
        return cmd.ExecuteNonQuery() > 0;
    }

    public Party? GetParty(long partyId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectPartyColumns} FROM parties WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", partyId);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadParty(r) : null;
    }

    public Party? GetPartyByName(string name)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectPartyColumns} FROM parties WHERE name = $name;";
        cmd.Parameters.AddWithValue("$name", name);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadParty(r) : null;
    }

    public List<Party> GetAllParties()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectPartyColumns} FROM parties ORDER BY name;";
        var list = new List<Party>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(ReadParty(r));
        return list;
    }

    public List<Faction> GetPartyMembers(long partyId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectFactionColumns} FROM factions WHERE party_id = $id ORDER BY popularity DESC, seats DESC;";
        cmd.Parameters.AddWithValue("$id", partyId);
        var list = new List<Faction>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(ReadFaction(r));
        return list;
    }

    public bool UpdateParty(long partyId, string name, string colour, ulong? emojiId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE OR IGNORE parties SET name = $name, colour = $colour, emoji_id = $emoji WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", partyId);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$colour", colour);
        cmd.Parameters.AddWithValue("$emoji", (object?)emojiId?.ToString() ?? DBNull.Value);
        return cmd.ExecuteNonQuery() > 0;
    }
}
