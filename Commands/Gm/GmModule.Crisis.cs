using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;

public class GmLiveCrisisAutocomplete : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        var typed = autocompleteInteraction.Data.Current.Value?.ToString() ?? "";
        var results = services.GetRequiredService<DatabaseService>().GetCrises(CrisisStatus.Live)
            .Select(c => (Crisis: c, Label: Choice(c)))
            .Where(x => x.Label.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .Take(25)
            .Select(x => new AutocompleteResult(Display.Truncate(x.Label, 100), x.Crisis.Id.ToString()));
        return Task.FromResult(AutocompletionResult.FromSuccess(results));
    }

    public static string Choice(Crisis crisis) => crisis.ChosenAnswer is null
        ? Texts.Get("gmCrisis.autocomplete.awaiting", ("id", crisis.Id), ("crisis", crisis.Title), ("ministry", crisis.Ministry.DisplayName()))
        : Texts.Get("gmCrisis.autocomplete.answered", ("id", crisis.Id), ("crisis", crisis.Title), ("ministry", crisis.Ministry.DisplayName()));
}

public partial class GmModule
{
    [Group("crisis", "Withdraw or amend live crises")]
    [RequireUserPermission(GuildPermission.ManageGuild)]
    public class CrisisGroup : InteractionModuleBase<SocketInteractionContext>
    {
        private const int MessageLimit = 1500;

        private readonly DatabaseService _data;
        private readonly Confirmations _confirmations;
        private readonly CrisisPublisher _crises;

        public CrisisGroup(DatabaseService data, Confirmations confirmations, CrisisPublisher crises)
        {
            _data = data;
            _confirmations = confirmations;
            _crises = crises;
        }

        [SlashCommand("cancel", "Withdraw a live crisis, with an optional in-world message")]
        public async Task CancelAsync(
            [Summary("crisis", "A live crisis"), Autocomplete(typeof(GmLiveCrisisAutocomplete))] string crisis,
            [Summary("message", "An in-world note posted to #news with the withdrawal"), MaxLength(MessageLimit)] string? message = null)
        {
            if (!long.TryParse(crisis.Trim(), out var crisisId) || _data.GetCrisis(crisisId) is not { Status: CrisisStatus.Live } target)
            {
                await RespondAsync(Texts.Get("gmCrisis.cancel.notLive"), ephemeral: true);
                return;
            }

            var condemnations = _data.GetCondemnations(crisisId).Count;
            var lines = new List<string> { Texts.Get("gmCrisis.cancel.confirm", ("crisis", target.Title), ("ministry", target.Ministry.DisplayName())) };
            if (target.ChosenAnswer is not null) lines.Add(Texts.Get("gmCrisis.cancel.responded"));
            if (condemnations > 0) lines.Add(condemnations == 1
                ? Texts.Get("gmCrisis.cancel.refundOne")
                : Texts.Get("gmCrisis.cancel.refundMany", ("count", condemnations)));

            var components = _confirmations.Create(Context.User.Id, () => WithdrawAsync(crisisId, message), Texts.Get("gmCrisis.cancel.button"));
            await RespondAsync(string.Join("\n", lines), components: components, ephemeral: true);
        }

        private async Task<string> WithdrawAsync(long crisisId, string? message)
        {
            var result = _data.CancelCrisis(crisisId);
            if (result.Refusal is { } refusal) return refusal;
            await _crises.WithdrawAsync(result.Crisis!, message, "gm crisis cancel");
            return Texts.Get("gmCrisis.cancel.done", ("crisis", result.Crisis!.Title));
        }

        [SlashCommand("edit", "Edit a live crisis as YAML, until its minister responds")]
        public async Task EditAsync([Summary("crisis", "A live crisis"), Autocomplete(typeof(GmLiveCrisisAutocomplete))] string crisis)
        {
            if (!long.TryParse(crisis.Trim(), out var crisisId))
            {
                await RespondAsync(Texts.Get("gmCrisis.cancel.notLive"), ephemeral: true);
                return;
            }
            if (ContentRules.FindLiveCrisis(_data, crisisId, out var refusal) is not { } target)
            {
                await RespondAsync(refusal, ephemeral: true);
                return;
            }

            var yaml = TemplateCodec.Serialize(target.Current);
            if (yaml.Length > ContentRules.ModalLimit)
            {
                await RespondAsync(Texts.Get("gmCrisis.edit.tooLong", ("crisis", target.Current.Title), ("length", yaml.Length), ("limit", ContentRules.ModalLimit)),
                    ephemeral: true);
                return;
            }
            await RespondWithModalAsync(ContentModule.EditModal(ContentModule.CrisisEditMode, crisisId.ToString(),
                Texts.Get("gmCrisis.edit.title", ("crisis", target.Current.Title)), yaml));
        }
    }
}
