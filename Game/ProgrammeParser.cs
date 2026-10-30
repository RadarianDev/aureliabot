using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

public sealed record ProgrammeLineError(int LineNumber, string Text, string Reason);

public sealed record ProgrammeIgnoredLine(int LineNumber, string Text);

public sealed record ProgrammeParseResult(
    IReadOnlyDictionary<string, Stance> Stances,
    IReadOnlyList<ProgrammeLineError> Errors,
    IReadOnlyList<ProgrammeIgnoredLine> Ignored)
{
    public bool IsValid => Errors.Count == 0;
}

public sealed record MessageLink(ulong GuildId, ulong ChannelId, ulong MessageId);

public static partial class ProgrammeParser
{
    private const int MinPartialLength = 4;

    private static readonly string[] Connectors = ["on", "of", "to", "towards", "regarding", "about"];

    private enum Found { None, Open, Closed, Ambiguous }

    private sealed record IssueLookup(Found Found, Issue? Issue, IReadOnlyList<Issue> Candidates, string Text);

    private sealed record LineReading(IssueLookup Lookup, Stance? Stance, string OpinionText);

    public static ProgrammeParseResult Parse(
        string messageText, IReadOnlyList<Issue> openIssues, IReadOnlyList<Issue> allIssues, ProgrammeVocabulary? vocabulary = null)
    {
        var phrases = (vocabulary ?? ProgrammeVocabulary.Current).Phrases();
        var stances = new Dictionary<string, Stance>();
        var firstLine = new Dictionary<string, int>();
        var errors = new List<ProgrammeLineError>();
        var ignored = new List<ProgrammeIgnoredLine>();
        var lines = (messageText ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            var lineNumber = index + 1;
            var line = lines[index].Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var text = Clean(line);
            if (text.StartsWith('#') || !text.Any(char.IsLetterOrDigit)) continue;

            if (Read(text, openIssues, allIssues, phrases) is not { } reading)
            {
                ignored.Add(new ProgrammeIgnoredLine(lineNumber, line));
                continue;
            }

            var lookup = reading.Lookup;
            string? reason = lookup.Found switch
            {
                Found.Closed => Texts.Get("programme.closed", ("issue", lookup.Issue!.Title)),
                Found.Ambiguous => Texts.Get("programme.ambiguous", ("issue", lookup.Text),
                    ("candidates", string.Join(", ", lookup.Candidates.Select(i => i.Title)))),
                Found.None when lookup.Text.Length == 0 => Texts.Get("programme.noIssue", ("opinion", reading.OpinionText)),
                Found.None => Texts.Get("programme.unknownIssue", ("issue", lookup.Text)),
                _ => null,
            };
            if (reason is null && firstLine.TryGetValue(lookup.Issue!.Id, out var earlier))
                reason = Texts.Get("programme.duplicate", ("issue", lookup.Issue.Title), ("line", earlier));
            if (reason is null && reading.Stance is null)
                reason = reading.OpinionText.Length == 0
                    ? Texts.Get("programme.noOpinion", ("issue", lookup.Issue!.Title))
                    : Texts.Get("programme.unknownOpinion", ("opinion", reading.OpinionText));

            if (reason is not null)
            {
                errors.Add(new ProgrammeLineError(lineNumber, line, reason));
                continue;
            }

            firstLine[lookup.Issue!.Id] = lineNumber;
            if (reading.Stance is { } stance and not Stance.Silent) stances[lookup.Issue.Id] = stance;
        }

        return errors.Count == 0
            ? new ProgrammeParseResult(stances, errors, ignored)
            : new ProgrammeParseResult(new Dictionary<string, Stance>(), errors, ignored);
    }

    public static bool TryParseMessageLink(string text, out MessageLink link)
    {
        link = new MessageLink(0, 0, 0);
        var match = MessageLinkPattern().Match(text.Trim());
        if (!match.Success
            || !ulong.TryParse(match.Groups["guild"].Value, out var guild)
            || !ulong.TryParse(match.Groups["channel"].Value, out var channel)
            || !ulong.TryParse(match.Groups["message"].Value, out var message))
            return false;
        link = new MessageLink(guild, channel, message);
        return true;
    }

    public static Stance? ParseOpinion(string text, ProgrammeVocabulary? vocabulary = null) =>
        ParseOpinion(ProgrammeVocabulary.Words(text), (vocabulary ?? ProgrammeVocabulary.Current).Phrases());

