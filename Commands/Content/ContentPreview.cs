using System.Text;

public static class ContentPreview
{
    public static string Render(DatabaseService data, GameConfig config, string id)
    {
        if (data.GetTemplate(id) is not { } row) return Texts.Get("content.noTemplate", ("id", id));
        var parsed = TemplateCodec.Parse(row.Yaml, $"{id}.yaml");
        var sb = new StringBuilder();
        sb.AppendLine(Texts.Get("content.preview.header", ("id", id), ("kind", TemplateCodec.KindName(row.Kind)), ("status", row.Status),
            ("times", ContentRules.TimesPosted(row.TimesPosted))));
        if (!parsed.IsValid)
        {
            sb.AppendLine(Texts.Get("content.preview.unparsable"));
            foreach (var error in parsed.Errors) sb.AppendLine(ContentRules.Bullet(error));
            return sb.ToString().TrimEnd();
        }

        var template = parsed.Template!;
        var state = data.GetGameState();
        var names = Publisher.Names(data, config);
        var summary = new Summariser(names, config.Constants.Display);
        sb.AppendLine(Texts.Get("content.preview.playersSee"));
        sb.AppendLine();
        var shown = template switch
        {
            IssueTemplate issue => Issue(issue),
            BillTemplate bill => Bill(bill, names, summary),
            ProjectTemplate project => Project(project, config, summary),
            EventTemplate ev => Event(ev, summary),
            CrisisTemplate crisis => Crisis(crisis, config, names, summary),
            _ => "",
        };
        sb.AppendLine(shown.TrimEnd());

        var warnings = Warnings(data, config, row, template, state).Concat(summary.Problems).ToList();
        sb.AppendLine();
        sb.AppendLine(Texts.Get("content.preview.forGm"));
        if (template is IssueTemplate hidden)
            sb.AppendLine(Texts.Get("content.preview.hidden",
                ("opinion", EffectParser.FormatNumber(hidden.Opinion)), ("salience", EffectParser.FormatNumber(hidden.Salience))));
        if (template is EventTemplate scheduled)
            sb.AppendLine(Texts.Get("content.preview.fires", ("when", FiresWhen(scheduled.Schedule, state))));
        if (template is ProjectTemplate works)
        {
            if (works.Ministerial) sb.AppendLine(Texts.Get("content.preview.ministerial"));
            if (works.Requires is { } requires)
                sb.AppendLine(Texts.Get("content.preview.requires", ("condition", config.Condition(requires)?.Name ?? requires)));
        }
        if (warnings.Count == 0)
            sb.AppendLine(row.Status == TemplateStatus.Ready
                ? Texts.Get("content.preview.noWarningsReady")
                : Texts.Get("content.preview.noWarnings"));
        else
            foreach (var warning in warnings) sb.AppendLine(Texts.Get("content.preview.warning", ("warning", warning)));
        return sb.ToString().TrimEnd();
    }

    private static IEnumerable<string> Warnings(DatabaseService data, GameConfig config, TemplateRow row, Template template, GameState state)
    {
        var full = TemplateCodec.Parse(row.Yaml, $"{row.Id}.yaml", data.BuildEffectContext(config));
        foreach (var error in full.Errors) yield return error.ToString();
        foreach (var problem in ContentRules.ReadinessProblems(template, data.GetTemplate)) yield return problem;
        if (template is EventTemplate { Schedule: { } schedule } && !data.HasScheduledEventFired(row.Id)
            && ContentRules.ScheduleFireAt(state, schedule).Problem is { } scheduleProblem)
            yield return scheduleProblem;
    }

    private static string Issue(IssueTemplate issue) =>
        Texts.Get("content.preview.issue", ("title", issue.Title), ("for", issue.For), ("against", issue.Against));

    private static string FiresWhen(EventSchedule? schedule, GameState state)
    {
        if (schedule?.Season is { } season && schedule.Year is { } year) return GameCalendar.Label(season, year);
        if (schedule?.Delay is { } delay)
        {
            var (s, y) = ContentRules.TurnOf(state, EventScheduling.FireAtForDelay(state, delay));
            return GameCalendar.Label(s, y);
        }
        return Texts.Get("content.preview.whenQueued");
    }

    private static string Title(string title) => Texts.Get("content.preview.title", ("title", title));

