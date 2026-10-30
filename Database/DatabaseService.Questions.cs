using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

public record Question(
    long Id,
    ulong AskerId,
    Ministry Ministry,
    string Text,
    long AskedResolution,
    long DueResolution,
    ulong? MinisterAtAsk,
    QuestionStatus Status,
    string? AnswerText,
    ulong? AnswererId,
    ulong? MessageId)
{
    public bool Extended => DueResolution > AskedResolution + DatabaseService.QuestionResolutions;
}

public sealed record AskResult(string? Refusal, Question? Question);

public sealed record AnswerResult(string? Refusal, Question? Question);

public partial class DatabaseService
{
    public const long QuestionResolutions = 2;

    private const string SelectQuestionColumns =
        "id, asker_faction_id, ministry, text, asked_resolution, due_resolution, minister_at_ask, status, answer_text, answerer_faction_id, message_id";

    private static Question ReadQuestion(SqliteDataReader r) => new(
        r.GetInt64(0),
        ulong.Parse(r.GetString(1)),
        Enum.Parse<Ministry>(r.GetString(2)),
        r.GetString(3),
        r.GetInt64(4),
        r.GetInt64(5),
        r.IsDBNull(6) ? null : ulong.Parse(r.GetString(6)),
        Enum.Parse<QuestionStatus>(r.GetString(7)),
        r.IsDBNull(8) ? null : r.GetString(8),
        r.IsDBNull(9) ? null : ulong.Parse(r.GetString(9)),
        r.IsDBNull(10) ? null : ulong.Parse(r.GetString(10)));

