using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Data.Sqlite;

public sealed record LiveProject(
    long Id,
    string TemplateId,
    string Title,
    Ministry Purse,
    string ProvinceId,
    decimal Price,
    int Term,
    decimal Progress,
    decimal Spending,
    long StartedResolution,
    long FirstPaymentResolution,
    long? BonusDueResolution,
    string Effects,
    string? BonusText,
    string? BonusEffects,
    ProjectStatus Status,
    long? FinishedResolution)
{
    public decimal Pace => ProjectMath.Pace(Price, Term);

    public decimal Remaining => Math.Max(0, Price - Progress);

    public bool IsComplete => Remaining <= ProjectMath.Tolerance;

    public bool IsLive => Status is ProjectStatus.Active or ProjectStatus.Stalled;

    public IReadOnlyList<Effect> ParsedEffects => EffectParser.Parse(Effects);

    public IReadOnlyList<Effect> ParsedBonusEffects => EffectParser.Parse(BonusEffects ?? "");

    public LedgerAccount Account => (LedgerAccount)(int)Purse;
}

public static class ProjectMath
{
    public const decimal Tolerance = 0.0001m;

    public static decimal Pace(decimal price, int term) => term > 0 ? price / term : price;

    public static decimal Work(decimal payment, decimal pace, decimal graft) =>
        Math.Min(payment, pace) + (1 - graft) * Math.Max(0, payment - pace);

    public static decimal GraftLost(decimal payment, decimal pace, decimal graft) =>
        Math.Round(graft * Math.Max(0, payment - pace), 2, MidpointRounding.AwayFromZero);

    public static decimal NeededToFinish(decimal remaining, decimal pace, decimal graft)
    {
        if (remaining <= pace) return remaining;
        return graft >= 1 ? decimal.MaxValue : pace + (remaining - pace) / (1 - graft);
    }

    public static decimal Payment(decimal spending, decimal available, decimal remaining, decimal pace, decimal graft) =>
        Math.Round(Math.Max(0, Math.Min(Math.Min(spending, available), NeededToFinish(remaining, pace, graft))), 2, MidpointRounding.AwayFromZero);
}

public sealed record ProjectForecast(
    decimal Spending,
    decimal Pace,
    decimal Excess,
    decimal GraftPerSeason,
    decimal WorkPerSeason,
    int? SeasonsToFinish,
    long? FinishResolution,
    (Season Season, int Year)? FinishTurn,
    bool? BeatsBonus)
{
    private const int Horizon = 400;

    public static ProjectForecast For(LiveProject project, decimal spending, decimal graft, GameState state)
    {
        var pace = project.Pace;
        var excess = Math.Max(0, spending - pace);
        var work = ProjectMath.Work(Math.Max(0, spending), pace, graft);
        var seasons = CountSeasons(project.Remaining, spending, pace, graft);
        long? finish = seasons is { } n ? Math.Max(project.FirstPaymentResolution, state.ResolutionNumber + 1) + Math.Max(n, 1) - 1 : null;
        bool? beats = project.BonusDueResolution is { } due ? finish is { } f && f <= due : null;
        return new ProjectForecast(spending, pace, excess, graft * excess, work, seasons, finish,
            finish is { } at ? TurnEndedBy(state, at) : null, beats);
    }

    public static int? CountSeasons(decimal remaining, decimal spending, decimal pace, decimal graft)
    {
        if (remaining <= ProjectMath.Tolerance) return 0;
        if (spending <= 0) return null;
        for (var season = 1; season <= Horizon; season++)
        {
            var payment = ProjectMath.Payment(spending, spending, remaining, pace, graft);
            var work = ProjectMath.Work(payment, pace, graft);
            if (work <= 0) return null;
            remaining -= work;
            if (remaining <= ProjectMath.Tolerance) return season;
        }
        return null;
    }

    public static (Season Season, int Year) TurnEndedBy(GameState state, long resolution) =>
        GameCalendar.Add(state.Season, state.Year, (int)(resolution - state.ResolutionNumber - 1));
}

public sealed record ProjectStartResult(string? Refusal, LiveProject? Project);

