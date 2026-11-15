using Discord;
using Discord.Interactions;

public class AgendaAutocomplete : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        var constants = (ConstantsStore)services.GetService(typeof(ConstantsStore))!;
        var typed = autocompleteInteraction.Data.Current.Value?.ToString() ?? "";
        var results = constants.Config.Agendas.Agendas
            .Where(a => a.Name.Contains(typed, StringComparison.OrdinalIgnoreCase) || a.Id.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .Take(25)
            .Select(a => new AutocompleteResult(Display.Truncate($"{a.Name} · {AgendaWording.Wishes(a)}", 100), a.Id));
        return Task.FromResult(AutocompletionResult.FromSuccess(results));
    }
}