    private static LineReading? Read(string text, IReadOnlyList<Issue> openIssues, IReadOnlyList<Issue> allIssues,
        IReadOnlyDictionary<string, ProgrammeWord> phrases)
    {
        var spans = Segments(text);
        var segments = spans.Select(s => TrimEdges(text[s.Start..s.End])).ToList();

        LineReading? stated = null;
        for (var o = segments.Count - 1; o >= 0; o--)
        {
            if (segments[o].Length == 0 || ParseOpinion(ProgrammeVocabulary.Words(segments[o]), phrases) is not { } stance) continue;

            var candidates = new List<string>();
            if (o > 0) candidates.Add(TrimEdges(text[..spans[o - 1].End]));
            if (o < segments.Count - 1) candidates.Add(TrimEdges(text[spans[o + 1].Start..]));
            if (o > 0) candidates.Add(segments[o - 1]);
            if (o < segments.Count - 1) candidates.Add(segments[o + 1]);

            var lookups = candidates.Where(c => c.Length > 0).Distinct().Select(c => Lookup(c, openIssues, allIssues)).ToList();
            var best = lookups.FirstOrDefault(l => l.Found == Found.Open) ?? lookups.FirstOrDefault(l => l.Found != Found.None);
            if (best is { Found: Found.Open }) return new LineReading(best, stance, segments[o]);
            stated ??= best is not null
                ? new LineReading(best, stance, segments[o])
                : new LineReading(new IssueLookup(Found.None, null, [], NeighbourOf(segments, o)), stance, segments[o]);
        }
        if (stated is { Lookup.Found: not Found.None }) return stated;

        foreach (var segment in segments.Where(s => s.Length > 0))
            if (ReadUnseparated(segment, openIssues, allIssues, phrases) is { } reading)
                return reading;
        if (stated is not null) return stated;

        var named = segments
            .Select((s, i) => (Index: i, Lookup: s.Length == 0 ? null : Lookup(s, openIssues, allIssues)))
            .FirstOrDefault(x => x.Lookup is { Found: Found.Open or Found.Closed });
        if (named.Lookup is null) return null;
        var opinionText = string.Join(" ", segments.Where((s, i) => i != named.Index && s.Length > 0));
        return new LineReading(named.Lookup, null, opinionText);
    }

    private static LineReading? ReadUnseparated(string segment, IReadOnlyList<Issue> openIssues, IReadOnlyList<Issue> allIssues,
        IReadOnlyDictionary<string, ProgrammeWord> phrases)
    {
        var words = segment.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        LineReading? fallback = null;
        for (var length = words.Length - 1; length >= 1; length--)
        {
            foreach (var (opinion, rest, leading) in new[] { (words[..length], words[length..], true), (words[^length..], words[..^length], false) })
            {
                if (ParseOpinion(ProgrammeVocabulary.Words(string.Join(" ", opinion)), phrases) is not { } stance) continue;
                var issueWords = leading ? rest.SkipWhile(w => Connectors.Contains(w, StringComparer.OrdinalIgnoreCase)).ToArray() : rest;
                if (issueWords.Length == 0) continue;
                var lookup = Lookup(string.Join(" ", issueWords), openIssues, allIssues);
                if (lookup.Found == Found.Open) return new LineReading(lookup, stance, string.Join(" ", opinion));
                if (lookup.Found is Found.Closed or Found.Ambiguous) fallback ??= new LineReading(lookup, stance, string.Join(" ", opinion));
            }
        }
        return fallback;
    }

    private static Stance? ParseOpinion(string[] words, IReadOnlyDictionary<string, ProgrammeWord> phrases)
    {
        var position = 0;
        var strong = false;
        var maxLength = phrases.Keys.Max(k => k.Count(c => c == ' ') + 1);

        ProgrammeWord? Next(Func<ProgrammeWord, bool> accept, bool consume)
        {
            for (var length = Math.Min(maxLength, words.Length - position); length >= 1; length--)
                if (phrases.TryGetValue(string.Join(' ', words, position, length), out var word) && accept(word))
                {
                    if (consume) position += length;
                    return word;
                }
            return null;
        }

        static bool IsCore(ProgrammeWord w) => w is not (ProgrammeWord.Filler or ProgrammeWord.Strength);

        while (Next(IsCore, consume: false) is null && Next(w => w == ProgrammeWord.Filler, consume: true) is not null) { }
        while (Next(IsCore, consume: false) is null && Next(w => w == ProgrammeWord.Strength, consume: true) is not null) strong = true;
        if (Next(IsCore, consume: true) is not { } core) return null;
        while (Next(w => w == ProgrammeWord.Strength, consume: true) is not null) strong = true;
        while (Next(w => w == ProgrammeWord.Filler, consume: true) is not null) { }
        if (position != words.Length) return null;

        return core switch
        {
            ProgrammeWord.For => strong ? Stance.StronglyFor : Stance.For,
            ProgrammeWord.Against => strong ? Stance.StronglyAgainst : Stance.Against,
            ProgrammeWord.StronglyFor => Stance.StronglyFor,
            ProgrammeWord.StronglyAgainst => Stance.StronglyAgainst,
            ProgrammeWord.Silent when !strong => Stance.Silent,
            _ => null,
        };
    }

