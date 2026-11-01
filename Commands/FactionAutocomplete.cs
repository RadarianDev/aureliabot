using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;

public class FactionAutocomplete : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        var typed = autocompleteInteraction.Data.Current.Value?.ToString() ?? "";
        var guild = context.Guild as SocketGuild;
        var results = Candidates(services.GetRequiredService<DatabaseService>())
            .Select(f => (Faction: f, Label: Label(f, guild?.GetUser(f.UserId)?.Username)))
            .Where(x => x.Label.Contains(typed, StringComparison.OrdinalIgnoreCase)
                || x.Faction.UserId.ToString().StartsWith(typed, StringComparison.Ordinal))
            .OrderBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
            .Take(25)
            .Select(x => new AutocompleteResult(Display.Truncate(x.Label, 100), x.Faction.UserId.ToString()));
        return Task.FromResult(AutocompletionResult.FromSuccess(results));
    }

    protected virtual IEnumerable<Faction> Candidates(DatabaseService data) => data.GetActiveFactions();

    private static string Label(Faction faction, string? username)
    {
        var who = username ?? faction.UserId.ToString();
        return string.IsNullOrWhiteSpace(faction.CharacterName) ? who : $"{faction.CharacterName} ({who})";
    }

    public static Faction? Find(DatabaseService data, string text)
    {
        var trimmed = text.Trim().TrimStart('<', '@', '!').TrimEnd('>');
        return ulong.TryParse(trimmed, out var id) ? data.GetFaction(id) : null;
    }
}

public class RetiredFactionAutocomplete : FactionAutocomplete
{
    protected override IEnumerable<Faction> Candidates(DatabaseService data) => data.GetAllFactions().Where(f => !f.IsActive);
}
