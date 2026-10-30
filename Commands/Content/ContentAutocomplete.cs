using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;

public class ContentAutocomplete : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        var typed = autocompleteInteraction.Data.Current.Value?.ToString() ?? "";
        var results = services.GetRequiredService<DatabaseService>().GetTemplates()
            .Where(t => t.Id.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Id.StartsWith(typed, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(t => t.Id, StringComparer.Ordinal)
            .Take(25)
            .Select(t => new AutocompleteResult(Display.Truncate($"{t.Id} · {TemplateCodec.KindName(t.Kind)} · {t.Status}", 100), t.Id));
        return Task.FromResult(AutocompletionResult.FromSuccess(results));
    }
}
