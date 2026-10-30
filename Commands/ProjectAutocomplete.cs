using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;

public class ProjectAutocomplete : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        var typed = autocompleteInteraction.Data.Current.Value?.ToString() ?? "";
        var config = services.GetRequiredService<ConstantsStore>().Config;
        var results = services.GetRequiredService<DatabaseService>().GetLiveProjects()
            .Where(p => p.Title.Contains(typed, StringComparison.OrdinalIgnoreCase) || p.TemplateId.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .Take(25)
            .Select(p => new AutocompleteResult(Display.Truncate(Texts.Get("projects.autocomplete.live",
                ("project", p.Title), ("province", Display.Province(config, p.ProvinceId)),
                ("progress", Display.Crowns(p.Progress)), ("price", Display.Crowns(p.Price))), 100), p.Id.ToString()));
        return Task.FromResult(AutocompletionResult.FromSuccess(results));
    }

    public static LiveProject? Find(DatabaseService data, string text)
    {
        var trimmed = text.Trim();
        var live = data.GetLiveProjects();
        return (long.TryParse(trimmed, out var id) ? live.FirstOrDefault(p => p.Id == id) : null)
            ?? live.FirstOrDefault(p => p.TemplateId == trimmed)
            ?? live.FirstOrDefault(p => p.Title.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
    }
}

public class MinisterialProjectAutocomplete : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        var typed = autocompleteInteraction.Data.Current.Value?.ToString() ?? "";
        var results = services.GetRequiredService<DatabaseService>().GetMinisterialProjectTemplates()
            .Where(t => t.Title.Contains(typed, StringComparison.OrdinalIgnoreCase) || t.Id.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .Take(25)
            .Select(t => new AutocompleteResult(Display.Truncate(Texts.Get("projects.autocomplete.template",
                ("project", t.Title), ("ministry", t.Purse.DisplayName()), ("price", Display.Crowns(t.Price)), ("term", t.Term)), 100), t.Id));
        return Task.FromResult(AutocompletionResult.FromSuccess(results));
    }

    public static ProjectTemplate? Find(DatabaseService data, string text)
    {
        var trimmed = text.Trim();
        var templates = data.GetMinisterialProjectTemplates();
        return templates.FirstOrDefault(t => t.Id == trimmed)
            ?? templates.FirstOrDefault(t => t.Title.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
    }
}