    private static IssueLookup Lookup(string text, IReadOnlyList<Issue> openIssues, IReadOnlyList<Issue> allIssues)
    {
        var key = IssueKey(text);
        if (key.Length == 0) return new IssueLookup(Found.None, null, [], text);

        if (openIssues.FirstOrDefault(i => IssueKey(i.Title) == key || IssueKey(i.Id) == key) is { } open)
            return new IssueLookup(Found.Open, open, [], text);
        if (allIssues.FirstOrDefault(i => IssueKey(i.Title) == key || IssueKey(i.Id) == key) is { } closed)
            return new IssueLookup(Found.Closed, closed, [], text);
        if (key.Length < MinPartialLength) return new IssueLookup(Found.None, null, [], text);

        var partial = openIssues.Where(i => Contains(i, key)).ToList();
        if (partial.Count == 1) return new IssueLookup(Found.Open, partial[0], [], text);
        if (partial.Count > 1) return new IssueLookup(Found.Ambiguous, null, partial, text);

        var closedPartial = allIssues.Where(i => Contains(i, key)).ToList();
        return closedPartial.Count switch
        {
            1 => new IssueLookup(Found.Closed, closedPartial[0], [], text),
            > 1 => new IssueLookup(Found.Ambiguous, null, closedPartial, text),
            _ => new IssueLookup(Found.None, null, [], text),
        };
    }

    private static bool Contains(Issue issue, string key) =>
        $" {IssueKey(issue.Title)} ".Contains($" {key} ") || $" {IssueKey(issue.Id)} ".Contains($" {key} ");

    private static string IssueKey(string text)
    {
        var chars = text.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray();
        var key = string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return key.StartsWith("the ") ? key[4..] : key;
    }

    private static string NeighbourOf(List<string> segments, int index) =>
        (index > 0 ? segments[index - 1] : "") is { Length: > 0 } before ? before
        : index < segments.Count - 1 ? segments[index + 1] : "";

    private static List<(int Start, int End)> Segments(string text)
    {
        var spans = new List<(int Start, int End)>();
        var position = 0;
        foreach (Match separator in Separator().Matches(text))
        {
            spans.Add((position, separator.Index));
            position = separator.Index + separator.Length;
        }
        spans.Add((position, text.Length));
        return spans;
    }

    private static string TrimEdges(string text) => text.Trim().Trim('.', ',', ';', '!', '?', ' ', '(', ')', '[', ']');

    private static string Clean(string line)
    {
        var text = DiscordToken().Replace(line, " ");
        text = WithoutEmoji(text).Replace("\\", "").Replace("||", " ").Replace('\t', '|');
        text = Quotes().Replace(text, " ");
        text = LeadingMarkers().Replace(text.Trim(), "");
        text = Emphasis().Replace(text, " ");
        text = LeadingMarkers().Replace(text.Trim(), "");
        return Whitespace().Replace(text, " ").Trim();
    }

    private static string WithoutEmoji(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            var category = Rune.GetUnicodeCategory(rune);
            var isEmoji = rune.Value > 0x7F && category is UnicodeCategory.OtherSymbol or UnicodeCategory.ModifierSymbol
                || rune.Value is 0xFE0E or 0xFE0F or 0x200D or 0x20E3;
            builder.Append(isEmoji ? " " : rune.ToString());
        }
        return builder.ToString();
    }

    [GeneratedRegex(@"<a?:\w+:\d+>|<(?:@[!&]?|#)\d+>|<t:-?\d+(?::\w)?>")]
    private static partial Regex DiscordToken();

    [GeneratedRegex("[\"“”„«»]")]
    private static partial Regex Quotes();

    [GeneratedRegex(@"^(?:\s*(?:>+|-#|[-*+•·◦‣–—](?=\s)|[•·◦‣]|\d{1,3}[.)](?!\d)))+\s*")]
    private static partial Regex LeadingMarkers();

    [GeneratedRegex(@"\*+|_+|~~|`+")]
    private static partial Regex Emphasis();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"(?:\s*(?:-->|->|=>|→|⟶|⇒|=|\||:|\s--?\s|[–—])\s*)+")]
    private static partial Regex Separator();

    [GeneratedRegex(@"^<?https?://(?:(?:ptb|canary)\.)?discord(?:app)?\.com/channels/(?<guild>\d+)/(?<channel>\d+)/(?<message>\d+)/?>?$", RegexOptions.IgnoreCase)]
    private static partial Regex MessageLinkPattern();
}
