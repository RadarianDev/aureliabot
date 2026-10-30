public sealed class IssueValueEffect(string prefix, Func<string, string> name) : EffectKind
{
    public override string Prefix => prefix;

    public override string? Parse(string[] parts, string? rawValue, EffectParseOptions options, out double? value)
    {
        value = null;
        if (parts.Length != 2) return Texts.Get("effects.errors.issueShape", ("prefix", prefix));
        return RequireNumber(rawValue, out value);
    }

    public override string? CheckIds(Effect effect, EffectContext context) =>
        context.Issues.Contains(effect.Target!) ? null : Texts.Get("effects.errors.unknownIssue", ("id", effect.Target));

    public override double? CurrentValue(Effect effect, IEffectStore store) =>
        store.Issue(effect.Target!) is { } issue ? prefix == "opinion" ? issue.Opinion : issue.Salience : null;

    public override string? Apply(Effect effect, IEffectStore store)
    {
        if (store.Issue(effect.Target!) is not { Status: IssueStatus.Open } issue)
            return Texts.Get("data.effect.issueNotOpen", ("id", effect.Target));
        if (prefix == "opinion")
            store.SetIssueOpinion(issue.Id, Math.Clamp(issue.Opinion + effect.Amount, -1, 1));
        else
        {
            var range = store.Config.Constants.Salience;
            store.SetIssueSalience(issue.Id, Math.Clamp(issue.Salience + effect.Amount, range.Min, range.Max), raised: effect.Amount > 0);
        }
        return null;
    }

    public override string? SummaryLine(Effect effect, EffectNames names, DisplayConstants display) =>
        effect.Amount == 0 ? null : NumberLine(effect.Amount, name(names.Issue(effect.Target!)), display, null);
}

public abstract class ContentActionEffect : EffectKind
{
    public override bool IsAction => true;

    public override string? Parse(string[] parts, string? rawValue, EffectParseOptions options, out double? value)
    {
        value = null;
        if (parts.Length != 2) return Texts.Get("effects.errors.actionShape", ("prefix", Prefix));
        return RequireNoValue(rawValue, out value);
    }
}

public sealed class OpenIssueEffect : ContentActionEffect
{
    public override string Prefix => "open";

    public override string? CheckIds(Effect effect, EffectContext context) =>
        context.IssueTemplates.Contains(effect.Target!) ? null : Texts.Get("effects.errors.noIssueTemplate", ("id", effect.Target));

    public override string? Apply(Effect effect, IEffectStore store) => store.OpenIssue(effect.Target!);

    public override string? SummaryLine(Effect effect, EffectNames names, DisplayConstants display) =>
        Texts.Get("effects.summary.open", ("issue", names.IssueTemplate(effect.Target!)));
}

public sealed class CloseIssueEffect : ContentActionEffect
{
    public override string Prefix => "close";

    public override string? CheckIds(Effect effect, EffectContext context) =>
        context.Issues.Contains(effect.Target!) ? null : Texts.Get("effects.errors.unknownIssue", ("id", effect.Target));

    public override string? Apply(Effect effect, IEffectStore store) => store.CloseIssue(effect.Target!);

    public override string? SummaryLine(Effect effect, EffectNames names, DisplayConstants display) =>
        Texts.Get("effects.summary.close", ("issue", names.Issue(effect.Target!)));
}

public sealed class BillEffect : ContentActionEffect
{
    public override string Prefix => "bill";

    public override string? CheckIds(Effect effect, EffectContext context) =>
        context.BillTemplates.Contains(effect.Target!) ? null : Texts.Get("effects.errors.noBillTemplate", ("id", effect.Target));

    public override string? Apply(Effect effect, IEffectStore store) => store.MakeBillAvailable(effect.Target!);

    public override string? SummaryLine(Effect effect, EffectNames names, DisplayConstants display) =>
        Texts.Get("effects.summary.bill", ("bill", names.BillTemplate(effect.Target!)));
}

public sealed class ProjectEffect : ContentActionEffect
{
    public override string Prefix => "project";

    public override string? CheckIds(Effect effect, EffectContext context) =>
        context.ProjectTemplates.Contains(effect.Target!) ? null : Texts.Get("effects.errors.noProjectTemplate", ("id", effect.Target));

    public override string? Apply(Effect effect, IEffectStore store) => store.StartProject(effect.Target!);

    public override string? SummaryLine(Effect effect, EffectNames names, DisplayConstants display) =>
        Texts.Get("effects.summary.project", ("project", names.ProjectTemplate(effect.Target!)));
}

public sealed class CrisisEffect : ContentActionEffect
{
    public override string Prefix => "crisis";

    public override string? Parse(string[] parts, string? rawValue, EffectParseOptions options, out double? value) =>
        base.Parse(parts, rawValue, options, out value) ?? (options.AllowCrisis ? null : Texts.Get("effects.errors.crisisOnlyEvents"));

    public override string? CheckIds(Effect effect, EffectContext context) =>
        context.CrisisTemplates.Contains(effect.Target!) ? null : Texts.Get("effects.errors.noCrisisTemplate", ("id", effect.Target));

    public override string? Apply(Effect effect, IEffectStore store) => store.PostCrisis(effect.Target!);

    public override string? SummaryLine(Effect effect, EffectNames names, DisplayConstants display) =>
        Texts.Get("effects.summary.crisis", ("crisis", names.CrisisTemplate(effect.Target!)));
}

public sealed class QueueEventEffect : EffectKind
{
    public override string Prefix => "event";

    public override bool IsAction => true;

    public override bool Hidden => true;

    public override string? Parse(string[] parts, string? rawValue, EffectParseOptions options, out double? value)
    {
        value = null;
        if (parts.Length != 2) return Texts.Get("effects.errors.actionShape", ("prefix", Prefix));
        if (rawValue is null || rawValue.Equals("true", StringComparison.OrdinalIgnoreCase)) return null;
        if (!EffectParser.TryParseNumber(rawValue, out var delay) || delay < 0 || delay != Math.Floor(delay))
            return Texts.Get("effects.errors.eventDelay", ("value", rawValue));
        value = delay;
        return null;
    }

    public override string? CheckIds(Effect effect, EffectContext context) =>
        context.EventTemplates is null || context.EventTemplates.Contains(effect.Target!)
            ? null
            : Texts.Get("effects.errors.noEventTemplate", ("id", effect.Target));

    public override string? Apply(Effect effect, IEffectStore store) => store.QueueEvent(effect.Target!, (int)(effect.Value ?? 0));

    public override string? SummaryLine(Effect effect, EffectNames names, DisplayConstants display) => null;
}

public sealed class CancelEventEffect : ContentActionEffect
{
    public override string Prefix => "cancel";

    public override bool Hidden => true;

    public override string? CheckIds(Effect effect, EffectContext context) =>
        context.EventTemplates is null || context.EventTemplates.Contains(effect.Target!)
            ? null
            : Texts.Get("effects.errors.noEventTemplate", ("id", effect.Target));

    public override string? Apply(Effect effect, IEffectStore store) => store.CancelEvents(effect.Target!);

    public override string? SummaryLine(Effect effect, EffectNames names, DisplayConstants display) => null;
}
