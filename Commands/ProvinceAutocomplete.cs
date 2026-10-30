using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;

public class ProvinceAutocomplete : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        var typed = autocompleteInteraction.Data.Current.Value?.ToString() ?? "";
        var results = services.GetRequiredService<ConstantsStore>().Config.Provinces
            .Where(p => p.Name.Contains(typed, StringComparison.OrdinalIgnoreCase)
                || p.Id.Contains(typed, StringComparison.OrdinalIgnoreCase)
                || p.Description.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .Take(25)
            .Select(p => new AutocompleteResult(Display.Truncate($"{p.Name}: {p.Description}", 100), p.Id));
        return Task.FromResult(AutocompletionResult.FromSuccess(results));
    }

    public static ProvinceDefinition? Find(GameConfig config, string text) =>
        config.Province(text.Trim())
        ?? config.Provinces.FirstOrDefault(p => p.Name.Equals(text.Trim(), StringComparison.OrdinalIgnoreCase));
}
