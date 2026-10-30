using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;

public abstract class UnitAutocomplete : AutocompleteHandler
{
    protected abstract UnitType Type { get; }

    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        var typed = autocompleteInteraction.Data.Current.Value?.ToString() ?? "";
        var units = services.GetRequiredService<ConstantsStore>().Current.UnitsOf(Type);
        var results = services.GetRequiredService<DatabaseService>().GetUnits(Type)
            .Select(u => (Unit: u, Label: Label(u, units)))
            .Where(x => x.Label.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .Take(25)
            .Select(x => new AutocompleteResult(Display.Truncate(x.Label, 100), x.Unit.Id.ToString()));
        return Task.FromResult(AutocompletionResult.FromSuccess(results));
    }

    public static string Label(Unit unit, UnitConstants units) =>
        units.NextTrainCost(unit.Sides) is { } cost
            ? Texts.Get("forces.autocomplete.unit", ("name", unit.Name), ("sides", unit.Sides), ("cost", Display.Crowns(cost)))
            : Texts.Get("forces.autocomplete.unitMax", ("name", unit.Name), ("sides", unit.Sides));

    public static Unit? Find(DatabaseService data, UnitType type, string text)
    {
        var trimmed = text.Trim();
        var units = data.GetUnits(type);
        return long.TryParse(trimmed, out var id) && units.FirstOrDefault(u => u.Id == id) is { } byId
            ? byId
            : units.FirstOrDefault(u => u.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class DivisionAutocomplete : UnitAutocomplete
{
    protected override UnitType Type => UnitType.Division;
}

public sealed class GendarmerieAutocomplete : UnitAutocomplete
{
    protected override UnitType Type => UnitType.Gendarmerie;
}

public sealed class CrackdownProvinceAutocomplete : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        var typed = autocompleteInteraction.Data.Current.Value?.ToString() ?? "";
        var config = services.GetRequiredService<ConstantsStore>().Config;
        var results = services.GetRequiredService<DatabaseService>().GetUnits(UnitType.Gendarmerie)
            .GroupBy(u => u.ProvinceId)
            .Select(g => (Id: g.Key, Label: Texts.Get("crackdown.autocomplete",
                ("province", Display.Province(config, g.Key)), ("count", g.Count()), ("sides", g.Sum(u => u.Sides)))))
            .Where(x => x.Label.Contains(typed, StringComparison.OrdinalIgnoreCase) || x.Id.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Label)
            .Take(25)
            .Select(x => new AutocompleteResult(Display.Truncate(x.Label, 100), x.Id));
        return Task.FromResult(AutocompletionResult.FromSuccess(results));
    }
}
