using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

public sealed record EffectSource(
    string Label, string SourceType, string SourceId, LedgerKind LedgerKind = LedgerKind.Effect, bool PublicLedger = false, ulong? ActorId = null);

public sealed record EffectApplication(IReadOnlyList<Effect> Applied, IReadOnlyList<string> Warnings);

public partial class DatabaseService
{
    public EffectApplication ApplyEffects(IReadOnlyList<Effect> effects, GameConfig config, EffectSource source)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        var result = ApplyEffectsTx(conn, tx, effects, config, source, null);
        LogTx(conn, tx, LogKinds.GmOverride, source.SourceType, $"{source.Label}: {EffectParser.Format(result.Applied)}");
        tx.Commit();
        return result;
    }

    private static EffectApplication ApplyEffectsTx(
        SqliteConnection conn, SqliteTransaction tx, IReadOnlyList<Effect> effects, GameConfig config, EffectSource source, ResolutionReport? report)
    {
        var store = new TxEffectStore(conn, tx, config, source, report);
        var applied = new List<Effect>();
        var warnings = new List<string>();

        foreach (var effect in effects)
        {
            var warning = effect.Kind is not { } kind
                ? Texts.Get("data.effect.unknownKey", ("key", effect.Key))
                : ApplyOne(kind, effect, store, applied);
            if (warning is not null)
                warnings.Add(Texts.Get("data.effect.warning", ("source", source.Label), ("key", effect.Key), ("warning", warning)));
        }

        if (report is not null)
            report.Alerts.AddRange(warnings);
        return new EffectApplication(applied, warnings);
    }

    private static string? ApplyOne(EffectKind kind, Effect effect, TxEffectStore store, List<Effect> applied)
    {
        var before = kind.IsAction ? null : kind.CurrentValue(effect, store);
        if (kind.Apply(effect, store) is { } warning) return warning;
        applied.Add(before is { } old && kind.CurrentValue(effect, store) is { } now
            ? effect with { Value = Math.Round(now - old, 6) }
            : effect);
        return null;
    }

    private sealed class TxEffectStore(
        SqliteConnection conn, SqliteTransaction tx, GameConfig config, EffectSource source, ResolutionReport? report) : IEffectStore
    {
        public GameState State => GetGameStateTx(conn, tx);

        public GameConfig Config => config;

        public EffectSource Source => source;

        public void SetState(string column, double value) => SetStateTx(conn, tx, column, value);

        public decimal Balance(LedgerAccount account) => GetBalanceTx(conn, tx, account);

        public void PostLedger(LedgerAccount account, decimal amount) =>
            PostLedgerTx(conn, tx, account, amount, source.LedgerKind, source.Label, source.SourceType, source.SourceId, source.PublicLedger);

        public IReadOnlyList<Province> Provinces => GetProvincesTx(conn, tx);

        public void SetDiscontent(string provinceId, double value) => SetDiscontentTx(conn, tx, provinceId, value);

        public bool SetCondition(string provinceId, string conditionId, bool present) =>
            SetConditionTx(conn, tx, State, provinceId, conditionId, present);

        public Issue? Issue(string issueId) => GetIssueTx(conn, tx, issueId);

        public void SetIssueOpinion(string issueId, double value) =>
            Exec(conn, tx, "UPDATE issues SET opinion = $value WHERE id = $id;", ("$id", issueId), ("$value", value));

        public void SetIssueSalience(string issueId, double value, bool raised)
        {
            Exec(conn, tx, "UPDATE issues SET salience = $value WHERE id = $id;", ("$id", issueId), ("$value", value));
            if (raised)
                Exec(conn, tx, "UPDATE issues SET salience_raised_resolution = $marker WHERE id = $id;",
                    ("$id", issueId), ("$marker", State.ResolutionNumber));
        }

        public IReadOnlyList<Faction> ActiveFactions => QueryFactionsTx(conn, tx, "status = 'Active'");

        public Cabinet? SittingCabinet => GetSittingCabinetTx(conn, tx);

        public FactionRole RoleOf(ulong factionId) => RoleOfTx(conn, tx, factionId);

        public void AddPopularity(ulong factionId, double delta) => AddPopularityTx(conn, tx, factionId, delta, config.Constants.Popularity);

        public string? OpenIssue(string templateId) => OpenIssueFromTemplateTx(conn, tx, State, templateId);

        public string? CloseIssue(string issueId) => CloseIssueTx(conn, tx, State, issueId);

        public string? MakeBillAvailable(string templateId) => MakeBillAvailableTx(conn, tx, State, templateId);

        public string? StartProject(string templateId) => Texts.Get("data.effect.projectNotBuilt");

        public string? PostCrisis(string templateId) => PostCrisisTx(conn, tx, State, config, templateId, source, report);

        public string? QueueEvent(string templateId, int delay)
        {
            if (ReadyTemplateTx<EventTemplate>(conn, tx, templateId, out var problem) is not { } template) return problem;
            QueueEventTx(conn, tx, template, EventScheduling.FireAtForDelay(State, delay), fromSchedule: false);
            return null;
        }

        public string? CancelEvents(string templateId)
        {
            Exec(conn, tx, "UPDATE events SET status = 'Cancelled' WHERE template_id = $template AND status = 'Queued';", ("$template", templateId));
            return null;
        }
    }

    private static string? ApplyIssueValueTx(
        SqliteConnection conn, SqliteTransaction tx, string issueId, string column, double amount, double min, double max, long? raisedMarker)
    {
        if (GetIssueTx(conn, tx, issueId) is not { Status: IssueStatus.Open } issue) return Texts.Get("data.effect.issueNotOpen", ("id", issueId));
        var current = column == "opinion" ? issue.Opinion : issue.Salience;
        Exec(conn, tx, $"UPDATE issues SET {column} = $value WHERE id = $id;", ("$id", issueId), ("$value", Math.Clamp(current + amount, min, max)));
        if (raisedMarker is { } marker)
            Exec(conn, tx, "UPDATE issues SET salience_raised_resolution = $marker WHERE id = $id;", ("$id", issueId), ("$marker", marker));
        return null;
    }

    private static string? SetStateTx(SqliteConnection conn, SqliteTransaction tx, string column, double value)
    {
        Exec(conn, tx, $"UPDATE game_state SET {column} = $value WHERE id = 1;", ("$value", value));
        return null;
    }

    private static T? ReadyTemplateTx<T>(SqliteConnection conn, SqliteTransaction tx, string id, out string? problem) where T : Template
    {
        problem = null;
        var row = GetTemplateTx(conn, tx, id);
        if (row is null) { problem = Texts.Get("data.effect.noTemplate", ("id", id)); return null; }
        if (row.Status != TemplateStatus.Ready) { problem = Texts.Get("data.effect.templateNotReady", ("id", id), ("status", row.Status)); return null; }
        if (TemplateCodec.Parse(row.Yaml, id).Template is not T template) { problem = Texts.Get("data.effect.templateInvalid", ("id", id), ("type", typeof(T).Name)); return null; }
        return template;
    }

    private static string? OpenIssueFromTemplateTx(SqliteConnection conn, SqliteTransaction tx, GameState state, string templateId)
    {
        if (ReadyTemplateTx<IssueTemplate>(conn, tx, templateId, out var problem) is not { } template) return problem;
        if (GetIssueTx(conn, tx, template.Id) is { Status: IssueStatus.Open }) return Texts.Get("data.effect.issueAlreadyOpen", ("id", template.Id));
        Exec(conn, tx, @"
            INSERT INTO issues (id, template_id, title, for_text, against_text, opinion, salience, status, opened_resolution)
            VALUES ($id, $template, $title, $for, $against, $opinion, $salience, 'Open', $resolution)
            ON CONFLICT(id) DO UPDATE SET title = excluded.title, for_text = excluded.for_text, against_text = excluded.against_text,
                opinion = excluded.opinion, salience = excluded.salience, status = 'Open', closed_resolution = NULL,
                salience_raised_resolution = NULL, opened_resolution = excluded.opened_resolution;",
            ("$id", template.Id), ("$template", template.Id), ("$title", template.Title), ("$for", template.For),
            ("$against", template.Against), ("$opinion", template.Opinion), ("$salience", template.Salience),
            ("$resolution", state.ResolutionNumber));
        MarkTemplatePostedTx(conn, tx, template.Id);
        return null;
    }

    private static string? CloseIssueTx(SqliteConnection conn, SqliteTransaction tx, GameState state, string issueId)
    {
        if (GetIssueTx(conn, tx, issueId) is not { Status: IssueStatus.Open }) return Texts.Get("data.effect.issueNotOpen", ("id", issueId));
        Exec(conn, tx, "DELETE FROM programme_stances WHERE issue_id = $id;", ("$id", issueId));
        Exec(conn, tx, "UPDATE issues SET status = 'Closed', closed_resolution = $resolution WHERE id = $id;",
            ("$id", issueId), ("$resolution", state.ResolutionNumber));
        Exec(conn, tx, @"
            UPDATE opposition_actions SET cancelled = 1
            WHERE kind = 'Bill' AND subject_id IN (
                SELECT r.id FROM readings r JOIN bills b ON b.id = r.bill_id
                WHERE b.issue_id = $id AND r.status IN ('Scheduled', 'Open'));", ("$id", issueId));
        Exec(conn, tx, @"
            UPDATE readings SET status = 'Cancelled'
            WHERE status IN ('Scheduled', 'Open') AND bill_id IN (SELECT id FROM bills WHERE issue_id = $id);", ("$id", issueId));
        Exec(conn, tx, "UPDATE bills SET status = 'Withdrawn' WHERE issue_id = $id AND status IN ('Available', 'Voting', 'Cooldown');",
            ("$id", issueId));
        return null;
    }

    private static string? MakeBillAvailableTx(SqliteConnection conn, SqliteTransaction tx, GameState state, string templateId)
    {
        if (ReadyTemplateTx<BillTemplate>(conn, tx, templateId, out var problem) is not { } template) return problem;
        if (GetIssueTx(conn, tx, template.Issue) is not { Status: IssueStatus.Open })
            return Texts.Get("data.effect.billIssueNotOpen", ("id", template.Issue));
        var existing = QueryBillsTx(conn, tx, "id = $id", ("$id", template.Id)).FirstOrDefault();
        if (existing is { Status: BillStatus.Available or BillStatus.Voting or BillStatus.Cooldown })
            return Texts.Get("data.effect.billInPlay", ("id", template.Id));
        if (existing is { Status: BillStatus.Passed })
            return Texts.Get("data.effect.billPassed", ("id", template.Id));

        Exec(conn, tx, @"
            INSERT INTO bills (id, template_id, issue_id, title, motion, lean, pass_text, pass_effects, status, available_resolution)
            VALUES ($id, $template, $issue, $title, $motion, $lean, $passText, $passEffects, 'Available', $resolution)
            ON CONFLICT(id) DO UPDATE SET issue_id = excluded.issue_id, title = excluded.title, motion = excluded.motion,
                lean = excluded.lean, pass_text = excluded.pass_text, pass_effects = excluded.pass_effects,
                status = 'Available', cooldown_until = NULL, available_resolution = excluded.available_resolution;",
            ("$id", template.Id), ("$template", template.Id), ("$issue", template.Issue), ("$title", template.Title),
            ("$motion", template.Motion), ("$lean", (int)template.Lean), ("$passText", template.PassText),
            ("$passEffects", EffectParser.Format(template.PassEffects)), ("$resolution", state.ResolutionNumber));
        MarkTemplatePostedTx(conn, tx, template.Id);
        return null;
    }

    public double? GetEffectValue(string key, GameConfig config)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        var effect = new Effect(key, 0);
        return effect.Kind?.CurrentValue(effect, new TxEffectStore(conn, tx, config, new EffectSource("", "", ""), null));
    }
}
