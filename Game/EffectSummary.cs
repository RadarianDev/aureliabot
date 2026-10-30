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
    public static IReadOnlyList<string> Lines(IReadOnlyList<Effect> effects, EffectNames names, DisplayConstants display)
    {
        var lines = new List<string>();
        foreach (var effect in effects)
            if (effect.Kind?.SummaryLine(effect, names, display) is { } line)
                lines.Add(line);
        return lines;
    }

    public static string FormatSigned(double value)
    {
        var magnitude = Math.Abs(value).ToString("0.##", CultureInfo.InvariantCulture);
        return (value < 0 ? "−" : "+") + magnitude;
    }
}
