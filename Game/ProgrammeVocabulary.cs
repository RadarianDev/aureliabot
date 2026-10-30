using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

public enum ProgrammeWord { For, Against, StronglyFor, StronglyAgainst, Silent, Strength, Filler }

public sealed record ProgrammeVocabulary
{
    public const string File = "programme.yaml";

    public List<string> For { get; init; } =
    [
        "for", "support", "supports", "supported", "supporting", "supportive", "in support", "in support of",
        "in favour", "in favour of", "in favor", "in favor of", "favour", "favours", "favor", "favors",
        "pro", "yes", "yea", "aye", "approve", "approves", "endorse", "endorses", "back", "backs", "+", "+1",
    ];

    public List<string> Against { get; init; } =
    [
        "against", "oppose", "opposes", "opposed", "opposing", "opposed to", "in opposition", "in opposition to",
        "anti", "no", "nay", "reject", "rejects", "contra", "-", "-1",
    ];

    public List<string> StronglyFor { get; init; } = ["++", "+2"];

    public List<string> StronglyAgainst { get; init; } = ["--", "-2"];

    public List<string> Silent { get; init; } =
    [
        "silent", "silence", "non-aligned", "nonaligned", "non aligned", "unaligned", "neutral", "no stance",
        "no position", "no opinion", "no comment", "abstain", "abstains", "abstaining", "abstention", "undecided",
        "none", "n/a", "na", "0",
    ];

    public List<string> Strength { get; init; } =
    [
        "strongly", "strong", "very", "very much", "much", "firmly", "firm", "fiercely", "staunchly", "wholly",
        "fully", "totally", "completely", "absolutely", "resolutely", "vehemently", "deeply", "utterly",
    ];

    public List<string> Filler { get; init; } =
    [
        "we", "we are", "were", "are", "is", "am", "i", "our party", "the party", "party", "our stance", "stance",
        "our position", "position", "it", "this", "remain", "remains", "stand", "stands",
    ];

    public static ProgrammeVocabulary Default { get; } = new();

    private static ProgrammeVocabulary? _current;

    public static ProgrammeVocabulary Current => Volatile.Read(ref _current) ?? Default;

    public static void Use(ProgrammeVocabulary vocabulary) => Volatile.Write(ref _current, vocabulary);

    public static ProgrammeVocabulary Load(string directory)
    {
        var path = Path.Combine(directory, File);
        if (!System.IO.File.Exists(path)) return Default;

        ProgrammeVocabulary vocabulary;
        try
        {
            vocabulary = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build()
                .Deserialize<ProgrammeVocabulary?>(System.IO.File.ReadAllText(path)) ?? Default;
        }
        catch (YamlException ex)
        {
            throw new ConfigException($"{File}, line {ex.Start.Line}: {(ex.InnerException ?? ex).Message}");
        }

        var errors = vocabulary.Validate();
        if (errors.Count > 0)
            throw new ConfigException($"{File} is invalid:\n" + string.Join("\n", errors.Select(e => $"- {e}")));
        return vocabulary;
    }

    public List<string> Validate()
    {
        var errors = new List<string>();
        var seen = new Dictionary<string, ProgrammeWord>();
        foreach (var (word, phrases) in Lists())
            foreach (var phrase in phrases)
            {
                var key = Phrase(phrase);
                if (key.Length == 0)
                    errors.Add($"{Name(word)} has an empty phrase.");
                else if (!seen.TryAdd(key, word))
                    errors.Add($"'{phrase}' is in both {Name(seen[key])} and {Name(word)}.");
            }
        if (For.Count == 0 || Against.Count == 0) errors.Add("for and against each need at least one phrase.");
        return errors;
    }

    public IReadOnlyDictionary<string, ProgrammeWord> Phrases()
    {
        var phrases = new Dictionary<string, ProgrammeWord>();
        foreach (var (word, list) in Lists())
            foreach (var phrase in list)
                if (Phrase(phrase) is { Length: > 0 } key)
                    phrases.TryAdd(key, word);
        return phrases;
    }

    public static string[] Words(string text)
    {
        var chars = text.ToLowerInvariant()
            .Where(c => c is not ('\'' or '’' or '‘'))
            .Select(c => char.IsLetterOrDigit(c) || c is '+' or '-' or '/' ? c : ' ')
            .ToArray();
        return new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    public static string Phrase(string text) => string.Join(' ', Words(text));

    private IEnumerable<(ProgrammeWord Word, List<string> Phrases)> Lists() =>
    [
        (ProgrammeWord.For, For), (ProgrammeWord.Against, Against), (ProgrammeWord.StronglyFor, StronglyFor),
        (ProgrammeWord.StronglyAgainst, StronglyAgainst), (ProgrammeWord.Silent, Silent),
        (ProgrammeWord.Strength, Strength), (ProgrammeWord.Filler, Filler),
    ];

    private static string Name(ProgrammeWord word) => word switch
    {
        ProgrammeWord.StronglyFor => "stronglyFor",
        ProgrammeWord.StronglyAgainst => "stronglyAgainst",
        _ => word.ToString().ToLowerInvariant(),
    };
}
