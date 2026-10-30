public sealed class ProgressProjectEffect : EffectKind
{
    public override string Prefix => "progress";

    public override string? Parse(string[] parts, string? rawValue, EffectParseOptions options, out double? value)
    {
        value = null;
        if (parts.Length != 2) return Texts.Get("effects.errors.issueShape", ("prefix", Prefix));
        return RequireNumber(rawValue, out value);
    }

    public override string? CheckIds(Effect effect, EffectContext context) =>
        context.ProjectTemplates.Contains(effect.Target!) ? null : Texts.Get("effects.errors.noProjectTemplate", ("id", effect.Target));

    public override string? Apply(Effect effect, IEffectStore store) => store.ProgressProject(effect.Target!, (decimal)effect.Amount);

    public override string? SummaryLine(Effect effect, EffectNames names, DisplayConstants display) =>
        effect.Amount == 0 ? null
        : NumberLine(effect.Amount, Texts.Get("effects.summary.progress", ("project", names.ProjectTemplate(effect.Target!))), display,
            EffectSummary.FormatSigned(effect.Amount));
}

public sealed class HaltProjectEffect : ContentActionEffect
{
    public override string Prefix => "halt";

    public override string? CheckIds(Effect effect, EffectContext context) =>
        context.ProjectTemplates.Contains(effect.Target!) ? null : Texts.Get("effects.errors.noProjectTemplate", ("id", effect.Target));

    public override string? Apply(Effect effect, IEffectStore store) => store.HaltProject(effect.Target!);

    public override string? SummaryLine(Effect effect, EffectNames names, DisplayConstants display) =>
        Texts.Get("effects.summary.halt", ("project", names.ProjectTemplate(effect.Target!)));
}
