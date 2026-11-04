using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

public record Reading(
    long Id,
    string BillId,
    ulong SponsorId,
    ReadingKind Kind,
    long ProposedResolution,
    long OpensResolution,
    long DueResolution,
    ReadingStatus Status,
    int? YesSeats,
    int? NoSeats,
    int? AbstainSeats,
    ulong? AnnounceMessageId,
    ulong? MessageId);

public record NoConfidenceMotion(
    long Id,
    ulong TablerId,
    long TabledResolution,
    long GatherUntilResolution,
    long? DueResolution,
    NoConfidenceStatus Status,
    int? YesSeats,
    int? NoSeats,
    ulong? MessageId);

public sealed record AssemblyRules(
    Func<IReadOnlyDictionary<ulong, VoteChoice>, IReadOnlyDictionary<ulong, int>, VoteTally> Tally,
    Func<VoteTally, bool> MajorityPasses,
    Func<VoteTally, int, bool> NoConfidencePasses,
    Func<int, double, int> TablingThreshold,
    Func<Lean, VoteChoice, Stance?> LoggedStance,
    Func<GameConstants, Lean, double, double, double> SponsorReward)
{
    public static AssemblyRules Standard { get; } = new(
        VoteCounting.Tally,
        VoteCounting.MajorityPasses,
        VoteCounting.NoConfidencePasses,
        VoteCounting.TablingThreshold,
        VoteCounting.LoggedStance,
        VoteCounting.SponsorReward);
}

public enum ProposeBillOutcome
{
    Proposed, NotActive, PreElection, BeforePreElection, NoSuchBill, NotAvailable, IssueNotOpen, IssueBusy,
    Independent, MinisterCannot, GovernmentBackbencher, ChancellorshipVacant, GovernmentLimit, OppositionActionUsed, PrivateBillCapReached,
}

public sealed record ProposeBillResult(ProposeBillOutcome Outcome, Reading? Reading = null, Bill? Bill = null);

public enum WithdrawOutcome { Withdrawn, NotFound, NotYours, NotScheduled }

public sealed record WithdrawResult(WithdrawOutcome Outcome, Reading? Reading = null, Bill? Bill = null);

public enum AssemblyVoteOutcome { Recorded, NotActive, NoSeats, NotOpen }

public sealed record AssemblyVoteResult(
    AssemblyVoteOutcome Outcome,
    VoteChoice? Previous = null,
    Stance? Logged = null,
    bool OffProgramme = false,
    Stance? ProgrammeStance = null,
    Bill? Bill = null);

public enum NoConfidenceOutcome
{
    Tabled, Cosigned, WentToVote, NotActive, InCabinet, PreElection, Cooldown, NoCabinet, MotionPending,
    NotFound, NotGathering, IsTabler, AlreadySigned,
}

public sealed record NoConfidenceResult(NoConfidenceOutcome Outcome, NoConfidenceMotion? Motion = null, int Supporters = 0, int Threshold = 0);

public sealed record MotionSupport(int Supporters, int Threshold, IReadOnlyList<ulong> Signers);

public partial class DatabaseService
{
    private const string SelectReadingColumns =
        "id, bill_id, sponsor_faction_id, kind, proposed_resolution, opens_resolution, due_resolution, status, yes_seats, no_seats, abstain_seats, announce_message_id, message_id";

    private static Reading ReadReading(SqliteDataReader r) => new(
        r.GetInt64(0),
        r.GetString(1),
        ulong.Parse(r.GetString(2)),
        Enum.Parse<ReadingKind>(r.GetString(3)),
        r.GetInt64(4),
        r.GetInt64(5),
        r.GetInt64(6),
        Enum.Parse<ReadingStatus>(r.GetString(7)),
        r.IsDBNull(8) ? null : r.GetInt32(8),
        r.IsDBNull(9) ? null : r.GetInt32(9),
        r.IsDBNull(10) ? null : r.GetInt32(10),
        r.IsDBNull(11) ? null : ulong.Parse(r.GetString(11)),
        r.IsDBNull(12) ? null : ulong.Parse(r.GetString(12)));

    private const string SelectMotionColumns =
        "id, tabler_faction_id, tabled_resolution, gather_until_resolution, due_resolution, status, yes_seats, no_seats, message_id";

