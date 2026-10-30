using System.Globalization;

public sealed record EffectNames(
    Func<string, string> Province,
    Func<string, string> Issue,
    Func<string, string> Condition,
    Func<string, string> Faction,
    Func<string, string> IssueTemplate,
    Func<string, string> BillTemplate,
    Func<string, string> ProjectTemplate,
    Func<string, string> CrisisTemplate)
{
    public Func<string, string> Party { get; init; } = id => id;
}

public static class EffectSummary
{
    private const string Nested = "→ ";

    public static IReadOnlyList<string> Lines(IReadOnlyList<Effect> effects, EffectNames names, DisplayConstants display, bool preview = false)
    {
        var lines = new List<string>();
        AddLines(effects, names, display, preview, "", lines);
        return lines;
    }

    private static void AddLines(IReadOnlyList<Effect> effects, EffectNames names, DisplayConstants display, bool preview, string depth, List<string> lines)
    {
        foreach (var effect in effects)
        {
            switch (effect.Block)
            {
                case IfBlock b:
                    lines.Add(depth + Texts.Get("effects.preview.if", ("condition", b.Condition.Source)));
                    AddLines(b.Then, names, display, preview, depth + Nested, lines);
                    if (b.Else.Count == 0) continue;
                    lines.Add(depth + Texts.Get("effects.preview.else"));
                    AddLines(b.Else, names, display, preview, depth + Nested, lines);
                    continue;
                case RandomBlock r:
                    var total = r.Outcomes.Sum(o => o.Weight);
                    lines.Add(depth + Texts.Get("effects.preview.random"));
                    foreach (var outcome in r.Outcomes)
                    {
                        lines.Add(depth + Nested + Texts.Get("effects.preview.chance",
                            ("chance", (outcome.Weight / total * 100).ToString("0.#", CultureInfo.InvariantCulture))));
                        if (outcome.Effects.Count == 0) lines.Add(depth + Nested + Nested + Texts.Get("effects.preview.nothing"));
                        AddLines(outcome.Effects, names, display, preview, depth + Nested + Nested, lines);
                    }
                    continue;
                case EachBlock x:
                    lines.Add(depth + EachLine(x));
                    AddLines(x.Do, names, display, preview, depth + Nested, lines);
                    continue;
            }

            if (effect.Formula is not null || effect.SetTo)
            {
                var value = EffectParser.FormatValue(effect).TrimStart('=');
                lines.Add(depth + (effect.SetTo
                    ? Texts.Get("effects.preview.setTo", ("key", effect.Key), ("value", value))
                    : Texts.Get("effects.preview.formula", ("key", effect.Key), ("value", value))));
                continue;
            }
            if (effect.Kind?.SummaryLine(effect, names, display) is { } line)
                lines.Add(depth + line);
            else if (preview && effect.Kind is VariableEffect)
                lines.Add(depth + Texts.Get("effects.preview.variable", ("name", effect.Target), ("amount", FormatSigned(effect.Amount))));
        }
    }

    private static string EachLine(EachBlock block)
    {
        var scope = block.Scope switch
        {
            EachScope.Province => Texts.Get("effects.preview.scopeProvince"),
            EachScope.Faction => Texts.Get("effects.preview.scopeFaction"),
            EachScope.Party => Texts.Get("effects.preview.scopeParty"),
            _ => Texts.Get("effects.preview.scopeIssue"),
        };
        var line = Texts.Get("effects.preview.each", ("scope", scope));
        if (block.Where is { } where) line += Texts.Get("effects.preview.where", ("condition", where.Source));
        if (block.By is { } by)
            line += block.Top is { } top
                ? block.Lowest
                    ? Texts.Get("effects.preview.bottom", ("count", top), ("score", by.Source))
                    : Texts.Get("effects.preview.top", ("count", top), ("score", by.Source))
                : Texts.Get("effects.preview.by", ("score", by.Source));
        return line;
    }

    public static string FormatSigned(double value)
    {
        var magnitude = Math.Abs(value).ToString("0.##", CultureInfo.InvariantCulture);
        return (value < 0 ? "−" : "+") + magnitude;
    }
}
