using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

public static partial class TemplateCodec
{
    public static readonly IReadOnlyList<Ministry> PurseMinistries =
        [Ministry.War, Ministry.Interior, Ministry.CommerceLabor, Ministry.Treasury];

    public static TemplateParseResult Parse(string yaml, string fileName, EffectContext? context = null)
    {
        var reader = new Reader(fileName, context);
        Template? template = null;
        try
        {
            template = reader.Read(yaml);
        }
        catch (YamlException ex)
        {
            reader.ErrorAtLine((int)ex.Start.Line, Texts.Get("templates.yamlSyntax", ("message", YamlMessage(ex))));
        }
        return new TemplateParseResult(reader.Errors.Count == 0 ? template : null, reader.Errors);
    }

    public static EffectContext ImportContext(EffectContext full) => full with
    {
        Issues = AnyId.Instance,
        IssueTemplates = AnyId.Instance,
        BillTemplates = AnyId.Instance,
        ProjectTemplates = AnyId.Instance,
        CrisisTemplates = AnyId.Instance,
    };

    public static bool IsSlug(string text) => SlugPattern().IsMatch(text);

    public static string FolderOf(TemplateKind kind) => kind switch
    {
        TemplateKind.Crisis => "crises",
        _ => kind.ToString().ToLowerInvariant() + "s",
    };

    public static string KindName(TemplateKind kind) => kind switch
    {
        TemplateKind.Issue => Texts.Get("names.templateKind.issue"),
        TemplateKind.Bill => Texts.Get("names.templateKind.bill"),
        TemplateKind.Project => Texts.Get("names.templateKind.project"),
        TemplateKind.Event => Texts.Get("names.templateKind.event"),
        TemplateKind.Crisis => Texts.Get("names.templateKind.crisis"),
        _ => kind.ToString().ToLowerInvariant(),
    };

    [GeneratedRegex("^[a-z0-9_]+$")]
    private static partial Regex SlugPattern();

    [GeneratedRegex(@"^\(Line: \d+, Col: \d+, Idx: \d+\) - \(Line: \d+, Col: \d+, Idx: \d+\): ")]
    private static partial Regex YamlMarkPrefix();

    private static string YamlMessage(YamlException ex) => YamlMarkPrefix().Replace(ex.Message, "");

    private sealed class Reader(string file, EffectContext? context)
    {
        public List<TemplateParseError> Errors { get; } = [];

        public void ErrorAtLine(int? line, string message) => Errors.Add(new TemplateParseError(file, line, message));

        public void Error(YamlNode node, string message) => ErrorAtLine((int)node.Start.Line, message);

        public Template? Read(string yaml)
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(yaml));
            if (stream.Documents.Count == 0)
            {
                ErrorAtLine(null, Texts.Get("templates.emptyFile"));
                return null;
            }
            if (stream.Documents.Count > 1)
                Error(stream.Documents[1].RootNode, Texts.Get("templates.onePerFile"));
            if (stream.Documents[0].RootNode is not YamlMappingNode root)
            {
                Error(stream.Documents[0].RootNode, Texts.Get("templates.notAMap"));
                return null;
            }

            var keys = root.Children.Keys.OfType<YamlScalarNode>().Select(k => k.Value).ToHashSet();
            TemplateKind? kind = keys.Contains("answers") ? TemplateKind.Crisis
                : keys.Contains("lean") ? TemplateKind.Bill
                : keys.Contains("price") ? TemplateKind.Project
                : keys.Contains("text") ? TemplateKind.Event
                : keys.Contains("for") ? TemplateKind.Issue
                : null;

