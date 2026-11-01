using System.Text;
using System.Text.RegularExpressions;

public sealed record YamlPart(string Key, string Body, string Trailing, int FirstLine)
{
    public string Text => Body + Trailing;
}

public sealed record YamlPage(int FirstLine, int LastLine, string Text);

public static partial class YamlParts
{
    public const string PreambleKey = "";

    public static List<YamlPart> Split(string yaml)
    {
        var lines = Lines(yaml);
        var starts = new List<int>();
        for (var i = 0; i < lines.Count; i++)
            if (KeyOf(lines[i]) is not null) starts.Add(OwnCommentStart(lines, i, starts.LastOrDefault(-1)));

        var parts = new List<YamlPart>();
        if (starts.Count == 0 || starts[0] > 0)
            parts.Add(Part(PreambleKey, lines, 0, starts.Count == 0 ? lines.Count : starts[0]));
        for (var p = 0; p < starts.Count; p++)
        {
            var end = p + 1 < starts.Count ? starts[p + 1] : lines.Count;
            var key = lines.Skip(starts[p]).Select(KeyOf).First(k => k is not null)!;
            parts.Add(Part(key, lines, starts[p], end));
        }
        return parts.Where(p => p.Text.Length > 0 || parts.Count == 1).ToList();
    }

    public static string Join(IEnumerable<YamlPart> parts) => string.Concat(parts.Select(p => p.Text));

    public static string? Splice(string yaml, string key, string body)
    {
        var parts = Split(yaml);
        var index = parts.FindIndex(p => p.Key == key);
        if (index < 0) return null;
        var old = parts[index];
        var last = index == parts.Count - 1;
        var trimmed = body.TrimEnd('\r', '\n');
        var ending = old.Body.EndsWith('\n') || !last ? NewLineOf(yaml) : "";
        parts[index] = old with { Body = trimmed.Length == 0 ? "" : trimmed + ending };
        return Join(parts);
    }

    public static List<string>? Group(string yaml, int maxChars, int maxGroups)
    {
        if (yaml.Length <= maxChars) return [yaml];
        var groups = new List<string>();
        var current = new StringBuilder();
        foreach (var part in Split(yaml))
        {
            if (part.Text.Length > maxChars) return null;
            if (current.Length + part.Text.Length > maxChars)
            {
                groups.Add(current.ToString());
                current.Clear();
            }
            current.Append(part.Text);
        }
        if (current.Length > 0) groups.Add(current.ToString());
        return groups.Count <= maxGroups ? groups : null;
    }

    public static string JoinGroups(IEnumerable<string> groups)
    {
        var sb = new StringBuilder();
        foreach (var group in groups.Where(g => g.Length > 0))
        {
            if (sb.Length > 0 && sb[^1] != '\n') sb.Append('\n');
            sb.Append(group);
        }
        return sb.ToString();
    }

    public static List<YamlPage> Pages(string yaml, int budget)
    {
        var lines = Lines(yaml).Select(l => l.TrimEnd('\r', '\n')).ToList();
        var pages = new List<YamlPage>();
        var sb = new StringBuilder();
        var first = 1;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Length > budget ? lines[i][..(budget - 1)] + "…" : lines[i];
            if (sb.Length > 0 && sb.Length + line.Length + 1 > budget)
            {
                pages.Add(new YamlPage(first, i, sb.ToString()));
                sb.Clear();
                first = i + 1;
            }
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(line);
        }
        pages.Add(new YamlPage(first, Math.Max(first, lines.Count), sb.ToString()));
        return pages;
    }

    public static string? KeyAtLine(string yaml, int line) =>
        Split(yaml).LastOrDefault(p => p.FirstLine <= line && p.Key != PreambleKey)?.Key;

    public static int? LineOf(string yaml, string word)
    {
        var pattern = new Regex($@"(?<![\w.]){Regex.Escape(word)}(?!\w)");
        var lines = Lines(yaml);
        for (var i = 0; i < lines.Count; i++)
            if (KeyOf(lines[i]) != "id" && pattern.IsMatch(lines[i])) return i + 1;
        return null;
    }

    public static string Excerpt(string yaml, int line)
    {
        var lines = Lines(yaml).Select(l => l.TrimEnd('\r', '\n')).ToList();
        var sb = new StringBuilder();
        for (var i = Math.Max(1, line - 1); i <= Math.Min(lines.Count, line + 1); i++)
            sb.Append(i == line ? "- " : "  ").Append(lines[i - 1]).Append('\n');
        return sb.ToString().TrimEnd('\n');
    }

    public static List<string> Diff(string before, string after)
    {
        var a = Lines(before).Select(l => l.TrimEnd('\r', '\n')).ToList();
        var b = Lines(after).Select(l => l.TrimEnd('\r', '\n')).ToList();
        var common = new int[a.Count + 1, b.Count + 1];
        for (var i = a.Count - 1; i >= 0; i--)
            for (var j = b.Count - 1; j >= 0; j--)
                common[i, j] = a[i] == b[j] ? common[i + 1, j + 1] + 1 : Math.Max(common[i + 1, j], common[i, j + 1]);

        var diff = new List<string>();
        int x = 0, y = 0;
        while (x < a.Count || y < b.Count)
        {
            if (x < a.Count && y < b.Count && a[x] == b[y]) { x++; y++; }
            else if (x < a.Count && (y == b.Count || common[x + 1, y] >= common[x, y + 1])) diff.Add("- " + a[x++]);
            else diff.Add("+ " + b[y++]);
        }
        return diff;
    }

    public static string SetId(string yaml, string id) =>
        IdLine().IsMatch(yaml) ? IdLine().Replace(yaml, $"id: {id}", 1) : $"id: {id}{NewLineOf(yaml)}{yaml}";

    private static YamlPart Part(string key, List<string> lines, int start, int end)
    {
        var bodyEnd = end;
        while (bodyEnd > start + 1 && IsBlankOrComment(lines[bodyEnd - 1]) && key != PreambleKey) bodyEnd--;
        return new YamlPart(key,
            string.Concat(lines.Skip(start).Take(bodyEnd - start)),
            string.Concat(lines.Skip(bodyEnd).Take(end - bodyEnd)),
            start + 1);
    }

    private static int OwnCommentStart(List<string> lines, int keyLine, int previousStart)
    {
        var start = keyLine;
        while (start - 1 > previousStart && lines[start - 1].TrimStart().StartsWith('#') && !char.IsWhiteSpace(lines[start - 1][0]))
            start--;
        return start;
    }

    private static bool IsBlankOrComment(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length == 0 || trimmed.StartsWith('#');
    }

    private static string? KeyOf(string line)
    {
        var match = TopLevelKey().Match(line);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static List<string> Lines(string text)
    {
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') continue;
            lines.Add(text[start..(i + 1)]);
            start = i + 1;
        }
        if (start < text.Length) lines.Add(text[start..]);
        return lines;
    }

    private static string NewLineOf(string yaml) => yaml.Contains("\r\n") ? "\r\n" : "\n";

    [GeneratedRegex(@"^([A-Za-z_][\w.-]*)\s*:(\s|$)")]
    private static partial Regex TopLevelKey();

    [GeneratedRegex(@"(?m)^id:[^\r\n]*")]
    private static partial Regex IdLine();
}
