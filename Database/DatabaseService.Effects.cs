using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

public sealed record EffectSource(string Label, string SourceType, string SourceId, LedgerKind LedgerKind = LedgerKind.Effect, bool PublicLedger = false);

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
        var c = config.Constants;
        var applied = new List<Effect>();
        var warnings = new List<string>();

        foreach (var effect in effects)
        {
            var state = GetGameStateTx(conn, tx);
            var before = effect.IsAction ? null : CurrentEffectValueTx(conn, tx, effect.Key);
            var target = effect.Target;
            var warning = effect.Prefix switch
            {
                "economy" => SetStateTx(conn, tx, "economy", Math.Max(0, state.Economy + effect.Amount)),
                "taxRate" => SetStateTx(conn, tx, "tax_rate", Math.Clamp(state.TaxRate + effect.Amount, 0, 1)),
                "favour" => SetStateTx(conn, tx, "favour", Math.Clamp(state.Favour + effect.Amount, 0, 100)),
                "favourBaseline" => SetStateTx(conn, tx, "favour_baseline", Math.Clamp(state.FavourBaseline + effect.Amount, 0, 100)),
                "threat" => SetStateTx(conn, tx, "threat", Math.Clamp(state.Threat + effect.Amount, 0, 100)),
                "seats" => SetStateTx(conn, tx, "seats", Math.Max(1, state.Seats + (int)Math.Round(effect.Amount))),
                "reserve" => Ledger(conn, tx, LedgerAccount.Reserve, effect.Amount, source),
                "purse" => Ledger(conn, tx, Enum.Parse<LedgerAccount>(target!), effect.Amount, source),
                "discontent" => ApplyDiscontentTx(conn, tx, c, target!, effect.Amount),
                "condition" => ApplyConditionTx(conn, tx, config, state, target!, effect.SubTarget!, effect.Amount > 0),
                "opinion" => ApplyIssueValueTx(conn, tx, target!, "opinion", effect.Amount, -1, 1, null),
                "salience" => ApplyIssueValueTx(conn, tx, target!, "salience", effect.Amount, c.Salience.Min, c.Salience.Max,
                    effect.Amount > 0 ? state.ResolutionNumber : null),
                "popularity" => ulong.TryParse(target, out var factionId) && AddPopularityTx(conn, tx, factionId, effect.Amount, c.Popularity) is not null
                    ? null : Texts.Get("data.effect.noFaction", ("id", target)),
                "open" => OpenIssueFromTemplateTx(conn, tx, state, target!),
                "close" => CloseIssueTx(conn, tx, state, target!),
                "bill" => MakeBillAvailableTx(conn, tx, state, target!),
                "project" => Texts.Get("data.effect.projectNotBuilt"),
                "crisis" => Texts.Get("data.effect.crisisNotBuilt"),
                _ => Texts.Get("data.effect.unknownKey", ("key", effect.Key)),
            };

            if (warning is null)
                applied.Add(before is { } old && CurrentEffectValueTx(conn, tx, effect.Key) is { } now
                    ? effect with { Value = Math.Round(now - old, 6) }
                    : effect);
            else warnings.Add(Texts.Get("data.effect.warning", ("source", source.Label), ("key", effect.Key), ("warning", warning)));
        }

        if (report is not null)
            report.Alerts.AddRange(warnings);
        return new EffectApplication(applied, warnings);
    }

    private static string? SetStateTx(SqliteConnection conn, SqliteTransaction tx, string column, double value)
    {
        Exec(conn, tx, $"UPDATE game_state SET {column} = $value WHERE id = 1;", ("$value", value));
        return null;
    }

    private static string? Ledger(SqliteConnection conn, SqliteTransaction tx, LedgerAccount account, double amount, EffectSource source)
    {
        PostLedgerTx(conn, tx, account, (decimal)amount, source.LedgerKind, source.Label, source.SourceType, source.SourceId, source.PublicLedger);
        return null;
    }

    private static string? ApplyDiscontentTx(SqliteConnection conn, SqliteTransaction tx, GameConstants c, string provinceId, double amount)
    {
        if (GetProvincesTx(conn, tx).FirstOrDefault(p => p.Id == provinceId) is not { } province) return Texts.Get("data.effect.noProvince", ("id", provinceId));
        SetDiscontentTx(conn, tx, provinceId, Math.Clamp(province.Discontent + amount, c.Discontent.Min, c.Discontent.Max));
        return null;
    }

    private static string? ApplyConditionTx(
        SqliteConnection conn, SqliteTransaction tx, GameConfig config, GameState state, string provinceId, string conditionId, bool present)
    {
        if (GetProvincesTx(conn, tx).All(p => p.Id != provinceId)) return Texts.Get("data.effect.noProvince", ("id", provinceId));
        if (config.Condition(conditionId) is null) return Texts.Get("data.effect.noCondition", ("id", conditionId));
        SetConditionTx(conn, tx, state, provinceId, conditionId, present);
        return null;
    }

    private static string? ApplyIssueValueTx(
        SqliteConnection conn, SqliteTransaction tx, string issueId, string column, double amount, double min, double max, long? raisedMarker)
    {
        if (GetIssueTx(conn, tx, issueId) is not { Status: IssueStatus.Open } issue) return Texts.Get("data.effect.issueNotOpen", ("id", issueId));
        var current = column == "opinion" ? issue.Opinion : issue.Salience;
        Exec(conn, tx, $"UPDATE issues SET {column} = $value WHERE id = $id;",
            ("$id", issueId), ("$value", Math.Clamp(current + amount, min, max)));
        if (raisedMarker is { } marker)
            Exec(conn, tx, "UPDATE issues SET salience_raised_resolution = $marker WHERE id = $id;", ("$id", issueId), ("$marker", marker));
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

    public double? GetEffectValue(string key)
    {
        using var conn = Open();
        return CurrentEffectValueTx(conn, null, key);
    }

    private static double? CurrentEffectValueTx(SqliteConnection conn, SqliteTransaction? tx, string key)
    {
        var effect = new Effect(key, 0);
        var state = GetGameStateTx(conn, tx);
        return effect.Prefix switch
        {
            "purse" when Enum.TryParse<LedgerAccount>(effect.Target, out var account) => (double)GetBalanceTx(conn, tx, account),
            "discontent" => GetProvincesTx(conn, tx).FirstOrDefault(p => p.Id == effect.Target)?.Discontent,
            "opinion" => GetIssueTx(conn, tx, effect.Target!)?.Opinion,
            "salience" => GetIssueTx(conn, tx, effect.Target!)?.Salience,
            "popularity" when ulong.TryParse(effect.Target, out var factionId) => GetFactionTx(conn, tx, factionId)?.Popularity,
            "economy" => state.Economy,
            "taxRate" => state.TaxRate,
            "favour" => state.Favour,
            "favourBaseline" => state.FavourBaseline,
            "threat" => state.Threat,
            "seats" => state.Seats,
            "reserve" => (double)state.Reserve,
            _ => null,
        };
    }
}
