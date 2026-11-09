public static class Briefing
{
    public static IReadOnlyList<string> ForEffects(DatabaseService data, IReadOnlyList<Effect> effects)
    {
        var briefings = new List<string>();
        foreach (var effect in EffectTree.Plain(effects))
        {
            switch (effect.Kind)
            {
                case OpenIssueEffect when data.GetIssue(effect.Target!) is { } issue:
                    briefings.Add(NewIssue(data, issue));
                    break;
                case BillEffect when data.GetBill(effect.Target!) is { } bill:
                    briefings.Add(NewBill(data, bill));
                    break;
            }
        }
        return briefings;
    }

    public static IEnumerable<string> ForCrisis(DatabaseService data, Crisis crisis)
    {
        if (data.SummaryOf(crisis.TemplateId) is { } summary) yield return Texts.Get("briefing.summary", ("text", summary));
        if (data.GetIssue(crisis.IssueId) is not { } issue) yield break;
        yield return Texts.Get("briefing.forMeans", ("text", issue.ForText));
        yield return Texts.Get("briefing.againstMeans", ("text", issue.AgainstText));
    }

    private static string NewIssue(DatabaseService data, Issue issue)
    {
        var lines = new List<string> { Texts.Get("briefing.newIssue", ("issue", issue.Title)) };
        if (data.SummaryOf(issue.TemplateId) is { } summary) lines.Add(summary);
        lines.Add(Texts.Get("briefing.forMeans", ("text", issue.ForText)));
        lines.Add(Texts.Get("briefing.againstMeans", ("text", issue.AgainstText)));
        return string.Join("\n", lines);
    }

    private static string NewBill(DatabaseService data, Bill bill) =>
        Texts.Get("briefing.newBill", ("bill", bill.Title), ("issue", data.GetIssue(bill.IssueId)?.Title ?? bill.IssueId),
            ("lean", AssemblyPublisher.LeanName(bill.Lean))) + "\n" + (data.SummaryOf(bill.TemplateId) ?? bill.Motion);
}