            return kind switch
            {
                TemplateKind.Issue => ReadIssue(root),
                TemplateKind.Bill => ReadBill(root),
                TemplateKind.Project => ReadProject(root),
                TemplateKind.Event => ReadEvent(root),
                TemplateKind.Crisis => ReadCrisis(root),
                _ => Unknown(root),
            };
        }

        private Template? Unknown(YamlNode root)
        {
            Error(root, Texts.Get("templates.unknownKind"));
            return null;
        }

        private IssueTemplate ReadIssue(YamlMappingNode root)
        {
            var map = new Map(this, root, "", "id", "title", "for", "against", "opinion", "salience");
            return new IssueTemplate(
                map.Slug("id", required: true) ?? "",
                map.Text("title", required: true) ?? "",
                map.Text("for", required: true) ?? "",
                map.Text("against", required: true) ?? "",
                map.Number("opinion", required: true, -1, 1) ?? 0,
                map.Number("salience", required: true, 0, 5) ?? 0);
        }

        private BillTemplate ReadBill(YamlMappingNode root)
        {
            var map = new Map(this, root, "", "id", "title", "issue", "lean", "motion", "pass");
            var id = map.Slug("id", required: true) ?? "";
            var title = map.Text("title", required: true) ?? "";
            var issue = map.Slug("issue", required: true);
            if (issue is not null && context is not null && !context.IssueTemplates.Contains(issue))
                Error(map.KeyNode("issue")!, Texts.Get("templates.noIssueTemplate", ("issue", issue)));
            var lean = map.Lean("lean", required: true, allowNeutral: false) ?? Lean.For;
            var motion = map.Text("motion", required: true) ?? "";
            var pass = map.Child("pass", required: true, "text", "effects");
            return new BillTemplate(
                id, title, issue ?? "", lean, motion,
                pass?.Text("text", required: true) ?? "",
                pass?.Effects("effects", allowHere: false, allowCrisis: false) ?? []);
        }

        private ProjectTemplate ReadProject(YamlMappingNode root)
        {
            var map = new Map(this, root, "", "id", "title", "purse", "province", "price", "term", "effects", "bonus", "ministerial", "requires");
            var id = map.Slug("id", required: true) ?? "";
            var title = map.Text("title", required: true) ?? "";
            var purse = map.Ministry("purse", required: false) ?? Ministry.CommerceLabor;
            var province = map.Province("province", required: false);
            var price = map.Money("price", required: true) ?? 0;
            var term = map.Integer("term", required: true, 1) ?? 1;
            var effects = map.Effects("effects", allowHere: true, allowCrisis: false);
            var bonus = ReadBonus(map);
            var ministerial = map.Boolean("ministerial") ?? false;
            var requires = map.Slug("requires", required: false);
            if (requires is not null && context is not null && !context.Conditions.Contains(requires))
                Error(map.KeyNode("requires")!, Texts.Get("templates.unknownCondition", ("condition", requires)));

            if (province is null && !ministerial && effects.Concat(bonus?.Effects ?? []).Any(e => e.Target == EffectParser.Here))
                Error(map.KeyNode("effects") ?? map.KeyNode("bonus") ?? (YamlNode)root, Texts.Get("templates.hereNeedsProvince"));

            return new ProjectTemplate(id, title, purse, province, price, term, effects, bonus, ministerial, requires);
        }

        private ProjectBonus? ReadBonus(Map map)
        {
            var bonus = map.Child("bonus", required: false, "by", "within", "text", "effects");
            if (bonus is null) return null;
            Season? season = null;
            int? year = null;
            int? within = null;
            var by = bonus.Child("by", required: false, "season", "year");
            if (by is not null)
            {
                season = by.Season("season", required: true);
                year = by.Integer("year", required: true, int.MinValue);
            }
            if (bonus.Has("within")) within = bonus.Integer("within", required: true, 1);
            if (bonus.Has("by") == bonus.Has("within"))
                Error(bonus.Node, Texts.Get("templates.bonusDeadline"));
            return new ProjectBonus(
                season, year, within,
                bonus.Text("text", required: false) ?? "",
                bonus.Effects("effects", allowHere: true, allowCrisis: false));
        }

        private EventTemplate ReadEvent(YamlMappingNode root)
        {
            var map = new Map(this, root, "", "id", "title", "text", "effects", "schedule");
            var id = map.Slug("id", required: true) ?? "";
            var title = map.Text("title", required: true) ?? "";
            var text = map.Text("text", required: true) ?? "";
            var effects = map.Effects("effects", allowHere: false, allowCrisis: true);
            EventSchedule? schedule = null;
            var node = map.Child("schedule", required: false, "season", "year", "delay");
            if (node is not null)
            {
                var hasDate = node.Has("season") || node.Has("year");
                if (hasDate && node.Has("delay") || !hasDate && !node.Has("delay"))
                    Error(node.Node, Texts.Get("templates.scheduleShape"));
                if (node.Has("delay"))
                    schedule = new EventSchedule(null, null, node.Integer("delay", required: true, 0) ?? 0);
                else
                    schedule = new EventSchedule(node.Season("season", required: true), node.Integer("year", required: true, int.MinValue), null);
            }
            return new EventTemplate(id, title, text, effects, schedule);
        }

        private CrisisTemplate ReadCrisis(YamlMappingNode root)
        {
            var map = new Map(this, root, "", "id", "title", "ministry", "province", "issue", "answers", "complication", "default");
            var id = map.Slug("id", required: true) ?? "";
            var title = map.Text("title", required: true) ?? "";
            var ministry = map.Ministry("ministry", required: true) ?? Ministry.CommerceLabor;
            var province = map.Province("province", required: true) ?? "";
            var issue = map.Slug("issue", required: true);
            if (issue is not null && context is not null && !context.IssueTemplates.Contains(issue))
                Error(map.KeyNode("issue")!, Texts.Get("templates.noIssueTemplate", ("issue", issue)));

            var answers = new List<CrisisAnswer>();
            var answersNode = map.Value("answers");
            if (answersNode is YamlSequenceNode sequence)
            {
                var number = 0;
                foreach (var item in sequence.Children)
                {
                    number++;
                    if (item is not YamlMappingNode answerNode)
                    {
                        Error(item, Texts.Get("templates.answerNotMap", ("number", number)));
                        continue;
                    }
                    var answer = new Map(this, answerNode, Texts.Get("templates.answerPath", ("number", number)),"lean", "label", "price", "text", "effects");
                    answers.Add(new CrisisAnswer(
                        answer.Lean("lean", required: true, allowNeutral: true) ?? Lean.Neutral,
                        answer.Text("label", required: true) ?? "",
                        answer.Money("price", required: false) ?? 0,
                        answer.Text("text", required: true) ?? "",
                        answer.Effects("effects", allowHere: false, allowCrisis: false)));
                }
                if (sequence.Children.Count < 2)
                    Error(sequence, Texts.Get("templates.twoAnswers"));
            }
            else if (answersNode is not null)
            {
                Error(answersNode, Texts.Get("templates.answersList"));
            }

            var complication = ReadOutcome(map, "complication", required: false);
            var fallback = ReadOutcome(map, "default", required: true);
            return new CrisisTemplate(id, title, ministry, province, issue ?? "", answers, complication,
                fallback ?? new CrisisOutcomeText("", []));
        }

        private static CrisisOutcomeText? ReadOutcome(Map map, string name, bool required)
        {
            var node = map.Child(name, required, "text", "effects");
            return node is null
                ? null
                : new CrisisOutcomeText(node.Text("text", required: true) ?? "", node.Effects("effects", allowHere: false, allowCrisis: false));
        }

        private sealed class Map
        {
            private readonly Reader _reader;
            private readonly string _path;
            private readonly Dictionary<string, (YamlScalarNode Key, YamlNode Value)> _entries = new(StringComparer.Ordinal);

            public YamlMappingNode Node { get; }

            public Map(Reader reader, YamlMappingNode node, string path, params string[] allowed)
            {
                _reader = reader;
                _path = path;
                Node = node;
                foreach (var (key, value) in node.Children)
                {
                    if (key is not YamlScalarNode scalar || scalar.Value is null)
                    {
                        reader.Error(key, Texts.Get("templates.plainFieldNames", ("path", path)));
                        continue;
                    }
                    if (!allowed.Contains(scalar.Value))
                    {
                        reader.Error(key, Texts.Get("templates.unknownField", ("path", path), ("field", scalar.Value), ("allowed", QuotedList(allowed))));
                        continue;
                    }
                    _entries[scalar.Value] = (scalar, value);
                }
            }

            private static string QuotedList(IEnumerable<string> fields) =>
                string.Join(", ", fields.Select(a => Texts.Get("templates.quotedField", ("field", a))));

            private string Name(string field) => Texts.Get("templates.fieldName", ("path", _path), ("field", field));

            public bool Has(string field) => _entries.ContainsKey(field);

            public YamlNode? KeyNode(string field) => _entries.TryGetValue(field, out var entry) ? entry.Key : null;

            public YamlNode? Value(string field) => _entries.TryGetValue(field, out var entry) ? entry.Value : null;

            private string? Scalar(string field, bool required)
            {
                if (!_entries.TryGetValue(field, out var entry))
                {
                    if (required) _reader.Error(Node, Texts.Get("templates.required", ("field", Name(field))));
                    return null;
                }
                if (entry.Value is not YamlScalarNode scalar)
                {
                    _reader.Error(entry.Value, Texts.Get("templates.singleValue", ("field", Name(field))));
                    return null;
                }
                if (string.IsNullOrWhiteSpace(scalar.Value))
                {
                    if (required) _reader.Error(entry.Key, Texts.Get("templates.emptyField", ("field", Name(field))));
                    return null;
                }
                return scalar.Value;
            }

            private YamlNode Line(string field) => _entries[field].Value;

            public string? Text(string field, bool required) => Scalar(field, required);

            public string? Slug(string field, bool required)
            {
                var value = Scalar(field, required);
                if (value is null) return null;
                if (IsSlug(value)) return value;
                _reader.Error(Line(field), Texts.Get("templates.slug", ("field", Name(field)), ("value", value)));
                return null;
            }

            public string? Province(string field, bool required)
            {
                var value = Slug(field, required);
                if (value is not null && _reader.Context is { } context && !context.Provinces.Contains(value))
                    _reader.Error(Line(field), Texts.Get("templates.unknownProvince", ("field", Name(field)), ("value", value)));
                return value;
            }

            public double? Number(string field, bool required, double min, double max)
            {
                var value = Scalar(field, required);
                if (value is null) return null;
                if (!EffectParser.TryParseNumber(value, out var number))
                {
                    _reader.Error(Line(field), Texts.Get("templates.number", ("field", Name(field)), ("value", value)));
                    return null;
                }
                if (number < min || number > max)
                {
                    _reader.Error(Line(field), Texts.Get("templates.range", ("field", Name(field)),
                        ("min", EffectParser.FormatNumber(min)), ("max", EffectParser.FormatNumber(max)), ("value", value)));
                    return null;
                }
                return number;
            }

            public decimal? Money(string field, bool required)
            {
                var value = Scalar(field, required);
                if (value is null) return null;
                if (!decimal.TryParse(value.Replace('−', '-'), NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
                {
                    _reader.Error(Line(field), Texts.Get("templates.money", ("field", Name(field)), ("value", value)));
                    return null;
                }
                if (number < 0)
                {
                    _reader.Error(Line(field), Texts.Get("templates.negative", ("field", Name(field))));
                    return null;
                }
                return number;
            }

            public int? Integer(string field, bool required, int min)
            {
                var value = Scalar(field, required);
                if (value is null) return null;
                if (!int.TryParse(value.Replace('−', '-'), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                {
                    _reader.Error(Line(field), Texts.Get("templates.wholeNumber", ("field", Name(field)), ("value", value)));
                    return null;
                }
                if (number < min)
                {
                    _reader.Error(Line(field), Texts.Get("templates.atLeast", ("field", Name(field)), ("min", min)));
                    return null;
                }
                return number;
            }

            public bool? Boolean(string field)
            {
                var value = Scalar(field, required: false);
                if (value is null) return null;
                if (bool.TryParse(value, out var flag)) return flag;
                _reader.Error(Line(field), Texts.Get("templates.boolean", ("field", Name(field))));
                return null;
            }

            public Lean? Lean(string field, bool required, bool allowNeutral)
            {
                var value = Scalar(field, required);
                if (value is null) return null;
                if (Enum.TryParse<Lean>(value, ignoreCase: true, out var lean) && Enum.IsDefined(lean)
                    && !int.TryParse(value, out _) && (allowNeutral || lean != global::Lean.Neutral))
                    return lean;
                _reader.Error(Line(field), allowNeutral
                    ? Texts.Get("templates.leanWithNeutral", ("field", Name(field)), ("value", value))
                    : Texts.Get("templates.lean", ("field", Name(field)), ("value", value)));
                return null;
            }

            public Ministry? Ministry(string field, bool required)
            {
                var value = Scalar(field, required);
                if (value is null) return null;
                var match = PurseMinistries.Where(m => m.ToString().Equals(value, StringComparison.OrdinalIgnoreCase)).ToList();
                if (match.Count == 1) return match[0];
                _reader.Error(Line(field), value.Equals(nameof(global::Ministry.Navy), StringComparison.OrdinalIgnoreCase)
                    ? Texts.Get("templates.navyMinistry", ("field", Name(field)))
                    : Texts.Get("templates.ministry", ("field", Name(field)), ("value", value)));
                return null;
            }

            public Season? Season(string field, bool required)
            {
                var value = Scalar(field, required);
                if (value is null) return null;
                if (Enum.TryParse<Season>(value, ignoreCase: true, out var season) && Enum.IsDefined(season) && !int.TryParse(value, out _))
                    return season;
                _reader.Error(Line(field), Texts.Get("templates.season", ("field", Name(field)), ("value", value)));
                return null;
            }

            public Map? Child(string field, bool required, params string[] allowed)
            {
                if (!_entries.TryGetValue(field, out var entry))
                {
                    if (required) _reader.Error(Node, Texts.Get("templates.required", ("field", Name(field))));
                    return null;
                }
                if (entry.Value is not YamlMappingNode mapping)
                {
                    _reader.Error(entry.Value, Texts.Get("templates.childMap", ("field", Name(field)), ("allowed", QuotedList(allowed))));
                    return null;
                }
                return new Map(_reader, mapping, $"{_path}{field}.", allowed);
            }

            public IReadOnlyList<Effect> Effects(string field, bool allowHere, bool allowCrisis)
            {
                if (!_entries.TryGetValue(field, out var entry)) return [];
                if (entry.Value is YamlScalarNode { Value: null or "" }) return [];
                if (entry.Value is not YamlMappingNode mapping)
                {
                    _reader.Error(entry.Value, Texts.Get("templates.effectsMap", ("field", Name(field))));
                    return [];
                }

                var context = _reader.Context is { } full ? full with { AllowHere = allowHere, AllowCrisis = allowCrisis } : null;
                var effects = new List<Effect>();
                foreach (var (key, value) in mapping.Children)
                {
                    if (key is not YamlScalarNode { Value: { Length: > 0 } keyText })
                    {
                        _reader.Error(key, Texts.Get("templates.effectKeyPlain", ("field", Name(field))));
                        continue;
                    }
                    if (value is not YamlScalarNode scalar)
                    {
                        _reader.Error(value, Texts.Get("templates.effectSingleValue", ("field", Name(field)), ("key", keyText)));
                        continue;
                    }
                    var raw = string.IsNullOrWhiteSpace(scalar.Value) ? null : scalar.Value;
                    var parsed = EffectParser.FromPairs([(keyText, raw)], context, allowHere, allowCrisis);
                    foreach (var error in parsed.Errors)
                        _reader.Error(key, Texts.Get("templates.effectError", ("field", Name(field)), ("error", error)));
                    effects.AddRange(parsed.Effects);
                }
                return effects;
            }
        }

        public EffectContext? Context => context;
    }

    public static string Serialize(Template template)
    {
        var w = new Writer();
        switch (template)
        {
            case IssueTemplate issue:
                w.Field("", "id", issue.Id);
                w.Field("", "title", issue.Title);
                w.Field("", "for", issue.For);
                w.Field("", "against", issue.Against);
                w.Raw("", "opinion", Number(issue.Opinion));
                w.Raw("", "salience", Number(issue.Salience));
                break;
            case BillTemplate bill:
                w.Field("", "id", bill.Id);
                w.Field("", "title", bill.Title);
                w.Field("", "issue", bill.Issue);
                w.Raw("", "lean", bill.Lean.ToString());
                w.Field("", "motion", bill.Motion);
                w.Line("pass:");
                w.Field("  ", "text", bill.PassText);
                w.Effects("  ", bill.PassEffects);
                break;
            case ProjectTemplate project:
                w.Field("", "id", project.Id);
                w.Field("", "title", project.Title);
                if (project.Ministerial) w.Raw("", "ministerial", "true");
                w.Raw("", "purse", project.Purse.ToString());
                if (project.Province is not null) w.Field("", "province", project.Province);
                if (project.Requires is not null) w.Field("", "requires", project.Requires);
                w.Raw("", "price", Money(project.Price));
                w.Raw("", "term", project.Term.ToString(CultureInfo.InvariantCulture));
                w.Effects("", project.Effects);
                if (project.Bonus is { } bonus)
                {
                    w.Line("bonus:");
                    if (bonus.Within is { } within)
                        w.Raw("  ", "within", within.ToString(CultureInfo.InvariantCulture));
                    else
                        w.Raw("  ", "by", $"{{ season: {bonus.BySeason}, year: {bonus.ByYear?.ToString(CultureInfo.InvariantCulture)} }}");
                    if (bonus.Text.Length > 0) w.Field("  ", "text", bonus.Text);
                    w.Effects("  ", bonus.Effects);
                }
                break;
            case EventTemplate ev:
                w.Field("", "id", ev.Id);
                w.Field("", "title", ev.Title);
                w.Field("", "text", ev.Text);
                w.Effects("", ev.Effects);
                if (ev.Schedule is { } schedule)
                    w.Raw("", "schedule", schedule.Delay is { } delay
                        ? $"{{ delay: {delay.ToString(CultureInfo.InvariantCulture)} }}"
                        : $"{{ season: {schedule.Season}, year: {schedule.Year?.ToString(CultureInfo.InvariantCulture)} }}");
                break;
            case CrisisTemplate crisis:
                w.Field("", "id", crisis.Id);
                w.Field("", "title", crisis.Title);
                w.Raw("", "ministry", crisis.Ministry.ToString());
                w.Field("", "province", crisis.Province);
                w.Field("", "issue", crisis.Issue);
                w.Line("answers:");
                foreach (var answer in crisis.Answers)
                {
                    w.Raw("  - ", "lean", answer.Lean.ToString());
                    w.Field("    ", "label", answer.Label);
                    if (answer.Price != 0) w.Raw("    ", "price", Money(answer.Price));
                    w.Field("    ", "text", answer.Text);
                    w.Effects("    ", answer.Effects);
                }
                if (crisis.Complication is { } complication)
                {
                    w.Line("complication:");
                    w.Field("  ", "text", complication.Text);
                    w.Effects("  ", complication.Effects);
                }
                w.Line("default:");
                w.Field("  ", "text", crisis.Default.Text);
                w.Effects("  ", crisis.Default.Effects);
                break;
            default:
                throw new ArgumentException($"Unknown template type {template.GetType().Name}.", nameof(template));
        }
        return w.ToString();
    }

    private static string Number(double value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Money(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static string EffectEntry(Effect effect) =>
        $"{effect.Key}: {(effect.Value is { } value ? Number(value) : "true")}";

    private sealed class Writer
    {
        private const int Width = 100;
        private readonly StringBuilder _sb = new();

        public void Line(string text) => _sb.Append(text).Append('\n');

        public void Raw(string prefix, string key, string value) => Line($"{prefix}{key}: {value}");

        public void Field(string prefix, string key, string value)
        {
            var indent = new string(' ', prefix.Length);
            if (IsPlainSafe(value))
            {
                Line($"{prefix}{key}: {value}");
            }
            else if (IsLiteralSafe(value))
            {
                Line($"{prefix}{key}: |-");
                foreach (var line in value.Split('\n'))
                    Line(line.Length == 0 ? "" : $"{indent}  {line}");
            }
            else
            {
                Line($"{prefix}{key}: {Quote(value)}");
            }
        }

        public void Effects(string prefix, IReadOnlyList<Effect> effects)
        {
            if (effects.Count == 0) return;
            var head = $"{prefix}effects: {{ ";
            var continuation = new string(' ', head.Length);
            var current = head;
            var atStart = true;
            for (var i = 0; i < effects.Count; i++)
            {
                var entry = EffectEntry(effects[i]) + (i < effects.Count - 1 ? "," : " }");
                if (!atStart && current.Length + 1 + entry.Length > Width)
                {
                    Line(current);
                    current = continuation;
                    atStart = true;
                }
                current += atStart ? entry : " " + entry;
                atStart = false;
            }
            Line(current);
        }

        public override string ToString() => _sb.ToString();
    }

    private static bool IsPlainSafe(string value) =>
        value.Length > 0
        && (char.IsLetterOrDigit(value[0]) || value[0] == '(')
        && !char.IsWhiteSpace(value[^1])
        && !value.Any(IsSpecialChar)
        && !value.Contains(": ")
        && !value.EndsWith(':')
        && !value.Contains(" #");

    private static bool IsLiteralSafe(string value) =>
        value.Contains('\n')
        && !char.IsWhiteSpace(value[0])
        && !value.EndsWith('\n')
        && !value.Any(c => c != '\n' && IsSpecialChar(c))
        && !value.Split('\n').Any(line => line.Length > 0 && char.IsWhiteSpace(line[^1]));

    private static bool IsSpecialChar(char c) =>
        char.IsControl(c) || (int)c is 0xFEFF or 0x2028 or 0x2029 or 0x85;

    private static string Quote(string value)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in value)
        {
            sb.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ when IsSpecialChar(c) => $"\\u{(int)c:X4}",
                _ => c.ToString(),
            });
        }
        return sb.Append('"').ToString();
    }

    private sealed class AnyId : IReadOnlySet<string>
    {
        public static readonly AnyId Instance = new();

        public int Count => 0;
        public bool Contains(string item) => true;
        public IEnumerator<string> GetEnumerator() => Enumerable.Empty<string>().GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public bool IsProperSubsetOf(IEnumerable<string> other) => false;
        public bool IsProperSupersetOf(IEnumerable<string> other) => true;
        public bool IsSubsetOf(IEnumerable<string> other) => false;
        public bool IsSupersetOf(IEnumerable<string> other) => true;
        public bool Overlaps(IEnumerable<string> other) => other.Any();
        public bool SetEquals(IEnumerable<string> other) => false;
    }
}
