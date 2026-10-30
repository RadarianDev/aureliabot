using Discord;
using Discord.Interactions;

public partial class GmModule
{
    [SlashCommand("start", "Open the game: Spring 1850, the founding pre-election (once only)")]
    public async Task StartAsync()
    {
        var state = _data.GetGameState();
        if (state.Started)
        {
            await RespondAsync($"The game has already started; it is {state.Label}.", ephemeral: true);
            return;
        }

        var queued = _data.GetQueuedEvents().Where(e => e.FireAtResolution <= 0).ToList();
        var warning = queued.Count == 0
            ? "\n⚠️ **No event is queued to fire at the start.** No issues will be open. Mark the opening event Ready first (`/content ready`)."
            : $"\nEvents that fire at once: {string.Join(", ", queued.Select(e => e.Title))}.";
        var c = _constants.Current;

        var confirm = _confirmations.Create(Context.User.Id, async () =>
        {
            var result = _data.StartGame(_constants.Config, DateTimeOffset.UtcNow);
            if (result.Refusal is not null || result.Report is not { } report) return result.Refusal ?? "The game could not start.";

            var after = _data.GetGameState();
            var source = "gm start";
            await _publisher.PostEventsAsync(report, source);
            await _publisher.PostAsync(ChannelRole.News,
                "## By His Majesty's Command\n" +
                $"His Majesty King Arthas II summons the first Aurelian Assembly. The hustings are open across the ten provinces " +
                $"through {after.Label}, and the nation goes to the polls when the season ends, {Display.When(after.NextResolutionAt!.Value)}.\n" +
                "Let every party declare where it stands.", source);
            await _publisher.UpdateDashboardAsync();
            _turnTracker.RequestCurrent();
            await _alerts.PostAllAsync(report.Alerts);

            return $"The game has started: {after.Label}, the founding pre-election. " +
                   $"{report.FiredEvents.Count} event(s) fired. The election runs at {Display.When(after.NextResolutionAt!.Value)}, " +
                   $"after {c.PreElectionDays - 1} campaign day(s).";
        }, "Start the game", ButtonStyle.Success);

        await RespondAsync(
            $"Start the game in {GameCalendar.Label(c.Start.Season, c.Start.Year)}? This loads the provinces, sets every starting value from " +
            $"`constants.yaml`, funds the purses, raises the starting forces and opens the founding pre-election ({c.PreElectionDays} days). " +
            $"It can only be done once.{warning}",
            components: confirm,
            ephemeral: true);
    }

    [SlashCommand("apply", "Apply effects written as text, as a delta (e.g. discontent.north_arpal -2, economy -1)")]
    public async Task ApplyAsync(
        [Summary("effects", "Effects as text: key value, key value, action.id")] string effects,
        [Summary("public", "Post the effect summary to #news")] bool isPublic = false)
    {
        var config = _constants.Config;
        var parsed = EffectParser.ParseText(effects, _data.BuildEffectContext(config, allowCrisis: true));
        if (!parsed.IsValid || parsed.Effects.Count == 0)
        {
            await RespondAsync(parsed.IsValid ? "No effects given." : "Nothing was applied:\n" + string.Join("\n", parsed.Errors), ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);
        var label = isPublic ? "By royal decree" : "Royal adjustment";
        var result = _data.ApplyEffects(parsed.Effects, config,
            new EffectSource(label, "gm apply", Context.User.Id.ToString(), LedgerKind.Adjustment, PublicLedger: isPublic));
        var lines = EffectSummary.Lines(result.Applied, Publisher.Names(_data, config), config.Constants.Display);

        if (isPublic && lines.Count > 0)
            await _publisher.PostAsync(ChannelRole.News, "## By His Majesty's Command\n" + string.Join("\n", lines), "gm apply");
        await _publisher.UpdateDashboardAsync();

        var reply = $"Applied {result.Applied.Count} effect(s){(isPublic ? " and posted them to #news" : "")}.";
        if (lines.Count > 0) reply += "\n" + string.Join("\n", lines);
        if (result.Warnings.Count > 0) reply += "\n⚠️ " + string.Join("\n⚠️ ", result.Warnings);
        await FollowupAsync(Display.Truncate(reply), ephemeral: true, allowedMentions: AllowedMentions.None);
    }
}
