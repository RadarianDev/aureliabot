using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

public sealed record CabinetVoteCounter(
    Func<IReadOnlyDictionary<ulong, VoteChoice>, IReadOnlyDictionary<ulong, int>, VoteTally> Tally,
    Func<VoteTally, bool> Passes)
{
    public static CabinetVoteCounter Standard { get; } = new(VoteCounting.Tally, VoteCounting.MajorityPasses);
}

public sealed record CancelledNomination(long Id, ulong? ChancellorId, ulong? MessageId);

public sealed record AppointmentResult(
    string? Refusal,
    bool FilledSeat,
    Ministry? VacatedMinistry,
    Cabinet? Nomination,
    IReadOnlyList<CancelledNomination> Cancelled);

public sealed record NominationResult(string? Refusal, Cabinet? Nomination);

public sealed record ImposeResult(string? Refusal, Cabinet? Cabinet, IReadOnlyList<CancelledNomination> Cancelled);

public sealed record ResignationResult(string? Refusal, bool WasChancellor, Ministry? Ministry, IReadOnlyList<CancelledNomination> Cancelled);

public sealed record CabinetVoteCast(string? Refusal, VoteChoice? Previous, Cabinet? Nomination);

public partial class DatabaseService
{
    private const string OpenNominationStatuses = "('Forming', 'Voting')";

    public Cabinet? GetCabinet(long id)
    {
        using var conn = Open();
        return GetCabinetTx(conn, null, id);
    }

    private static Cabinet? GetCabinetTx(SqliteConnection conn, SqliteTransaction? tx, long id) =>
        QueryCabinetsTx(conn, tx, "id = $id", ("$id", id)).FirstOrDefault();

    public Cabinet? GetOpenNomination()
    {
        using var conn = Open();
        return GetOpenNominationTx(conn, null);
    }

    private static Cabinet? GetOpenNominationTx(SqliteConnection conn, SqliteTransaction? tx) =>
        QueryCabinetsTx(conn, tx, $"kind = 'Nominated' AND status IN {OpenNominationStatuses}").LastOrDefault();

    public List<Cabinet> GetCabinetsInVote()
    {
        using var conn = Open();
        return QueryCabinetsTx(conn, null, "kind = 'Nominated' AND status = 'Voting'");
    }

    public List<Cabinet> CabinetVotesAwaiting(ulong factionId)
    {
        using var conn = Open();
        if (!VotingSeatsTx(conn, null).ContainsKey(factionId)) return [];
        return QueryCabinetsTx(conn, null, "kind = 'Nominated' AND status = 'Voting'")
            .Where(c => !GetVotesTx(conn, null, VoteSubject.Cabinet, c.Id).ContainsKey(factionId))
            .ToList();
    }

    public ulong? GetCabinetMessageId(long cabinetId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT message_id FROM cabinets WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", cabinetId);
        return cmd.ExecuteScalar() is string text && ulong.TryParse(text, out var id) ? id : null;
    }

    public void SetCabinetMessageId(long cabinetId, ulong messageId)
    {
        using var conn = Open();
        Exec(conn, null, "UPDATE cabinets SET message_id = $message WHERE id = $id;", ("$id", cabinetId), ("$message", messageId.ToString()));
    }

