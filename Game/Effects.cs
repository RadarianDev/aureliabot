using System.Globalization;

public sealed record Effect(string Key, double? Value)
{
    public static readonly IReadOnlySet<string> ActionPrefixes =
        new HashSet<string>(StringComparer.Ordinal) { "open", "close", "bill", "project", "crisis" };

    public static readonly IReadOnlySet<string> ScalarKeys =
        new HashSet<string>(StringComparer.Ordinal) { "economy", "taxRate", "favour", "favourBaseline", "threat", "reserve", "seats" };

    public string[] Parts => Key.Split('.');

    public string Prefix => Parts[0];

    public bool IsAction => ActionPrefixes.Contains(Prefix);

    public string? Target => Parts.Length > 1 ? Parts[1] : null;

    public string? SubTarget => Parts.Length > 2 ? Parts[2] : null;

    public double Amount => Value ?? 0;
}

public sealed record EffectContext(
    IReadOnlySet<string> Provinces,
    IReadOnlySet<string> Conditions,
    IReadOnlySet<string> Issues,
    IReadOnlySet<string> IssueTemplates,
    IReadOnlySet<string> BillTemplates,
    IReadOnlySet<string> ProjectTemplates,
    IReadOnlySet<string> CrisisTemplates,
    IReadOnlySet<string> Factions)
{
    public bool AllowHere { get; init; }
    public bool AllowCrisis { get; init; }
}