public sealed record ProjectFundResult(string? Refusal, LiveProject? Project, ProjectForecast? Forecast, decimal Balance);

public partial class DatabaseService
{
    private const string SelectProjectColumns =
        "id, template_id, title, purse, province_id, price, term, progress, spending, started_resolution, first_payment_resolution, " +
        "bonus_due_resolution, effects, bonus_text, bonus_effects, status, finished_resolution";

    private const string LiveProjectStatuses = "('Active', 'Stalled')";

    private static LiveProject ReadProject(SqliteDataReader r) => new(
        r.GetInt64(0),
        r.GetString(1),
        r.GetString(2),
        Enum.Parse<Ministry>(r.GetString(3)),
        r.GetString(4),
        ParseDecimal(r.GetString(5)),
        r.GetInt32(6),
        ParseDecimal(r.GetString(7)),
        ParseDecimal(r.GetString(8)),
        r.GetInt64(9),
        r.GetInt64(10),
        r.IsDBNull(11) ? null : r.GetInt64(11),
        r.GetString(12),
        r.IsDBNull(13) ? null : r.GetString(13),
        r.IsDBNull(14) ? null : r.GetString(14),
        Enum.Parse<ProjectStatus>(r.GetString(15)),
        r.IsDBNull(16) ? null : r.GetInt64(16));