    private static List<Cabinet> QueryCabinetsTx(SqliteConnection conn, SqliteTransaction? tx, string where, params (string Name, object? Value)[] parameters)
    {
        var rows = new List<(long Id, CabinetKind Kind, ulong? Chancellor, CabinetStatus Status, long? Due)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = $"SELECT id, kind, chancellor_faction_id, status, due_resolution FROM cabinets WHERE {where} ORDER BY id;";
            foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                rows.Add((r.GetInt64(0), Enum.Parse<CabinetKind>(r.GetString(1)),
                    r.IsDBNull(2) ? null : ulong.Parse(r.GetString(2)),
                    Enum.Parse<CabinetStatus>(r.GetString(3)),
                    r.IsDBNull(4) ? null : r.GetInt64(4)));
        }
        return rows.Select(row => new Cabinet(row.Id, row.Kind, row.Chancellor, row.Status, GetCabinetPostsTx(conn, tx, row.Id), row.Due)).ToList();
    }

    private static long InsertCabinetTx(
        SqliteConnection conn, SqliteTransaction tx, CabinetKind kind, CabinetStatus status, ulong? chancellorId,
        IReadOnlyDictionary<Ministry, ulong?> posts, long createdResolution, long? dueResolution)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO cabinets (kind, chancellor_faction_id, status, created_at, created_resolution, due_resolution)
            VALUES ($kind, $chancellor, $status, $now, $created, $due) RETURNING id;";
        cmd.Parameters.AddWithValue("$kind", kind.ToString());
        cmd.Parameters.AddWithValue("$chancellor", (object?)chancellorId?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$status", status.ToString());
        cmd.Parameters.AddWithValue("$now", Now());
        cmd.Parameters.AddWithValue("$created", createdResolution);
        cmd.Parameters.AddWithValue("$due", (object?)dueResolution ?? DBNull.Value);
        var id = (long)cmd.ExecuteScalar()!;
        SetCabinetPostsTx(conn, tx, id, posts);
        return id;
    }

    private static void SetCabinetPostsTx(SqliteConnection conn, SqliteTransaction tx, long cabinetId, IReadOnlyDictionary<Ministry, ulong?> posts)
    {
        foreach (var ministry in GameEnumExtensions.CabinetMinistries)
            Exec(conn, tx, @"
                INSERT INTO cabinet_posts (cabinet_id, ministry, faction_id) VALUES ($cabinet, $ministry, $faction)
                ON CONFLICT(cabinet_id, ministry) DO UPDATE SET faction_id = excluded.faction_id;",
                ("$cabinet", cabinetId), ("$ministry", ministry.ToString()),
                ("$faction", posts.TryGetValue(ministry, out var holder) && holder is { } h ? h.ToString() : null));
    }

    private static Dictionary<Ministry, ulong?> VacantPosts() =>
        GameEnumExtensions.CabinetMinistries.ToDictionary(m => m, _ => (ulong?)null);

    private static List<CancelledNomination> CancelNominationsTx(SqliteConnection conn, SqliteTransaction tx, Func<Cabinet, bool> which, string reason)
    {
        var cancelled = new List<CancelledNomination>();
        foreach (var nomination in QueryCabinetsTx(conn, tx, $"kind = 'Nominated' AND status IN {OpenNominationStatuses}").Where(which))
        {
            Exec(conn, tx, "UPDATE cabinets SET status = 'Cancelled' WHERE id = $id;", ("$id", nomination.Id));
            cancelled.Add(new CancelledNomination(nomination.Id, nomination.ChancellorId, CabinetMessageIdTx(conn, tx, nomination.Id)));
            LogTx(conn, tx, LogKinds.StateChange, "cabinet", $"Cancelled nomination {nomination.Id}: {reason}.");
        }
        return cancelled;
    }

    private static ulong? CabinetMessageIdTx(SqliteConnection conn, SqliteTransaction tx, long cabinetId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT message_id FROM cabinets WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", cabinetId);
        return cmd.ExecuteScalar() is string text && ulong.TryParse(text, out var id) ? id : null;
    }

    private static bool Includes(Cabinet cabinet, ulong factionId) =>
        cabinet.ChancellorId == factionId || cabinet.Posts.Values.Contains(factionId);

    public static string? CabinetRefusal(ulong? chancellorId, IReadOnlyDictionary<Ministry, ulong?> posts, Func<ulong, bool> isActive)
    {
        var holders = posts.Values.OfType<ulong>().ToList();
        if (chancellorId is { } chancellor && holders.Contains(chancellor))
            return Texts.Get("cabinet.refusal.chancellorHoldsMinistry", ("faction", $"<@{chancellor}>"));
        if (holders.GroupBy(h => h).FirstOrDefault(g => g.Count() > 1) is { } twice)
            return Texts.Get("cabinet.refusal.twoMinistries", ("faction", $"<@{twice.Key}>"));
        var inactive = holders.Concat(chancellorId is { } c ? [c] : []).Where(h => !isActive(h)).ToList();
        return inactive.Count > 0 ? Texts.Get("cabinet.refusal.inactive", ("faction", $"<@{inactive[0]}>")) : null;
    }

    private static bool IsActiveTx(SqliteConnection conn, SqliteTransaction tx, ulong factionId) =>
        GetFactionTx(conn, tx, factionId) is { IsActive: true };

    public AppointmentResult AppointChancellor(ulong factionId)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        if (!IsActiveTx(conn, tx, factionId))
            return new AppointmentResult(Texts.Get("cabinet.refusal.inactive", ("faction", $"<@{factionId}>")), false, null, null, []);

        var state = GetGameStateTx(conn, tx);
        var sitting = GetSittingCabinetTx(conn, tx);
        var cancelled = CancelNominationsTx(conn, tx, n => n.ChancellorId != factionId, "a new Chancellor was appointed");
        var filledSeat = sitting?.ChancellorId is null;
        Ministry? vacated = null;
        Cabinet? nomination = null;

        if (sitting is null)
        {
            InsertCabinetTx(conn, tx, CabinetKind.Sitting, CabinetStatus.Caretaker, factionId, VacantPosts(), state.ResolutionNumber, null);
        }
        else if (filledSeat)
        {
            Exec(conn, tx, "UPDATE cabinets SET chancellor_faction_id = $faction WHERE id = $id;", ("$faction", factionId.ToString()), ("$id", sitting.Id));
            vacated = sitting.Posts.FirstOrDefault(p => p.Value == factionId) is { Value: not null } held ? held.Key : null;
            if (vacated is { } ministry)
                Exec(conn, tx, "UPDATE cabinet_posts SET faction_id = NULL WHERE cabinet_id = $id AND ministry = $ministry;",
                    ("$id", sitting.Id), ("$ministry", ministry.ToString()));
        }
        else if (sitting.ChancellorId != factionId)
        {
            nomination = GetOpenNominationTx(conn, tx);
            if (nomination is null)
            {
                var id = InsertCabinetTx(conn, tx, CabinetKind.Nominated, CabinetStatus.Forming, factionId, VacantPosts(), state.ResolutionNumber, null);
                nomination = GetCabinetTx(conn, tx, id);
            }
        }

        LogTx(conn, tx, LogKinds.StateChange, "gm cabinet chancellor",
            filledSeat ? $"Appointed {factionId} to the vacant Chancellorship." : $"Appointed {factionId} Chancellor-designate.", factionId, isPublic: true);
        tx.Commit();
        return new AppointmentResult(null, filledSeat, vacated, nomination, cancelled);
    }

    public NominationResult Nominate(ulong nominatorId, IReadOnlyDictionary<Ministry, ulong> posts)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        var state = GetGameStateTx(conn, tx);
        var open = GetOpenNominationTx(conn, tx);
        var sitting = GetSittingCabinetTx(conn, tx);

        string? refusal = open switch
        {
            { Status: CabinetStatus.Voting } => Texts.Get("cabinet.refusal.voteOpen"),
            { Status: CabinetStatus.Forming } when open.ChancellorId != nominatorId =>
                Texts.Get("cabinet.refusal.designateOnly", ("designate", $"<@{open.ChancellorId}>")),
            null when sitting?.ChancellorId != nominatorId => Texts.Get("cabinet.refusal.notChancellor"),
            _ => null,
        };
        var nominated = GameEnumExtensions.CabinetMinistries.ToDictionary(m => m, m => posts.TryGetValue(m, out var f) ? f : (ulong?)null);
        if (refusal is null && nominated.Values.Any(v => v is null))
            refusal = Texts.Get("cabinet.refusal.fourNeeded");
        refusal ??= CabinetRefusal(nominatorId, nominated, f => IsActiveTx(conn, tx, f));
        if (refusal is not null) return new NominationResult(refusal, null);

        var due = state.ResolutionNumber + 2;
        long id;
        if (open is not null)
        {
            id = open.Id;
            Exec(conn, tx, "UPDATE cabinets SET status = 'Voting', due_resolution = $due WHERE id = $id;", ("$due", due), ("$id", id));
            SetCabinetPostsTx(conn, tx, id, nominated);
        }
        else
        {
            id = InsertCabinetTx(conn, tx, CabinetKind.Nominated, CabinetStatus.Voting, nominatorId, nominated, state.ResolutionNumber, due);
        }

        LogTx(conn, tx, LogKinds.StateChange, "chancellor nominate",
            $"{nominatorId} nominated cabinet {id}: {string.Join(", ", nominated.Select(p => $"{p.Key} {p.Value}"))}; due at resolution {due}.",
            nominatorId, isPublic: true);
        var nomination = GetCabinetTx(conn, tx, id);
        tx.Commit();
        return new NominationResult(null, nomination);
    }

    public CabinetVoteCast CastCabinetVote(long cabinetId, ulong factionId, VoteChoice choice)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        if (GetCabinetTx(conn, tx, cabinetId) is not { Kind: CabinetKind.Nominated, Status: CabinetStatus.Voting } nomination)
            return new CabinetVoteCast(Texts.Get("cabinet.vote.closed"), null, null);
        if (!VotingSeatsTx(conn, tx).ContainsKey(factionId))
            return new CabinetVoteCast(Texts.Get("cabinet.vote.noSeats"), null, nomination);
        var result = CastVoteTx(conn, tx, VoteSubject.Cabinet, cabinetId, factionId, choice);
        tx.Commit();
        return new CabinetVoteCast(null, result.Previous, nomination);
    }

    public ImposeResult ImposeCabinet(ulong? chancellorId, IReadOnlyDictionary<Ministry, ulong?> posts)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        var state = GetGameStateTx(conn, tx);
        var chancellor = chancellorId ?? GetSittingCabinetTx(conn, tx)?.ChancellorId ?? GetOpenNominationTx(conn, tx)?.ChancellorId;
        var imposed = GameEnumExtensions.CabinetMinistries.ToDictionary(m => m, m => posts.TryGetValue(m, out var f) ? f : null);
        if (CabinetRefusal(chancellor, imposed, f => IsActiveTx(conn, tx, f)) is { } refusal)
            return new ImposeResult(refusal, null, []);

        Exec(conn, tx, "UPDATE cabinets SET status = 'Replaced' WHERE kind = 'Sitting' AND status IN ('Approved', 'Caretaker');");
        var cancelled = CancelNominationsTx(conn, tx, _ => true, "a cabinet was imposed");
        var id = InsertCabinetTx(conn, tx, CabinetKind.Sitting, CabinetStatus.Caretaker, chancellor, imposed, state.ResolutionNumber, null);
        LogTx(conn, tx, LogKinds.GmOverride, "gm cabinet impose",
            $"Imposed cabinet {id}: Chancellor {chancellor?.ToString() ?? "vacant"}, {string.Join(", ", imposed.Select(p => $"{p.Key} {p.Value?.ToString() ?? "vacant"}"))}.",
            isPublic: true);
        var cabinet = GetCabinetTx(conn, tx, id);
        tx.Commit();
        return new ImposeResult(null, cabinet, cancelled);
    }

    public ResignationResult Resign(ulong factionId)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        if (GetSittingCabinetTx(conn, tx) is not { } sitting || !sitting.Holders.Contains(factionId))
            return new ResignationResult(Texts.Get("ministry.resign.notInCabinet"), false, null, []);

        var wasChancellor = sitting.ChancellorId == factionId;
        Ministry? ministry = null;
        if (wasChancellor)
        {
            Exec(conn, tx, "UPDATE cabinets SET chancellor_faction_id = NULL WHERE id = $id;", ("$id", sitting.Id));
        }
        else
        {
            ministry = sitting.Posts.First(p => p.Value == factionId).Key;
            Exec(conn, tx, "UPDATE cabinet_posts SET faction_id = NULL WHERE cabinet_id = $id AND ministry = $ministry;",
                ("$id", sitting.Id), ("$ministry", ministry.Value.ToString()));
        }
        var cancelled = CancelNominationsTx(conn, tx, n => Includes(n, factionId), $"{factionId} resigned");
        LogTx(conn, tx, LogKinds.StateChange, "ministry resign",
            $"{factionId} resigned as {(wasChancellor ? "Chancellor" : ministry.ToString())}.", factionId, isPublic: true);
        tx.Commit();
        return new ResignationResult(null, wasChancellor, ministry, cancelled);
    }

    internal ResolutionReport CountDueCabinetVotes(CabinetVoteCounter counter)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        var state = GetGameStateTx(conn, tx);
        var report = new ResolutionReport { ResolutionNumber = state.ResolutionNumber + 1 };
        CountDueCabinetVotesTx(conn, tx, state, report, counter);
        tx.Commit();
        return report;
    }

    private static void CountDueCabinetVotesTx(SqliteConnection conn, SqliteTransaction tx, GameState state, GameConfig config, ResolutionReport report) =>
        CountDueCabinetVotesTx(conn, tx, state, report, CabinetVoteCounter.Standard);

    private static void CountDueCabinetVotesTx(SqliteConnection conn, SqliteTransaction tx, GameState state, ResolutionReport report, CabinetVoteCounter counter)
    {
        var resolution = state.ResolutionNumber + 1;
        var due = QueryCabinetsTx(conn, tx, "kind = 'Nominated' AND status = 'Voting' AND due_resolution <= $resolution", ("$resolution", resolution));
        foreach (var nomination in due)
        {
            var tally = counter.Tally(GetVotesTx(conn, tx, VoteSubject.Cabinet, nomination.Id), VotingSeatsTx(conn, tx));
            var approved = counter.Passes(tally);
            if (approved)
            {
                Exec(conn, tx, "UPDATE cabinets SET status = 'Replaced' WHERE kind = 'Sitting' AND status IN ('Approved', 'Caretaker');");
                Exec(conn, tx, "UPDATE cabinets SET kind = 'Sitting', status = 'Approved' WHERE id = $id;", ("$id", nomination.Id));
            }
            else
            {
                Exec(conn, tx, "UPDATE cabinets SET status = 'Rejected' WHERE id = $id;", ("$id", nomination.Id));
                if (nomination.ChancellorId is { } designate && GetSittingCabinetTx(conn, tx)?.ChancellorId != designate)
                    InsertCabinetTx(conn, tx, CabinetKind.Nominated, CabinetStatus.Forming, designate, VacantPosts(), state.ResolutionNumber, null);
                report.Alerts.Add(Texts.Get("cabinet.alert.rejected",
                    ("chancellor", nomination.ChancellorId is { } c ? $"<@{c}>" : Texts.Get("cabinet.vacant")),
                    ("yes", tally.YesSeats), ("no", tally.NoSeats)));
                if (nomination.ChancellorId is { } chancellor)
                    report.AddPrivate(chancellor, Texts.Get("cabinet.private.rejected", ("yes", tally.YesSeats), ("no", tally.NoSeats)));
            }
            report.CabinetVotes.Add(new CabinetVoteReport(nomination.Id, nomination.ChancellorId, approved, tally.YesSeats, tally.NoSeats, tally.AbstainSeats));
            LogTx(conn, tx, LogKinds.Resolution, "cabinet vote",
                $"Cabinet {nomination.Id} {(approved ? "approved" : "rejected")}: {tally.YesSeats} yes, {tally.NoSeats} no, {tally.AbstainSeats} abstain.", isPublic: true);
        }
    }
}
