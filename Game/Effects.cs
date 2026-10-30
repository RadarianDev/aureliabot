using System.Globalization;
using System.Text;

public sealed record Effect(string Key, double? Value)
{
    public Formula? Formula { get; init; }

    public bool SetTo { get; init; }

    public EffectBlock? Block { get; init; }

    public string[] Parts => Key.Split('.');

    public string Prefix => Parts[0];

    public EffectKind? Kind => Block is null ? EffectKinds.Find(Prefix) : null;

    public bool IsAction => Kind?.IsAction ?? false;

    public string? Target => Parts.Length > 1 ? Parts[1] : null;

    public string? SubTarget => Parts.Length > 2 ? Parts[2] : null;

    public double Amount => Value ?? 0;

    public static Effect Of(EffectBlock block) => new(block.Keyword, null) { Block = block };
}

public abstract record EffectBlock
{
    public abstract string Keyword { get; }

    public abstract IEnumerable<IReadOnlyList<Effect>> Branches { get; }

    public abstract IEnumerable<Formula> Formulas { get; }
}

public sealed record IfBlock(Formula Condition, IReadOnlyList<Effect> Then, IReadOnlyList<Effect> Else) : EffectBlock
{
    public const string Word = "if";

    public override string Keyword => Word;

    public override IEnumerable<IReadOnlyList<Effect>> Branches => [Then, Else];

    public override IEnumerable<Formula> Formulas => [Condition];
}

public sealed record RandomOutcome(double Weight, IReadOnlyList<Effect> Effects);

public sealed record RandomBlock(IReadOnlyList<RandomOutcome> Outcomes) : EffectBlock
{
    public const string Word = "random";

    public override string Keyword => Word;

    public override IEnumerable<IReadOnlyList<Effect>> Branches => Outcomes.Select(o => o.Effects);

    public override IEnumerable<Formula> Formulas => [];
}

public enum EachScope { Province, Faction, Party, Issue }

public sealed record EachBlock(EachScope Scope, Formula? Where, Formula? By, int? Top, bool Lowest, IReadOnlyList<Effect> Do) : EffectBlock
{
    public const string Word = "each";
    public const string It = "it";

    public override string Keyword => Word;

    public override IEnumerable<IReadOnlyList<Effect>> Branches => [Do];

    public override IEnumerable<Formula> Formulas => new[] { Where, By }.OfType<Formula>();

    public static bool TryScope(string text, out EachScope scope) =>
        Enum.TryParse(text, ignoreCase: true, out scope) && Enum.IsDefined(scope) && !int.TryParse(text, out _);

    public static string ScopeWord(EachScope scope) => scope.ToString().ToLowerInvariant();
}

public static class EffectTree
{
    public static IEnumerable<Effect> Plain(IEnumerable<Effect> effects)
    {
        foreach (var effect in effects)
        {
            if (effect.Block is not { } block)
            {
                yield return effect;
                continue;
            }
            foreach (var branch in block.Branches)
                foreach (var inner in Plain(branch))
                    yield return inner;
        }
    }

    public static IEnumerable<Formula> Formulas(IEnumerable<Effect> effects)
    {
        foreach (var effect in effects)
        {
            if (effect.Formula is { } formula) yield return formula;
            if (effect.Block is not { } block) continue;
            foreach (var blockFormula in block.Formulas) yield return blockFormula;
            foreach (var branch in block.Branches)
                foreach (var inner in Formulas(branch))
                    yield return inner;
        }
    }

    public static IReadOnlyList<Effect> Bind(IReadOnlyList<Effect> effects, string placeholder, string value) =>
        effects.Select(e => Bind(e, placeholder, value)).ToList();

