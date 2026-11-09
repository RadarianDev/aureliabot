using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;

public record Crisis(
    long Id,
    string TemplateId,
    long? EventId,
    string Title,
    Ministry Ministry,
    string ProvinceId,
    string IssueId,
    IReadOnlyList<CrisisAnswer> Answers,
    CrisisOutcomeText? Complication,
    CrisisOutcomeText Default,
    long PostedResolution,
    long DueResolution,
    CrisisStatus Status,
    int? ChosenAnswer,
    ulong? ResponderId,
    string? ResponseText,
    CrisisOutcome? Outcome,
    ulong? MessageId)
{
    public CrisisAnswer? Chosen => AnswerNumbered(ChosenAnswer);

    public bool CondemnationsRevealed => Status == CrisisStatus.Resolved;

    public CrisisAnswer? AnswerNumbered(int? number) =>
        number is { } n && n >= 1 && n <= Answers.Count ? Answers[n - 1] : null;
}

public record Condemnation(long Id, ulong FactionId, long CrisisId, Lean Side, long MadeAt);

public sealed record AwaitingCrisis(Crisis Crisis, bool Responded);

public sealed record CrisisResponseResult(string? Refusal, Crisis? Crisis = null, CrisisAnswer? Answer = null, decimal Balance = 0, bool Replaced = false)
{
    public bool PurseCovers => Answer is { } answer && answer.Price <= Balance;
}

public sealed record CondemnResult(string? Refusal, Crisis? Crisis = null, Condemnation? Condemnation = null);

public sealed record CrisisCancelResult(string? Refusal, Crisis? Crisis = null, IReadOnlyList<ulong>? Refunded = null);

public sealed record CrisisRules(
    Func<GameConstants, Lean, double, double, double> Gain,
    Func<GameConstants, double, double> DefaultLoss,
    Func<GameConstants, Lean, Lean?, double, double> Condemnation)
{
    public static CrisisRules Standard { get; } = new(Formulas.CrisisGain, Formulas.CrisisDefaultLoss, Formulas.CondemnationChange);
}

internal sealed record StoredCrisisAnswer(string Lean, string Label, decimal Price, string Text, string Effects);

public partial class DatabaseService
{
    private const string SelectCrisisColumns =
        "id, template_id, event_id, title, ministry, province_id, issue_id, answers, complication_text, complication_effects, " +
        "default_text, default_effects, posted_resolution, due_resolution, status, chosen_answer, responder_faction_id, response_text, outcome, message_id";

    private static Crisis ReadCrisis(SqliteDataReader r) => new(
        r.GetInt64(0),
        r.GetString(1),
        r.IsDBNull(2) ? null : r.GetInt64(2),
        r.GetString(3),
        Enum.Parse<Ministry>(r.GetString(4)),
        r.GetString(5),
        r.GetString(6),
        ParseAnswers(r.GetString(7)),
        r.IsDBNull(8) ? null : new CrisisOutcomeText(r.GetString(8), r.IsDBNull(9) ? [] : EffectParser.Parse(r.GetString(9))),
        new CrisisOutcomeText(r.GetString(10), EffectParser.Parse(r.GetString(11))),
        r.GetInt64(12),
        r.GetInt64(13),
        Enum.Parse<CrisisStatus>(r.GetString(14)),
        r.IsDBNull(15) ? null : r.GetInt32(15),
        r.IsDBNull(16) ? null : ulong.Parse(r.GetString(16)),
        r.IsDBNull(17) ? null : r.GetString(17),
        r.IsDBNull(18) ? null : Enum.Parse<CrisisOutcome>(r.GetString(18)),
        r.IsDBNull(19) ? null : ulong.Parse(r.GetString(19)));

    private const string SelectCondemnationColumns = "id, faction_id, crisis_id, side, made_at";

    private static Condemnation ReadCondemnation(SqliteDataReader r) => new(
        r.GetInt64(0),
        ulong.Parse(r.GetString(1)),
        r.GetInt64(2),
        (Lean)r.GetInt32(3),
        r.GetInt64(4));

