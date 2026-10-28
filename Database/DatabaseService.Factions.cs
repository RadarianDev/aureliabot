using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

public record Faction(ulong UserId, double Popularity, long Seats, long? PartyId);

public partial class DatabaseService
{
    private const string SelectFactionColumns = "user_id, popularity, seats, party_id";

    private static Faction ReadFaction(SqliteDataReader r) => new(
        ulong.Parse(r.GetString(0)),
        r.GetDouble(1),
        r.GetInt64(2),
        r.IsDBNull(3) ? null : r.GetInt64(3));

    public bool CreateFaction(ulong userId, double popularity = 0, long seats = 0)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO factions (user_id, popularity, seats)
            VALUES ($user, $popularity, $seats)
            ON CONFLICT(user_id) DO NOTHING;";
        cmd.Parameters.AddWithValue("$user", userId.ToString());
        cmd.Parameters.AddWithValue("$popularity", popularity);
        cmd.Parameters.AddWithValue("$seats", seats);
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
        cmd.CommandText = $"SELECT {SelectFactionColumns} FROM factions ORDER BY popularity DESC, seats DESC;";
        var list = new List<Faction>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(ReadFaction(r));
        return list;
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

    public bool SetFactionSeats(ulong userId, long seats)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE factions SET seats = $seats WHERE user_id = $user;";
        cmd.Parameters.AddWithValue("$user", userId.ToString());
        cmd.Parameters.AddWithValue("$seats", seats);
        return cmd.ExecuteNonQuery() > 0;
    }

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
