using Discord;
using Discord.Interactions;

public class ReadyEventTemplateAutocomplete : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        var data = (DatabaseService)services.GetService(typeof(DatabaseService))!;
        var typed = autocompleteInteraction.Data.Current.Value?.ToString() ?? "";
        var results = data.GetTemplates(TemplateKind.Event, TemplateStatus.Ready)
            .Where(t => t.Id.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .Take(25)
            .Select(t => new AutocompleteResult(t.Id, t.Id));
        return Task.FromResult(AutocompletionResult.FromSuccess(results));
    }
}

public class QueuedEventAutocomplete : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        var data = (DatabaseService)services.GetService(typeof(DatabaseService))!;
        var typed = autocompleteInteraction.Data.Current.Value?.ToString() ?? "";
        var results = data.GetQueuedEvents()
            .Where(e => e.Title.Contains(typed, StringComparison.OrdinalIgnoreCase) || e.TemplateId.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .Take(25)
            .Select(e => new AutocompleteResult(Display.Truncate($"#{e.Id} {e.Title} · resolution {e.FireAtResolution}", 100), e.Id.ToString()));
        return Task.FromResult(AutocompletionResult.FromSuccess(results));
    }
}

public partial class GmModule
{
    [Group("event", "Queue and cancel events")]
    [RequireUserPermission(GuildPermission.ManageGuild)]
    public class EventGroup : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly DatabaseService _data;

        public EventGroup(DatabaseService data)
        {
            _data = data;
        }

        [SlashCommand("queue", "Queue an event from a Ready template, to fire at the start of the next turn or later")]
        public async Task QueueAsync(
            [Summary("template", "A Ready event template"), Autocomplete(typeof(ReadyEventTemplateAutocomplete))] string template,
            [Summary("delay", "Extra turns to wait (0 = the start of the next turn)")] int delay = 0)
        {
            var row = _data.GetTemplate(template);
            if (row is not { Kind: TemplateKind.Event, Status: TemplateStatus.Ready })
            {
                await RespondAsync($"`{template}` is not a Ready event template.", ephemeral: true);
                return;
            }
            if (_data.GetParsedTemplate(template) is not EventTemplate parsed)
            {
                await RespondAsync($"`{template}` no longer parses; fix it with `/content edit`.", ephemeral: true);
                return;
            }

            var state = _data.GetGameState();
            var fireAt = EventScheduling.FireAtForDelay(state, delay);
            var id = _data.QueueEvent(parsed, fireAt, fromSchedule: false);
            var when = state.Started
                ? $"at the start of {TurnLabel(state, fireAt)}"
                : fireAt == 0 ? "at `/gm start`" : $"{fireAt} turn(s) after `/gm start`";
            await RespondAsync($"Queued **{parsed.Title}** (#{id}), firing {when}.", ephemeral: true);
        }

        [SlashCommand("cancel", "Remove a queued event before it fires")]
        public async Task CancelAsync(
            [Summary("event", "The queued event"), Autocomplete(typeof(QueuedEventAutocomplete))] string eventId)
        {
            if (!long.TryParse(eventId, out var id) || _data.GetEvent(id) is not { Status: EventStatus.Queued } queued)
            {
                await RespondAsync("That is not a queued event.", ephemeral: true);
                return;
            }
            _data.CancelEvent(id);
            await RespondAsync($"Cancelled **{queued.Title}** (#{id}).", ephemeral: true);
        }

        private static string TurnLabel(GameState state, long fireAt)
        {
            var (season, year) = GameCalendar.Add(state.Season, state.Year, (int)(fireAt - state.ResolutionNumber));
            return GameCalendar.Label(season, year);
        }
    }
}
