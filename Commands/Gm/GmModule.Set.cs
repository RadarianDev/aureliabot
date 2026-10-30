using System.Globalization;
using Discord;
using Discord.Interactions;

public static class GameStateFields
{
    public sealed record Field(string Name, Func<GameState, string> Get, Func<GameState, string, GameState?> Set, bool MovesCalendar = false);

    private static double? Number(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static int? Whole(string text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static Season? SeasonOf(string text) =>
        Enum.TryParse<Season>(text, ignoreCase: true, out var season) && Enum.IsDefined(season) ? season : null;

    private static string Show(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    public static IReadOnlyList<Field> All { get; } =
    [
        new("season", s => s.Season.ToString(), (s, v) => SeasonOf(v) is { } x ? s with { Season = x } : null, true),
        new("year", s => s.Year.ToString(CultureInfo.InvariantCulture), (s, v) => Whole(v) is { } x ? s with { Year = x } : null, true),
        new("preElectionSeason", s => s.PreElectionSeason.ToString(), (s, v) => SeasonOf(v) is { } x ? s with { PreElectionSeason = x } : null, true),
        new("preElectionYear", s => s.PreElectionYear.ToString(CultureInfo.InvariantCulture), (s, v) => Whole(v) is { } x ? s with { PreElectionYear = x } : null, true),
        new("seasonLengthDays", s => s.SeasonLengthDays?.ToString(CultureInfo.InvariantCulture) ?? "default",
            (s, v) => v.Equals("default", StringComparison.OrdinalIgnoreCase) ? s with { SeasonLengthDays = null }
                : Whole(v) is { } x and >= 1 ? s with { SeasonLengthDays = x } : null, true),
        new("termNumber", s => s.TermNumber.ToString(CultureInfo.InvariantCulture), (s, v) => Whole(v) is { } x and >= 0 ? s with { TermNumber = x } : null),
        new("seats", s => s.Seats.ToString(CultureInfo.InvariantCulture), (s, v) => Whole(v) is { } x and >= 1 ? s with { Seats = x } : null),
        new("taxRate", s => Show(s.TaxRate), (s, v) => Number(v) is { } x ? s with { TaxRate = Math.Clamp(x, 0, 1) } : null),
        new("economy", s => Show(s.Economy), (s, v) => Number(v) is { } x ? s with { Economy = Math.Max(0, x) } : null),
        new("favour", s => Show(s.Favour), (s, v) => Number(v) is { } x ? s with { Favour = Math.Clamp(x, 0, 100) } : null),
        new("favourBaseline", s => Show(s.FavourBaseline), (s, v) => Number(v) is { } x ? s with { FavourBaseline = Math.Clamp(x, 0, 100) } : null),
        new("threat", s => Show(s.Threat), (s, v) => Number(v) is { } x ? s with { Threat = Math.Clamp(x, 0, 100) } : null),
        new("lastNavyShare", s => Show(s.LastNavyShare), (s, v) => Number(v) is { } x ? s with { LastNavyShare = Math.Clamp(x, 0, 1) } : null),
        new("reserve", s => s.Reserve.ToString("0.00", CultureInfo.InvariantCulture),
            (s, v) => decimal.TryParse(v, NumberStyles.Number, CultureInfo.InvariantCulture, out var x) ? s with { Reserve = Math.Max(0, Math.Round(x, 2)) } : null),
    ];

    public static Field? Find(string name) =>
        All.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}

public class GmSetKeyAutocomplete : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        var typed = autocompleteInteraction.Data.Current.Value?.ToString() ?? "";
        var state = ((DatabaseService)services.GetService(typeof(DatabaseService))!).GetGameState();
        var results = GameStateFields.All
            .Where(f => f.Name.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .Take(25)
            .Select(f => new AutocompleteResult($"{f.Name} (now {f.Get(state)})", f.Name));
        return Task.FromResult(AutocompletionResult.FromSuccess(results));
    }
}

public partial class GmModule
{
    [SlashCommand("set", "Set a game value to an absolute number")]
    public async Task SetAsync(
        [Summary("key", "The value to set"), Autocomplete(typeof(GmSetKeyAutocomplete))] string key,
        [Summary("value", "The new value")] string value)
    {
        var field = GameStateFields.Find(key);
        if (field is null)
        {
            await SetEffectKeyAsync(key.Trim(), value.Trim());
            return;
        }

        var before = _data.GetGameState();
        if (field.Set(before, value.Trim()) is not { } preview)
        {
            await RespondAsync($"`{value}` is not a valid value for `{field.Name}`.", ephemeral: true);
            return;
        }

        var confirm = _confirmations.Create(Context.User.Id, () =>
        {
            var current = _data.GetGameState();
            if (field.Set(current, value.Trim()) is not { } updated) return Task.FromResult("The value is no longer valid.");
            var oldValue = field.Get(current);
            if (field.MovesCalendar && updated.NextResolutionAt is not null)
            {
                var constants = _constants.Current;
                updated = updated with
                {
                    NextResolutionAt = GameCalendar.NextResolutionAt(
                        updated.LastResolutionAt ?? DateTimeOffset.UtcNow, constants, GameCalendar.SeasonLengthDays(updated, constants)),
                };
            }
            _data.SaveGameState(updated);
            _data.Log(LogKinds.GmOverride, "gm set", $"{field.Name}: {oldValue} -> {field.Get(updated)}", Context.User.Id);
            if (field.MovesCalendar) _turnTracker.RequestCurrent();
            var schedule = field.MovesCalendar ? $" Next resolution: {NextResolutionText(updated)}." : "";
            return Task.FromResult($"`{field.Name}` changed from {oldValue} to {field.Get(updated)}.{schedule}");
        }, "Set");

        await RespondAsync(
            $"Set `{field.Name}` from **{field.Get(before)}** to **{field.Get(preview)}**? Nothing is posted.",
            components: confirm,
            ephemeral: true);
    }

    private async Task SetEffectKeyAsync(string key, string value)
    {
        var config = _constants.Config;
        var parsed = EffectParser.ParseText($"{key} {value}", _data.BuildEffectContext(config));
        if (!parsed.IsValid || parsed.Effects is not [{ IsAction: false } effect] || effect.Prefix == "condition")
        {
            await RespondAsync(
                $"`{key}` is not a value `/gm set` knows. Use a game value from the list, or an effect key such as " +
                "`purse.War`, `discontent.north_arpal`, `opinion.labour`, `salience.labour` or `popularity.<user id>`." +
                (parsed.IsValid ? "" : "\n" + string.Join("\n", parsed.Errors)),
                ephemeral: true);
            return;
        }
        if (_data.GetEffectValue(effect.Key) is not { } current)
        {
            await RespondAsync($"`{effect.Key}` has no current value (is the game started and the issue open?).", ephemeral: true);
            return;
        }

        var target = effect.Amount;
        var confirm = _confirmations.Create(Context.User.Id, () =>
        {
            var now = _data.GetEffectValue(effect.Key) ?? current;
            var result = _data.ApplyEffects([new Effect(effect.Key, target - now)], config,
                new EffectSource("Royal adjustment", "gm set", Context.User.Id.ToString(), LedgerKind.Adjustment));
            var after = _data.GetEffectValue(effect.Key);
            var warning = result.Warnings.Count > 0 ? "\n⚠️ " + string.Join("\n⚠️ ", result.Warnings) : "";
            return Task.FromResult($"`{effect.Key}` changed from {EffectParser.FormatNumber(now)} to {(after is { } a ? EffectParser.FormatNumber(a) : "?")}.{warning}");
        }, "Set");

        await RespondAsync(
            $"Set `{effect.Key}` from **{EffectParser.FormatNumber(current)}** to **{EffectParser.FormatNumber(target)}** (clamped to its range)? Nothing is posted.",
            components: confirm,
            ephemeral: true);
    }
}