    private static Effect Bind(Effect effect, string placeholder, string value)
    {
        switch (effect.Block)
        {
            case IfBlock b:
                return Effect.Of(new IfBlock(b.Condition.Bind(placeholder, value), Bind(b.Then, placeholder, value), Bind(b.Else, placeholder, value)));
            case RandomBlock r:
                return Effect.Of(new RandomBlock(r.Outcomes.Select(o => o with { Effects = Bind(o.Effects, placeholder, value) }).ToList()));
            case EachBlock when placeholder == EachBlock.It:
                return effect;
            case EachBlock x:
                return Effect.Of(x with { Where = x.Where?.Bind(placeholder, value), By = x.By?.Bind(placeholder, value), Do = Bind(x.Do, placeholder, value) });
        }
        var parts = effect.Parts;
        var key = parts.Skip(1).Contains(placeholder)
            ? string.Join('.', parts.Select((p, i) => i >= 1 && p == placeholder ? value : p))
            : effect.Key;
        return effect with { Key = key, Formula = effect.Formula?.Bind(placeholder, value) };
    }
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
    public IReadOnlySet<string>? Variables { get; init; }
}

public sealed record EffectParseResult(IReadOnlyList<Effect> Effects, IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

public static class EffectParser
{
    public const string Here = "here";

    public static EffectParseResult ParseText(string text, EffectContext? context = null, bool allowHere = false, bool allowCrisis = true)
    {
        allowHere |= context?.AllowHere ?? false;
        allowCrisis = context?.AllowCrisis ?? allowCrisis;
        var reader = new TextReader(text, new EffectParseOptions(allowHere, allowCrisis));
        var items = reader.ParseTop();
        if (reader.Error is { } syntax) return new EffectParseResult([], [syntax]);

        var effects = new List<Effect>();
        var errors = new List<string>(reader.ItemErrors);
        foreach (var effect in items)
        {
            var problems = context is null ? new List<string>() : Check([effect], context);
            if (problems.Count == 0) effects.Add(effect);
            errors.AddRange(problems);
        }
        return new EffectParseResult(effects, errors);
    }

    public static EffectParseResult FromPairs(
        IEnumerable<(string Key, string? Value)> pairs, EffectContext? context = null, bool allowHere = false, bool allowCrisis = true)
    {
        var effects = new List<Effect>();
        var errors = new List<string>();
        allowHere |= context?.AllowHere ?? false;
        allowCrisis = context?.AllowCrisis ?? allowCrisis;
        var options = new EffectParseOptions(allowHere, allowCrisis);

        foreach (var (rawKey, rawValue) in pairs)
        {
            var key = rawKey.Trim();
            if (ParsePlain(key, rawValue, options, null, out var effect) is { } error)
            {
                errors.Add(Texts.Get("effects.errors.forKey", ("key", key), ("error", error)));
                continue;
            }
            var problems = context is null ? new List<string>() : Check([effect!], context);
            if (problems.Count == 0) effects.Add(effect!);
            errors.AddRange(problems);
        }
        return new EffectParseResult(effects, errors);
    }

    public static IReadOnlyList<string> Validate(IReadOnlyList<Effect> effects, EffectContext context) => Check(effects, context);

    public static string? ParsePlain(string key, string? rawValue, EffectParseOptions options, EachScope? scope, out Effect? effect)
    {
        effect = null;
        var setTo = false;
        Formula? formula = null;
        var value = rawValue?.Trim();
        if (value is { Length: > 0 } && value[0] == '=')
        {
            setTo = true;
            value = value[1..].Trim();
            if (value.Length == 0) return Texts.Get("effects.errors.needsNumber");
        }
        if (value is { Length: > 1 } && value[0] == '{' && value[^1] == '}')
        {
            formula = Formula.TryParse(value[1..^1], out var formulaError);
            if (formula is null) return formulaError;
        }

        var checkedKey = scope is { } s ? BindKey(key, Representative(s)) : key;
        var error = CheckSyntax(checkedKey, formula is null ? value : "1", options, out var parsed);
        if (error is not null) return error;
        if ((setTo || formula is not null) && parsed!.IsAction) return Texts.Get("effects.errors.actionValue");
        if (setTo && parsed!.Prefix == "condition") return Texts.Get("effects.errors.setCondition");
        effect = new Effect(key, formula is null ? parsed!.Value : null) { Formula = formula, SetTo = setTo };
        return null;
    }

    private static string BindKey(string key, string value)
    {
        var parts = key.Split('.');
        return string.Join('.', parts.Select((p, i) => i >= 1 && p == EachBlock.It ? value : p));
    }

    private static string Representative(EachScope scope) => scope is EachScope.Faction or EachScope.Party ? "0" : EachBlock.It;

    private static string? CheckSyntax(string key, string? rawValue, EffectParseOptions options, out Effect? effect)
    {
        effect = null;
        var parts = key.Split('.');
        if (parts.Any(string.IsNullOrWhiteSpace)) return Texts.Get("effects.errors.invalidKey");
        if (EffectKinds.Find(parts[0]) is not { } kind)
            return parts[0] == "baseline" ? Texts.Get("effects.errors.noBaseline") : Texts.Get("effects.errors.unknownKey");
        if (kind.Parse(parts, rawValue, options, out var value) is { } error) return error;
        effect = new Effect(key, value);
        return null;
    }

    public static List<string> Check(IReadOnlyList<Effect> effects, EffectContext context)
    {
        var errors = new List<string>();
        CheckInto(effects, context, errors);
        return errors;
    }

    private static void CheckInto(IReadOnlyList<Effect> effects, EffectContext context, List<string> errors)
    {
        foreach (var effect in effects)
        {
            switch (effect.Block)
            {
                case IfBlock b:
                    CheckFormula(b.Condition, context, errors);
                    CheckInto(b.Then, context, errors);
                    CheckInto(b.Else, context, errors);
                    continue;
                case RandomBlock r:
                    foreach (var outcome in r.Outcomes) CheckInto(outcome.Effects, context, errors);
                    continue;
                case EachBlock x:
                    var representative = Representative(x.Scope);
                    var scoped = Widen(context, x.Scope, representative);
                    if (x.Where is { } where) CheckFormula(where.Bind(EachBlock.It, representative), scoped, errors);
                    if (x.By is { } by) CheckFormula(by.Bind(EachBlock.It, representative), scoped, errors);
                    CheckInto(EffectTree.Bind(x.Do, EachBlock.It, representative), scoped, errors);
                    continue;
            }
            if (effect.Formula is { } formula) CheckFormula(formula, context, errors);
            if (effect.Kind?.CheckIds(effect, context) is { } error)
                errors.Add(Texts.Get("effects.errors.forKey", ("key", effect.Key), ("error", error)));
        }
    }

    private static EffectContext Widen(EffectContext context, EachScope scope, string representative)
    {
        static IReadOnlySet<string> With(IReadOnlySet<string> set, string extra) => new HashSet<string>(set) { extra };
        return scope switch
        {
            EachScope.Province => context with { Provinces = With(context.Provinces, representative) },
            EachScope.Issue => context with { Issues = With(context.Issues, representative) },
            EachScope.Faction => context with { Factions = With(context.Factions, representative) },
            _ => context,
        };
    }

    private static void CheckFormula(Formula formula, EffectContext context, List<string> errors)
    {
        foreach (var reference in formula.References)
            if (FormulaReferences.Check(reference, context) is { } error)
                errors.Add(Texts.Get("formula.errors.inFormula", ("formula", formula.Source), ("error", error)));
    }

    public static bool TryParseNumber(string text, out double value) =>
        double.TryParse(text.Replace('−', '-').Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    public static string FormatNumber(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    public static string Format(IReadOnlyList<Effect> effects) => string.Join(", ", effects.Select(FormatOne));

    public static string FormatValue(Effect effect)
    {
        var value = effect.Formula is { } formula ? $"{{{formula.Source}}}" : effect.Value is { } v ? FormatNumber(v) : null;
        return value is null ? "" : effect.SetTo ? "=" + value : value;
    }

    private static string FormatOne(Effect effect)
    {
        switch (effect.Block)
        {
            case IfBlock b:
                var text = $"if {{{b.Condition.Source}}} [{Format(b.Then)}]";
                return b.Else.Count == 0 ? text : $"{text} else [{Format(b.Else)}]";
            case RandomBlock r:
                return $"random [{string.Join(" | ", r.Outcomes.Select(o => $"{FormatNumber(o.Weight)}: {Format(o.Effects)}"))}]";
            case EachBlock x:
                var sb = new StringBuilder($"each {EachBlock.ScopeWord(x.Scope)}");
                if (x.Where is { } where) sb.Append($" where {{{where.Source}}}");
                if (x.By is { } by) sb.Append($" by {{{by.Source}}}");
                if (x.Top is { } top) sb.Append($" {(x.Lowest ? "bottom" : "top")} {top.ToString(CultureInfo.InvariantCulture)}");
                return sb.Append($" [{Format(x.Do)}]").ToString();
        }
        var value = FormatValue(effect);
        return value.Length == 0 ? effect.Key : $"{effect.Key} {value}";
    }

    public static IReadOnlyList<Effect> Parse(string stored) =>
        string.IsNullOrWhiteSpace(stored) ? [] : ParseText(stored, allowHere: true).Effects;

    public static IReadOnlyList<Effect> ResolveHere(IReadOnlyList<Effect> effects, string provinceId) =>
        EffectTree.Bind(effects, Here, provinceId);

    public static IEnumerable<string> ReferencedTemplates(IEnumerable<Effect> effects) =>
        EffectTree.Plain(effects).Where(e => e.Prefix is "open" or "bill" or "project" or "crisis" or "event").Select(e => e.Target!);

    private sealed class TextReader(string text, EffectParseOptions options)
    {
        private int _position;
        private readonly Stack<EachScope> _scopes = new();

        public string? Error { get; private set; }

        public List<string> ItemErrors { get; } = [];

        public List<Effect> ParseTop()
        {
            var items = ParseList();
            SkipSpace(newlines: true);
            if (Error is null && _position < text.Length) Fail(Texts.Get("effects.errors.unexpected", ("text", Rest())));
            return items;
        }

        private List<Effect> ParseList()
        {
            var items = new List<Effect>();
            while (Error is null)
            {
                SkipSpace(newlines: true);
                while (_position < text.Length && text[_position] == ',') { _position++; SkipSpace(newlines: true); }
                if (_position >= text.Length || text[_position] is ']' or '|') break;
                if (ParseItem() is { } item) items.Add(item);
                SkipSpace(newlines: false);
                if (_position < text.Length && text[_position] is ',' or '\n') _position++;
                else if (_position < text.Length && text[_position] is not (']' or '|') && Error is null)
                    Fail(Texts.Get("effects.errors.unexpected", ("text", Rest())));
            }
            return items;
        }

        private Effect? ParseItem()
        {
            var word = ReadWord();
            if (word.Length == 0)
            {
                Fail(Texts.Get("effects.errors.unexpected", ("text", Rest())));
                return null;
            }
            switch (word)
            {
                case IfBlock.Word:
                    var condition = ReadFormula();
                    var then = ReadBracketList();
                    var mark = _position;
                    SkipSpace(newlines: true);
                    IReadOnlyList<Effect> otherwise = [];
                    if (ReadWord() == "else") otherwise = ReadBracketList();
                    else _position = mark;
                    return condition is null || Error is not null ? null : Effect.Of(new IfBlock(condition, then, otherwise));
                case RandomBlock.Word:
                    return ParseRandom();
                case EachBlock.Word:
                    return ParseEach();
            }

            SkipSpace(newlines: false);
            var value = ReadValue();
            var error = ParsePlain(word, value, options, _scopes.Count > 0 ? _scopes.Peek() : null, out var effect);
            if (error is null) return effect;
            ItemErrors.Add(Texts.Get("effects.errors.forKey", ("key", word), ("error", error)));
            return null;
        }

        private Effect? ParseRandom()
        {
            SkipSpace(newlines: true);
            if (!Take('[')) return Fail(Texts.Get("effects.errors.expected", ("expected", "["), ("text", Rest())));
            var outcomes = new List<RandomOutcome>();
            while (Error is null)
            {
                SkipSpace(newlines: true);
                var weightText = ReadWord();
                if (!TryParseNumber(weightText, out var weight) || weight <= 0)
                    return Fail(Texts.Get("effects.errors.randomWeight", ("weight", weightText)));
                SkipSpace(newlines: false);
                if (!Take(':')) return Fail(Texts.Get("effects.errors.expected", ("expected", ":"), ("text", Rest())));
                outcomes.Add(new RandomOutcome(weight, ParseList()));
                SkipSpace(newlines: true);
                if (Take('|')) continue;
                if (Take(']')) break;
                return Fail(Texts.Get("effects.errors.expected", ("expected", "]"), ("text", Rest())));
            }
            if (outcomes.Count == 0) return Fail(Texts.Get("effects.errors.randomEmpty"));
            return Error is null ? Effect.Of(new RandomBlock(outcomes)) : null;
        }

        private Effect? ParseEach()
        {
            SkipSpace(newlines: false);
            var scopeWord = ReadWord();
            if (!EachBlock.TryScope(scopeWord, out var scope))
                return Fail(Texts.Get("effects.errors.eachScope", ("scope", scopeWord)));
            Formula? where = null, by = null;
            int? top = null;
            var lowest = false;
            while (Error is null)
            {
                var mark = _position;
                SkipSpace(newlines: true);
                var word = ReadWord();
                if (word == "where") where = ReadFormula();
                else if (word == "by") by = ReadFormula();
                else if (word is "top" or "bottom")
                {
                    SkipSpace(newlines: false);
                    var count = ReadWord();
                    if (!int.TryParse(count, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n < 1)
                        return Fail(Texts.Get("effects.errors.eachTop", ("count", count)));
                    top = n;
                    lowest = word == "bottom";
                }
                else
                {
                    _position = mark;
                    break;
                }
            }
            if (top is not null && by is null) return Fail(Texts.Get("effects.errors.eachTopNeedsBy"));
            _scopes.Push(scope);
            var body = ReadBracketList();
            _scopes.Pop();
            return Error is null ? Effect.Of(new EachBlock(scope, where, by, top, lowest, body)) : null;
        }

        private List<Effect> ReadBracketList()
        {
            SkipSpace(newlines: true);
            if (!Take('['))
            {
                Fail(Texts.Get("effects.errors.expected", ("expected", "["), ("text", Rest())));
                return [];
            }
            var items = ParseList();
            SkipSpace(newlines: true);
            if (Error is null && !Take(']')) Fail(Texts.Get("effects.errors.expected", ("expected", "]"), ("text", Rest())));
            return items;
        }

        private Formula? ReadFormula()
        {
            SkipSpace(newlines: false);
            if (ReadBraced() is not { } source)
            {
                Fail(Texts.Get("effects.errors.expected", ("expected", "{"), ("text", Rest())));
                return null;
            }
            var formula = Formula.TryParse(source, out var error);
            if (formula is null) Fail(error!);
            return formula;
        }

        private string? ReadBraced()
        {
            if (!Take('{')) return null;
            var start = _position;
            var depth = 1;
            while (_position < text.Length)
            {
                var c = text[_position++];
                if (c == '{') depth++;
                else if (c == '}' && --depth == 0) return text[start..(_position - 1)];
            }
            Fail(Texts.Get("effects.errors.expected", ("expected", "}"), ("text", "")));
            return null;
        }

        private string? ReadValue()
        {
            if (_position >= text.Length || text[_position] is ',' or '\n' or ']' or '|') return null;
            var prefix = Take('=') ? "=" : "";
            SkipSpace(newlines: false);
            if (_position < text.Length && text[_position] == '{')
                return ReadBraced() is { } source ? $"{prefix}{{{source}}}" : null;
            var start = _position;
            while (_position < text.Length && text[_position] is not (',' or '\n' or ']' or '|')) _position++;
            var raw = text[start.._position].Trim();
            return prefix + raw;
        }

        private string ReadWord()
        {
            var start = _position;
            while (_position < text.Length && (char.IsLetterOrDigit(text[_position]) || text[_position] is '_' or '.' or '-' or '+' or '−'))
                _position++;
            return text[start.._position];
        }

        private void SkipSpace(bool newlines)
        {
            while (_position < text.Length && char.IsWhiteSpace(text[_position]) && (newlines || text[_position] != '\n')) _position++;
        }

        private bool Take(char c)
        {
            if (_position >= text.Length || text[_position] != c) return false;
            _position++;
            return true;
        }

        private string Rest() => text[_position..].Length > 30 ? text[_position..(_position + 30)] + "…" : text[_position..];

        private Effect? Fail(string error)
        {
            Error ??= error;
            return null;
        }
    }
}
