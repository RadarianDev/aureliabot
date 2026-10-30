using System.Globalization;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

public sealed class TextCatalog
{
    public const string MainFile = "texts.yaml";
    public const string PartPattern = "texts-*.yaml";

    public TextCatalog(IReadOnlyDictionary<string, string> entries)
    {
        Entries = entries;
    }

    public IReadOnlyDictionary<string, string> Entries { get; }

    public static TextCatalog Empty { get; } = new(new Dictionary<string, string>());

    public static TextCatalog Load(string directory)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        var files = new List<string>();
        var main = Path.Combine(directory, MainFile);
        if (File.Exists(main)) files.Add(main);
        if (Directory.Exists(directory))
            files.AddRange(Directory.EnumerateFiles(directory, PartPattern).OrderBy(f => f, StringComparer.Ordinal));

        foreach (var file in files)
        {
            var stream = new YamlStream();
            try
            {
                stream.Load(new StringReader(File.ReadAllText(file)));
            }
            catch (YamlException ex)
            {
                throw new ConfigException($"{Path.GetFileName(file)}, line {ex.Start.Line}: {(ex.InnerException ?? ex).Message}");
            }
            if (stream.Documents.Count == 0) continue;
            if (stream.Documents[0].RootNode is not YamlMappingNode root)
                throw new ConfigException($"{Path.GetFileName(file)} must be a map of sections.");
            Flatten(root, "", entries, Path.GetFileName(file));
        }
        return new TextCatalog(entries);
    }

    private static void Flatten(YamlMappingNode node, string prefix, Dictionary<string, string> entries, string file)
    {
        foreach (var (keyNode, value) in node.Children)
        {
            var key = prefix + ((YamlScalarNode)keyNode).Value;
            switch (value)
            {
                case YamlMappingNode child:
                    Flatten(child, key + ".", entries, file);
                    break;
                case YamlScalarNode scalar:
                    if (!entries.TryAdd(key, scalar.Value ?? ""))
                        throw new ConfigException($"{file}, line {keyNode.Start.Line}: the text `{key}` is defined twice.");
                    break;
                default:
                    throw new ConfigException($"{file}, line {keyNode.Start.Line}: `{key}` must be a text or a section.");
            }
        }
    }
}

public static partial class Texts
{
    private static TextCatalog? _current;

    public static TextCatalog Current
    {
        get
        {
            if (Volatile.Read(ref _current) is { } current) return current;
            var folder = Path.Combine(AppContext.BaseDirectory, "config");
            try
            {
                Use(TextCatalog.Load(folder));
            }
            catch (ConfigException ex)
            {
                Console.WriteLine($"[Texts] {ex.Message}");
                Use(TextCatalog.Empty);
            }
            return _current!;
        }
    }

    public static void Use(TextCatalog catalog) => Volatile.Write(ref _current, catalog);

    public static string Get(string key, params (string Name, object? Value)[] values)
    {
        if (!Current.Entries.TryGetValue(key, out var template))
        {
            Console.WriteLine($"[Texts] Missing text `{key}` in config/{TextCatalog.MainFile}.");
            return $"[{key}]";
        }
        if (values.Length == 0) return template;
        return Placeholder().Replace(template, match =>
        {
            var name = match.Groups[1].Value;
            foreach (var (n, v) in values)
                if (n == name)
                    return v switch
                    {
                        null => "",
                        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                        _ => v.ToString() ?? "",
                    };
            return match.Value;
        });
    }

    public static IEnumerable<string> PlaceholdersIn(string template) =>
        Placeholder().Matches(template).Select(m => m.Groups[1].Value).Distinct();

    [GeneratedRegex(@"\{([A-Za-z][A-Za-z0-9]*)\}")]
    private static partial Regex Placeholder();
}