    private static string Bill(BillTemplate bill, EffectNames names, Summariser summary)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Title(bill.Title));
        sb.AppendLine(Texts.Get("content.preview.billMeta", ("issue", names.Issue(bill.Issue)), ("lean", bill.Lean)));
        sb.AppendLine(Texts.Get("content.preview.motion", ("motion", bill.Motion)));
        sb.AppendLine();
        sb.AppendLine(Texts.Get("content.preview.billPasses"));
        sb.AppendLine(Publisher.FormatEffectPost(Texts.Get("content.preview.billLaw", ("title", bill.Title)), [], bill.PassText,
            summary.Lines(bill.PassEffects)));
        return sb.ToString();
    }

    private static string Project(ProjectTemplate project, GameConfig config, Summariser summary)
    {
        var sb = new StringBuilder();
        var where = project.Province is { } province ? Display.Province(config, province) : Texts.Get("content.preview.anyProvince");
        sb.AppendLine(Texts.Get("content.preview.projectBegun", ("title", project.Title)));
        sb.AppendLine(Texts.Get("content.preview.projectMeta", ("where", where), ("ministry", project.Purse.DisplayName()),
            ("price", Display.Crowns(project.Price)), ("term", project.Term)));
        if (project.Bonus is { } bonus)
            sb.AppendLine(bonus.Within is { } within
                ? Texts.Get("content.preview.bonusWithin", ("within", within))
                : Texts.Get("content.preview.bonusBy", ("season", bonus.BySeason), ("year", bonus.ByYear)));
        sb.AppendLine();
        sb.AppendLine(Texts.Get("content.preview.projectFinishes"));
        sb.AppendLine(Publisher.FormatEffectPost(Texts.Get("content.preview.projectFinished", ("title", project.Title)), [], null,
            summary.Lines(project.Effects)));
        if (project.Bonus is { } earned)
        {
            sb.AppendLine();
            sb.AppendLine(Texts.Get("content.preview.withBonus"));
            sb.AppendLine(Publisher.FormatEffectPost(null, [], earned.Text, summary.Lines(earned.Effects)));
        }
        return sb.ToString();
    }

    private static string Event(EventTemplate ev, Summariser summary)
    {
        return Publisher.FormatEvent(ev.Title, ev.Text, summary.Lines(ev.Effects)) + "\n";
    }

    private static string Crisis(CrisisTemplate crisis, GameConfig config, EffectNames names, Summariser summary)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Texts.Get("content.preview.crisisTitle", ("title", crisis.Title)));
        sb.AppendLine(Texts.Get("content.preview.crisisMeta", ("ministry", crisis.Ministry.DisplayName()),
            ("province", Display.Province(config, crisis.Province)), ("issue", names.Issue(crisis.Issue))));
        sb.AppendLine(Texts.Get("content.preview.crisisDue"));
        sb.AppendLine();
        var number = 0;
        foreach (var answer in crisis.Answers)
            sb.AppendLine(Texts.Get("content.preview.answer", ("number", ++number), ("label", answer.Label), ("lean", answer.Lean),
                ("price", answer.Price == 0 ? Texts.Get("content.preview.free") : Display.Crowns(answer.Price))));

        number = 0;
        foreach (var answer in crisis.Answers)
        {
            sb.AppendLine();
            sb.AppendLine(Texts.Get("content.preview.outcome", ("number", ++number)));
            sb.AppendLine(Publisher.FormatEffectPost(null, [], answer.Text, summary.Lines(answer.Effects)));
        }
        sb.AppendLine();
        sb.AppendLine(Texts.Get("content.preview.cantPay"));
        sb.AppendLine(crisis.Complication is { } complication
            ? Publisher.FormatEffectPost(null, [], complication.Text, summary.Lines(complication.Effects))
            : Publisher.FormatEffectPost(null, [], Texts.Get("content.preview.moneyRunsOut"),
                summary.Lines([new Effect($"discontent.{crisis.Province}", 1)])));
        sb.AppendLine();
        sb.AppendLine(Texts.Get("content.preview.noAnswer"));
        sb.AppendLine(Publisher.FormatEffectPost(null, [], crisis.Default.Text, summary.Lines(crisis.Default.Effects)));
        return sb.ToString();
    }

    private sealed class Summariser(EffectNames names, DisplayConstants display)
    {
        public List<string> Problems { get; } = [];

        public IReadOnlyList<string> Lines(IReadOnlyList<Effect> effects)
        {
            if (effects.Count == 0) return [];
            try
            {
                return EffectSummary.Lines(effects, names, display, preview: true);
            }
            catch (Exception ex)
            {
                if (Problems.Count == 0) Problems.Add(Texts.Get("content.preview.summaryFailed", ("error", ex.Message)));
                return effects.Select(e => Texts.Get("content.preview.rawEffect", ("effect", EffectParser.Format([e])))).ToList();
            }
        }
    }
}
