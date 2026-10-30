using System.Text;

public static class ContentPreview
{
    public static string Render(DatabaseService data, GameConfig config, string id)
    {
        if (data.GetTemplate(id) is not { } row) return $"There is no template `{id}`.";
        var parsed = TemplateCodec.Parse(row.Yaml, $"{id}.yaml");
        var sb = new StringBuilder();
        sb.AppendLine($"**Preview of `{id}`** · {TemplateCodec.KindName(row.Kind)} · {row.Status} · posted {Times(row.TimesPosted)}");
        if (!parsed.IsValid)
        {
            sb.AppendLine("It doesn't parse, so there is nothing to show:");
            foreach (var error in parsed.Errors) sb.AppendLine($"- {error}");
            return sb.ToString().TrimEnd();
        }

        var template = parsed.Template!;
        var state = data.GetGameState();
        var names = Publisher.Names(data, config);
        var summary = new Summariser(names, config.Constants.Display);
        sb.AppendLine("-# What players will see:");
        sb.AppendLine();
        var shown = template switch
        {
            IssueTemplate issue => Issue(issue),
            BillTemplate bill => Bill(bill, names, summary),
            ProjectTemplate project => Project(project, config, summary),
            EventTemplate ev => Event(ev, state, summary),
            CrisisTemplate crisis => Crisis(crisis, config, names, summary),
            _ => "",
        };
        sb.AppendLine(shown.TrimEnd());

        var warnings = Warnings(data, config, row, template, state).Concat(summary.Problems).ToList();
        sb.AppendLine();
        sb.AppendLine("**For the GM**");
        if (template is IssueTemplate hidden)
            sb.AppendLine($"Hidden: opinion {EffectParser.FormatNumber(hidden.Opinion)}, salience {EffectParser.FormatNumber(hidden.Salience)}.");
        if (template is ProjectTemplate works)
        {
            if (works.Ministerial) sb.AppendLine("Ministerial: the minister can start it with `/ministry project`.");
            if (works.Requires is { } requires) sb.AppendLine($"Needs a province with {config.Condition(requires)?.Name ?? requires}.");
        }
        if (warnings.Count == 0)
            sb.AppendLine(row.Status == TemplateStatus.Ready ? "No warnings. It is Ready." : "No warnings: `/content ready` will accept it.");
        else
            foreach (var warning in warnings) sb.AppendLine($"- ⚠️ {warning}");
        return sb.ToString().TrimEnd();
    }

    private static string Times(int count) => count == 1 ? "once" : $"{count} times";

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
        $"## {issue.Title}\nFor: {issue.For}\nAgainst: {issue.Against}\nStatus: Open";

    private static string Bill(BillTemplate bill, EffectNames names, Summariser summary)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## {bill.Title}");
        sb.AppendLine($"{names.Issue(bill.Issue)} · {bill.Lean}");
        sb.AppendLine($"> {bill.Motion}");
        sb.AppendLine();
        sb.AppendLine("-# When it passes, in #news:");
        sb.AppendLine($"## {bill.Title} becomes law");
        sb.AppendLine(bill.PassText);
        summary.Append(sb, bill.PassEffects);
        return sb.ToString();
    }

    private static string Project(ProjectTemplate project, GameConfig config, Summariser summary)
    {
        var sb = new StringBuilder();
        var where = project.Province is { } province ? Display.Province(config, province) : "A province of the minister's choosing";
        sb.AppendLine($"## Project begun: {project.Title}");
        sb.AppendLine($"{where} · Ministry of {project.Purse.DisplayName()} · {Display.Crowns(project.Price)} over {project.Term} seasons");
        if (project.Bonus is { } bonus)
            sb.AppendLine(bonus.Within is { } within
                ? $"Bonus if finished within {within} seasons"
                : $"Bonus if finished by the end of {bonus.BySeason} {bonus.ByYear}");
        sb.AppendLine();
        sb.AppendLine("-# When it finishes, in #news:");
        sb.AppendLine($"## {project.Title} is finished");
        summary.Append(sb, project.Effects);
        if (project.Bonus is { } earned)
        {
            sb.AppendLine();
            sb.AppendLine("-# With the bonus, on top:");
            if (earned.Text.Length > 0) sb.AppendLine(earned.Text);
            summary.Append(sb, earned.Effects);
        }
        return sb.ToString();
    }

    private static string Event(EventTemplate ev, GameState state, Summariser summary)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## {ev.Title}");
        sb.AppendLine(When(ev.Schedule, state));
        sb.AppendLine(ev.Text);
        summary.Append(sb, ev.Effects);
        return sb.ToString();
    }

    private static string When(EventSchedule? schedule, GameState state)
    {
        if (schedule?.Season is { } season && schedule.Year is { } year) return GameCalendar.Label(season, year);
        if (schedule?.Delay is { } delay)
        {
            var (s, y) = ContentRules.TurnOf(state, EventScheduling.FireAtForDelay(state, delay));
            return GameCalendar.Label(s, y);
        }
        return "(the turn it fires)";
    }

    private static string Crisis(CrisisTemplate crisis, GameConfig config, EffectNames names, Summariser summary)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"## Crisis: {crisis.Title}");
        sb.AppendLine($"Ministry of {crisis.Ministry.DisplayName()} · {Display.Province(config, crisis.Province)} · {names.Issue(crisis.Issue)}");
        sb.AppendLine("Due at the next resolution");
        sb.AppendLine();
        var number = 0;
        foreach (var answer in crisis.Answers)
            sb.AppendLine($"{++number}. {answer.Label} · {answer.Lean} · {(answer.Price == 0 ? "free" : Display.Crowns(answer.Price))}");

        number = 0;
        foreach (var answer in crisis.Answers)
        {
            sb.AppendLine();
            sb.AppendLine($"-# Outcome of answer {++number}:");
            sb.AppendLine(answer.Text);
            summary.Append(sb, answer.Effects);
        }
        sb.AppendLine();
        sb.AppendLine("-# When the purse can't pay, on top of the answer:");
        if (crisis.Complication is { } complication)
        {
            sb.AppendLine(complication.Text);
            summary.Append(sb, complication.Effects);
        }
        else
        {
            sb.AppendLine("The money runs out, and the province pays for it.");
            summary.Append(sb, [new Effect($"discontent.{crisis.Province}", 1)]);
        }
        sb.AppendLine();
        sb.AppendLine("-# With no answer in time:");
        sb.AppendLine(crisis.Default.Text);
        summary.Append(sb, crisis.Default.Effects);
        return sb.ToString();
    }

    private sealed class Summariser(EffectNames names, DisplayConstants display)
    {
        public List<string> Problems { get; } = [];

        public void Append(StringBuilder sb, IReadOnlyList<Effect> effects)
        {
            if (effects.Count == 0) return;
            IReadOnlyList<string> lines;
            try
            {
                lines = EffectSummary.Lines(effects, names, display);
            }
            catch (Exception ex)
            {
                if (Problems.Count == 0) Problems.Add($"Effect summaries couldn't be drawn ({ex.Message}); the raw effects are shown instead.");
                lines = effects.Select(e => $"`{EffectParser.Format([e])}`").ToList();
            }
            if (lines.Count == 0) return;
            sb.AppendLine();
            foreach (var line in lines) sb.AppendLine(line);
        }
    }
}
