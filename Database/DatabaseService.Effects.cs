using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

public sealed record EffectSource(
    string Label, string SourceType, string SourceId, LedgerKind LedgerKind = LedgerKind.Effect, bool PublicLedger = false, ulong? ActorId = null);

public sealed record EffectApplication(IReadOnlyList<Effect> Applied, IReadOnlyList<string> Warnings)
{
    public ResolutionReport Report { get; init; } = new();
}

public partial class DatabaseService
{
    public EffectApplication ApplyEffects(IReadOnlyList<Effect> effects, GameConfig config, EffectSource source)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        var report = new ResolutionReport();
        var result = ApplyEffectsTx(conn, tx, effects, config, source, report);
        LogTx(conn, tx, LogKinds.GmOverride, source.SourceType, $"{source.Label}: {EffectParser.Format(result.Applied)}");
        tx.Commit();
        return result with { Report = report };
    }

    private static EffectApplication ApplyEffectsTx(
        SqliteConnection conn, SqliteTransaction tx, IReadOnlyList<Effect> effects, GameConfig config, EffectSource source, ResolutionReport? report)
    {
        var store = new TxEffectStore(conn, tx, config, source, report, EffectRandom(report, source));
        var applied = new List<Effect>();
        var warnings = new List<string>();
        RunEffects(effects, store, applied, warnings, source, 0);

        if (report is not null)
            report.Alerts.AddRange(warnings);
        return new EffectApplication(applied, warnings);
    }

    private const int MaxEffectDepth = 16;
    private const int MaxEffectsRun = 5000;

    private static void RunEffects(
        IReadOnlyList<Effect> effects, TxEffectStore store, List<Effect> applied, List<string> warnings, EffectSource source, int depth)
    {
        var scope = new StoreFormulaScope(store);
        foreach (var effect in effects)
        {
            if (++store.Steps > MaxEffectsRun || depth > MaxEffectDepth)
            {
                if (!store.Exhausted) warnings.Add(Texts.Get("data.effect.tooMany", ("source", source.Label)));
                store.Exhausted = true;
                return;
            }
            try
            {
                switch (effect.Block)
                {
                    case IfBlock b:
                        RunEffects(b.Condition.IsTrue(scope) ? b.Then : b.Else, store, applied, warnings, source, depth + 1);
                        continue;
                    case RandomBlock r:
                        RunEffects(PickOutcome(r, store.Random), store, applied, warnings, source, depth + 1);
                        continue;
                    case EachBlock x:
                        foreach (var id in EachTargets(x, store, scope))
                            RunEffects(EffectTree.Bind(x.Do, EachBlock.It, id), store, applied, warnings, source, depth + 1);
                        continue;
                }

                var warning = effect.Kind is not { } kind
                    ? Texts.Get("data.effect.unknownKey", ("key", effect.Key))
                    : Concrete(effect, kind, store, scope, out var concrete) ?? ApplyOne(kind, concrete!, store, applied);
                if (warning is not null)
                    warnings.Add(Texts.Get("data.effect.warning", ("source", source.Label), ("key", effect.Key), ("warning", warning)));
            }
            catch (FormulaException ex)
            {
                warnings.Add(Texts.Get("data.effect.warning", ("source", source.Label), ("key", effect.Key), ("warning", ex.Message)));
            }
        }
    }

    private static string? Concrete(Effect effect, EffectKind kind, IEffectStore store, IFormulaScope scope, out Effect? concrete)
    {
        concrete = null;
        var value = effect.Formula is { } formula ? Math.Round(formula.Evaluate(scope), 6) : effect.Value;
        if (effect.SetTo)
        {
            if (kind.CurrentValue(effect, store) is not { } current) return Texts.Get("data.effect.cannotSet");
            value = (value ?? 0) - current;
        }
        concrete = new Effect(effect.Key, value);
        return null;
    }

    private static IReadOnlyList<Effect> PickOutcome(RandomBlock block, IRandomSource random)
    {
        var roll = random.NextDouble() * block.Outcomes.Sum(o => o.Weight);
        foreach (var outcome in block.Outcomes)
        {
            if (roll < outcome.Weight) return outcome.Effects;
            roll -= outcome.Weight;
        }
        return block.Outcomes[^1].Effects;
    }

    private static List<string> EachTargets(EachBlock block, IEffectStore store, IFormulaScope scope)
    {
        IEnumerable<string> ids = block.Scope switch
        {
            EachScope.Province => store.Provinces.Select(p => p.Id),
            EachScope.Faction => store.ActiveFactions.Select(f => f.UserId.ToString()),
            EachScope.Party => store.ActiveFactions.Where(f => f.PartyId is not null).Select(f => f.PartyId!.Value.ToString()).Distinct(),
            _ => store.OpenIssues.Select(i => i.Id),
        };
        var chosen = ids.Where(id => block.Where?.Bind(EachBlock.It, id).IsTrue(scope) ?? true).ToList();
        if (block.By is not { } by) return chosen;
        var scored = chosen.Select(id => (Id: id, Score: by.Bind(EachBlock.It, id).Evaluate(scope))).ToList();
        var ordered = block.Lowest ? scored.OrderBy(s => s.Score) : scored.OrderByDescending(s => s.Score);
        return ordered.ThenBy(s => s.Id, StringComparer.Ordinal).Take(block.Top ?? int.MaxValue).Select(s => s.Id).ToList();
    }

    private static IRandomSource EffectRandom(ResolutionReport? report, EffectSource source)
    {
        if (report is not { Seed: not 0 } seeded) return new SeededRandomSource(SeededRandomSource.NewSeed());
        var hash = 2166136261u;
        foreach (var c in $"{seeded.Seed}|{seeded.ResolutionNumber}|{source.SourceType}|{source.SourceId}|{source.Label}")
            hash = (hash ^ c) * 16777619u;
        return new SeededRandomSource((int)hash);
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
        SqliteConnection conn, SqliteTransaction tx, GameConfig config, EffectSource source, ResolutionReport? report, IRandomSource random) : IEffectStore
    {
        public int Steps { get; set; }

        public bool Exhausted { get; set; }

        public IRandomSource Random => random;

        public double Variable(string name) => GetVariableTx(conn, tx, config, name);

        public void SetVariable(string name, double value) => SetVariableTx(conn, tx, name, value);

        public bool HasCondition(string provinceId, string conditionId) =>
            GetProvinceConditionsTx(conn, tx).GetValueOrDefault(provinceId, []).Contains(conditionId);

        public IReadOnlyList<Issue> OpenIssues => QueryIssuesTx(conn, tx, "status = 'Open'");

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

        public string? StartProject(string templateId) => StartProjectTx(conn, tx, config, templateId, null, source, report);

        public int UnitCount(UnitType type, string provinceId) =>
            GetUnitsTx(conn, tx, type).Count(u => u.ProvinceId == provinceId);

        public string? RaiseUnits(UnitType type, string provinceId, int count) => RaiseUnitsTx(conn, tx, config, type, provinceId, count);

        public string? RemoveUnits(UnitType type, string provinceId, int count) => RemoveUnitsTx(conn, tx, type, provinceId, count);

        public string? ProgressProject(string templateId, decimal amount) => ProgressProjectTx(conn, tx, templateId, amount);

        public string? HaltProject(string templateId) => HaltProjectTx(conn, tx, templateId);

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
        foreach (var crisis in QueryCrisesTx(conn, tx, "issue_id = $id AND status = 'Live'", ("$id", issueId)))
            CancelCrisisTx(conn, tx, crisis.Id);
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
        return effect.Kind?.CurrentValue(effect, new TxEffectStore(conn, tx, config, new EffectSource("", "", ""), null, new SeededRandomSource(0)));
    }
}