    private static List<Question> QueryQuestionsTx(SqliteConnection conn, SqliteTransaction? tx, string where, params (string Name, object? Value)[] parameters)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT {SelectQuestionColumns} FROM questions WHERE {where} ORDER BY id;";
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        var list = new List<Question>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadQuestion(r));
        return list;
    }

    public Question? GetQuestion(long id)
    {
        using var conn = Open();
        return QueryQuestionsTx(conn, null, "id = $id", ("$id", id)).FirstOrDefault();
    }

    public List<Question> GetOpenQuestions(Ministry? ministry = null)
    {
        using var conn = Open();
        return ministry is { } m
            ? QueryQuestionsTx(conn, null, "status = 'Open' AND ministry = $ministry", ("$ministry", m.ToString()))
            : QueryQuestionsTx(conn, null, "status = 'Open'");
    }

    public List<Question> QuestionsToAnswer(ulong factionId)
    {
        using var conn = Open();
        return QueryQuestionsTx(conn, null, "status = 'Open'")
            .Where(q => ResponsibleMinisterTx(conn, null, q.Ministry) == factionId)
            .ToList();
    }

    public void SetQuestionMessageId(long questionId, ulong messageId)
    {
        using var conn = Open();
        Exec(conn, null, "UPDATE questions SET message_id = $message WHERE id = $id;", ("$id", questionId), ("$message", messageId.ToString()));
    }

    public string? QuestionRefusal(ulong askerId, Ministry ministry, GameConstants constants)
    {
        using var conn = Open();
        return QuestionRefusalTx(conn, null, askerId, ministry, constants);
    }

    private static string? QuestionRefusalTx(SqliteConnection conn, SqliteTransaction? tx, ulong askerId, Ministry ministry, GameConstants constants)
    {
        if (!GameEnumExtensions.CabinetMinistries.Contains(ministry))
            return Texts.Get("questions.refusal.notAMinistry");
        if (RoleOfTx(conn, tx, askerId) != FactionRole.Opposition)
            return Texts.Get("questions.refusal.notOpposition");
        if (!GetOppositionStandingTx(conn, tx, askerId, constants).CanAct)
        {
            var state = GetGameStateTx(conn, tx);
            var (season, year) = GameCalendar.Next(state.Season, state.Year);
            return Texts.Get("questions.refusal.actionUsed", ("season", GameCalendar.Label(season, year)), ("when", Display.NextResolution(state)));
        }
        if (ResponsibleMinisterTx(conn, tx, ministry) is null)
            return Texts.Get("questions.refusal.nobodyToAnswer", ("ministry", ministry.DisplayName()));
        return null;
    }

    public AskResult AskQuestion(ulong askerId, Ministry ministry, string text, GameConstants constants)
    {
        text = text.Trim();
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        var refusal = text.Length == 0 ? Texts.Get("questions.refusal.empty") : QuestionRefusalTx(conn, tx, askerId, ministry, constants);
        if (refusal is not null) return new AskResult(refusal, null);

        var state = GetGameStateTx(conn, tx);
        var minister = ResponsibleMinisterTx(conn, tx, ministry);
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO questions (asker_faction_id, ministry, text, asked_at, asked_resolution, due_resolution, minister_at_ask, status)
            VALUES ($asker, $ministry, $text, $now, $asked, $due, $minister, 'Open') RETURNING id;";
        cmd.Parameters.AddWithValue("$asker", askerId.ToString());
        cmd.Parameters.AddWithValue("$ministry", ministry.ToString());
        cmd.Parameters.AddWithValue("$text", text);
        cmd.Parameters.AddWithValue("$now", Now());
        cmd.Parameters.AddWithValue("$asked", state.ResolutionNumber);
        cmd.Parameters.AddWithValue("$due", state.ResolutionNumber + QuestionResolutions);
        cmd.Parameters.AddWithValue("$minister", (object?)minister?.ToString() ?? DBNull.Value);
        var id = (long)cmd.ExecuteScalar()!;
        RecordOppositionActionTx(conn, tx, askerId, OppositionActionKind.Question, id);
        LogTx(conn, tx, LogKinds.StateChange, "opposition question", $"{askerId} asked question {id} of {ministry} ({minister}).", askerId, isPublic: true);
        var question = QueryQuestionsTx(conn, tx, "id = $id", ("$id", id)).Single();
        tx.Commit();
        return new AskResult(null, question);
    }

    public string? AnswerRefusal(long questionId, ulong factionId)
    {
        using var conn = Open();
        return AnswerRefusalTx(conn, null, questionId, factionId);
    }

    private static string? AnswerRefusalTx(SqliteConnection conn, SqliteTransaction? tx, long questionId, ulong factionId)
    {
        if (QueryQuestionsTx(conn, tx, "id = $id", ("$id", questionId)).FirstOrDefault() is not { } question)
            return Texts.Get("questions.refusal.gone");
        if (question.Status != QuestionStatus.Open)
            return Texts.Get("questions.refusal.closed");
        var minister = ResponsibleMinisterTx(conn, tx, question.Ministry);
        return minister == factionId
            ? null
            : Texts.Get("questions.refusal.notYours", ("minister", minister is { } m ? $"<@{m}>" : Texts.Get("cabinet.vacant")));
    }

    public AnswerResult AnswerQuestion(long questionId, ulong factionId, string text)
    {
        text = text.Trim();
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        var refusal = text.Length == 0 ? Texts.Get("questions.refusal.empty") : AnswerRefusalTx(conn, tx, questionId, factionId);
        if (refusal is not null) return new AnswerResult(refusal, null);

        Exec(conn, tx, @"
            UPDATE questions SET status = 'Answered', answer_text = $text, answered_at = $now, answerer_faction_id = $faction
            WHERE id = $id;",
            ("$id", questionId), ("$text", text), ("$now", Now()), ("$faction", factionId.ToString()));
        LogTx(conn, tx, LogKinds.StateChange, "question answer", $"{factionId} answered question {questionId}.", factionId, isPublic: true);
        var question = QueryQuestionsTx(conn, tx, "id = $id", ("$id", questionId)).Single();
        tx.Commit();
        return new AnswerResult(null, question);
    }

    internal ResolutionReport ResolveDueQuestions(GameConfig config)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        var state = GetGameStateTx(conn, tx);
        var report = new ResolutionReport { ResolutionNumber = state.ResolutionNumber + 1 };
        ResolveDueQuestionsTx(conn, tx, state, config, report);
        tx.Commit();
        return report;
    }

    private static void ResolveDueQuestionsTx(SqliteConnection conn, SqliteTransaction tx, GameState state, GameConfig config, ResolutionReport report)
    {
        var constants = config.Constants;
        var resolution = state.ResolutionNumber + 1;
        foreach (var question in QueryQuestionsTx(conn, tx, "status = 'Open' AND due_resolution <= $resolution", ("$resolution", resolution)))
        {
            var minister = ResponsibleMinisterTx(conn, tx, question.Ministry);
            if (minister is not { } ministerId)
            {
                Exec(conn, tx, "UPDATE questions SET status = 'Dropped' WHERE id = $id;", ("$id", question.Id));
                report.Questions.Add(new QuestionReport(question.Id, question.Ministry, question.AskerId, null, QuestionOutcomeKind.Dropped, 0, 0));
                report.AddPrivate(question.AskerId, Texts.Get("questions.private.dropped", ("ministry", question.Ministry.DisplayName())));
                LogTx(conn, tx, LogKinds.Resolution, "question", $"Question {question.Id} dropped: nobody holds {question.Ministry} or the Chancellorship.", question.AskerId);
                continue;
            }

            if (ministerId != question.MinisterAtAsk && !question.Extended)
            {
                Exec(conn, tx, "UPDATE questions SET due_resolution = $due, minister_at_ask = $minister WHERE id = $id;",
                    ("$id", question.Id), ("$due", question.DueResolution + 1), ("$minister", ministerId.ToString()));
                report.Questions.Add(new QuestionReport(question.Id, question.Ministry, question.AskerId, ministerId, QuestionOutcomeKind.Extended, 0, 0));
                report.AddPrivate(ministerId, Texts.Get("questions.private.extended", ("ministry", question.Ministry.DisplayName()), ("text", question.Text)));
                LogTx(conn, tx, LogKinds.Resolution, "question", $"Question {question.Id} extended to resolution {question.DueResolution + 1} for {ministerId}.", ministerId);
                continue;
            }

            Exec(conn, tx, "UPDATE questions SET status = 'Unanswered' WHERE id = $id;", ("$id", question.Id));
            var ministerChange = PopularityChangeTx(conn, tx, ministerId, -constants.Opposition.UnansweredQuestion, constants.Popularity);
            var askerChange = PopularityChangeTx(conn, tx, question.AskerId, constants.Opposition.QuestionReward, constants.Popularity);
            report.Questions.Add(new QuestionReport(question.Id, question.Ministry, question.AskerId, ministerId, QuestionOutcomeKind.Unanswered, ministerChange, askerChange));
            LogTx(conn, tx, LogKinds.Resolution, "question",
                $"Question {question.Id} unanswered: {ministerId} {ministerChange:+0.##;-0.##;0}, {question.AskerId} {askerChange:+0.##;-0.##;0}.", ministerId, isPublic: true);
        }

        foreach (var question in QueryQuestionsTx(conn, tx, "status = 'Open' AND due_resolution = $next", ("$next", resolution + 1)))
            if (ResponsibleMinisterTx(conn, tx, question.Ministry) is null)
                report.Alerts.Add(Texts.Get("questions.alert.nobodyToAnswer",
                    ("ministry", question.Ministry.DisplayName()), ("asker", $"<@{question.AskerId}>")));
    }

    private static double PopularityChangeTx(SqliteConnection conn, SqliteTransaction tx, ulong factionId, double delta, PopularityConstants range)
    {
        if (GetFactionTx(conn, tx, factionId) is not { } faction) return 0;
        return AddPopularityTx(conn, tx, factionId, delta, range) is { } updated ? updated - faction.Popularity : 0;
    }
}
