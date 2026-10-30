public sealed record Effect(string Key, double? Value)
{
    public static readonly IReadOnlySet<string> ActionPrefixes =
        new HashSet<string>(StringComparer.Ordinal) { "open", "close", "bill", "project", "crisis" };

    public string[] Parts => Key.Split('.');

    public string Prefix => Parts[0];

    public bool IsAction => ActionPrefixes.Contains(Prefix);

    public string? Target => Parts.Length > 1 ? Parts[1] : null;

    public string? SubTarget => Parts.Length > 2 ? Parts[2] : null;

    public double Amount => Value ?? 0;
}
