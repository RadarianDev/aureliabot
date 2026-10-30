using System.Text.RegularExpressions;

public sealed record ProgrammeLineError(int LineNumber, string Text, string Reason);

public sealed record ProgrammeParseResult(
    IReadOnlyDictionary<string, Stance> Stances,
    IReadOnlyList<ProgrammeLineError> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

public sealed record MessageLink(ulong GuildId, ulong ChannelId, ulong MessageId);

public static partial class ProgrammeParser
{
    private static readonly char[] Emphasis = [' ', '\t', '*', '_'];

    private static readonly Dictionary<string, Stance> Opinions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["strongly for"] = Stance.StronglyFor,
        ["for"] = Stance.For,
        ["against"] = Stance.Against,
        ["strongly against"] = Stance.StronglyAgainst,
    };

    public static ProgrammeParseResult Parse(string messageText, IReadOnlyList<Issue> openIssues, IReadOnlyList<Issue> allIssues)
    {
        var stances = new Dictionary<string, Stance>();
        var firstLine = new Dictionary<string, int>();
        var errors = new List<ProgrammeLineError>();
        var lines = (messageText ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            var lineNumber = index + 1;
            var raw = lines[index];
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var colon = line.LastIndexOf(':');
            if (colon < 0)
            {
                errors.Add(new ProgrammeLineError(lineNumber, line, Texts.Get("programme.noColon")));
                continue;
            }

            var issueText = Clean(line[..colon]);
            var opinionText = Clean(line[(colon + 1)..]);

            var issue = Match(openIssues, issueText);
            string? issueError = null;
            if (issue is null)
                issueError = Match(allIssues, issueText) is { } closed
                    ? Texts.Get("programme.closed", ("issue", closed.Title))
                    : Texts.Get("programme.unknownIssue", ("issue", issueText));
            else if (firstLine.TryGetValue(issue.Id, out var earlier))
                issueError = Texts.Get("programme.duplicate", ("issue", issue.Title), ("line", earlier));

            string? opinionError = null;
            Stance stance = Stance.Silent;
            if (opinionText.Equals("silent", StringComparison.OrdinalIgnoreCase))
                opinionError = Texts.Get("programme.silent");
            else if (!Opinions.TryGetValue(opinionText, out stance))
                opinionError = Texts.Get("programme.unknownOpinion", ("opinion", opinionText));

            if (issueError is not null || opinionError is not null)
            {
                var reason = string.Join("; ", new[] { issueError, opinionError }.Where(e => e is not null));
                errors.Add(new ProgrammeLineError(lineNumber, line, reason));
                continue;
            }

            firstLine[issue!.Id] = lineNumber;
            stances[issue.Id] = stance;
        }

        return errors.Count == 0
            ? new ProgrammeParseResult(stances, errors)
            : new ProgrammeParseResult(new Dictionary<string, Stance>(), errors);
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

    private static string Clean(string text) =>
        Regex.Replace(text.Trim(Emphasis), @"\s+", " ");

    private static Issue? Match(IReadOnlyList<Issue> issues, string text) =>
        issues.FirstOrDefault(i => string.Equals(i.Title.Trim(), text, StringComparison.OrdinalIgnoreCase))
        ?? issues.FirstOrDefault(i => string.Equals(i.Id, text, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"^<?https?://(?:(?:ptb|canary)\.)?discord(?:app)?\.com/channels/(?<guild>\d+)/(?<channel>\d+)/(?<message>\d+)/?>?$", RegexOptions.IgnoreCase)]
    private static partial Regex MessageLinkPattern();
}