    private static decimal ParseDecimal(string text) => decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture);

    private static string StoreDecimal(decimal value) =>
        Math.Round(value, 6, MidpointRounding.AwayFromZero).ToString("0.######", CultureInfo.InvariantCulture);

    public List<LiveProject> GetProjects(ProjectStatus? status = null)
    {
        using var conn = Open();
        return status is { } only
            ? QueryProjectsTx(conn, null, "status = $status", ("$status", only.ToString()))
            : QueryProjectsTx(conn, null, "1 = 1");
    }

    public LiveProject? GetProject(long id)
    {
        using var conn = Open();
        return GetProjectTx(conn, null, id);
    }

    public List<LiveProject> GetLiveProjects()
    {
        using var conn = Open();
        return QueryProjectsTx(conn, null, $"status IN {LiveProjectStatuses}");
    }

    public List<ProjectTemplate> GetMinisterialProjectTemplates() =>
        GetTemplates(TemplateKind.Project, TemplateStatus.Ready)
            .Select(row => TemplateCodec.Parse(row.Yaml, row.Id).Template)
            .OfType<ProjectTemplate>()
            .Where(t => t.Ministerial)
            .OrderBy(t => t.Title)
            .ToList();

    public ProjectStartResult StartMinisterialProject(ulong userId, string templateId, string provinceId, GameConfig config)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        if (ReadyTemplateTx<ProjectTemplate>(conn, tx, templateId, out var problem) is not { } template)
            return new ProjectStartResult(Texts.Get("projects.refusal.unavailable", ("id", templateId), ("problem", problem)), null);
        if (!template.Ministerial)
            return new ProjectStartResult(Texts.Get("projects.refusal.notMinisterial", ("project", template.Title)), null);
        if (ResponsibleMinisterTx(conn, tx, template.Purse) != userId)
            return new ProjectStartResult(Texts.Get("projects.refusal.notYours", ("ministry", template.Purse.DisplayName())), null);

        var (refusal, id) = InsertProjectTx(conn, tx, config, template, provinceId, null);
        if (refusal is not null) return new ProjectStartResult(refusal, null);
        LogTx(conn, tx, LogKinds.StateChange, "ministry project", $"Project {id} ({template.Id}) started in {provinceId} by {userId}.", userId);
        tx.Commit();
        return new ProjectStartResult(null, GetProjectTx(conn, null, id!.Value));
    }

    public ProjectFundResult FundProject(ulong userId, long projectId, decimal spending, GameConfig config)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        if (GetProjectTx(conn, tx, projectId) is not { IsLive: true } project)
            return new ProjectFundResult(Texts.Get("projects.refusal.noProject"), null, null, 0);
        if (spending < 0)
            return new ProjectFundResult(Texts.Get("projects.refusal.negative"), project, null, 0);
        if (ResponsibleMinisterTx(conn, tx, project.Purse) != userId)
            return new ProjectFundResult(Texts.Get("projects.refusal.notYours", ("ministry", project.Purse.DisplayName())), project, null, 0);

        var amount = Math.Round(spending, 2, MidpointRounding.AwayFromZero);
        Exec(conn, tx, "UPDATE projects SET spending = $spending WHERE id = $id;", ("$id", projectId), ("$spending", StoreDecimal(amount)));
        LogTx(conn, tx, LogKinds.StateChange, "ministry fund", $"Project {projectId} spending set to {amount} by {userId}.", userId);
        var updated = GetProjectTx(conn, tx, projectId)!;
        var forecast = ProjectForecast.For(updated, amount, config.Constants.Project.Graft, GetGameStateTx(conn, tx));
        var balance = GetBalanceTx(conn, tx, updated.Account);
        tx.Commit();
        return new ProjectFundResult(null, updated, forecast, balance);
    }

    public string? EditLiveProject(long projectId, IReadOnlyList<Effect> effects, string? bonusText, IReadOnlyList<Effect>? bonusEffects)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        if (GetProjectTx(conn, tx, projectId) is not { IsLive: true }) return Texts.Get("projects.refusal.noProject");
        Exec(conn, tx, "UPDATE projects SET effects = $effects, bonus_text = $bonusText, bonus_effects = $bonusEffects WHERE id = $id;",
            ("$id", projectId), ("$effects", EffectParser.Format(effects)), ("$bonusText", bonusText),
            ("$bonusEffects", bonusEffects is null ? null : EffectParser.Format(bonusEffects)));
        LogTx(conn, tx, LogKinds.GmOverride, "project edit", $"Project {projectId} effects edited.");
        tx.Commit();
        return null;
    }

    private static LiveProject? GetProjectTx(SqliteConnection conn, SqliteTransaction? tx, long id) =>
        QueryProjectsTx(conn, tx, "id = $id", ("$id", id)).FirstOrDefault();

    private static LiveProject? LiveProjectOfTemplateTx(SqliteConnection conn, SqliteTransaction? tx, string templateId) =>
        QueryProjectsTx(conn, tx, $"template_id = $template AND status IN {LiveProjectStatuses}", ("$template", templateId)).FirstOrDefault();

    private static List<LiveProject> QueryProjectsTx(
        SqliteConnection conn, SqliteTransaction? tx, string where, params (string Name, object? Value)[] parameters)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT {SelectProjectColumns} FROM projects WHERE {where} ORDER BY id;";
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        var list = new List<LiveProject>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(ReadProject(r));
        return list;
    }

    private static string? StartProjectTx(
        SqliteConnection conn, SqliteTransaction tx, GameConfig config, string templateId, string? provinceId, EffectSource source, ResolutionReport? report)
    {
        if (ReadyTemplateTx<ProjectTemplate>(conn, tx, templateId, out var problem) is not { } template) return problem;
        var (refusal, id) = InsertProjectTx(conn, tx, config, template, provinceId, report);
        if (refusal is not null) return refusal;
        LogTx(conn, tx, LogKinds.StateChange, "project", $"Project {id} ({template.Id}) started by {source.SourceType} {source.SourceId}.", isPublic: true);
        return null;
    }

    private static (string? Refusal, long? Id) InsertProjectTx(
        SqliteConnection conn, SqliteTransaction tx, GameConfig config, ProjectTemplate template, string? provinceId, ResolutionReport? report)
    {
        if ((provinceId ?? template.Province) is not { } province)
            return (Texts.Get("projects.warning.noProvince", ("id", template.Id)), null);
        if (config.Province(province) is not { } definition || GetProvincesTx(conn, tx).All(p => p.Id != province))
            return (Texts.Get("projects.warning.unknownProvince", ("id", province)), null);
        if (template.Requires is { } requires && !GetProvinceConditionsTx(conn, tx).GetValueOrDefault(province, []).Contains(requires))
            return (Texts.Get("projects.warning.requires", ("province", definition.Name),
                ("condition", config.Condition(requires)?.Name ?? requires), ("project", template.Title)), null);
        if (LiveProjectOfTemplateTx(conn, tx, template.Id) is not null)
            return (Texts.Get("projects.warning.alreadyLive", ("project", template.Title)), null);

        var state = GetGameStateTx(conn, tx);
        var insideResolution = report is { CampaignDay: false } && report.ResolutionNumber == state.ResolutionNumber + 1;
        var firstPayment = state.ResolutionNumber + (insideResolution ? 2 : 1);
        var started = firstPayment - 1;
        long? bonusDue = template.Bonus switch
        {
            { Within: { } within } => started + within,
            { BySeason: { } season, ByYear: { } year } => GameCalendar.ResolutionForTurn(state, season, year) + 1,
            _ => null,
        };

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO projects (template_id, title, purse, province_id, price, term, progress, spending, started_resolution,
                first_payment_resolution, bonus_due_resolution, effects, bonus_text, bonus_effects, status)
            VALUES ($template, $title, $purse, $province, $price, $term, '0', $spending, $started,
                $firstPayment, $bonusDue, $effects, $bonusText, $bonusEffects, 'Active')
            RETURNING id;";
        cmd.Parameters.AddWithValue("$template", template.Id);
        cmd.Parameters.AddWithValue("$title", template.Title);
        cmd.Parameters.AddWithValue("$purse", template.Purse.ToString());
        cmd.Parameters.AddWithValue("$province", province);
        cmd.Parameters.AddWithValue("$price", StoreDecimal(template.Price));
        cmd.Parameters.AddWithValue("$term", template.Term);
        cmd.Parameters.AddWithValue("$spending", StoreDecimal(template.Pace));
        cmd.Parameters.AddWithValue("$started", started);
        cmd.Parameters.AddWithValue("$firstPayment", firstPayment);
        cmd.Parameters.AddWithValue("$bonusDue", (object?)bonusDue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$effects", EffectParser.Format(template.Effects));
        cmd.Parameters.AddWithValue("$bonusText", (object?)template.Bonus?.Text ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$bonusEffects", template.Bonus is { } bonus ? EffectParser.Format(bonus.Effects) : DBNull.Value);
        var id = (long)cmd.ExecuteScalar()!;
        MarkTemplatePostedTx(conn, tx, template.Id);
        report?.ProjectsStarted.Add(new ProjectStartedReport(id, template.Title));
        return (null, id);
    }

    private static string? ProgressProjectTx(SqliteConnection conn, SqliteTransaction tx, string templateId, decimal amount)
    {
        if (LiveProjectOfTemplateTx(conn, tx, templateId) is not { } project)
            return Texts.Get("projects.warning.noLive", ("id", templateId));
        var progress = Math.Clamp(project.Progress + amount, 0, project.Price);
        Exec(conn, tx, "UPDATE projects SET progress = $progress WHERE id = $id;", ("$id", project.Id), ("$progress", StoreDecimal(progress)));
        return null;
    }

    private static string? HaltProjectTx(SqliteConnection conn, SqliteTransaction tx, string templateId)
    {
        if (LiveProjectOfTemplateTx(conn, tx, templateId) is not { } project)
            return Texts.Get("projects.warning.noLive", ("id", templateId));
        Exec(conn, tx, "UPDATE projects SET status = 'Halted' WHERE id = $id;", ("$id", project.Id));
        LogTx(conn, tx, LogKinds.StateChange, "project", $"Project {project.Id} ({project.TemplateId}) halted.", isPublic: true);
        return null;
    }

    private static void PayProjectsTx(SqliteConnection conn, SqliteTransaction tx, GameState state, GameConfig config, ResolutionReport report)
    {
        var current = state.ResolutionNumber + 1;
        var graft = config.Constants.Project.Graft;

        foreach (var id in QueryProjectsTx(conn, tx, $"status IN {LiveProjectStatuses}").Select(p => p.Id).ToList())
        {
            if (GetProjectTx(conn, tx, id) is not { IsLive: true } project) continue;
            if (project.IsComplete)
            {
                FinishProjectTx(conn, tx, project, current, config, report);
                continue;
            }
            if (project.FirstPaymentResolution > current) continue;

            var payment = ProjectMath.Payment(project.Spending, GetBalanceTx(conn, tx, project.Account), project.Remaining, project.Pace, graft);
            if (payment <= 0)
            {
                Exec(conn, tx, "UPDATE projects SET status = 'Stalled' WHERE id = $id;", ("$id", project.Id));
                if (ResponsibleMinisterTx(conn, tx, project.Purse) is { } minister)
                    report.AddPrivate(minister, project.Spending <= 0
                        ? Texts.Get("projects.private.stalledUnfunded", ("project", project.Title))
                        : Texts.Get("projects.private.stalledEmpty", ("project", project.Title), ("ministry", project.Purse.DisplayName())));
                continue;
            }

            var lost = ProjectMath.GraftLost(payment, project.Pace, graft);
            PostLedgerTx(conn, tx, project.Account, -(payment - lost), LedgerKind.Project,
                Texts.Get("projects.ledger.work", ("project", project.Title)), "project", project.Id.ToString());
            if (lost > 0)
                PostLedgerTx(conn, tx, project.Account, -lost, LedgerKind.Graft,
                    Texts.Get("projects.ledger.graft", ("project", project.Title)), "project", project.Id.ToString());

            var progress = Math.Min(project.Price, project.Progress + ProjectMath.Work(payment, project.Pace, graft));
            Exec(conn, tx, "UPDATE projects SET progress = $progress, status = 'Active' WHERE id = $id;",
                ("$id", project.Id), ("$progress", StoreDecimal(progress)));
            if (GetProjectTx(conn, tx, project.Id) is { IsComplete: true } paid)
                FinishProjectTx(conn, tx, paid, current, config, report);
        }

        foreach (var project in QueryProjectsTx(conn, tx, $"status IN {LiveProjectStatuses} AND bonus_due_resolution = $next", ("$next", current + 1)))
        {
            var payment = ProjectMath.Payment(project.Spending, project.Spending, project.Remaining, project.Pace, graft);
            if (project.FirstPaymentResolution <= current + 1 && ProjectMath.Work(payment, project.Pace, graft) >= project.Remaining - ProjectMath.Tolerance)
                continue;
            report.Alerts.Add(Texts.Get("projects.alert.bonusAtRisk", ("project", project.Title),
                ("spending", Display.Crowns(project.Spending)), ("remaining", Display.Crowns(project.Remaining))));
        }
    }

    private static void FinishProjectTx(
        SqliteConnection conn, SqliteTransaction tx, LiveProject project, long current, GameConfig config, ResolutionReport report)
    {
        Exec(conn, tx, "UPDATE projects SET status = 'Finished', progress = $price, finished_resolution = $resolution WHERE id = $id;",
            ("$id", project.Id), ("$price", StoreDecimal(project.Price)), ("$resolution", current));

        var minister = ResponsibleMinisterTx(conn, tx, project.Purse);
        var source = new EffectSource(project.Title, "project", project.Id.ToString(), PublicLedger: true, ActorId: minister);
        var applied = ApplyEffectsTx(conn, tx, EffectParser.ResolveHere(project.ParsedEffects, project.ProvinceId), config, source, report)
            .Applied.ToList();
        if (minister is { } holder)
            AddPopularityTx(conn, tx, holder, config.Constants.Project.Popularity, config.Constants.Popularity);

        var earned = project.BonusDueResolution is { } due && current <= due;
        if (earned)
            applied.AddRange(ApplyEffectsTx(conn, tx, EffectParser.ResolveHere(project.ParsedBonusEffects, project.ProvinceId), config, source, report)
                .Applied);

        report.ProjectsFinished.Add(new ProjectFinishedReport(project.Id, project.Title, minister, earned, earned ? project.BonusText : null, applied));
        LogTx(conn, tx, LogKinds.StateChange, "project", $"Project {project.Id} ({project.TemplateId}) finished{(earned ? " with its bonus" : "")}.",
            isPublic: true);
    }
}
