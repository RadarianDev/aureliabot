using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

public record Faction(ulong UserId, string Name, double Popularity, long? PartyId);

// Player factions, one per Discord user. Party membership lives here (factions.party_id) rather
// than on the party — see the CREATE TABLE comment in InitializeDatabase.
public partial class DatabaseService
{
    private const string SelectFactionColumns = "user_id, name, popularity, party_id";

    private static Faction ReadFaction(SqliteDataReader r) => new(
        ulong.Parse(r.GetString(0)),
        r.GetString(1),
        r.GetDouble(2),
        r.IsDBNull(3) ? null : r.GetInt64(3));

    // Returns false if the user already has a faction.
    public bool CreateFaction(ulong userId, string name, double popularity = 0)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO factions (user_id, name, popularity)
            VALUES ($user, $name, $popularity)
            ON CONFLICT(user_id) DO NOTHING;";
        cmd.Parameters.AddWithValue("$user", userId.ToString());
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$popularity", popularity);
        return cmd.ExecuteNonQuery() > 0;
    }

    public bool DeleteFaction(ulong userId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM factions WHERE user_id = $user;";
        cmd.Parameters.AddWithValue("$user", userId.ToString());
        return cmd.ExecuteNonQuery() > 0;
    }

    public Faction? GetFaction(ulong userId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectFactionColumns} FROM factions WHERE user_id = $user;";
        cmd.Parameters.AddWithValue("$user", userId.ToString());
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadFaction(r) : null;
    }

    public List<Faction> GetAllFactions()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectFactionColumns} FROM factions ORDER BY popularity DESC, name;";
        var list = new List<Faction>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(ReadFaction(r));
        return list;
    }

    public bool RenameFaction(ulong userId, string name)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE factions SET name = $name WHERE user_id = $user;";
        cmd.Parameters.AddWithValue("$user", userId.ToString());
        cmd.Parameters.AddWithValue("$name", name);
        return cmd.ExecuteNonQuery() > 0;
    }

    public bool SetFactionPopularity(ulong userId, double popularity)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE factions SET popularity = $popularity WHERE user_id = $user;";
        cmd.Parameters.AddWithValue("$user", userId.ToString());
        cmd.Parameters.AddWithValue("$popularity", popularity);
        return cmd.ExecuteNonQuery() > 0;
    }

    // Moves a faction into a party, or out of any party when partyId is null. Joining a party
    // implicitly leaves the previous one. Throws SqliteException (foreign key) if partyId doesn't exist.
    public bool SetFactionParty(ulong userId, long? partyId)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE factions SET party_id = $party WHERE user_id = $user;";
        cmd.Parameters.AddWithValue("$user", userId.ToString());
        cmd.Parameters.AddWithValue("$party", (object?)partyId ?? DBNull.Value);
        return cmd.ExecuteNonQuery() > 0;
    }
}