    private static NoConfidenceMotion ReadMotion(SqliteDataReader r) => new(
        r.GetInt64(0),
        ulong.Parse(r.GetString(1)),
        r.GetInt64(2),
        r.GetInt64(3),
        r.IsDBNull(4) ? null : r.GetInt64(4),
        Enum.Parse<NoConfidenceStatus>(r.GetString(5)),
        r.IsDBNull(6) ? null : r.GetInt32(6),
        r.IsDBNull(7) ? null : r.GetInt32(7),
        r.IsDBNull(8) ? null : ulong.Parse(r.GetString(8)));

    private static List<Reading> QueryReadingsTx(SqliteConnection conn, SqliteTransaction? tx, string where, params (string Name, object? Value)[] parameters)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT {SelectReadingColumns} FROM readings WHERE {where} ORDER BY id;";
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        var list = new List<Reading>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(ReadReading(r));
        return list;
    }

    private static List<NoConfidenceMotion> QueryMotionsTx(SqliteConnection conn, SqliteTransaction? tx, string where, params (string Name, object? Value)[] parameters)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT {SelectMotionColumns} FROM no_confidence WHERE {where} ORDER BY id;";
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        var list = new List<NoConfidenceMotion>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(ReadMotion(r));
        return list;
    }

    public Reading? GetReading(long readingId)
    {
        using var conn = Open();
        return QueryReadingsTx(conn, null, "id = $id", ("$id", readingId)).FirstOrDefault();
    }

    public List<Reading> GetReadings(ReadingStatus status)
    {
        using var conn = Open();
        return QueryReadingsTx(conn, null, "status = $status", ("$status", status.ToString()));
    }

    public List<Reading> GetReadingsOfBill(string billId)
    {
        using var conn = Open();
        return QueryReadingsTx(conn, null, "bill_id = $bill", ("$bill", billId));
    }

    public List<Reading> GetScheduledReadingsBy(ulong factionId)
    {
        using var conn = Open();
        return QueryReadingsTx(conn, null, "status = 'Scheduled' AND sponsor_faction_id = $faction", ("$faction", factionId.ToString()));
    }

    public List<Reading> GetOpenReadingsNotVotedBy(ulong factionId)
    {
        using var conn = Open();
        return QueryReadingsTx(conn, null, @"status = 'Open' AND id NOT IN
            (SELECT subject_id FROM votes WHERE subject_type = 'Reading' AND faction_id = $faction)", ("$faction", factionId.ToString()));
    }

    public NoConfidenceMotion? GetMotion(long motionId)
    {
        using var conn = Open();
        return QueryMotionsTx(conn, null, "id = $id", ("$id", motionId)).FirstOrDefault();
    }

    public List<NoConfidenceMotion> GetMotions(NoConfidenceStatus status)
    {
        using var conn = Open();
        return QueryMotionsTx(conn, null, "status = $status", ("$status", status.ToString()));
    }

    public List<NoConfidenceMotion> GetVotingMotionsNotVotedBy(ulong factionId)
    {
        using var conn = Open();
        return QueryMotionsTx(conn, null, @"status = 'Voting' AND id NOT IN
            (SELECT subject_id FROM votes WHERE subject_type = 'NoConfidence' AND faction_id = $faction)", ("$faction", factionId.ToString()));
    }

    public List<NoConfidenceMotion> GetCosignableMotions(ulong factionId)
    {
        using var conn = Open();
        if (NoConfidenceEligibilityTx(conn, null, factionId) is not null) return [];
        return QueryMotionsTx(conn, null, @"status = 'Gathering' AND tabler_faction_id <> $faction AND id NOT IN
            (SELECT no_confidence_id FROM no_confidence_cosigners WHERE faction_id = $faction)", ("$faction", factionId.ToString()));
    }

    public List<Bill> GetProposableBills()
    {
        using var conn = Open();
        return QueryBillsTx(conn, null, @"status = 'Available'
            AND issue_id IN (SELECT id FROM issues WHERE status = 'Open')
            AND issue_id NOT IN (SELECT issue_id FROM bills WHERE status = 'Voting')
            AND issue_id NOT IN (SELECT b.issue_id FROM readings r JOIN bills b ON b.id = r.bill_id WHERE r.status IN ('Scheduled', 'Open'))");
    }

    public void SetReadingAnnounceMessage(long readingId, ulong messageId)
    {
        using var conn = Open();
        Exec(conn, null, "UPDATE readings SET announce_message_id = $message WHERE id = $id;", ("$id", readingId), ("$message", messageId.ToString()));
    }

    public void SetReadingMessage(long readingId, ulong messageId)
    {
        using var conn = Open();
        Exec(conn, null, "UPDATE readings SET message_id = $message WHERE id = $id;", ("$id", readingId), ("$message", messageId.ToString()));
    }

    public void SetMotionMessage(long motionId, ulong messageId)
    {
        using var conn = Open();
        Exec(conn, null, "UPDATE no_confidence SET message_id = $message WHERE id = $id;", ("$id", motionId), ("$message", messageId.ToString()));
    }

    public VoteTally TallyOf(VoteSubject subject, long subjectId, AssemblyRules? rules = null)
    {
        using var conn = Open();
        return (rules ?? AssemblyRules.Standard).Tally(GetVotesTx(conn, null, subject, subjectId), VotingSeatsTx(conn, null));
    }

    public ProposeBillResult ProposeBill(ulong factionId, string billId, GameConstants constants)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        var result = ProposeBillTx(conn, tx, factionId, billId, constants);
        if (result.Outcome == ProposeBillOutcome.Proposed) tx.Commit();
        return result;
    }

    public static ProposeBillOutcome? ProposalTiming(GameState state)
    {
        if (state.IsPreElection) return ProposeBillOutcome.PreElection;
        return GameCalendar.Next(state.Season, state.Year) == (state.PreElectionSeason, state.PreElectionYear)
            ? ProposeBillOutcome.BeforePreElection
            : null;
    }

    private static ProposeBillResult ProposeBillTx(SqliteConnection conn, SqliteTransaction tx, ulong factionId, string billId, GameConstants c)
    {
        if (GetFactionTx(conn, tx, factionId) is not { IsActive: true }) return new(ProposeBillOutcome.NotActive);
        var state = GetGameStateTx(conn, tx);
        if (ProposalTiming(state) is { } timing) return new(timing);

        if (QueryBillsTx(conn, tx, "id = $id", ("$id", billId)).FirstOrDefault() is not { } bill) return new(ProposeBillOutcome.NoSuchBill);
        if (bill.Status != BillStatus.Available) return new(ProposeBillOutcome.NotAvailable, Bill: bill);
        if (GetIssueTx(conn, tx, bill.IssueId) is not { Status: IssueStatus.Open }) return new(ProposeBillOutcome.IssueNotOpen, Bill: bill);
        var busy = QueryBillsTx(conn, tx, "issue_id = $issue AND status = 'Voting'", ("$issue", bill.IssueId)).Count > 0
                   || QueryReadingsTx(conn, tx, "status IN ('Scheduled', 'Open') AND bill_id IN (SELECT id FROM bills WHERE issue_id = $issue)",
                       ("$issue", bill.IssueId)).Count > 0;
        if (busy) return new(ProposeBillOutcome.IssueBusy, Bill: bill);

        var cabinet = GetSittingCabinetTx(conn, tx);
        ReadingKind kind;
        if (cabinet?.ChancellorId == factionId)
        {
            var proposed = QueryReadingsTx(conn, tx, "kind = 'Government' AND proposed_resolution = $resolution AND status <> 'Cancelled'",
                ("$resolution", state.ResolutionNumber)).Count;
            if (proposed >= c.Government.BillsPerSeason) return new(ProposeBillOutcome.GovernmentLimit, Bill: bill);
            kind = ReadingKind.Government;
        }
        else
        {
            switch (RoleOfTx(conn, tx, factionId))
            {
                case FactionRole.Independent:
                    return new(ProposeBillOutcome.Independent, Bill: bill);
                case FactionRole.Government:
                    if (cabinet?.ChancellorId is null) return new(ProposeBillOutcome.ChancellorshipVacant, Bill: bill);
                    return new(cabinet.Holders.Contains(factionId) ? ProposeBillOutcome.MinisterCannot : ProposeBillOutcome.GovernmentBackbencher, Bill: bill);
            }
            var standing = GetOppositionStandingTx(conn, tx, factionId, c);
            if (standing.UsedThisSeason) return new(ProposeBillOutcome.OppositionActionUsed, Bill: bill);
            if (standing.BillsThisYear >= standing.BillsAllowedPerYear) return new(ProposeBillOutcome.PrivateBillCapReached, Bill: bill);
            kind = ReadingKind.Private;
        }

        long readingId;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO readings (bill_id, sponsor_faction_id, kind, proposed_at, proposed_resolution, opens_resolution, due_resolution, status)
                VALUES ($bill, $faction, $kind, $now, $proposed, $opens, $due, 'Scheduled') RETURNING id;";
            cmd.Parameters.AddWithValue("$bill", bill.Id);
            cmd.Parameters.AddWithValue("$faction", factionId.ToString());
            cmd.Parameters.AddWithValue("$kind", kind.ToString());
            cmd.Parameters.AddWithValue("$now", Now());
            cmd.Parameters.AddWithValue("$proposed", state.ResolutionNumber);
            cmd.Parameters.AddWithValue("$opens", state.ResolutionNumber + 1);
            cmd.Parameters.AddWithValue("$due", state.ResolutionNumber + 2);
            readingId = (long)cmd.ExecuteScalar()!;
        }
        Exec(conn, tx, "UPDATE bills SET status = 'Voting' WHERE id = $id;", ("$id", bill.Id));
        if (kind == ReadingKind.Private)
            RecordOppositionActionTx(conn, tx, factionId, OppositionActionKind.Bill, readingId);
        LogTx(conn, tx, LogKinds.StateChange, "bill propose", $"{factionId} proposed {bill.Id} as a {kind} bill (reading {readingId}).", factionId, isPublic: true);
        var reading = QueryReadingsTx(conn, tx, "id = $id", ("$id", readingId)).Single();
        return new(ProposeBillOutcome.Proposed, reading, bill with { Status = BillStatus.Voting });
    }

    public WithdrawResult WithdrawReading(long readingId, ulong factionId)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        if (QueryReadingsTx(conn, tx, "id = $id", ("$id", readingId)).FirstOrDefault() is not { } reading) return new(WithdrawOutcome.NotFound);
        var bill = QueryBillsTx(conn, tx, "id = $id", ("$id", reading.BillId)).FirstOrDefault();
        if (reading.SponsorId != factionId) return new(WithdrawOutcome.NotYours, reading, bill);
        if (reading.Status != ReadingStatus.Scheduled) return new(WithdrawOutcome.NotScheduled, reading, bill);

        Exec(conn, tx, "UPDATE readings SET status = 'Cancelled' WHERE id = $id;", ("$id", readingId));
        Exec(conn, tx, "UPDATE bills SET status = 'Available', cooldown_until = NULL WHERE id = $bill AND status = 'Voting';", ("$bill", reading.BillId));
        if (reading.Kind == ReadingKind.Private)
            RefundOppositionActionTx(conn, tx, OppositionActionKind.Bill, readingId);
        LogTx(conn, tx, LogKinds.StateChange, "bill withdraw", $"{factionId} withdrew reading {readingId} of {reading.BillId}.", factionId, isPublic: true);
        tx.Commit();
        return new(WithdrawOutcome.Withdrawn, reading with { Status = ReadingStatus.Cancelled }, bill);
    }

    public AssemblyVoteResult CastAssemblyVote(VoteSubject subject, long subjectId, ulong factionId, VoteChoice choice, AssemblyRules? rules = null)
    {
        rules ??= AssemblyRules.Standard;
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        if (GetFactionTx(conn, tx, factionId) is not { IsActive: true }) return new(AssemblyVoteOutcome.NotActive);
        if (!VotingSeatsTx(conn, tx).ContainsKey(factionId)) return new(AssemblyVoteOutcome.NoSeats);

        Bill? bill = null;
        if (subject == VoteSubject.Reading)
        {
            if (QueryReadingsTx(conn, tx, "id = $id AND status = 'Open'", ("$id", subjectId)).FirstOrDefault() is not { } reading)
                return new(AssemblyVoteOutcome.NotOpen);
            bill = QueryBillsTx(conn, tx, "id = $id", ("$id", reading.BillId)).Single();
        }
        else if (subject != VoteSubject.NoConfidence || QueryMotionsTx(conn, tx, "id = $id AND status = 'Voting'", ("$id", subjectId)).Count == 0)
        {
            return new(AssemblyVoteOutcome.NotOpen);
        }

        var cast = CastVoteTx(conn, tx, subject, subjectId, factionId, choice);
        Stance? logged = null;
        Stance? programme = null;
        var offProgramme = false;
        if (bill is not null && rules.LoggedStance(bill.Lean, choice) is { } stance && stance != Stance.Silent)
        {
            logged = stance;
            programme = GetProgrammeStanceTx(conn, tx, factionId, bill.IssueId);
            offProgramme = IsOffProgramme(programme, stance);
        }
        tx.Commit();
        return new(AssemblyVoteOutcome.Recorded, cast.Previous, logged, offProgramme, programme, bill);
    }

    private static string? NoConfidenceEligibilityTx(SqliteConnection conn, SqliteTransaction? tx, ulong factionId)
    {
        if (GetFactionTx(conn, tx, factionId) is not { IsActive: true }) return nameof(NoConfidenceOutcome.NotActive);
        if (GetSittingCabinetTx(conn, tx) is not { } cabinet) return nameof(NoConfidenceOutcome.NoCabinet);
        if (cabinet.Holders.Contains(factionId)) return nameof(NoConfidenceOutcome.InCabinet);
        return GetGameStateTx(conn, tx).IsPreElection ? nameof(NoConfidenceOutcome.PreElection) : null;
    }

    public NoConfidenceResult TableNoConfidence(ulong factionId, GameConstants constants, AssemblyRules? rules = null)
    {
        rules ??= AssemblyRules.Standard;
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        if (NoConfidenceEligibilityTx(conn, tx, factionId) is { } refusal) return new(Enum.Parse<NoConfidenceOutcome>(refusal));
        var state = GetGameStateTx(conn, tx);
        if (state.NoConfidenceCooldownUntil is { } until && until > state.ResolutionNumber) return new(NoConfidenceOutcome.Cooldown);
        if (QueryMotionsTx(conn, tx, "status IN ('Gathering', 'Voting')").FirstOrDefault() is { } pending)
            return new(NoConfidenceOutcome.MotionPending, pending);

        long motionId;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO no_confidence (tabler_faction_id, tabled_at, tabled_resolution, gather_until_resolution, status)
                VALUES ($faction, $now, $resolution, $until, 'Gathering') RETURNING id;";
            cmd.Parameters.AddWithValue("$faction", factionId.ToString());
            cmd.Parameters.AddWithValue("$now", Now());
            cmd.Parameters.AddWithValue("$resolution", state.ResolutionNumber);
            cmd.Parameters.AddWithValue("$until", state.ResolutionNumber + constants.Assembly.NoConfidenceGatherResolutions);
            motionId = (long)cmd.ExecuteScalar()!;
        }
        LogTx(conn, tx, LogKinds.StateChange, "noconfidence table", $"{factionId} tabled no-confidence motion {motionId}.", factionId, isPublic: true);
        var result = CheckSupportTx(conn, tx, state, motionId, constants, rules, NoConfidenceOutcome.Tabled);
        tx.Commit();
        return result;
    }

    public NoConfidenceResult CosignNoConfidence(long motionId, ulong factionId, GameConstants constants, AssemblyRules? rules = null)
    {
        rules ??= AssemblyRules.Standard;
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        if (QueryMotionsTx(conn, tx, "id = $id", ("$id", motionId)).FirstOrDefault() is not { } motion) return new(NoConfidenceOutcome.NotFound);
        if (motion.Status != NoConfidenceStatus.Gathering) return new(NoConfidenceOutcome.NotGathering, motion);
        if (NoConfidenceEligibilityTx(conn, tx, factionId) is { } refusal) return new(Enum.Parse<NoConfidenceOutcome>(refusal), motion);
        if (motion.TablerId == factionId) return new(NoConfidenceOutcome.IsTabler, motion);
        if (MotionSignersTx(conn, tx, motion).Contains(factionId)) return new(NoConfidenceOutcome.AlreadySigned, motion);

        Exec(conn, tx, "INSERT INTO no_confidence_cosigners (no_confidence_id, faction_id, signed_at) VALUES ($id, $faction, $now);",
            ("$id", motionId), ("$faction", factionId.ToString()), ("$now", Now()));
        LogTx(conn, tx, LogKinds.StateChange, "noconfidence cosign", $"{factionId} co-signed no-confidence motion {motionId}.", factionId, isPublic: true);
        var result = CheckSupportTx(conn, tx, GetGameStateTx(conn, tx), motionId, constants, rules, NoConfidenceOutcome.Cosigned);
        tx.Commit();
        return result;
    }

    public MotionSupport GetMotionSupport(long motionId, GameConstants constants, AssemblyRules? rules = null)
    {
        using var conn = Open();
        var motion = QueryMotionsTx(conn, null, "id = $id", ("$id", motionId)).Single();
        return MotionSupportTx(conn, null, GetGameStateTx(conn, null), motion, constants, rules ?? AssemblyRules.Standard);
    }

    private static List<ulong> MotionSignersTx(SqliteConnection conn, SqliteTransaction? tx, NoConfidenceMotion motion)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT faction_id FROM no_confidence_cosigners WHERE no_confidence_id = $id ORDER BY signed_at, faction_id;";
        cmd.Parameters.AddWithValue("$id", motion.Id);
        var list = new List<ulong> { motion.TablerId };
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(ulong.Parse(r.GetString(0)));
        return list;
    }

    private static MotionSupport MotionSupportTx(
        SqliteConnection conn, SqliteTransaction? tx, GameState state, NoConfidenceMotion motion, GameConstants c, AssemblyRules rules)
    {
        var signers = MotionSignersTx(conn, tx, motion);
        var seats = QueryFactionsTx(conn, tx, "status = 'Active'").ToDictionary(f => f.UserId, f => (int)f.Seats);
        return new MotionSupport(signers.Sum(id => seats.GetValueOrDefault(id)), rules.TablingThreshold(state.Seats, c.Assembly.NoConfidenceShare), signers);
    }

    private static NoConfidenceResult CheckSupportTx(
        SqliteConnection conn, SqliteTransaction tx, GameState state, long motionId, GameConstants c, AssemblyRules rules, NoConfidenceOutcome outcome)
    {
        var motion = QueryMotionsTx(conn, tx, "id = $id", ("$id", motionId)).Single();
        var support = MotionSupportTx(conn, tx, state, motion, c, rules);
        if (support.Supporters < support.Threshold) return new(outcome, motion, support.Supporters, support.Threshold);

        Exec(conn, tx, "UPDATE no_confidence SET status = 'Voting', due_resolution = $due WHERE id = $id;",
            ("$id", motionId), ("$due", state.ResolutionNumber + 2));
        LogTx(conn, tx, LogKinds.StateChange, "noconfidence vote", $"No-confidence motion {motionId} went to a vote.", isPublic: true);
        motion = QueryMotionsTx(conn, tx, "id = $id", ("$id", motionId)).Single();
        return new(NoConfidenceOutcome.WentToVote, motion, support.Supporters, support.Threshold);
    }

    internal ResolutionReport CountDueAssemblyVotes(GameConfig config, AssemblyRules rules)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        var state = GetGameStateTx(conn, tx);
        var report = new ResolutionReport { ResolutionNumber = state.ResolutionNumber + 1, FromSeason = state.Season, FromYear = state.Year };
        CountDueReadingsTx(conn, tx, state, config, report, rules);
        CountDueNoConfidenceTx(conn, tx, GetGameStateTx(conn, tx), config, report, rules);
        tx.Commit();
        return report;
    }

    private static void CountDueReadingsTx(SqliteConnection conn, SqliteTransaction tx, GameState state, GameConfig config, ResolutionReport report) =>
        CountDueReadingsTx(conn, tx, state, config, report, AssemblyRules.Standard);

    private static void CountDueReadingsTx(
        SqliteConnection conn, SqliteTransaction tx, GameState state, GameConfig config, ResolutionReport report, AssemblyRules rules)
    {
        var c = config.Constants;
        var resolution = state.ResolutionNumber + 1;
        var due = QueryReadingsTx(conn, tx, "status = 'Open' AND due_resolution <= $resolution", ("$resolution", resolution));
        if (due.Count == 0) return;
        var seats = VotingSeatsTx(conn, tx);

        foreach (var dueReading in due)
        {
            if (QueryReadingsTx(conn, tx, "id = $id AND status = 'Open'", ("$id", dueReading.Id)).FirstOrDefault() is not { } reading) continue;
            var bill = QueryBillsTx(conn, tx, "id = $id", ("$id", reading.BillId)).Single();
            var votes = GetVotesTx(conn, tx, VoteSubject.Reading, reading.Id);
            var tally = rules.Tally(votes, seats);
            var passed = rules.MajorityPasses(tally);
            LogVoteStancesTx(conn, tx, state, bill, votes, rules);
            Exec(conn, tx, "UPDATE readings SET yes_seats = $yes, no_seats = $no, abstain_seats = $abstain WHERE id = $id;",
                ("$id", reading.Id), ("$yes", tally.YesSeats), ("$no", tally.NoSeats), ("$abstain", tally.AbstainSeats));

            if (passed)
            {
                var snapshot = LoadOpinionSnapshotTx(conn, tx, config);
                var reward = rules.SponsorReward(c, bill.Lean, snapshot.National(bill.IssueId), snapshot.Salience(bill.IssueId));
                AddPopularityTx(conn, tx, reading.SponsorId, reward, c.Popularity);
                Exec(conn, tx, "UPDATE bills SET status = 'Passed', cooldown_until = NULL WHERE id = $id;", ("$id", bill.Id));
                Exec(conn, tx, "UPDATE readings SET status = 'Passed' WHERE id = $id;", ("$id", reading.Id));
                var applied = ApplyEffectsTx(conn, tx, bill.ParsedPassEffects, config, new EffectSource(bill.Title, "law", bill.Id, PublicLedger: true, ActorId: reading.SponsorId), report);
                report.Laws.Add(new LawReport(bill.Id, bill.Title, reading.SponsorId, tally.YesSeats, tally.NoSeats, reward, bill.PassText, applied.Applied));
                report.AddPrivate(reading.SponsorId, Texts.Get("law.sponsorPrivate", ("bill", bill.Title), ("change", Display.Signed(reward))));
            }
            else
            {
                if (GetIssueTx(conn, tx, bill.IssueId) is { Status: IssueStatus.Open } issue)
                    Exec(conn, tx, "UPDATE issues SET salience = $value WHERE id = $id;",
                        ("$id", issue.Id), ("$value", Math.Clamp(issue.Salience - c.Salience.RejectDrop, c.Salience.Min, c.Salience.Max)));
                Exec(conn, tx, "UPDATE bills SET status = 'Cooldown', cooldown_until = $until WHERE id = $id AND status = 'Voting';",
                    ("$id", bill.Id), ("$until", resolution + c.RejectedCooldownSeasons));
                Exec(conn, tx, "UPDATE readings SET status = 'Rejected' WHERE id = $id;", ("$id", reading.Id));
            }

            report.VoteResults.Add(new VoteResultReport(VoteSubject.Reading, reading.Id, bill.Title, passed, tally.YesSeats, tally.NoSeats, tally.AbstainSeats));
            LogTx(conn, tx, LogKinds.Resolution, "reading",
                $"Reading {reading.Id} of {bill.Id} {(passed ? "passed" : "rejected")} {tally.YesSeats}-{tally.NoSeats} ({tally.AbstainSeats} abstaining).", isPublic: true);
        }
    }

    private static bool IsOffProgramme(Stance? programme, Stance stance) => programme is { } p && p.Sign() * stance.Sign() < 0;

    private static void LogVoteStancesTx(SqliteConnection conn, SqliteTransaction tx, GameState state, Bill bill,
        IReadOnlyDictionary<ulong, VoteChoice> votes, AssemblyRules rules)
    {
        foreach (var (factionId, choice) in votes.OrderBy(v => v.Key))
        {
            if (rules.LoggedStance(bill.Lean, choice) is not { } stance || stance == Stance.Silent) continue;
            var offProgramme = IsOffProgramme(GetProgrammeStanceTx(conn, tx, factionId, bill.IssueId), stance);
            InsertStanceLogTx(conn, tx, state, factionId, bill.IssueId, stance, StanceSource.Vote, offProgramme);
        }
    }

    private static void CountDueNoConfidenceTx(SqliteConnection conn, SqliteTransaction tx, GameState state, GameConfig config, ResolutionReport report) =>
        CountDueNoConfidenceTx(conn, tx, state, config, report, AssemblyRules.Standard);

    private static void CountDueNoConfidenceTx(
        SqliteConnection conn, SqliteTransaction tx, GameState state, GameConfig config, ResolutionReport report, AssemblyRules rules)
    {
        var c = config.Constants;
        if (state.IsPreElection) return;
        var resolution = state.ResolutionNumber + 1;

        foreach (var motion in QueryMotionsTx(conn, tx, "status = 'Gathering' AND gather_until_resolution <= $resolution", ("$resolution", resolution)))
        {
            Exec(conn, tx, "UPDATE no_confidence SET status = 'Lapsed' WHERE id = $id;", ("$id", motion.Id));
            report.NoConfidenceMotions.Add(new NoConfidenceReport(motion.Id, motion.TablerId, NoConfidenceStatus.Lapsed, 0, 0, 0));
            LogTx(conn, tx, LogKinds.Resolution, "noconfidence", $"No-confidence motion {motion.Id} lapsed.", isPublic: true);
        }

        var due = QueryMotionsTx(conn, tx, "status = 'Voting' AND due_resolution <= $resolution", ("$resolution", resolution));
        if (due.Count == 0) return;
        var seats = VotingSeatsTx(conn, tx);
        var filled = seats.Values.Sum();

        foreach (var motion in due)
        {
            var tally = rules.Tally(GetVotesTx(conn, tx, VoteSubject.NoConfidence, motion.Id), seats);
            var passed = rules.NoConfidencePasses(tally, filled);
            var outcome = passed ? NoConfidenceStatus.Passed : NoConfidenceStatus.Failed;
            Exec(conn, tx, "UPDATE no_confidence SET status = $status, yes_seats = $yes, no_seats = $no WHERE id = $id;",
                ("$id", motion.Id), ("$status", outcome.ToString()), ("$yes", tally.YesSeats), ("$no", tally.NoSeats));

            if (passed)
            {
                Exec(conn, tx, "UPDATE cabinets SET status = 'Caretaker' WHERE kind = 'Sitting' AND status = 'Approved';");
                var (season, year) = GameCalendar.Next(state.Season, state.Year);
                Exec(conn, tx, "UPDATE game_state SET pre_election_season = $season, pre_election_year = $year WHERE id = 1;",
                    ("$season", season.ToString()), ("$year", year));
                report.SnapElectionCalled = true;
                report.Alerts.Add(Texts.Get("noConfidence.alert.snapElection", ("season", GameCalendar.Label(season, year))));
            }
            else
            {
                Exec(conn, tx, "UPDATE game_state SET no_confidence_cooldown_until = $until WHERE id = 1;",
                    ("$until", resolution + c.Assembly.NoConfidenceCooldownSeasons));
            }

            report.NoConfidenceMotions.Add(new NoConfidenceReport(motion.Id, motion.TablerId, outcome, tally.YesSeats, tally.NoSeats, filled));
            LogTx(conn, tx, LogKinds.Resolution, "noconfidence",
                $"No-confidence motion {motion.Id} {outcome}: {tally.YesSeats} of {filled} filled seats for.", isPublic: true);
        }
    }

    private static void OpenScheduledReadingsTx(SqliteConnection conn, SqliteTransaction tx, GameConfig config, ResolutionReport report)
    {
        var state = GetGameStateTx(conn, tx);
        foreach (var reading in QueryReadingsTx(conn, tx, "status = 'Scheduled' AND opens_resolution <= $resolution", ("$resolution", state.ResolutionNumber)))
        {
            var title = QueryBillsTx(conn, tx, "id = $id", ("$id", reading.BillId)).FirstOrDefault()?.Title ?? reading.BillId;
            if (state.IsPreElection)
            {
                Exec(conn, tx, "UPDATE readings SET status = 'Cancelled' WHERE id = $id;", ("$id", reading.Id));
                Exec(conn, tx, "UPDATE bills SET status = 'Available', cooldown_until = NULL WHERE id = $bill AND status = 'Voting';", ("$bill", reading.BillId));
                Exec(conn, tx, "UPDATE opposition_actions SET cancelled = 1 WHERE kind = 'Bill' AND subject_id = $id;", ("$id", reading.Id));
                report.ReadingsCancelled.Add(new ReadingCancelledReport(reading.Id, reading.BillId, title, Texts.Get("assembly.cancelledPreElection")));
            }
            else
            {
                Exec(conn, tx, "UPDATE readings SET status = 'Open', due_resolution = $due WHERE id = $id;",
                    ("$id", reading.Id), ("$due", state.ResolutionNumber + 1));
                report.ReadingsOpened.Add(new ReadingOpenedReport(reading.Id, reading.BillId, title));
            }
        }
    }
}
