using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

public record Party(
    long Id,
    string Name,
    string Colour,
    ulong? EmojiId,
    Season? CreatedSeason,
    int? CreatedYear,
    ulong? RoleId,
    ulong? ChannelId,
    string? ProgrammeMessageUrl,
    ulong? ForeignChannelId);

public record JoinRequest(long Id, ulong FactionId, long PartyId, DateTimeOffset RequestedAt, JoinRequestStatus Status, ulong? MessageId);

public enum PartyCreateOutcome { Created, NameTaken, TooFewFounders, FounderNotRegistered, FounderNotIndependent }

public sealed record PartyCreateResult(PartyCreateOutcome Outcome, long? PartyId, ulong? OffendingFaction = null);

public sealed record MoveResult(Faction Faction, long? FromPartyId, Party? DissolvedParty, ulong? DissolvedLastMemberId = null);

public enum JoinRequestOutcome { Created, NotIndependent, NotRegistered, AlreadyPending, NoSuchParty }

public sealed record JoinRequestResult(JoinRequestOutcome Outcome, long? RequestId);

public enum JoinAnswerOutcome { Recorded, Accepted, Declined, NotPending, NotAMember, Dropped }

public sealed record JoinAnswerResult(JoinAnswerOutcome Outcome, JoinRequest? Request, int Accepts, int Declines, int Members, Party? DissolvedParty = null);

public sealed record ProgrammeRecordResult(IReadOnlyList<string> FlippedIssueIds, double PenaltyPerMember, IReadOnlyList<ulong> PenalisedFactions);

public partial class DatabaseService
{
    private const string SelectPartyColumns =
        "id, name, colour, emoji_id, created_season, created_year, role_id, channel_id, programme_message_url, foreign_channel_id";

    private static Party ReadParty(SqliteDataReader r) => new(
        r.GetInt64(0),
        r.GetString(1),
        r.GetString(2),
        r.IsDBNull(3) ? null : ulong.Parse(r.GetString(3)),
        r.IsDBNull(4) ? null : Enum.Parse<Season>(r.GetString(4)),
        r.IsDBNull(5) ? null : r.GetInt32(5),
        r.IsDBNull(6) ? null : ulong.Parse(r.GetString(6)),
        r.IsDBNull(7) ? null : ulong.Parse(r.GetString(7)),
        r.IsDBNull(8) ? null : r.GetString(8),
        r.IsDBNull(9) ? null : ulong.Parse(r.GetString(9)));

    private const string SelectJoinRequestColumns = "id, faction_id, party_id, requested_at, status, message_id";

