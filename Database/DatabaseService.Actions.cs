using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

public record PendingAction(
    long Id,
    ulong FactionId,
    ActionKind Kind,
    string IssueId,
    string? ProvinceId,
    Stance? Stance,
    bool OffProgramme,
    ulong? TargetFactionId,
    DateTimeOffset SubmittedAt);

public enum SubmitActionOutcome { Submitted, Replaced, TooMany, NotActive, IssueNotOpen, StanceRequired, AttackRefused, UnknownProvince }

public sealed record SubmitActionResult(
    SubmitActionOutcome Outcome,
    PendingAction? Action,
    PendingAction? Replaced,
    IReadOnlyList<PendingAction> Pending,
    string? Refusal = null);

public partial class DatabaseService
{
    private const string SelectPendingActionColumns =
        "id, faction_id, kind, issue_id, province_id, stance, off_programme, target_faction_id, submitted_at";

    private static PendingAction ReadPendingAction(SqliteDataReader r) => new(
        r.GetInt64(0),
        ulong.Parse(r.GetString(1)),
        Enum.Parse<ActionKind>(r.GetString(2)),
        r.GetString(3),
        r.IsDBNull(4) ? null : r.GetString(4),
        r.IsDBNull(5) ? null : (Stance)r.GetInt32(5),
        r.GetInt64(6) != 0,
        r.IsDBNull(7) ? null : ulong.Parse(r.GetString(7)),
        DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(8)));

    public List<PendingAction> GetPendingActions(ulong factionId)
    {
        using var conn = Open();
        return GetPendingActionsTx(conn, null, factionId);
    }

    private static List<PendingAction> GetPendingActionsTx(SqliteConnection conn, SqliteTransaction? tx, ulong factionId) =>
        QueryPendingActionsTx(conn, tx, "resolved = 0 AND faction_id = $faction", ("$faction", factionId.ToString()));

    private static List<PendingAction> QueryPendingActionsTx(
        SqliteConnection conn, SqliteTransaction? tx, string where, params (string Name, object? Value)[] parameters)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT {SelectPendingActionColumns} FROM actions WHERE {where} ORDER BY id;";
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        var list = new List<PendingAction>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(ReadPendingAction(r));
        return list;
    }

    public PendingAction? GetPendingAction(long actionId)
    {
        using var conn = Open();
        return QueryPendingActionsTx(conn, null, "resolved = 0 AND id = $id", ("$id", actionId)).FirstOrDefault();
    }

    public bool CancelAction(long actionId, ulong factionId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM actions WHERE id = $id AND faction_id = $faction AND resolved = 0;";
        cmd.Parameters.AddWithValue("$id", actionId);
        cmd.Parameters.AddWithValue("$faction", factionId.ToString());
        var cancelled = cmd.ExecuteNonQuery() > 0;
        if (cancelled) LogTx(conn, null, LogKinds.Command, "action cancel", $"Cancelled action {actionId}.", factionId);
        return cancelled;
    }

    public Stance? GetProgrammeStance(ulong factionId, string issueId)
    {
        using var conn = Open();
        return GetProgrammeStanceTx(conn, null, factionId, issueId);
    }

    private static Stance? GetProgrammeStanceTx(SqliteConnection conn, SqliteTransaction? tx, ulong factionId, string issueId)
    {
        if (GetFactionTx(conn, tx, factionId) is not { PartyId: { } partyId }) return null;
        return GetProgrammeTx(conn, tx, partyId).TryGetValue(issueId, out var stance) && stance != Stance.Silent ? stance : null;
    }

    public Stance? GetStance(ulong factionId, string issueId)
    {
        using var conn = Open();
        return GetStanceTx(conn, null, factionId, issueId);
    }

    private static Stance? GetStanceTx(SqliteConnection conn, SqliteTransaction? tx, ulong factionId, string issueId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            SELECT stance FROM stance_log
            WHERE faction_id = $faction AND issue_id = $issue
              AND resolution_number >= COALESCE((SELECT opened_resolution FROM issues WHERE id = $issue), 0)
            ORDER BY id DESC LIMIT 1;";
        cmd.Parameters.AddWithValue("$faction", factionId.ToString());
        cmd.Parameters.AddWithValue("$issue", issueId);
        if (cmd.ExecuteScalar() is long logged) return (Stance)logged;
        return GetProgrammeStanceTx(conn, tx, factionId, issueId);
    }

    public AttackTarget CheckAttack(ulong attackerId, ulong targetId, string issueId)
    {
        using var conn = Open();
        return CheckAttackTx(conn, null, attackerId, targetId, issueId);
    }

    private static AttackTarget CheckAttackTx(SqliteConnection conn, SqliteTransaction? tx, ulong attackerId, ulong targetId, string issueId)
    {
        if (attackerId == targetId) return new AttackTarget(null, Texts.Get("data.attack.self"));
        if (GetFactionTx(conn, tx, targetId) is not { IsActive: true } target) return new AttackTarget(null, Texts.Get("data.attack.targetInactive"));
        var attacker = GetFactionTx(conn, tx, attackerId);
        if (attacker?.PartyId is { } partyId && target.PartyId == partyId)
            return new AttackTarget(null, Texts.Get("data.attack.ownParty"));
        var stance = GetStanceTx(conn, tx, targetId, issueId);
        return stance is null or Stance.Silent
            ? new AttackTarget(null, Texts.Get("data.attack.targetNoStance"))
            : new AttackTarget(stance, null);
    }

    public SubmitActionResult SubmitAction(
        ulong factionId, ActionKind kind, string issueId, int maxPending,
        Stance? stance = null, string? provinceId = null, ulong? targetFactionId = null)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        var pending = GetPendingActionsTx(conn, tx, factionId);

        if (GetFactionTx(conn, tx, factionId) is not { IsActive: true })
            return new SubmitActionResult(SubmitActionOutcome.NotActive, null, null, pending);
        if (GetIssueTx(conn, tx, issueId) is not { Status: IssueStatus.Open })
            return new SubmitActionResult(SubmitActionOutcome.IssueNotOpen, null, null, pending);

        var offProgramme = false;
        Stance? recordedStance = null;
        ulong? target = null;
        string? province = null;
        switch (kind)
        {
            case ActionKind.Campaign:
                var decision = ActionPlanner.DecideCampaignStance(GetProgrammeStanceTx(conn, tx, factionId, issueId), stance);
                if (decision.Outcome != CampaignStanceOutcome.Ok)
                    return new SubmitActionResult(SubmitActionOutcome.StanceRequired, null, null, pending);
                recordedStance = decision.Stance;
                offProgramme = decision.OffProgramme;
                break;
            case ActionKind.Attack:
                if (targetFactionId is not { } targetId)
                    return new SubmitActionResult(SubmitActionOutcome.AttackRefused, null, null, pending, Texts.Get("data.attack.noTarget"));
                var check = CheckAttackTx(conn, tx, factionId, targetId, issueId);
                if (check.Refusal is not null)
                    return new SubmitActionResult(SubmitActionOutcome.AttackRefused, null, null, pending, check.Refusal);
                target = targetId;
                break;
            case ActionKind.Poll:
                if (provinceId is not null && GetProvincesTx(conn, tx).All(p => p.Id != provinceId))
                    return new SubmitActionResult(SubmitActionOutcome.UnknownProvince, null, null, pending);
                province = provinceId;
                break;
        }

        var slot = ActionPlanner.SlotFor(pending, kind, maxPending);
        if (!slot.Allowed)
            return new SubmitActionResult(SubmitActionOutcome.TooMany, null, null, pending);
        if (slot.Replaces is { } replaced)
            Exec(conn, tx, "DELETE FROM actions WHERE id = $id;", ("$id", replaced.Id));

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO actions (faction_id, kind, issue_id, province_id, stance, off_programme, target_faction_id, submitted_at)
                VALUES ($faction, $kind, $issue, $province, $stance, $off, $target, $now);
                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$faction", factionId.ToString());
            cmd.Parameters.AddWithValue("$kind", kind.ToString());
            cmd.Parameters.AddWithValue("$issue", issueId);
            cmd.Parameters.AddWithValue("$province", (object?)province ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$stance", recordedStance is { } s ? (int)s : DBNull.Value);
            cmd.Parameters.AddWithValue("$off", offProgramme ? 1 : 0);
            cmd.Parameters.AddWithValue("$target", (object?)target?.ToString() ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$now", Now());
            cmd.ExecuteScalar();
        }

        var after = GetPendingActionsTx(conn, tx, factionId);
        var action = after.First(a => a.Kind == kind);
        LogTx(conn, tx, LogKinds.Command, kind.ToString().ToLowerInvariant(),
            $"Submitted {kind} {action.Id} on {issueId}{(slot.Replaces is { } r ? $", replacing {r.Id}" : "")}.", factionId);
        tx.Commit();
        return new SubmitActionResult(
            slot.Replaces is null ? SubmitActionOutcome.Submitted : SubmitActionOutcome.Replaced, action, slot.Replaces, after);
    }

    private static void ResolveActionsTx(
        SqliteConnection conn, SqliteTransaction tx, GameState state, GameConfig config, IRandomSource random, ResolutionReport report)
    {
        var constants = config.Constants;
        var actions = QueryPendingActionsTx(conn, tx,
            "resolved = 0 AND faction_id IN (SELECT user_id FROM factions WHERE status = 'Active')");
        var resolutionNumber = state.ResolutionNumber + 1;
        Exec(conn, tx, @"
            UPDATE actions SET resolved = 1, resolution_number = $resolution, result = $result
            WHERE resolved = 0 AND faction_id NOT IN (SELECT user_id FROM factions WHERE status = 'Active');",
            ("$resolution", resolutionNumber), ("$result", Texts.Get("data.action.dropped")));
        if (actions.Count == 0) return;

        var snapshot = LoadOpinionSnapshotTx(conn, tx, config);
        var plan = ActionPlanner.Plan(actions, ActionStartTx(conn, tx, state, config, snapshot, actions),
            Scoring(config, action => PollLine(config, snapshot, random, action)));

        foreach (var (factionId, delta) in plan.PopularityDeltas)
            AddPopularityTx(conn, tx, factionId, delta, constants.Popularity);

        foreach (var campaign in plan.StanceLogs)
            InsertStanceLogTx(conn, tx, state, campaign.FactionId, campaign.IssueId, campaign.Stance!.Value, StanceSource.Campaign, campaign.OffProgramme);

        foreach (var issueId in plan.SalienceRaises)
            Exec(conn, tx, @"
                UPDATE issues SET salience = MIN($max, salience + $raise), salience_raised_resolution = $resolution
                WHERE id = $id;",
                ("$id", issueId),
                ("$max", constants.Salience.Max),
                ("$raise", constants.Salience.CampaignRaise),
                ("$resolution", state.ResolutionNumber));

        foreach (var outcome in plan.Outcomes)
        {
            var action = outcome.Action;
            Exec(conn, tx, "UPDATE actions SET resolved = 1, resolution_number = $resolution, result = $result WHERE id = $id;",
                ("$id", action.Id), ("$resolution", resolutionNumber), ("$result", outcome.Result));
            if (outcome.PrivateMessage is { } message) report.AddPrivate(action.FactionId, message);
            if (outcome.Fizzled || action.Kind == ActionKind.Poll) continue;
            report.Actions.Add(new ActionReport(
                action.Id,
                action.FactionId,
                action.Kind,
                action.IssueId,
                action.Kind == ActionKind.Attack ? outcome.TargetStance : action.Stance,
                action.OffProgramme,
                action.TargetFactionId,
                outcome.ActorChange,
                outcome.TargetChange,
                action.Kind == ActionKind.Attack && outcome.Succeeded));
        }

        LogTx(conn, tx, LogKinds.Resolution, "actions",
            $"Resolved {plan.Outcomes.Count} actions; salience raised on {string.Join(", ", plan.SalienceRaises)}.");
    }

    internal ActionPreview PreviewActions(GameConfig config)
    {
        using var conn = Open();
        var state = GetGameStateTx(conn, null);
        var actions = QueryPendingActionsTx(conn, null,
            "resolved = 0 AND faction_id IN (SELECT user_id FROM factions WHERE status = 'Active')");
        var snapshot = LoadOpinionSnapshotTx(conn, null, config);
        var plan = ActionPlanner.Plan(actions, ActionStartTx(conn, null, state, config, snapshot, actions), Scoring(config, _ => ""));
        return new ActionPreview(plan, snapshot);
    }

    private static ActionStartState ActionStartTx(SqliteConnection conn, SqliteTransaction? tx, GameState state, GameConfig config,
        OpinionSnapshot snapshot, IReadOnlyList<PendingAction> actions)
    {
        var attackTargets = actions
            .Where(a => a.Kind == ActionKind.Attack)
            .ToDictionary(a => a.Id, a => a.TargetFactionId is { } target
                ? CheckAttackTx(conn, tx, a.FactionId, target, a.IssueId)
                : new AttackTarget(null, Texts.Get("data.attack.noTarget")));

        return new ActionStartState(
            state.ResolutionNumber,
            config.Constants.Campaign.Crowding,
            issueId => snapshot.Issue(issueId) is not null,
            snapshot.National,
            snapshot.Salience,
            issueId => snapshot.Issue(issueId)?.SalienceRaisedResolution,
            action => attackTargets[action.Id]);
    }

    private static ActionScoring Scoring(GameConfig config, Func<PendingAction, string> poll)
    {
        var constants = config.Constants;
        return new ActionScoring(
            (stance, opinion, salience, m) => Formulas.CampaignGain(constants, stance, opinion, salience, m),
            (stance, opinion, salience) => Formulas.Attack(constants, stance, opinion, salience),
            poll);
    }

    private static string PollLine(GameConfig config, OpinionSnapshot snapshot, IRandomSource random, PendingAction action)
    {
        var poll = config.Constants.Poll;
        var salienceRange = config.Constants.Salience;
        var issue = snapshot.Issue(action.IssueId)!;
        var opinion = action.ProvinceId is { } provinceId
            ? snapshot.Provincial(action.IssueId, provinceId)
            : snapshot.National(action.IssueId);
        var noisyOpinion = Math.Clamp(opinion + Noise(random, poll.OpinionNoise), -1, 1);
        var noisySalience = Math.Clamp(issue.Salience + Noise(random, poll.SalienceNoise), salienceRange.Min, salienceRange.Max);
        var provinceName = action.ProvinceId is { } id
            ? snapshot.Provinces.FirstOrDefault(p => p.Id == id)?.Name ?? config.Province(id)?.Name ?? id
            : null;
        return Wording.PollLine(provinceName, issue.Title, noisyOpinion, noisySalience);
    }

    private static double Noise(IRandomSource random, double amplitude) => (random.NextDouble() * 2 - 1) * amplitude;
}
