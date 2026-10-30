using Discord;
using Discord.Interactions;

public class PartyAutocomplete : AutocompleteHandler
{
    private readonly DatabaseService _data;

    public PartyAutocomplete(DatabaseService data)
    {
        _data = data;
    }

    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context, IAutocompleteInteraction autocompleteInteraction,
        IParameterInfo parameter, IServiceProvider services)
    {
        var typed = autocompleteInteraction.Data.Current.Value?.ToString() ?? "";
        var suggestions = _data.GetAllParties()
            .Where(p => p.Name.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Take(25)
            .Select(p => new AutocompleteResult(p.Name, p.Id));

        return Task.FromResult(AutocompletionResult.FromSuccess(suggestions));
    }
}