public sealed record EffectParseResult(IReadOnlyList<Effect> Effects, IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

public static class EffectParser
{
    public const string Here = "here";

    private static readonly HashSet<string> PurseMinistries =
        [nameof(Ministry.War), nameof(Ministry.Interior), nameof(Ministry.CommerceLabor), nameof(Ministry.Treasury)];

    public static EffectParseResult ParseText(string text, EffectContext? context = null, bool allowHere = false, bool allowCrisis = true)
    {
        var pairs = new List<(string Key, string? Value)>();
        var errors = new List<string>();
        foreach (var raw in text.Split([',', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var tokens = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length > 2)
            {
                errors.Add($"`{raw}`: write one key and at most one value, such as `economy -2`.");
                continue;
            }
            pairs.Add((tokens[0], tokens.Length == 2 ? tokens[1] : null));
        }
        var parsed = FromPairs(pairs, context, allowHere, allowCrisis);
        return new EffectParseResult(parsed.Effects, errors.Concat(parsed.Errors).ToList());
    }

    public static EffectParseResult FromPairs(
        IEnumerable<(string Key, string? Value)> pairs, EffectContext? context = null, bool allowHere = false, bool allowCrisis = true)
    {
        var effects = new List<Effect>();
        var errors = new List<string>();
        allowHere |= context?.AllowHere ?? false;
        allowCrisis = context?.AllowCrisis ?? allowCrisis;

        foreach (var (rawKey, rawValue) in pairs)
        {
            var key = rawKey.Trim();
            var error = CheckSyntax(key, rawValue, allowHere, allowCrisis, out var effect);
            if (error is null && context is not null)
                error = CheckIds(effect!, context);
            if (error is null) effects.Add(effect!);
            else errors.Add($"`{key}`: {error}");
        }
        return new EffectParseResult(effects, errors);
    }

    public static IReadOnlyList<string> Validate(IReadOnlyList<Effect> effects, EffectContext context) =>
        FromPairs(effects.Select(e => (e.Key, e.Value is { } v ? FormatNumber(v) : (string?)null)), context, context.AllowHere, context.AllowCrisis).Errors;

    private static string? CheckSyntax(string key, string? rawValue, bool allowHere, bool allowCrisis, out Effect? effect)
    {
        effect = null;
        var parts = key.Split('.');
        if (parts.Any(string.IsNullOrWhiteSpace)) return "is not a valid effect key.";
        var prefix = parts[0];

        if (Effect.ActionPrefixes.Contains(prefix))
        {
            if (parts.Length != 2) return $"write it as `{prefix}.<id>`.";
            if (rawValue is not null && !rawValue.Equals("true", StringComparison.OrdinalIgnoreCase))
                return "an action takes no value (or `true` in YAML).";
            if (prefix == "crisis" && !allowCrisis) return "only events can post a crisis.";
            effect = new Effect(key, null);
            return null;
        }

        if (rawValue is null) return "needs a number.";
        if (!TryParseNumber(rawValue, out var value)) return $"`{rawValue}` is not a number.";

        var shapeError = prefix switch
        {
            _ when Effect.ScalarKeys.Contains(prefix) => parts.Length == 1 ? null : $"`{prefix}` takes no id.",
            "purse" => parts.Length != 2 ? "write it as `purse.<ministry>`."
                : parts[1] == nameof(Ministry.Navy) ? "the Navy purse is a contribution and cannot be changed by effects."
                : PurseMinistries.Contains(parts[1]) ? null
                : "the ministry must be War, Interior, CommerceLabor or Treasury.",
            "discontent" => parts.Length == 2 ? HereCheck(parts[1], allowHere) : "write it as `discontent.<province>`.",
            "condition" => parts.Length != 3 ? "write it as `condition.<province>.<condition>`."
                : value is not (1 or -1) ? "a condition takes 1 (gives it) or -1 (removes it)."
                : HereCheck(parts[1], allowHere),
            "opinion" or "salience" => parts.Length == 2 ? null : $"write it as `{prefix}.<issue>`.",
            "popularity" => parts.Length == 2 && ulong.TryParse(parts[1], out _) ? null : "write it as `popularity.<faction user id>`.",
            "baseline" => "issue opinion has no baseline; use `opinion.<issue>`, which lasts.",
            _ => "is not an effect key.",
        };
        if (shapeError is not null) return shapeError;
        effect = new Effect(key, value);
        return null;
    }

    private static string? HereCheck(string province, bool allowHere) =>
        province == Here && !allowHere ? "`here` only works in a project's effects." : null;

    private static string? CheckIds(Effect effect, EffectContext context)
    {
        var target = effect.Target;
        return effect.Prefix switch
        {
            "discontent" when target != Here && !context.Provinces.Contains(target!) => $"unknown province `{target}`.",
            "condition" when target != Here && !context.Provinces.Contains(target!) => $"unknown province `{target}`.",
            "condition" when !context.Conditions.Contains(effect.SubTarget!) => $"unknown condition `{effect.SubTarget}`.",
            "opinion" or "salience" or "close" when !context.Issues.Contains(target!) => $"unknown issue `{target}`.",
            "open" when !context.IssueTemplates.Contains(target!) => $"no issue template `{target}`.",
            "bill" when !context.BillTemplates.Contains(target!) => $"no bill template `{target}`.",
            "project" when !context.ProjectTemplates.Contains(target!) => $"no project template `{target}`.",
            "crisis" when !context.CrisisTemplates.Contains(target!) => $"no crisis template `{target}`.",
            "popularity" when !context.Factions.Contains(target!) => $"no faction for user `{target}`.",
            _ => null,
        };
    }

    public static bool TryParseNumber(string text, out double value) =>
        double.TryParse(text.Replace('−', '-').Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    public static string FormatNumber(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    public static string Format(IReadOnlyList<Effect> effects) =>
        string.Join(", ", effects.Select(e => e.Value is { } v ? $"{e.Key} {FormatNumber(v)}" : e.Key));

    public static IReadOnlyList<Effect> Parse(string stored) =>
        string.IsNullOrWhiteSpace(stored) ? [] : ParseText(stored, allowHere: true).Effects;

    public static IReadOnlyList<Effect> ResolveHere(IReadOnlyList<Effect> effects, string provinceId) =>
        effects.Select(e => e.Target == Here
            ? e with { Key = string.Join('.', e.Parts.Select((p, i) => i == 1 ? provinceId : p)) }
            : e).ToList();

    public static IEnumerable<string> ReferencedTemplates(IEnumerable<Effect> effects) =>
        effects.Where(e => e.Prefix is "open" or "bill" or "project" or "crisis").Select(e => e.Target!);
}
