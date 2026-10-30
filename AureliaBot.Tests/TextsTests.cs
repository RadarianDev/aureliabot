using System.Text.RegularExpressions;
using Xunit;

namespace AureliaBot.Tests;

public class TextsTests
{
    private static readonly Regex KeyUse = new(@"Texts\.Get\(\s*""([^""]+)""", RegexOptions.Compiled);

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "AureliaBot.csproj"))) return dir.FullName;
        throw new InvalidOperationException("Can't find the repository root.");
    }

    private static TextCatalog Catalog() => TextCatalog.Load(Path.Combine(RepoRoot(), "config"));

    private static IEnumerable<(string File, string Key)> KeysUsedInCode()
    {
        var root = RepoRoot();
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, file);
            var folders = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (relative.StartsWith("DiscordBot") || relative.StartsWith("AureliaBot.Tests") || folders.Contains("obj")
                || relative.StartsWith(".claude") || folders.Contains("bin")) continue;
            foreach (Match match in KeyUse.Matches(File.ReadAllText(file)))
                yield return (relative, match.Groups[1].Value);
        }
    }

    [Fact]
    public void EveryTextUsedInCode_ExistsInTheTextsFile()
    {
        var catalog = Catalog();
        var missing = KeysUsedInCode().Where(u => !catalog.Entries.ContainsKey(u.Key)).Select(u => $"{u.Key} ({u.File})").Distinct().ToList();
        Assert.True(missing.Count == 0, "Missing from config/texts.yaml:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void EveryTextInTheFile_IsUsed()
    {
        var used = KeysUsedInCode().Select(u => u.Key).ToHashSet();
        var unused = Catalog().Entries.Keys.Where(k => !used.Contains(k)).OrderBy(k => k).ToList();
        Assert.True(unused.Count == 0, "Unused texts in config/texts.yaml:\n" + string.Join("\n", unused));
    }

    [Fact]
    public void PlaceholdersAreFilled_AndUnknownOnesAreLeftVisible()
    {
        Texts.Use(new TextCatalog(new Dictionary<string, string> { ["a.b"] = "Hello {name}, you have {count} seats and {missing}." }));
        try
        {
            Assert.Equal("Hello Lord Halvard, you have 12 seats and {missing}.", Texts.Get("a.b", ("name", "Lord Halvard"), ("count", 12)));
            Assert.Equal("[no.such.key]", Texts.Get("no.such.key"));
        }
        finally
        {
            Texts.Use(Catalog());
        }
    }
}