    private static JoinRequest ReadJoinRequest(SqliteDataReader r) => new(
        r.GetInt64(0),
        ulong.Parse(r.GetString(1)),
        r.GetInt64(2),
        DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(3)),
        Enum.Parse<JoinRequestStatus>(r.GetString(4)),
        r.IsDBNull(5) ? null : ulong.Parse(r.GetString(5)));

    public PartyCreateResult CreateParty(string name, string colour, ulong? emojiId, IReadOnlyCollection<ulong> founders)
    {
        var distinct = founders.Distinct().ToList();
        if (distinct.Count < 2) return new PartyCreateResult(PartyCreateOutcome.TooFewFounders, null);

        using var conn = Open();
        using var tx = conn.BeginTransaction();
        foreach (var founder in distinct)
        {
            var faction = GetFactionTx(conn, tx, founder);
            if (faction is not { IsActive: true }) return new PartyCreateResult(PartyCreateOutcome.FounderNotRegistered, null, founder);
            if (!faction.IsIndependent) return new PartyCreateResult(PartyCreateOutcome.FounderNotIndependent, null, founder);
        }
        if (GetPartyByNameTx(conn, tx, name) is not null) return new PartyCreateResult(PartyCreateOutcome.NameTaken, null);

        var state = GetGameStateTx(conn, tx);
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO parties (name, colour, emoji_id, created_season, created_year, created_at)
            VALUES ($name, $colour, $emoji, $season, $year, $now)
            RETURNING id;";
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$colour", colour);
        cmd.Parameters.AddWithValue("$emoji", (object?)emojiId?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$season", state.Season.ToString());
        cmd.Parameters.AddWithValue("$year", state.Year);
        cmd.Parameters.AddWithValue("$now", Now());
        var partyId = (long)cmd.ExecuteScalar()!;

        foreach (var founder in distinct)
            JoinPartyTx(conn, tx, founder, partyId);
        LogTx(conn, tx, LogKinds.StateChange, "gm party create", $"Founded party {partyId} '{name}' with {string.Join(", ", distinct)}.");
        tx.Commit();
        return new PartyCreateResult(PartyCreateOutcome.Created, partyId);
    }

    public Party? GetParty(long partyId)
    {
        using var conn = Open();
        return GetPartyTx(conn, null, partyId);
    }

    private static Party? GetPartyTx(SqliteConnection conn, SqliteTransaction? tx, long partyId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT {SelectPartyColumns} FROM parties WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", partyId);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadParty(r) : null;
    }

    public Party? GetPartyByName(string name)
    {
        using var conn = Open();
        return GetPartyByNameTx(conn, null, name);
    }

    private static Party? GetPartyByNameTx(SqliteConnection conn, SqliteTransaction? tx, string name)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT {SelectPartyColumns} FROM parties WHERE name = $name;";
        cmd.Parameters.AddWithValue("$name", name);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadParty(r) : null;
    }

    public List<Party> GetAllParties()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectPartyColumns} FROM parties ORDER BY id;";
        var list = new List<Party>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(ReadParty(r));
        return list;
    }

    public List<Faction> GetPartyMembers(long partyId)
    {
        using var conn = Open();
        return QueryFactionsTx(conn, null, "party_id = $party AND status = 'Active'", ("$party", partyId));
    }

    public bool UpdateParty(long partyId, string name, string colour, ulong? emojiId)
    {
        using var conn = Open();
        var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE OR IGNORE parties SET name = $name, colour = $colour, emoji_id = $emoji WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", partyId);
        cmd.Parameters.AddWithValue("$name", name);
        cmd.Parameters.AddWithValue("$colour", colour);
        cmd.Parameters.AddWithValue("$emoji", (object?)emojiId?.ToString() ?? DBNull.Value);
        return cmd.ExecuteNonQuery() > 0;
    }

    public void SetPartyDiscordIds(long partyId, ulong? roleId, ulong? channelId, ulong? foreignChannelId)
    {
        using var conn = Open();
        Exec(conn, null, "UPDATE parties SET role_id = $role, channel_id = $channel, foreign_channel_id = $foreign WHERE id = $id;",
            ("$id", partyId),
            ("$role", roleId?.ToString()),
            ("$channel", channelId?.ToString()),
            ("$foreign", foreignChannelId?.ToString()));
    }

    public MoveResult? MoveFaction(ulong userId, long? partyId, string source)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        if (GetFactionTx(conn, tx, userId) is not { IsActive: true } faction) return null;
        if (partyId is { } target && GetPartyTx(conn, tx, target) is null) return null;
        if (faction.PartyId == partyId) return new MoveResult(faction, faction.PartyId, null);

        if (partyId is { } joined)
            JoinPartyTx(conn, tx, userId, joined);
        else
            Exec(conn, tx, "UPDATE factions SET party_id = NULL WHERE user_id = $user;", ("$user", userId.ToString()));
        var (dissolved, lastMember) = faction.PartyId is { } from ? DissolveIfUnderTwoTx(conn, tx, from) : (null, null);
        if (dissolved is null && faction.PartyId is { } stillStanding)
            RetallyJoinRequestsTx(conn, tx, stillStanding);
        LogTx(conn, tx, LogKinds.StateChange, source, $"Moved {userId} from {faction.PartyId?.ToString() ?? "independent"} to {partyId?.ToString() ?? "independent"}.", userId);
        tx.Commit();
        return new MoveResult(faction with { PartyId = partyId }, faction.PartyId, dissolved, lastMember);
    }

    public Party? DeleteParty(long partyId, string source)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        var party = DeletePartyTx(conn, tx, partyId);
        if (party is not null)
            LogTx(conn, tx, LogKinds.StateChange, source, $"Deleted party {partyId} '{party.Name}'.");
        tx.Commit();
        return party;
    }

    private static Party? DeletePartyTx(SqliteConnection conn, SqliteTransaction tx, long partyId)
    {
        if (GetPartyTx(conn, tx, partyId) is not { } party) return null;
        Exec(conn, tx, "UPDATE factions SET party_id = NULL WHERE party_id = $party;", ("$party", partyId));
        Exec(conn, tx, "DELETE FROM parties WHERE id = $party;", ("$party", partyId));
        return party;
    }

    private static (Party? Party, ulong? LastMemberId) DissolveIfUnderTwoTx(SqliteConnection conn, SqliteTransaction tx, long partyId)
    {
        var members = QueryFactionsTx(conn, tx, "party_id = $party AND status = 'Active'", ("$party", partyId));
        if (members.Count >= 2) return (null, null);
        var party = DeletePartyTx(conn, tx, partyId);
        if (party is not null)
            LogTx(conn, tx, LogKinds.StateChange, "dissolution", $"Party {partyId} '{party.Name}' dissolved with {members.Count} active member(s).");
        return (party, members.Count == 1 ? members[0].UserId : null);
    }

    public JoinRequestResult CreateJoinRequest(ulong userId, long partyId)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        if (GetFactionTx(conn, tx, userId) is not { IsActive: true } faction) return new JoinRequestResult(JoinRequestOutcome.NotRegistered, null);
        if (!faction.IsIndependent) return new JoinRequestResult(JoinRequestOutcome.NotIndependent, null);
        if (GetPartyTx(conn, tx, partyId) is null) return new JoinRequestResult(JoinRequestOutcome.NoSuchParty, null);

        using (var check = conn.CreateCommand())
        {
            check.Transaction = tx;
            check.CommandText = "SELECT COUNT(*) FROM party_join_requests WHERE faction_id = $user AND party_id = $party AND status = 'Pending';";
            check.Parameters.AddWithValue("$user", userId.ToString());
            check.Parameters.AddWithValue("$party", partyId);
            if (Convert.ToInt64(check.ExecuteScalar()) > 0) return new JoinRequestResult(JoinRequestOutcome.AlreadyPending, null);
        }

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO party_join_requests (faction_id, party_id, requested_at, status)
            VALUES ($user, $party, $now, 'Pending') RETURNING id;";
        cmd.Parameters.AddWithValue("$user", userId.ToString());
        cmd.Parameters.AddWithValue("$party", partyId);
        cmd.Parameters.AddWithValue("$now", Now());
        var id = (long)cmd.ExecuteScalar()!;
        LogTx(conn, tx, LogKinds.StateChange, "party apply", $"{userId} applied to party {partyId} (request {id}).", userId);
        tx.Commit();
        return new JoinRequestResult(JoinRequestOutcome.Created, id);
    }

    public void SetJoinRequestMessage(long requestId, ulong messageId)
    {
        using var conn = Open();
        Exec(conn, null, "UPDATE party_join_requests SET message_id = $message WHERE id = $id;",
            ("$id", requestId), ("$message", messageId.ToString()));
    }

    public JoinRequest? GetJoinRequest(long requestId)
    {
        using var conn = Open();
        return GetJoinRequestTx(conn, null, requestId);
    }

    private static JoinRequest? GetJoinRequestTx(SqliteConnection conn, SqliteTransaction? tx, long requestId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT {SelectJoinRequestColumns} FROM party_join_requests WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", requestId);
        using var r = cmd.ExecuteReader();
        return r.Read() ? ReadJoinRequest(r) : null;
    }

    public List<JoinRequest> GetPendingJoinRequests()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {SelectJoinRequestColumns} FROM party_join_requests WHERE status = 'Pending' ORDER BY id;";
        var list = new List<JoinRequest>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(ReadJoinRequest(r));
        return list;
    }

    public Dictionary<ulong, bool> GetJoinAnswers(long requestId)
    {
        using var conn = Open();
        return GetJoinAnswersTx(conn, null, requestId);
    }

    private static Dictionary<ulong, bool> GetJoinAnswersTx(SqliteConnection conn, SqliteTransaction? tx, long requestId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT faction_id, accept FROM party_join_answers WHERE request_id = $id;";
        cmd.Parameters.AddWithValue("$id", requestId);
        var map = new Dictionary<ulong, bool>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            map[ulong.Parse(r.GetString(0))] = r.GetInt64(1) != 0;
        return map;
    }

    public JoinAnswerResult AnswerJoinRequest(long requestId, ulong voterId, bool accept)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        var request = GetJoinRequestTx(conn, tx, requestId);
        if (request is not { Status: JoinRequestStatus.Pending })
            return new JoinAnswerResult(JoinAnswerOutcome.NotPending, request, 0, 0, 0);

        var members = QueryFactionsTx(conn, tx, "party_id = $party AND status = 'Active'", ("$party", request.PartyId));
        if (members.All(m => m.UserId != voterId))
            return new JoinAnswerResult(JoinAnswerOutcome.NotAMember, request, 0, 0, members.Count);

        if (!GetGameStateTx(conn, tx).IsJoiningOpen)
        {
            SetJoinRequestStatusTx(conn, tx, requestId, JoinRequestStatus.Dropped);
            tx.Commit();
            return new JoinAnswerResult(JoinAnswerOutcome.NotPending, request with { Status = JoinRequestStatus.Dropped }, 0, 0, members.Count);
        }

        Exec(conn, tx, @"
            INSERT INTO party_join_answers (request_id, faction_id, accept, answered_at) VALUES ($id, $voter, $accept, $now)
            ON CONFLICT(request_id, faction_id) DO UPDATE SET accept = excluded.accept, answered_at = excluded.answered_at;",
            ("$id", requestId), ("$voter", voterId.ToString()), ("$accept", accept ? 1 : 0), ("$now", Now()));

        var (outcome, accepts, declines) = TallyJoinRequestTx(conn, tx, request, members);
        LogTx(conn, tx, LogKinds.StateChange, "party join answer", $"{voterId} answered request {requestId}: {(accept ? "accept" : "decline")} -> {outcome}.", voterId);
        tx.Commit();
        return new JoinAnswerResult(outcome, GetJoinRequest(requestId), accepts, declines, members.Count);
    }

    private static (JoinAnswerOutcome Outcome, int Accepts, int Declines) TallyJoinRequestTx(
        SqliteConnection conn, SqliteTransaction tx, JoinRequest request, List<Faction> members)
    {
        var memberIds = members.Select(m => m.UserId).ToHashSet();
        var answers = GetJoinAnswersTx(conn, tx, request.Id).Where(a => memberIds.Contains(a.Key)).ToList();
        var accepts = answers.Count(a => a.Value);
        var declines = answers.Count(a => !a.Value);

        if (accepts * 2 > members.Count)
        {
            if (GetFactionTx(conn, tx, request.FactionId) is { IsActive: true, IsIndependent: true })
            {
                JoinPartyTx(conn, tx, request.FactionId, request.PartyId);
                SetJoinRequestStatusTx(conn, tx, request.Id, JoinRequestStatus.Accepted);
                return (JoinAnswerOutcome.Accepted, accepts, declines);
            }
            SetJoinRequestStatusTx(conn, tx, request.Id, JoinRequestStatus.Dropped);
            return (JoinAnswerOutcome.Dropped, accepts, declines);
        }
        if (declines * 2 > members.Count)
        {
            SetJoinRequestStatusTx(conn, tx, request.Id, JoinRequestStatus.Declined);
            return (JoinAnswerOutcome.Declined, accepts, declines);
        }
        return (JoinAnswerOutcome.Recorded, accepts, declines);
    }

    private static void RetallyJoinRequestsTx(SqliteConnection conn, SqliteTransaction tx, long partyId)
    {
        var pending = new List<JoinRequest>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = $"SELECT {SelectJoinRequestColumns} FROM party_join_requests WHERE party_id = $party AND status = 'Pending' ORDER BY id;";
            cmd.Parameters.AddWithValue("$party", partyId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) pending.Add(ReadJoinRequest(r));
        }
        foreach (var request in pending)
        {
            var members = QueryFactionsTx(conn, tx, "party_id = $party AND status = 'Active'", ("$party", partyId));
            if (members.Count == 0) return;
            TallyJoinRequestTx(conn, tx, request, members);
        }
    }

    private static void JoinPartyTx(SqliteConnection conn, SqliteTransaction tx, ulong userId, long partyId)
    {
        Exec(conn, tx, "UPDATE factions SET party_id = $party WHERE user_id = $user;",
            ("$party", partyId), ("$user", userId.ToString()));
        Exec(conn, tx, "UPDATE party_join_requests SET status = 'Dropped' WHERE faction_id = $user AND status = 'Pending';",
            ("$user", userId.ToString()));
        var state = GetGameStateTx(conn, tx);
        foreach (var (issueId, stance) in GetProgrammeTx(conn, tx, partyId))
            InsertStanceLogTx(conn, tx, state, userId, issueId, stance, StanceSource.Programme, offProgramme: false);
    }

    private static void DropPendingJoinRequestsTx(SqliteConnection conn, SqliteTransaction tx) =>
        Exec(conn, tx, "UPDATE party_join_requests SET status = 'Dropped' WHERE status = 'Pending';");

    private static void SetJoinRequestStatusTx(SqliteConnection conn, SqliteTransaction tx, long requestId, JoinRequestStatus status) =>
        Exec(conn, tx, "UPDATE party_join_requests SET status = $status WHERE id = $id;",
            ("$id", requestId), ("$status", status.ToString()));


    public Dictionary<string, Stance> GetProgramme(long partyId)
    {
        using var conn = Open();
        return GetProgrammeTx(conn, null, partyId);
    }

    private static Dictionary<string, Stance> GetProgrammeTx(SqliteConnection conn, SqliteTransaction? tx, long partyId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT issue_id, stance FROM programme_stances WHERE party_id = $party;";
        cmd.Parameters.AddWithValue("$party", partyId);
        var map = new Dictionary<string, Stance>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            map[r.GetString(0)] = (Stance)r.GetInt32(1);
        return map;
    }

    public static IReadOnlyList<string> ProgrammeFlips(IReadOnlyDictionary<string, Stance> before, IReadOnlyDictionary<string, Stance> after) =>
        after.Where(kv => before.TryGetValue(kv.Key, out var old) && old.Sign() * kv.Value.Sign() < 0)
            .Select(kv => kv.Key)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

    public ProgrammeRecordResult RecordProgramme(
        long partyId, IReadOnlyDictionary<string, Stance> stances, string? messageUrl, GameConstants constants)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        var state = GetGameStateTx(conn, tx);
        var before = GetProgrammeTx(conn, tx, partyId);
        var flips = ProgrammeFlips(before, stances);
        var penalty = state.IsPreElection ? 0 : flips.Count * constants.Programme.FlipPenalty;
        var members = QueryFactionsTx(conn, tx, "party_id = $party AND status = 'Active'", ("$party", partyId));

        Exec(conn, tx, "DELETE FROM programme_stances WHERE party_id = $party;", ("$party", partyId));
        foreach (var (issueId, stance) in stances.Where(kv => kv.Value != Stance.Silent))
        {
            Exec(conn, tx, "INSERT INTO programme_stances (party_id, issue_id, stance) VALUES ($party, $issue, $stance);",
                ("$party", partyId), ("$issue", issueId), ("$stance", (int)stance));
            foreach (var member in members)
                InsertStanceLogTx(conn, tx, state, member.UserId, issueId, stance, StanceSource.Programme, offProgramme: false);
        }
        Exec(conn, tx, "UPDATE parties SET programme_message_url = $url WHERE id = $party;", ("$party", partyId), ("$url", messageUrl));

        var penalised = new List<ulong>();
        if (penalty > 0)
            foreach (var member in members)
            {
                AddPopularityTx(conn, tx, member.UserId, -penalty, constants.Popularity);
                penalised.Add(member.UserId);
            }

        LogTx(conn, tx, LogKinds.StateChange, "gm party programme",
            $"Programme of party {partyId}: {string.Join(", ", stances.Where(kv => kv.Value != Stance.Silent).Select(kv => $"{kv.Key}={kv.Value}"))}; flips: {string.Join(", ", flips)}; penalty {penalty}.");
        tx.Commit();
        return new ProgrammeRecordResult(flips, penalty, penalised);
    }

    private static void InsertStanceLogTx(
        SqliteConnection conn, SqliteTransaction tx, GameState state, ulong factionId, string issueId, Stance stance, StanceSource source, bool offProgramme) =>
        Exec(conn, tx, @"
            INSERT INTO stance_log (faction_id, issue_id, stance, season, year, resolution_number, source, off_programme, logged_at)
            VALUES ($faction, $issue, $stance, $season, $year, $resolution, $source, $off, $now);",
            ("$faction", factionId.ToString()),
            ("$issue", issueId),
            ("$stance", (int)stance),
            ("$season", state.Season.ToString()),
            ("$year", state.Year),
            ("$resolution", state.ResolutionNumber),
            ("$source", source.ToString()),
            ("$off", offProgramme ? 1 : 0),
            ("$now", Now()));
}
