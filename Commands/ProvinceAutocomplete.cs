using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;

public class ProvinceAutocomplete : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        var typed = autocompleteInteraction.Data.Current.Value?.ToString() ?? "";
        var config = services.GetRequiredService<ConstantsStore>().Config;
        var results = services.GetRequiredService<DatabaseService>().ProvincesInPlay(config)
            .Where(p => p.Name.Contains(typed, StringComparison.OrdinalIgnoreCase)
                || p.Id.Contains(typed, StringComparison.OrdinalIgnoreCase)
                || p.Description.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .Take(25)
            .Select(p => new AutocompleteResult(Display.Truncate($"{p.Name}: {p.Description}", 100), p.Id));
        return Task.FromResult(AutocompletionResult.FromSuccess(results));
    }

    public static ProvinceDefinition? Find(DatabaseService data, GameConfig config, string text)
    {
        var provinces = data.ProvincesInPlay(config);
        var trimmed = text.Trim();
        return provinces.FirstOrDefault(p => p.Id == trimmed)
            ?? provinces.FirstOrDefault(p => p.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
    }
}
