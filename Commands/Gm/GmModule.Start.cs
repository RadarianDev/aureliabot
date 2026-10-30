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
            await RespondAsync(Texts.Get("gm.start.alreadyStarted", ("label", state.Label)), ephemeral: true);
            return;
        }

        var queued = _data.GetQueuedEvents().Where(e => e.FireAtResolution <= 0).ToList();
        var warning = queued.Count == 0
            ? Texts.Get("gm.start.noOpeningEvent")
            : Texts.Get("gm.start.openingEvents", ("events", string.Join(", ", queued.Select(e => e.Title))));
        var c = _constants.Current;

        var confirm = _confirmations.Create(Context.User.Id, async () =>
        {
            var result = _data.StartGame(_constants.Config, DateTimeOffset.UtcNow);
            if (result.Refusal is not null || result.Report is not { } report) return result.Refusal ?? Texts.Get("gm.start.couldNotStart");

            var after = _data.GetGameState();
            var source = "gm start";
            await _publisher.PostEventsAsync(report, source);
            await _publisher.PostAsync(ChannelRole.News,
                Texts.Get("gm.start.proclamation", ("label", after.Label), ("when", Display.When(after.NextResolutionAt!.Value))), source);
            await _publisher.UpdateDashboardAsync();
            _turnTracker.RequestCurrent();
            await _alerts.PostAllAsync(report.Alerts);

            return Texts.Get("gm.start.started",
                ("label", after.Label), ("events", report.FiredEvents.Count),
                ("when", Display.When(after.NextResolutionAt!.Value)), ("days", c.PreElectionDays - 1));
        }, Texts.Get("gm.start.button"), ButtonStyle.Success);

        await RespondAsync(
            Texts.Get("gm.start.confirm", ("season", GameCalendar.Label(c.Start.Season, c.Start.Year)), ("days", c.PreElectionDays))
            + "\n" + warning,
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
            await RespondAsync(
                parsed.IsValid ? Texts.Get("gm.apply.noEffects") : Texts.Get("gm.apply.nothingApplied") + "\n" + string.Join("\n", parsed.Errors),
                ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);
        var label = isPublic ? Texts.Get("gm.royalDecree") : Texts.Get("gm.royalAdjustment");
        var result = _data.ApplyEffects(parsed.Effects, config,
            new EffectSource(label, "gm apply", Context.User.Id.ToString(), LedgerKind.Adjustment, PublicLedger: isPublic));
        var lines = EffectSummary.Lines(result.Applied, Publisher.Names(_data, config), config.Constants.Display);

        if (isPublic && lines.Count > 0)
            await _publisher.PostAsync(ChannelRole.News, Texts.Get("gm.decreeHeading") + "\n" + string.Join("\n", lines), "gm apply");
        await _publisher.UpdateDashboardAsync();

        var reply = isPublic
            ? Texts.Get("gm.apply.appliedPublic", ("count", result.Applied.Count))
            : Texts.Get("gm.apply.applied", ("count", result.Applied.Count));
        if (lines.Count > 0) reply += "\n" + string.Join("\n", lines);
        reply += string.Concat(result.Warnings.Select(w => "\n" + Texts.Get("gm.warning", ("warning", w))));
        await FollowupAsync(Display.Truncate(reply), ephemeral: true, allowedMentions: AllowedMentions.None);
    }
}
