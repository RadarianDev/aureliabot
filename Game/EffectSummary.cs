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
            "open" => Texts.Get("effects.summary.open", ("issue", names.IssueTemplate(target))),
            "close" => Texts.Get("effects.summary.close", ("issue", names.Issue(target))),
            "bill" => Texts.Get("effects.summary.bill", ("bill", names.BillTemplate(target))),
            "project" => Texts.Get("effects.summary.project", ("project", names.ProjectTemplate(target))),
            _ => Texts.Get("effects.summary.crisis", ("crisis", names.CrisisTemplate(target))),
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
            return amount > 0
                ? Texts.Get("effects.summary.conditionGained", ("province", province), ("condition", condition))
                : Texts.Get("effects.summary.conditionLost", ("province", province), ("condition", condition));
        }

        var arrow = amount > 0 ? display.UpEmoji : display.DownEmoji;
        var target = effect.Target;
        (string name, string? shown) = effect.Prefix switch
        {
            "economy" => (Texts.Get("effects.summary.economy"), FormatSigned(amount)),
            "reserve" => (Texts.Get("effects.summary.reserve"), FormatSigned(amount)),
            "seats" => (Texts.Get("effects.summary.seats"), FormatSigned(amount)),
            "taxRate" => (Texts.Get("effects.summary.taxRate"), Texts.Get("effects.summary.percent", ("amount", FormatSigned(amount * 100)))),
            "purse" => (Texts.Get("effects.summary.purse", ("ministry", PurseName(target!))), FormatSigned(amount)),
            "popularity" => (Texts.Get("effects.summary.popularity", ("faction", names.Faction(target!))), FormatSigned(amount)),
            "discontent" => (Texts.Get("effects.summary.discontent", ("province", names.Province(target!))), null),
            "opinion" => (Texts.Get("effects.summary.opinion", ("issue", names.Issue(target!))), null),
            "salience" => (Texts.Get("effects.summary.salience", ("issue", names.Issue(target!))), null),
            "favour" or "favourBaseline" => (Texts.Get("effects.summary.favour"), null),
            "threat" => (Texts.Get("effects.summary.threat"), null),
            _ => (effect.Key, null),
        };
        return shown is null
            ? Texts.Get("effects.summary.line", ("arrow", arrow), ("name", name))
            : Texts.Get("effects.summary.lineWithAmount", ("arrow", arrow), ("name", name), ("amount", shown));
    }

    private static string PurseName(string ministry) =>
        Enum.TryParse<Ministry>(ministry, out var parsed) ? parsed.DisplayName() : ministry;
}
