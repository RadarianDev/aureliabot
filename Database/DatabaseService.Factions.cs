using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

public record Faction(
    ulong UserId,
    string CharacterName,
    double Popularity,
    long Seats,
    long? PartyId,
    string? HomeProvinceId,
    ulong? PrivateChannelId,
    FactionStatus Status,
    long SenateSeats = 0)
{
    public bool IsActive => Status == FactionStatus.Active;

    public bool IsIndependent => PartyId is null;
}

public sealed record RetireResult(Faction Faction, Party? DissolvedParty, ulong? DissolvedLastMemberId = null);

public partial class DatabaseService
{
    private const string SelectFactionColumns =
        "user_id, character_name, popularity, seats, party_id, home_province_id, private_channel_id, status, senate_seats";

    private static Faction ReadFaction(SqliteDataReader r) => new(
        ulong.Parse(r.GetString(0)),
        r.GetString(1),
        r.GetDouble(2),
        r.GetInt64(3),
        r.IsDBNull(4) ? null : r.GetInt64(4),
        r.IsDBNull(5) ? null : r.GetString(5),
        r.IsDBNull(6) ? null : ulong.Parse(r.GetString(6)),
        Enum.Parse<FactionStatus>(r.GetString(7)),
        r.GetInt64(8));

    public bool CreateFaction(ulong userId, string characterName, string homeProvinceId, double popularity)
    {
        using var conn = Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO factions (user_id, character_name, home_province_id, popularity, seats, status, created_at)
            VALUES ($user, $character, $home, $popularity, 0, 'Active', $now)
            ON CONFLICT(user_id) DO NOTHING;";
        cmd.Parameters.AddWithValue("$user", userId.ToString());
        cmd.Parameters.AddWithValue("$character", characterName);
        cmd.Parameters.AddWithValue("$home", homeProvinceId);
        cmd.Parameters.AddWithValue("$popularity", popularity);
        cmd.Parameters.AddWithValue("$now", Now());
        return cmd.ExecuteNonQuery() > 0;
    }

    public Faction? GetFaction(ulong userId)
    {
        using var conn = Open();
        return GetFactionTx(conn, null, userId);
    }

    private static Faction? GetFactionTx(SqliteConnection conn, SqliteTransaction? tx, ulong userId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT {SelectFactionColumns} FROM factions WHERE user_id = $user;";
        cmd.Parameters.AddWithValue("$user", userId.ToString());
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadFaction(r) : null;
    }

    public List<Faction> GetAllFactions()
    {
        using var conn = Open();
        return QueryFactionsTx(conn, null, "1 = 1");
    }

    public List<Faction> GetActiveFactions()
    {
        using var conn = Open();
        return QueryFactionsTx(conn, null, "status = 'Active'");
    }

