using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

public record Cabinet(
    long Id,
    CabinetKind Kind,
    ulong? ChancellorId,
    CabinetStatus Status,
    IReadOnlyDictionary<Ministry, ulong?> Posts,
    long? DueResolution)
{
    public IEnumerable<ulong> Holders =>
        Posts.Values.OfType<ulong>().Concat(ChancellorId is { } c ? [c] : []).Distinct();

    public ulong? HolderOf(Ministry ministry) => Posts.TryGetValue(ministry, out var holder) ? holder : null;
}

public partial class DatabaseService
{
    public Cabinet? GetSittingCabinet()
    {
        using var conn = Open();
        return GetSittingCabinetTx(conn, null);
    }

    private static Cabinet? GetSittingCabinetTx(SqliteConnection conn, SqliteTransaction? tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            SELECT id, kind, chancellor_faction_id, status, due_resolution FROM cabinets
            WHERE kind = 'Sitting' AND status IN ('Approved', 'Caretaker') ORDER BY id DESC LIMIT 1;";
        long id;
        CabinetKind kind;
        ulong? chancellor;
        CabinetStatus status;
        long? due;
        using (var r = cmd.ExecuteReader())
        {
            if (!r.Read()) return null;
            id = r.GetInt64(0);
            kind = Enum.Parse<CabinetKind>(r.GetString(1));
            chancellor = r.IsDBNull(2) ? null : ulong.Parse(r.GetString(2));
            status = Enum.Parse<CabinetStatus>(r.GetString(3));
            due = r.IsDBNull(4) ? null : r.GetInt64(4);
        }
        return new Cabinet(id, kind, chancellor, status, GetCabinetPostsTx(conn, tx, id), due);
    }

    private static Dictionary<Ministry, ulong?> GetCabinetPostsTx(SqliteConnection conn, SqliteTransaction? tx, long cabinetId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT ministry, faction_id FROM cabinet_posts WHERE cabinet_id = $id;";
        cmd.Parameters.AddWithValue("$id", cabinetId);
        var posts = GameEnumExtensions.CabinetMinistries.ToDictionary(m => m, _ => (ulong?)null);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            posts[Enum.Parse<Ministry>(r.GetString(0))] = r.IsDBNull(1) ? null : ulong.Parse(r.GetString(1));
        return posts;
    }

    public HashSet<long> GetGoverningPartyIds()
    {
        var cabinet = GetSittingCabinet();
        if (cabinet is null) return [];
        var holders = cabinet.Holders.ToHashSet();
        return GetActiveFactions().Where(f => holders.Contains(f.UserId) && f.PartyId is not null)
            .Select(f => f.PartyId!.Value).ToHashSet();
    }
}
