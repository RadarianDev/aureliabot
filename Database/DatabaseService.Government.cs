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

public sealed record OppositionStanding(bool UsedThisSeason, int BillsThisYear, int BillsAllowedPerYear)
{
    public bool CanAct => !UsedThisSeason;

    public bool CanProposeBill => !UsedThisSeason && BillsThisYear < BillsAllowedPerYear;
}

public sealed record CastVoteResult(bool Recorded, VoteChoice? Previous);

public partial class DatabaseService
{
    public FactionRole RoleOf(ulong factionId)
    {
        using var conn = Open();
        return RoleOfTx(conn, null, factionId);
    }

    private static FactionRole RoleOfTx(SqliteConnection conn, SqliteTransaction? tx, ulong factionId)
    {
        if (GetFactionTx(conn, tx, factionId) is not { IsActive: true } faction) return FactionRole.Independent;
        var cabinet = GetSittingCabinetTx(conn, tx);
        var holders = cabinet?.Holders.ToHashSet() ?? [];
        if (holders.Contains(factionId)) return FactionRole.Government;
        if (faction.PartyId is not { } partyId) return FactionRole.Independent;
        var governing = QueryFactionsTx(conn, tx, "party_id = $party AND status = 'Active'", ("$party", partyId))
            .Any(member => holders.Contains(member.UserId));
        return governing ? FactionRole.Government : FactionRole.Opposition;
    }

    public bool IsInCabinet(ulong factionId) => GetSittingCabinet()?.Holders.Contains(factionId) ?? false;

    public ulong? ResponsibleMinister(Ministry ministry)
    {
        using var conn = Open();
        return ResponsibleMinisterTx(conn, null, ministry);
    }

    private static ulong? ResponsibleMinisterTx(SqliteConnection conn, SqliteTransaction? tx, Ministry ministry)
    {
        var cabinet = GetSittingCabinetTx(conn, tx);
        return cabinet?.HolderOf(ministry) ?? cabinet?.ChancellorId;
    }

    public OppositionStanding GetOppositionStanding(ulong factionId, GameConstants constants)
    {
        using var conn = Open();
        return GetOppositionStandingTx(conn, null, factionId, constants);
    }

    private static OppositionStanding GetOppositionStandingTx(SqliteConnection conn, SqliteTransaction? tx, ulong factionId, GameConstants constants)
    {
        var state = GetGameStateTx(conn, tx);
        using var season = conn.CreateCommand();
        season.Transaction = tx;
        season.CommandText = "SELECT COUNT(*) FROM opposition_actions WHERE faction_id = $faction AND season = $season AND year = $year;";
        season.Parameters.AddWithValue("$faction", factionId.ToString());
        season.Parameters.AddWithValue("$season", state.Season.ToString());
        season.Parameters.AddWithValue("$year", state.Year);
        var usedThisSeason = Convert.ToInt64(season.ExecuteScalar()) >= constants.Opposition.ActionsPerSeason;

        using var bills = conn.CreateCommand();
        bills.Transaction = tx;
        bills.CommandText = "SELECT COUNT(*) FROM opposition_actions WHERE faction_id = $faction AND kind = 'Bill' AND year = $year AND cancelled = 0;";
        bills.Parameters.AddWithValue("$faction", factionId.ToString());
        bills.Parameters.AddWithValue("$year", state.Year);
        return new OppositionStanding(usedThisSeason, (int)Convert.ToInt64(bills.ExecuteScalar()), constants.Opposition.PrivateBillsPerYear);
    }

    private static long RecordOppositionActionTx(SqliteConnection conn, SqliteTransaction tx, ulong factionId, OppositionActionKind kind, long? subjectId)
    {
        var state = GetGameStateTx(conn, tx);
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO opposition_actions (faction_id, kind, season, year, subject_id, made_at)
            VALUES ($faction, $kind, $season, $year, $subject, $now) RETURNING id;";
        cmd.Parameters.AddWithValue("$faction", factionId.ToString());
        cmd.Parameters.AddWithValue("$kind", kind.ToString());
        cmd.Parameters.AddWithValue("$season", state.Season.ToString());
        cmd.Parameters.AddWithValue("$year", state.Year);
        cmd.Parameters.AddWithValue("$subject", (object?)subjectId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$now", Now());
        return (long)cmd.ExecuteScalar()!;
    }

    private static void RefundOppositionActionTx(SqliteConnection conn, SqliteTransaction tx, OppositionActionKind kind, long subjectId) =>
        Exec(conn, tx, "DELETE FROM opposition_actions WHERE kind = $kind AND subject_id = $subject;",
            ("$kind", kind.ToString()), ("$subject", subjectId));

    public CastVoteResult CastVote(VoteSubject subject, long subjectId, ulong factionId, VoteChoice choice)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        var result = CastVoteTx(conn, tx, subject, subjectId, factionId, choice);
        tx.Commit();
        return result;
    }

    private static CastVoteResult CastVoteTx(SqliteConnection conn, SqliteTransaction tx, VoteSubject subject, long subjectId, ulong factionId, VoteChoice choice)
    {
        var previous = GetVotesTx(conn, tx, subject, subjectId).TryGetValue(factionId, out var old) ? old : (VoteChoice?)null;
        Exec(conn, tx, @"
            INSERT INTO votes (subject_type, subject_id, faction_id, choice, cast_at) VALUES ($type, $id, $faction, $choice, $now)
            ON CONFLICT(subject_type, subject_id, faction_id) DO UPDATE SET choice = excluded.choice, cast_at = excluded.cast_at;",
            ("$type", subject.ToString()), ("$id", subjectId), ("$faction", factionId.ToString()), ("$choice", choice.ToString()), ("$now", Now()));
        LogTx(conn, tx, LogKinds.StateChange, "vote", $"{factionId} voted {choice} on {subject} {subjectId}.", factionId, isPublic: true);
        return new CastVoteResult(true, previous);
    }

    public Dictionary<ulong, VoteChoice> GetVotes(VoteSubject subject, long subjectId)
    {
        using var conn = Open();
        return GetVotesTx(conn, null, subject, subjectId);
    }

    private static Dictionary<ulong, VoteChoice> GetVotesTx(SqliteConnection conn, SqliteTransaction? tx, VoteSubject subject, long subjectId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT faction_id, choice FROM votes WHERE subject_type = $type AND subject_id = $id;";
        cmd.Parameters.AddWithValue("$type", subject.ToString());
        cmd.Parameters.AddWithValue("$id", subjectId);
        var map = new Dictionary<ulong, VoteChoice>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            map[ulong.Parse(r.GetString(0))] = Enum.Parse<VoteChoice>(r.GetString(1));
        return map;
    }

    private static Dictionary<ulong, int> VotingSeatsTx(SqliteConnection conn, SqliteTransaction? tx) =>
        QueryFactionsTx(conn, tx, "status = 'Active' AND seats > 0").ToDictionary(f => f.UserId, f => (int)f.Seats);

    public Dictionary<ulong, int> GetVotingSeats()
    {
        using var conn = Open();
        return VotingSeatsTx(conn, null);
    }
}
