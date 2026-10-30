public sealed record EffectNames(
    Func<string, string> Province,
    Func<string, string> Issue,
    Func<string, string> Condition,
    Func<string, string> Faction,
    Func<string, string> IssueTemplate,
    Func<string, string> BillTemplate,
    Func<string, string> ProjectTemplate,
    Func<string, string> CrisisTemplate);

public static class EffectSummary
{
    public static IReadOnlyList<string> Lines(IReadOnlyList<Effect> effects, EffectNames names, DisplayConstants display) =>
        throw new NotImplementedException();
}
