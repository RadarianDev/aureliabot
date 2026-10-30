using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;

public class OpenIssueAutocomplete : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        var typed = autocompleteInteraction.Data.Current.Value?.ToString() ?? "";
        var results = services.GetRequiredService<DatabaseService>().GetOpenIssues()
            .Where(i => Matches(i, typed))
            .Take(25)
            .Select(i => new AutocompleteResult(Display.Truncate(i.Title, 100), i.Id));
        return Task.FromResult(AutocompletionResult.FromSuccess(results));
    }

    public static bool Matches(Issue issue, string typed) =>
        issue.Title.Contains(typed, StringComparison.OrdinalIgnoreCase) || issue.Id.Contains(typed, StringComparison.OrdinalIgnoreCase);

    public static Issue? Find(DatabaseService data, string text)
    {
        var trimmed = text.Trim();
        return data.GetOpenIssues().FirstOrDefault(i => i.Id == trimmed)
            ?? data.GetOpenIssues().FirstOrDefault(i => i.Title.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
    }
}

public class CampaignIssueAutocomplete : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        var typed = autocompleteInteraction.Data.Current.Value?.ToString() ?? "";
        var data = services.GetRequiredService<DatabaseService>();
        var faction = data.GetFaction(context.User.Id);
        var programme = faction?.PartyId is { } partyId ? data.GetProgramme(partyId) : [];
        var results = data.GetOpenIssues()
            .Where(i => OpenIssueAutocomplete.Matches(i, typed))
            .Take(25)
            .Select(i => new AutocompleteResult(
                Display.Truncate(programme.TryGetValue(i.Id, out var stance) && stance != Stance.Silent
                    ? Texts.Get("campaign.autocomplete.programme", ("issue", i.Title), ("stance", stance.DisplayName()))
                    : Texts.Get("campaign.autocomplete.chooseStance", ("issue", i.Title)), 100),
                i.Id));
        return Task.FromResult(AutocompletionResult.FromSuccess(results));
    }
}

public class RivalAutocomplete : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        var typed = autocompleteInteraction.Data.Current.Value?.ToString() ?? "";
        var data = services.GetRequiredService<DatabaseService>();
        var me = data.GetFaction(context.User.Id);
        var guild = context.Guild as SocketGuild;
        var results = data.GetActiveFactions()
            .Where(f => f.UserId != context.User.Id && (me?.PartyId is null || f.PartyId != me.PartyId))
            .Select(f => (Faction: f, Label: Label(data, f, guild?.GetUser(f.UserId)?.Username)))
            .Where(x => x.Label.Contains(typed, StringComparison.OrdinalIgnoreCase)
                || x.Faction.UserId.ToString().StartsWith(typed, StringComparison.Ordinal))
            .OrderBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
            .Take(25)
            .Select(x => new AutocompleteResult(Display.Truncate(x.Label, 100), x.Faction.UserId.ToString()));
        return Task.FromResult(AutocompletionResult.FromSuccess(results));
    }

    private static string Label(DatabaseService data, Faction faction, string? username)
    {
        var who = username ?? faction.UserId.ToString();
        var name = string.IsNullOrWhiteSpace(faction.CharacterName) ? who : $"{faction.CharacterName} ({who})";
        var party = faction.PartyId is { } partyId ? data.GetParty(partyId)?.Name : null;
        return $"{name} · {party ?? Display.Independent}";
    }
}

public class RivalStanceIssueAutocomplete : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        var typed = autocompleteInteraction.Data.Current.Value?.ToString() ?? "";
        var data = services.GetRequiredService<DatabaseService>();
        var rivalText = autocompleteInteraction.Data.Options.FirstOrDefault(o => o.Name == "faction")?.Value?.ToString();
        if (rivalText is null || FactionAutocomplete.Find(data, rivalText) is not { } rival)
            return Task.FromResult(AutocompletionResult.FromSuccess());

        var results = data.GetOpenIssues()
            .Where(i => OpenIssueAutocomplete.Matches(i, typed))
            .Select(i => (Issue: i, Stance: data.GetStance(rival.UserId, i.Id)))
            .Where(x => x.Stance is { } s && s != Stance.Silent)
            .Take(25)
            .Select(x => new AutocompleteResult(Display.Truncate($"{x.Issue.Title} · {x.Stance!.Value.DisplayName()}", 100), x.Issue.Id));
        return Task.FromResult(AutocompletionResult.FromSuccess(results));
    }
}