    public static string AnswersStamp(Crisis crisis) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(FormatAnswers(crisis.Answers))))[..8];

    private static string FormatAnswers(IEnumerable<CrisisAnswer> answers) =>
        JsonSerializer.Serialize(answers.Select(a =>
            new StoredCrisisAnswer(a.Lean.ToString(), a.Label, a.Price, a.Text, EffectParser.Format(a.Effects))).ToList());

    private static IReadOnlyList<CrisisAnswer> ParseAnswers(string json) =>
        (JsonSerializer.Deserialize<List<StoredCrisisAnswer>>(json) ?? [])
            .Select(a => new CrisisAnswer(Enum.Parse<Lean>(a.Lean), a.Label, a.Price, a.Text, EffectParser.Parse(a.Effects)))
            .ToList();

    private static List<Crisis> QueryCrisesTx(SqliteConnection conn, SqliteTransaction? tx, string where, params (string Name, object? Value)[] parameters)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT {SelectCrisisColumns} FROM crises WHERE {where} ORDER BY id;";
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        var list = new List<Crisis>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadCrisis(r));
        return list;
    }

    private static Crisis? GetCrisisTx(SqliteConnection conn, SqliteTransaction? tx, long crisisId) =>
        QueryCrisesTx(conn, tx, "id = $id", ("$id", crisisId)).FirstOrDefault();

    private static List<Condemnation> GetCondemnationsTx(SqliteConnection conn, SqliteTransaction? tx, long crisisId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT {SelectCondemnationColumns} FROM condemnations WHERE crisis_id = $crisis ORDER BY id;";
        cmd.Parameters.AddWithValue("$crisis", crisisId);
        var list = new List<Condemnation>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadCondemnation(r));
        return list;
    }

    public Crisis? GetCrisis(long crisisId)
    {
        using var conn = Open();
        return GetCrisisTx(conn, null, crisisId);
    }

    public List<Crisis> GetCrises(CrisisStatus? status = null)
    {
        using var conn = Open();
        return status is { } s
            ? QueryCrisesTx(conn, null, "status = $status", ("$status", s.ToString()))
            : QueryCrisesTx(conn, null, "1 = 1");
    }

    public List<Condemnation> GetCondemnations(long crisisId)
    {
        using var conn = Open();
        return GetCondemnationsTx(conn, null, crisisId);
    }

    public List<AwaitingCrisis> CrisesAwaiting(ulong factionId)
    {
        using var conn = Open();
        return QueryCrisesTx(conn, null, "status = 'Live'")
            .Where(c => ResponsibleMinisterTx(conn, null, c.Ministry) == factionId)
            .Select(c => new AwaitingCrisis(c, c.ChosenAnswer is not null && c.ResponderId == factionId))
            .ToList();
    }

    public List<Crisis> CondemnableCrises(ulong factionId)
    {
        using var conn = Open();
        return QueryCrisesTx(conn, null, "status = 'Live' AND id NOT IN (SELECT crisis_id FROM condemnations WHERE faction_id = $faction)",
            ("$faction", factionId.ToString()));
    }

    public bool IsCrisisLocked(long crisisId)
    {
        using var conn = Open();
        return GetCrisisTx(conn, null, crisisId) is not { Status: CrisisStatus.Live, ChosenAnswer: null };
    }

    public void SetCrisisMessageId(long crisisId, ulong messageId)
    {
        using var conn = Open();
        Exec(conn, null, "UPDATE crises SET message_id = $message WHERE id = $id;", ("$id", crisisId), ("$message", messageId.ToString()));
    }

    public CrisisCancelResult CancelCrisis(long crisisId)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        if (GetCrisisTx(conn, tx, crisisId) is not { } crisis) return new CrisisCancelResult(Texts.Get("crisis.refusal.gone"));
        if (crisis.Status != CrisisStatus.Live) return new CrisisCancelResult(Texts.Get("crisis.refusal.closed", ("crisis", crisis.Title)));
        var refunded = GetCondemnationsTx(conn, tx, crisisId).Select(c => c.FactionId).ToList();
        CancelCrisisTx(conn, tx, crisisId);
        LogTx(conn, tx, LogKinds.GmOverride, "gm crisis cancel",
            $"Crisis {crisisId} ({crisis.TemplateId}) withdrawn; {refunded.Count} condemnation(s) refunded.", isPublic: true);
        var cancelled = GetCrisisTx(conn, tx, crisisId)!;
        tx.Commit();
        return new CrisisCancelResult(null, cancelled, refunded);
    }

    private static void CancelCrisisTx(SqliteConnection conn, SqliteTransaction tx, long crisisId)
    {
        Exec(conn, tx, "UPDATE crises SET status = 'Cancelled' WHERE id = $id AND status = 'Live';", ("$id", crisisId));
        RefundOppositionActionTx(conn, tx, OppositionActionKind.Condemn, crisisId);
    }

    public string? EditLiveCrisis(long crisisId, CrisisTemplate edited)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        if (GetCrisisTx(conn, tx, crisisId) is not { } crisis) return Texts.Get("crisis.refusal.gone");
        if (crisis.Status != CrisisStatus.Live) return Texts.Get("crisis.refusal.closed", ("crisis", crisis.Title));
        if (crisis.ChosenAnswer is not null) return Texts.Get("gmCrisis.edit.locked", ("crisis", crisis.Title));
        if (edited.Ministry != crisis.Ministry || edited.Issue != crisis.IssueId) return Texts.Get("gmCrisis.edit.keeps");
        if (GetProvincesTx(conn, tx).All(p => p.Id != edited.Province)) return Texts.Get("crisis.warning.noProvince", ("id", edited.Province));

        var updated = ExecCount(conn, tx, @"
            UPDATE crises SET title = $title, province_id = $province, answers = $answers,
                complication_text = $complicationText, complication_effects = $complicationEffects,
                default_text = $defaultText, default_effects = $defaultEffects
            WHERE id = $id AND status = 'Live' AND chosen_answer IS NULL;",
            ("$id", crisisId), ("$title", edited.Title), ("$province", edited.Province), ("$answers", FormatAnswers(edited.Answers)),
            ("$complicationText", edited.Complication?.Text),
            ("$complicationEffects", edited.Complication is { } complication ? EffectParser.Format(complication.Effects) : null),
            ("$defaultText", edited.Default.Text), ("$defaultEffects", EffectParser.Format(edited.Default.Effects)));
        if (updated == 0) return Texts.Get("gmCrisis.edit.locked", ("crisis", crisis.Title));
        LogTx(conn, tx, LogKinds.GmOverride, "gm crisis edit", $"Live crisis {crisisId} ({crisis.TemplateId}) edited.");
        tx.Commit();
        return null;
    }

    public static CrisisTemplate LiveCopyOf(Crisis crisis) =>
        new(crisis.TemplateId, crisis.Title, crisis.Ministry, crisis.ProvinceId, crisis.IssueId, crisis.Answers, crisis.Complication, crisis.Default);

    private static string? PostCrisisTx(
        SqliteConnection conn, SqliteTransaction tx, GameState state, GameConfig config, string templateId, EffectSource source, ResolutionReport? report)
    {
        if (ReadyTemplateTx<CrisisTemplate>(conn, tx, templateId, out var problem) is not { } template) return problem;
        if (!template.Ministry.HoldsCrises()) return Texts.Get("crisis.warning.ministry", ("ministry", template.Ministry.DisplayName()));
        if (GetIssueTx(conn, tx, template.Issue) is not { Status: IssueStatus.Open })
            return Texts.Get("crisis.warning.issueNotOpen", ("id", template.Issue));
        if (config.Province(template.Province) is null || GetProvincesTx(conn, tx).All(p => p.Id != template.Province))
            return Texts.Get("crisis.warning.noProvince", ("id", template.Province));

        long? eventId = source.SourceType == "event" && long.TryParse(source.SourceId, out var parsed)
                        && Convert.ToInt64(CrisisScalarTx(conn, tx, "SELECT COUNT(*) FROM events WHERE id = $id;", ("$id", parsed))) > 0
            ? parsed
            : null;

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO crises (template_id, event_id, title, ministry, province_id, issue_id, answers, complication_text, complication_effects,
                default_text, default_effects, posted_at, posted_resolution, due_resolution, status)
            VALUES ($template, $event, $title, $ministry, $province, $issue, $answers, $complicationText, $complicationEffects,
                $defaultText, $defaultEffects, $now, $posted, $due, 'Live')
            RETURNING id;";
        cmd.Parameters.AddWithValue("$template", template.Id);
        cmd.Parameters.AddWithValue("$event", (object?)eventId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$title", template.Title);
        cmd.Parameters.AddWithValue("$ministry", template.Ministry.ToString());
        cmd.Parameters.AddWithValue("$province", template.Province);
        cmd.Parameters.AddWithValue("$issue", template.Issue);
        cmd.Parameters.AddWithValue("$answers", FormatAnswers(template.Answers));
        cmd.Parameters.AddWithValue("$complicationText", (object?)template.Complication?.Text ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$complicationEffects", template.Complication is { } complication ? EffectParser.Format(complication.Effects) : DBNull.Value);
        cmd.Parameters.AddWithValue("$defaultText", template.Default.Text);
        cmd.Parameters.AddWithValue("$defaultEffects", EffectParser.Format(template.Default.Effects));
        cmd.Parameters.AddWithValue("$now", Now());
        cmd.Parameters.AddWithValue("$posted", state.ResolutionNumber);
        cmd.Parameters.AddWithValue("$due", state.ResolutionNumber + 1);
        var id = (long)cmd.ExecuteScalar()!;
        MarkTemplatePostedTx(conn, tx, template.Id);
        LogTx(conn, tx, LogKinds.StateChange, "crisis", $"Crisis {id} ({template.Id}) posted for {template.Ministry}, due at resolution {state.ResolutionNumber + 1}.", isPublic: true);

        if (report is not null)
        {
            report.NewCrises.Add(new CrisisPostedReport(id, template.Title));
            if (ResponsibleMinisterTx(conn, tx, template.Ministry) is null)
                report.Alerts.Add(Texts.Get("crisis.alert.nobody", ("crisis", template.Title), ("ministry", template.Ministry.DisplayName())));
        }
        return null;
    }

    private static object? CrisisScalarTx(SqliteConnection conn, SqliteTransaction? tx, string sql, params (string Name, object? Value)[] parameters)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd.ExecuteScalar();
    }

    public string? CrisisResponseRefusal(long crisisId, ulong factionId)
    {
        using var conn = Open();
        return CrisisResponseRefusalTx(conn, null, crisisId, factionId);
    }

    private static string? CrisisResponseRefusalTx(SqliteConnection conn, SqliteTransaction? tx, long crisisId, ulong factionId)
    {
        if (GetCrisisTx(conn, tx, crisisId) is not { } crisis) return Texts.Get("crisis.refusal.gone");
        if (crisis.Status != CrisisStatus.Live) return Texts.Get("crisis.refusal.closed", ("crisis", crisis.Title));
        var minister = ResponsibleMinisterTx(conn, tx, crisis.Ministry);
        return minister == factionId
            ? null
            : Texts.Get("crisis.refusal.notYours", ("crisis", crisis.Title), ("minister", minister is { } m ? $"<@{m}>" : Texts.Get("cabinet.vacant")));
    }

    public CrisisResponseResult RespondToCrisis(long crisisId, ulong factionId, int answerNumber, string text, string? answersStamp = null)
    {
        text = text.Trim();
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        if (CrisisResponseRefusalTx(conn, tx, crisisId, factionId) is { } refusal) return new CrisisResponseResult(refusal);
        var crisis = GetCrisisTx(conn, tx, crisisId)!;
        if (answersStamp is not null && answersStamp != AnswersStamp(crisis))
            return new CrisisResponseResult(Texts.Get("crisis.refusal.amended", ("crisis", crisis.Title)));
        if (crisis.AnswerNumbered(answerNumber) is not { } answer)
            return new CrisisResponseResult(Texts.Get("crisis.refusal.noAnswer", ("crisis", crisis.Title), ("answer", answerNumber)));
        if (text.Length == 0) return new CrisisResponseResult(Texts.Get("crisis.refusal.empty"));

        Exec(conn, tx, @"
            UPDATE crises SET chosen_answer = $answer, responder_faction_id = $faction, response_text = $text, responded_at = $now
            WHERE id = $id;",
            ("$id", crisisId), ("$answer", answerNumber), ("$faction", factionId.ToString()), ("$text", text), ("$now", Now()));
        LogTx(conn, tx, LogKinds.StateChange, "crisis respond", $"{factionId} chose answer {answerNumber} on crisis {crisisId}.", factionId);
        var balance = GetBalanceTx(conn, tx, (LedgerAccount)(int)crisis.Ministry);
        var updated = GetCrisisTx(conn, tx, crisisId)!;
        var committed = QueryCrisesTx(conn, tx, "status = 'Live' AND id < $id AND ministry = $ministry", ("$id", crisisId), ("$ministry", crisis.Ministry.ToString()))
            .Where(c => c.ResponderId == factionId && c.Chosen is not null)
            .Sum(c => c.Chosen!.Price);
        tx.Commit();
        return new CrisisResponseResult(null, updated, answer, balance - committed, crisis.ChosenAnswer is not null && crisis.ResponderId == factionId);
    }

    public string? CondemnRefusal(ulong factionId, long? crisisId, GameConstants constants)
    {
        using var conn = Open();
        return CondemnRefusalTx(conn, null, factionId, crisisId, constants);
    }

    private static string? CondemnRefusalTx(SqliteConnection conn, SqliteTransaction? tx, ulong factionId, long? crisisId, GameConstants constants)
    {
        if (RoleOfTx(conn, tx, factionId) != FactionRole.Opposition) return Texts.Get("condemn.refusal.notOpposition");
        if (!GetOppositionStandingTx(conn, tx, factionId, constants).CanAct)
        {
            var state = GetGameStateTx(conn, tx);
            var (season, year) = GameCalendar.Next(state.Season, state.Year);
            return Texts.Get("condemn.refusal.actionUsed", ("season", GameCalendar.Label(season, year)), ("when", Display.NextResolution(state)));
        }
        if (crisisId is not { } id) return null;
        if (GetCrisisTx(conn, tx, id) is not { } crisis) return Texts.Get("crisis.refusal.gone");
        if (crisis.Status != CrisisStatus.Live) return Texts.Get("crisis.refusal.closed", ("crisis", crisis.Title));
        if (GetCondemnationsTx(conn, tx, id).Any(c => c.FactionId == factionId))
            return Texts.Get("condemn.refusal.already", ("crisis", crisis.Title));
        return null;
    }

    public CondemnResult Condemn(ulong factionId, long crisisId, Lean side, GameConstants constants)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        if (side == Lean.Neutral) return new CondemnResult(Texts.Get("condemn.refusal.side"));
        if (CondemnRefusalTx(conn, tx, factionId, crisisId, constants) is { } refusal) return new CondemnResult(refusal);

        var id = Convert.ToInt64(CrisisScalarTx(conn, tx, @"
            INSERT INTO condemnations (faction_id, crisis_id, side, made_at) VALUES ($faction, $crisis, $side, $now) RETURNING id;",
            ("$faction", factionId.ToString()), ("$crisis", crisisId), ("$side", (int)side), ("$now", Now())));
        RecordOppositionActionTx(conn, tx, factionId, OppositionActionKind.Condemn, crisisId);
        LogTx(conn, tx, LogKinds.StateChange, "opposition condemn", $"{factionId} condemned crisis {crisisId}, standing {side}.", factionId);
        var crisis = GetCrisisTx(conn, tx, crisisId);
        var condemnation = GetCondemnationsTx(conn, tx, crisisId).Single(c => c.Id == id);
        tx.Commit();
        return new CondemnResult(null, crisis, condemnation);
    }

    internal ResolutionReport ResolveDueCrises(GameConfig config, CrisisRules rules)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        var state = GetGameStateTx(conn, tx);
        var report = new ResolutionReport { ResolutionNumber = state.ResolutionNumber + 1, FromSeason = state.Season, FromYear = state.Year };
        ResolveDueCrisesTx(conn, tx, state, config, report, rules);
        tx.Commit();
        return report;
    }

    private static void ResolveDueCrisesTx(SqliteConnection conn, SqliteTransaction tx, GameState state, GameConfig config, ResolutionReport report) =>
        ResolveDueCrisesTx(conn, tx, state, config, report, CrisisRules.Standard);

    private static void ResolveDueCrisesTx(
        SqliteConnection conn, SqliteTransaction tx, GameState state, GameConfig config, ResolutionReport report, CrisisRules rules)
    {
        var c = config.Constants;
        var due = QueryCrisesTx(conn, tx, "status = 'Live' AND due_resolution <= $resolution", ("$resolution", state.ResolutionNumber + 1));
        if (due.Count == 0) return;
        var snapshot = LoadOpinionSnapshotTx(conn, tx, config);

        foreach (var crisis in due)
        {
            var opinion = snapshot.Provincial(crisis.IssueId, crisis.ProvinceId);
            var salience = snapshot.Salience(crisis.IssueId);
            var minister = ResponsibleMinisterTx(conn, tx, crisis.Ministry);
            var answer = minister is { } current && crisis.ResponderId == current ? crisis.Chosen : null;
            var texts = new List<string>();
            var effects = new List<Effect>();
            var source = new EffectSource(crisis.Title, "crisis", crisis.Id.ToString(), PublicLedger: true, ActorId: minister);
            CrisisOutcome outcome;
            double change = 0;

            if (answer is not null && minister is { } responder)
            {
                var account = (LedgerAccount)(int)crisis.Ministry;
                var label = Texts.Get("crisis.ledger", ("crisis", crisis.Title), ("answer", answer.Label));
                outcome = answer.Price <= GetBalanceTx(conn, tx, account) ? CrisisOutcome.Clean : CrisisOutcome.Complicated;
                if (answer.Price > 0)
                    PostLedgerTx(conn, tx, account, -answer.Price, LedgerKind.Crisis, label, "crisis", crisis.Id.ToString());

                texts.Add(answer.Text);
                effects.AddRange(ApplyEffectsTx(conn, tx, answer.Effects, config, source with { Label = label }, report).Applied);

                if (outcome == CrisisOutcome.Complicated)
                {
                    var complication = crisis.Complication ?? new CrisisOutcomeText(
                        Texts.Get("crisis.standardComplication"), [new Effect($"discontent.{crisis.ProvinceId}", 1)]);
                    texts.Add(complication.Text);
                    effects.AddRange(ApplyEffectsTx(conn, tx, complication.Effects, config, source, report).Applied);
                }

                change = PopularityChangeTx(conn, tx, responder, rules.Gain(c, answer.Lean, opinion, salience), c.Popularity);
                if (answer.Lean != Lean.Neutral)
                {
                    var direction = outcome == CrisisOutcome.Clean ? 1 : -1;
                    ApplyIssueValueTx(conn, tx, crisis.IssueId, "opinion", direction * c.Opinion.CrisisShift * (int)answer.Lean, -1, 1, null);
                }
                ApplyIssueValueTx(conn, tx, crisis.IssueId, "salience", c.Salience.CrisisRaise, c.Salience.Min, c.Salience.Max, state.ResolutionNumber);
            }
            else
            {
                outcome = CrisisOutcome.Default;
                texts.Add(crisis.Default.Text);
                effects.AddRange(ApplyEffectsTx(conn, tx, crisis.Default.Effects, config, source, report).Applied);
                if (minister is { } neglectful)
                    change = PopularityChangeTx(conn, tx, neglectful, -Math.Abs(rules.DefaultLoss(c, salience)), c.Popularity);
            }

            var scores = new List<CondemnationScore>();
            foreach (var condemnation in GetCondemnationsTx(conn, tx, crisis.Id))
            {
                var delta = rules.Condemnation(c, condemnation.Side, answer?.Lean, opinion);
                PopularityChangeTx(conn, tx, condemnation.FactionId, delta, c.Popularity);
                scores.Add(new CondemnationScore(condemnation.FactionId, condemnation.Side, delta));
                LogTx(conn, tx, LogKinds.Resolution, "condemnation",
                    $"{condemnation.FactionId} stood {condemnation.Side} on crisis {crisis.Id}: {delta:+0.##;-0.##;0}.", condemnation.FactionId, isPublic: true);
            }

            Exec(conn, tx, "UPDATE crises SET status = 'Resolved', outcome = $outcome WHERE id = $id;",
                ("$id", crisis.Id), ("$outcome", outcome.ToString()));
            report.CrisisOutcomes.Add(new CrisisOutcomeReport(
                crisis.Id, crisis.Title, crisis.Ministry, minister, outcome, answer?.Label, answer is null ? null : crisis.ResponseText,
                texts, effects, change, scores));
            LogTx(conn, tx, LogKinds.Resolution, "crisis",
                $"Crisis {crisis.Id} resolved {outcome}{(answer is null ? "" : $" with answer {crisis.ChosenAnswer}")}: {minister?.ToString() ?? "nobody"} {change:+0.##;-0.##;0}.",
                minister, isPublic: true);
        }
    }
}
