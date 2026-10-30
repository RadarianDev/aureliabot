using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;

public class ResponsibleCrisisAutocomplete : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        var typed = autocompleteInteraction.Data.Current.Value?.ToString() ?? "";
        var results = services.GetRequiredService<DatabaseService>().CrisesAwaiting(context.User.Id)
            .Where(a => a.Crisis.Title.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .Take(25)
            .Select(a => new AutocompleteResult(Display.Truncate(a.Responded
                    ? Texts.Get("crisis.autocomplete.answered", ("crisis", a.Crisis.Title), ("ministry", a.Crisis.Ministry.DisplayName()))
                    : Texts.Get("crisis.autocomplete.awaiting", ("crisis", a.Crisis.Title), ("ministry", a.Crisis.Ministry.DisplayName())), 100),
                a.Crisis.Id.ToString()));
        return Task.FromResult(AutocompletionResult.FromSuccess(results));
    }

    public static Crisis? Find(DatabaseService data, IEnumerable<Crisis> crises, string text)
    {
        var trimmed = text.Trim();
        var list = crises.ToList();
        return long.TryParse(trimmed, out var id) && list.FirstOrDefault(c => c.Id == id) is { } byId
            ? byId
            : list.FirstOrDefault(c => c.Title.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
    }
}

public class CrisisAnswerAutocomplete : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        var typed = autocompleteInteraction.Data.Current.Value?.ToString() ?? "";
        var data = services.GetRequiredService<DatabaseService>();
        var crisisText = autocompleteInteraction.Data.Options.FirstOrDefault(o => o.Name == "crisis")?.Value?.ToString();
        if (crisisText is null || ResponsibleCrisisAutocomplete.Find(data, data.CrisesAwaiting(context.User.Id).Select(a => a.Crisis), crisisText) is not { } crisis)
            return Task.FromResult(AutocompletionResult.FromSuccess());

        var results = crisis.Answers
            .Select((answer, index) => (Number: index + 1, Line: CrisisPublisher.AnswerLine(index + 1, answer)))
            .Where(x => x.Line.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .Select(x => new AutocompleteResult(Display.Truncate(x.Line, 100), x.Number.ToString()));
        return Task.FromResult(AutocompletionResult.FromSuccess(results));
    }

    public static int? Find(Crisis crisis, string text)
    {
        var trimmed = text.Trim();
        if (int.TryParse(trimmed, out var number)) return crisis.AnswerNumbered(number) is null ? null : number;
        var index = crisis.Answers.ToList().FindIndex(a => a.Label.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
        return index < 0 ? null : index + 1;
    }
}

public class LiveCrisisAutocomplete : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        var typed = autocompleteInteraction.Data.Current.Value?.ToString() ?? "";
        var data = services.GetRequiredService<DatabaseService>();
        var results = data.CondemnableCrises(context.User.Id)
            .Select(c => (Crisis: c, Label: Texts.Get("condemn.autocomplete", ("crisis", c.Title), ("issue", CrisisPublisher.IssueTitle(data, c.IssueId)))))
            .Where(x => x.Label.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .Take(25)
            .Select(x => new AutocompleteResult(Display.Truncate(x.Label, 100), x.Crisis.Id.ToString()));
        return Task.FromResult(AutocompletionResult.FromSuccess(results));
    }
}

[Group("crisis", "Answer the crises that fall to you")]
public class CrisisModule : InteractionModuleBase<SocketInteractionContext>
{
    private const int ResponseLimit = 1500;

    private readonly DatabaseService _data;

    public CrisisModule(DatabaseService data)
    {
        _data = data;
    }

    [SlashCommand("respond", "Choose your answer to a crisis, with a public response read out at the resolution")]
    [RequireFaction]
    public async Task RespondCommandAsync(
        [Summary("crisis", "A live crisis you are responsible for"), Autocomplete(typeof(ResponsibleCrisisAutocomplete))] string crisis,
        [Summary("answer", "The answer to choose"), Autocomplete(typeof(CrisisAnswerAutocomplete))] string answer)
    {
        var target = ResponsibleCrisisAutocomplete.Find(_data, _data.GetCrises(CrisisStatus.Live), crisis);
        if (target is null)
        {
            await RespondAsync(Texts.Get("crisis.refusal.gone"), ephemeral: true);
            return;
        }
        if (_data.CrisisResponseRefusal(target.Id, Context.User.Id) is { } refusal)
        {
            await RespondAsync(refusal, ephemeral: true, allowedMentions: AllowedMentions.None);
            return;
        }
        if (CrisisAnswerAutocomplete.Find(target, answer) is not { } number)
        {
            await RespondAsync(Texts.Get("crisis.refusal.noAnswer", ("crisis", target.Title), ("answer", answer)), ephemeral: true);
            return;
        }

        var modal = new ModalBuilder()
            .WithTitle(Display.Truncate(Texts.Get("crisis.modal.title", ("crisis", target.Title)), 45))
            .WithCustomId($"crisis:respond:{target.Id}:{number}")
            .AddTextInput(Display.Truncate(Texts.Get("crisis.modal.label", ("answer", target.Answers[number - 1].Label)), 45),
                CrisisResponseModal.InputId, TextInputStyle.Paragraph, maxLength: ResponseLimit, required: true, value: target.ResponseText)
            .Build();
        await RespondWithModalAsync(modal);
    }

    [ModalInteraction("crisis:respond:*:*", ignoreGroupNames: true)]
    public async Task SubmitAsync(string rawId, string rawAnswer, CrisisResponseModal modal)
    {
        if (!long.TryParse(rawId, out var crisisId) || !int.TryParse(rawAnswer, out var number))
        {
            await RespondAsync(Texts.Get("crisis.refusal.gone"), ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);
        var result = _data.RespondToCrisis(crisisId, Context.User.Id, number, modal.Text);
        if (result is not { Crisis: { } crisis, Answer: { } answer })
        {
            await FollowupAsync(result.Refusal ?? Texts.Get("crisis.refusal.gone"), ephemeral: true, allowedMentions: AllowedMentions.None);
            return;
        }

        var ministry = crisis.Ministry.DisplayName();
        var chosen = CrisisPublisher.AnswerLine(number, answer);
        var due = GovernmentPublisher.Deadline(_data.GetGameState(), crisis.DueResolution);
        var lines = new List<string>
        {
            result.Replaced
                ? Texts.Get("crisis.respond.changed", ("crisis", crisis.Title), ("answer", chosen), ("due", due))
                : Texts.Get("crisis.respond.recorded", ("crisis", crisis.Title), ("answer", chosen), ("due", due)),
            answer.Price == 0
                ? Texts.Get("crisis.respond.free")
                : result.PurseCovers
                    ? Texts.Get("crisis.respond.covered", ("price", Display.Crowns(answer.Price)), ("ministry", ministry), ("balance", Display.Crowns(result.Balance)))
                    : Texts.Get("crisis.respond.short", ("price", Display.Crowns(answer.Price)), ("ministry", ministry), ("balance", Display.Crowns(result.Balance))),
        };
        await FollowupAsync(string.Join("\n", lines), ephemeral: true, allowedMentions: AllowedMentions.None);
    }
}

public class CrisisResponseModal : IModal
{
    public const string InputId = "text";

    public string Title => Texts.Get("crisis.modal.fallbackTitle");

    [ModalTextInput(InputId, TextInputStyle.Paragraph)]
    public string Text { get; set; } = "";
}
