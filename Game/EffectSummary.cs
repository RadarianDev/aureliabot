using System.Globalization;

public sealed record EffectNames(
    Func<string, string> Province,
    Func<string, string> Issue,
    Func<string, string> Condition,
    Func<string, string> Faction,
    Func<string, string> IssueTemplate,
    Func<string, string> BillTemplate,
    Func<string, string> ProjectTemplate,
    Func<string, string> CrisisTemplate);

public static class EffectSummary
{
    public static IReadOnlyList<string> Lines(IReadOnlyList<Effect> effects, EffectNames names, DisplayConstants display)
    {
        var lines = new List<string>();
        foreach (var effect in effects)
        {
            var line = effect.IsAction ? ActionLine(effect, names) : NumberLine(effect, names, display);
            if (line is not null) lines.Add(line);
        }
        return lines;
    }

    public static string FormatSigned(double value)
    {
        var magnitude = Math.Abs(value).ToString("0.##", CultureInfo.InvariantCulture);
        return (value < 0 ? "−" : "+") + magnitude;
    }

    private static string ActionLine(Effect effect, EffectNames names)
    {
        var target = effect.Target!;
        return effect.Prefix switch
        {
            "open" => $"New issue: {names.IssueTemplate(target)}",
            "close" => $"Issue closed: {names.Issue(target)}",
            "bill" => $"New bill: {names.BillTemplate(target)}",
            "project" => $"Project begun: {names.ProjectTemplate(target)}",
            _ => $"Crisis: {names.CrisisTemplate(target)}",
        };
    }

    private static string? NumberLine(Effect effect, EffectNames names, DisplayConstants display)
    {
        var amount = effect.Amount;
        if (amount == 0) return null;

        if (effect.Prefix == "condition")
        {
            var province = names.Province(effect.Target!);
            var condition = names.Condition(effect.SubTarget!);
            return amount > 0 ? $"{province} is now *{condition}*" : $"{province} is no longer *{condition}*";
        }

        var arrow = amount > 0 ? display.UpEmoji : display.DownEmoji;
        var target = effect.Target;
        (string name, string? shown) = effect.Prefix switch
        {
            "economy" => ("Economy", FormatSigned(amount)),
            "reserve" => ("Reserve", FormatSigned(amount)),
            "seats" => ("Assembly seats", FormatSigned(amount)),
            "taxRate" => ("Tax rate", FormatSigned(amount * 100) + "%"),
            "purse" => ($"{PurseName(target!)} purse", FormatSigned(amount)),
            "popularity" => ($"{names.Faction(target!)} popularity", FormatSigned(amount)),
            "discontent" => ($"Discontent in {names.Province(target!)}", null),
            "opinion" => ($"Opinion on {names.Issue(target!)}", null),
            "salience" => ($"Salience: {names.Issue(target!)}", null),
            "favour" or "favourBaseline" => ("Anzarian Favour", null),
            "threat" => ("Foreign Threat", null),
            _ => (effect.Key, null),
        };
        return shown is null ? $"{arrow} {name}" : $"{arrow} {name} {shown}";
    }

    private static string PurseName(string ministry) =>
        Enum.TryParse<Ministry>(ministry, out var parsed) ? parsed.DisplayName() : ministry;
}