    private static List<Faction> QueryFactionsTx(SqliteConnection conn, SqliteTransaction? tx, string where, params (string Name, object? Value)[] parameters)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT {SelectFactionColumns} FROM factions WHERE {where} ORDER BY popularity DESC, seats DESC, user_id;";
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        var list = new List<Faction>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(ReadFaction(r));
        return list;
    }

    public bool UpdateFaction(ulong userId, string? characterName, string? homeProvinceId)
    {
        using var conn = Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            UPDATE factions SET
                character_name = COALESCE($character, character_name),
                home_province_id = COALESCE($home, home_province_id)
            WHERE user_id = $user;";
        cmd.Parameters.AddWithValue("$user", userId.ToString());
        cmd.Parameters.AddWithValue("$character", (object?)characterName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$home", (object?)homeProvinceId ?? DBNull.Value);
        return cmd.ExecuteNonQuery() > 0;
    }

    public void SetFactionPrivateChannel(ulong userId, ulong? channelId)
    {
        using var conn = Open();
        Exec(conn, null, "UPDATE factions SET private_channel_id = $channel WHERE user_id = $user;",
            ("$user", userId.ToString()),
            ("$channel", channelId?.ToString()));
    }

    public bool SetFactionSeats(ulong userId, long seats)
    {
        using var conn = Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE factions SET seats = $seats WHERE user_id = $user;";
        cmd.Parameters.AddWithValue("$user", userId.ToString());
        cmd.Parameters.AddWithValue("$seats", seats);
        return cmd.ExecuteNonQuery() > 0;
    }

    public bool SetFactionSenateSeats(ulong userId, long seats)
    {
        using var conn = Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE factions SET senate_seats = $seats WHERE user_id = $user;";
        cmd.Parameters.AddWithValue("$user", userId.ToString());
        cmd.Parameters.AddWithValue("$seats", seats);
        return cmd.ExecuteNonQuery() > 0;
    }

    public bool IsHomeProvinceFull(string provinceId, GameConstants constants) =>
        GetActiveFactions().Count(f => f.HomeProvinceId == provinceId) >= constants.Senate.MaxHomeFactions;

    public double? AddPopularity(ulong userId, double delta, PopularityConstants range)
    {
        using var conn = Open();
        return AddPopularityTx(conn, null, userId, delta, range);
    }

    private static double? AddPopularityTx(SqliteConnection conn, SqliteTransaction? tx, ulong userId, double delta, PopularityConstants range)
    {
        if (GetFactionTx(conn, tx, userId) is not { } faction) return null;
        var updated = Math.Clamp(faction.Popularity + delta, range.Min, range.Max);
        Exec(conn, tx, "UPDATE factions SET popularity = $popularity WHERE user_id = $user;",
            ("$user", userId.ToString()),
            ("$popularity", updated));
        return updated;
    }

    public RetireResult? RetireFaction(ulong userId)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        if (GetFactionTx(conn, tx, userId) is not { IsActive: true } faction) return null;
        var id = userId.ToString();

        Exec(conn, tx, "UPDATE factions SET status = 'Retired', party_id = NULL WHERE user_id = $user;", ("$user", id));
        Exec(conn, tx, @"
            UPDATE cabinet_posts SET faction_id = NULL
            WHERE faction_id = $user AND cabinet_id IN (SELECT id FROM cabinets WHERE kind = 'Sitting');", ("$user", id));
        Exec(conn, tx, "UPDATE cabinets SET chancellor_faction_id = NULL WHERE kind = 'Sitting' AND chancellor_faction_id = $user;", ("$user", id));
        Exec(conn, tx, @"
            UPDATE cabinets SET status = 'Cancelled'
            WHERE kind = 'Nominated' AND status IN ('Forming', 'Voting')
              AND (chancellor_faction_id = $user OR id IN (SELECT cabinet_id FROM cabinet_posts WHERE faction_id = $user));", ("$user", id));
        Exec(conn, tx, "DELETE FROM actions WHERE faction_id = $user AND resolved = 0;", ("$user", id));
        Exec(conn, tx, "UPDATE party_join_requests SET status = 'Dropped' WHERE faction_id = $user AND status = 'Pending';", ("$user", id));

        var (dissolved, lastMember) = faction.PartyId is { } partyId ? DissolveIfUnderTwoTx(conn, tx, partyId) : (null, null);
        if (dissolved is null && faction.PartyId is { } stillStanding)
            RetallyJoinRequestsTx(conn, tx, stillStanding);
        LogTx(conn, tx, LogKinds.StateChange, "gm faction retire", $"Retired {id}.", userId);
        tx.Commit();
        return new RetireResult(faction, dissolved, lastMember);
    }

    public Faction? UnretireFaction(ulong userId)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        if (GetFactionTx(conn, tx, userId) is not { IsActive: false }) return null;
        Exec(conn, tx, "UPDATE factions SET status = 'Active', party_id = NULL WHERE user_id = $user;", ("$user", userId.ToString()));
        LogTx(conn, tx, LogKinds.StateChange, "gm faction unretire", $"Unretired {userId}.", userId);
        var faction = GetFactionTx(conn, tx, userId);
        tx.Commit();
        return faction;
    }

    public bool TryMarkUnregisteredAlert(ulong userId)
    {
        using var conn = Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO alerted_users (user_id, alerted_at) VALUES ($user, $now) ON CONFLICT(user_id) DO NOTHING;";
        cmd.Parameters.AddWithValue("$user", userId.ToString());
        cmd.Parameters.AddWithValue("$now", Now());
        return cmd.ExecuteNonQuery() > 0;
    }
}
