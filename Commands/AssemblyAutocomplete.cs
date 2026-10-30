using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;

public class ProposableBillAutocomplete : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        var typed = autocompleteInteraction.Data.Current.Value?.ToString() ?? "";
        var data = services.GetRequiredService<DatabaseService>();
        var issues = data.GetOpenIssues().ToDictionary(i => i.Id, i => i.Title);
        var results = data.GetProposableBills()
            .Select(b => (Bill: b, Issue: issues.GetValueOrDefault(b.IssueId, b.IssueId)))
            .Where(x => x.Bill.Title.Contains(typed, StringComparison.OrdinalIgnoreCase)
                || x.Bill.Id.Contains(typed, StringComparison.OrdinalIgnoreCase)
                || x.Issue.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .Take(25)
            .Select(x => new AutocompleteResult(
                Display.Truncate(Texts.Get("bill.autocomplete", ("bill", x.Bill.Title), ("issue", x.Issue), ("lean", AssemblyPublisher.LeanName(x.Bill.Lean))), 100),
                x.Bill.Id));
        return Task.FromResult(AutocompletionResult.FromSuccess(results));
    }

    public static Bill? Find(DatabaseService data, string text)
    {
        var trimmed = text.Trim();
        var bills = data.GetBills();
        return bills.FirstOrDefault(b => b.Id == trimmed)
            ?? bills.FirstOrDefault(b => b.Title.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
    }
}

public class VoteSubjectAutocomplete : AutocompleteHandler
{
    private const string ReadingPrefix = "reading:";
    private const string MotionPrefix = "nc:";

    public static string ReadingValue(long readingId) => ReadingPrefix + readingId;

    public static string MotionValue(long motionId) => MotionPrefix + motionId;

    public static (VoteSubject Subject, long Id)? Parse(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith(ReadingPrefix, StringComparison.Ordinal) && long.TryParse(trimmed[ReadingPrefix.Length..], out var reading))
            return (VoteSubject.Reading, reading);
        if (trimmed.StartsWith(MotionPrefix, StringComparison.Ordinal) && long.TryParse(trimmed[MotionPrefix.Length..], out var motion))
            return (VoteSubject.NoConfidence, motion);
        return null;
    }

    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        var typed = autocompleteInteraction.Data.Current.Value?.ToString() ?? "";
        var data = services.GetRequiredService<DatabaseService>();
        var mine = data.GetVotes;
        var options = new List<(string Label, string Value)>();
        foreach (var reading in data.GetReadings(ReadingStatus.Open))
        {
            var bill = data.GetBill(reading.BillId);
            options.Add((Texts.Get("vote.autocomplete.reading", ("bill", bill?.Title ?? reading.BillId),
                ("lean", bill is null ? "" : AssemblyPublisher.LeanName(bill.Lean)),
                ("mine", MineLabel(mine(VoteSubject.Reading, reading.Id), context.User.Id))), ReadingValue(reading.Id)));
        }
        foreach (var motion in data.GetMotions(NoConfidenceStatus.Voting))
            options.Add((Texts.Get("vote.autocomplete.motion", ("tabler", data.GetFaction(motion.TablerId)?.CharacterName ?? motion.TablerId.ToString()),
                ("mine", MineLabel(mine(VoteSubject.NoConfidence, motion.Id), context.User.Id))), MotionValue(motion.Id)));

        var results = options
            .Where(o => o.Label.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .Take(25)
            .Select(o => new AutocompleteResult(Display.Truncate(o.Label, 100), o.Value));
        return Task.FromResult(AutocompletionResult.FromSuccess(results));
    }

    private static string MineLabel(IReadOnlyDictionary<ulong, VoteChoice> votes, ulong userId) =>
        votes.TryGetValue(userId, out var choice)
            ? Texts.Get("vote.autocomplete.voted", ("choice", AssemblyPublisher.ChoiceName(choice)))
            : Texts.Get("vote.autocomplete.notVoted");
}
