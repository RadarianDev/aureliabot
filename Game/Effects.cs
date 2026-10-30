using System.Globalization;

public sealed record Effect(string Key, double? Value)
{
    public string[] Parts => Key.Split('.');

    public string Prefix => Parts[0];

    public EffectKind? Kind => EffectKinds.Find(Prefix);

    public bool IsAction => Kind?.IsAction ?? false;

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
    public IReadOnlySet<string>? EventTemplates { get; init; }
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
                errors.Add(Texts.Get("effects.errors.tooManyTokens", ("raw", raw)));
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
            else errors.Add(Texts.Get("effects.errors.forKey", ("key", key), ("error", error)));
        }
        return new EffectParseResult(effects, errors);
    }

    public static IReadOnlyList<string> Validate(IReadOnlyList<Effect> effects, EffectContext context) =>
        FromPairs(effects.Select(e => (e.Key, e.Value is { } v ? FormatNumber(v) : (string?)null)), context, context.AllowHere, context.AllowCrisis).Errors;

    private static string? CheckSyntax(string key, string? rawValue, bool allowHere, bool allowCrisis, out Effect? effect)
    {
        effect = null;
        var parts = key.Split('.');
        if (parts.Any(string.IsNullOrWhiteSpace)) return Texts.Get("effects.errors.invalidKey");
        if (EffectKinds.Find(parts[0]) is not { } kind)
            return parts[0] == "baseline" ? Texts.Get("effects.errors.noBaseline") : Texts.Get("effects.errors.unknownKey");
        if (kind.Parse(parts, rawValue, new EffectParseOptions(allowHere, allowCrisis), out var value) is { } error) return error;
        effect = new Effect(key, value);
        return null;
    }

    private static string? CheckIds(Effect effect, EffectContext context) => effect.Kind?.CheckIds(effect, context);

    public static bool TryParseNumber(string text, out double value) =>
        double.TryParse(text.Replace('−', '-').Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    public static string FormatNumber(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    public static string Format(IReadOnlyList<Effect> effects) =>
        string.Join(", ", effects.Select(e => e.Value is { } v ? $"{e.Key} {FormatNumber(v)}" : e.Key));

    public static IReadOnlyList<Effect> Parse(string stored) =>
        string.IsNullOrWhiteSpace(stored) ? [] : ParseText(stored, allowHere: true).Effects;

    public static IReadOnlyList<Effect> ResolveHere(IReadOnlyList<Effect> effects, string provinceId) =>
        effects.Select(e => e.Parts.Skip(1).Contains(Here)
            ? e with { Key = string.Join('.', e.Parts.Select((p, i) => i >= 1 && p == Here ? provinceId : p)) }
            : e).ToList();

    public static IEnumerable<string> ReferencedTemplates(IEnumerable<Effect> effects) =>
        effects.Where(e => e.Prefix is "open" or "bill" or "project" or "crisis" or "event").Select(e => e.Target!);
}
