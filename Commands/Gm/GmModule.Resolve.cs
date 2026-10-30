using Discord;
using Discord.Interactions;

public enum ResolveAction { Now, Delay, Pause, Resume }

public partial class GmModule
{
    [SlashCommand("resolve", "Control the resolution schedule: run now, delay, pause or resume")]
    public async Task ResolveAsync(
        [Summary("action", "What to do")] ResolveAction action,
        [Summary("hours", "Hours to delay the next resolution (delay only)")] double? hours = null)
    {
        switch (action)
        {
            case ResolveAction.Now:
                if (!_data.GetGameState().Started)
                {
                    await RespondAsync(Texts.Get("gm.resolve.notStarted"), ephemeral: true);
                    return;
                }
                await DeferAsync(ephemeral: true);
                var report = await _resolution.ResolveNowAsync();
                var after = _data.GetGameState();
                await FollowupAsync(
                    report is null
                        ? Texts.Get("gm.resolve.failed")
                        : Texts.Get("gm.resolve.resolved",
                            ("from", GameCalendar.Label(report.FromSeason, report.FromYear)), ("now", after.Label), ("next", NextResolutionText(after))),
                    ephemeral: true);
                return;

            case ResolveAction.Delay:
                if (hours is not > 0)
                {
                    await RespondAsync(Texts.Get("gm.resolve.needHours"), ephemeral: true);
                    return;
                }
                await DeferAsync(ephemeral: true);
                var delayed = await _resolution.ExclusiveAsync(() =>
                {
                    var state = _data.GetGameState();
                    if (state.NextResolutionAt is not { } next) return Task.FromResult<GameState?>(null);
                    var updated = state with { NextResolutionAt = next.AddHours(hours.Value) };
                    _data.SaveGameState(updated);
                    return Task.FromResult<GameState?>(updated);
                });
                if (delayed is null)
                {
                    await FollowupAsync(Texts.Get("gm.resolve.nothingToDelay"), ephemeral: true);
                    return;
                }
                _data.Log(LogKinds.GmOverride, "gm resolve delay", $"Delayed the next resolution by {hours} h.", Context.User.Id);
                await FollowupAsync(Texts.Get("gm.resolve.delayed", ("hours", hours.Value), ("when", NextResolutionText(delayed))), ephemeral: true);
                return;

            case ResolveAction.Pause:
                var toPause = _data.GetGameState();
                if (toPause.Paused)
                {
                    await RespondAsync(Texts.Get("gm.resolve.alreadyPaused"), ephemeral: true);
                    return;
                }
                _data.SaveGameState(toPause with { Paused = true });
                _data.Log(LogKinds.GmOverride, "gm resolve pause", "Paused the schedule.", Context.User.Id);
                _turnTracker.RequestCurrent();
                await RespondAsync(Texts.Get("gm.resolve.paused"), ephemeral: true);
                return;

            case ResolveAction.Resume:
                var toResume = _data.GetGameState();
                if (!toResume.Paused)
                {
                    await RespondAsync(Texts.Get("gm.resolve.notPaused"), ephemeral: true);
                    return;
                }
                var resumed = toResume with
                {
                    Paused = false,
                    NextResolutionAt = toResume.NextResolutionAt ?? GameCalendar.NextResolutionAt(
                        DateTimeOffset.UtcNow, _constants.Current, GameCalendar.SeasonLengthDays(toResume, _constants.Current)),
                };
                _data.SaveGameState(resumed);
                _data.Log(LogKinds.GmOverride, "gm resolve resume", "Resumed the schedule.", Context.User.Id);
                _turnTracker.RequestCurrent();
                await RespondAsync(
                    resumed.NextResolutionAt <= DateTimeOffset.UtcNow
                        ? Texts.Get("gm.resolve.resumedOverdue", ("next", NextResolutionText(resumed)))
                        : Texts.Get("gm.resolve.resumed", ("next", NextResolutionText(resumed))),
                    ephemeral: true);
                return;
        }
    }

    [SlashCommand("reload", "Reload config/ and load any new or changed files from content/")]
    public async Task ReloadAsync()
    {
        await DeferAsync(ephemeral: true);
        var error = _constants.Reload();
        if (error is null)
            _data.Log(LogKinds.GmOverride, "gm reload", "Reloaded the config.", Context.User.Id);
        var content = await _gameFiles.LoadContentAsync();
        var configText = error is null
            ? Texts.Get("gm.reload.configReloaded", ("folder", _gameFiles.ConfigFolder))
            : Texts.Get("gm.reload.configFailed", ("error", error));
        var contentText = content.Changed
            ? GameFilesService.Describe(content)
            : Texts.Get("gm.reload.contentUnchanged", ("count", content.Unchanged), ("folder", _gameFiles.ContentFolder));
        await FollowupAsync(Display.Truncate($"{configText}\n{contentText}"), ephemeral: true);
    }

    [SlashCommand("undo", "Restore the snapshot taken before the last resolution, and pause")]
    public async Task UndoAsync()
    {
        var latest = _snapshots.List().FirstOrDefault();
        if (latest is null)
        {
            await RespondAsync(Texts.Get("gm.undo.noSnapshot"), ephemeral: true);
            return;
        }

        var confirm = _confirmations.Create(Context.User.Id, async () =>
        {
            var result = await _resolution.ExclusiveAsync(() => _snapshots.UndoAsync());
            if (result is null) return Texts.Get("gm.undo.noSnapshot");

            _turnTracker.RequestCurrent();
            await _publisher.UpdateDashboardAsync();
            var state = _data.GetGameState();
            var lines = new List<string>
            {
                Texts.Get("gm.undo.restored", ("when", Timestamp(result.Restored.TakenAt)), ("label", state.Label)),
                result.PostsNotDeleted > 0
                    ? Texts.Get("gm.undo.postsPartlyDeleted", ("count", result.PostsDeleted), ("failed", result.PostsNotDeleted))
                    : Texts.Get("gm.undo.postsDeleted", ("count", result.PostsDeleted)),
            };
            if (result.LostCommands.Count == 0)
                lines.Add(Texts.Get("gm.undo.noCommandsLost"));
            else
            {
                lines.Add(Texts.Get("gm.undo.commandsLost", ("count", result.LostCommands.Count)));
                lines.AddRange(result.LostCommands.Take(30).Select(c => Texts.Get("gm.undo.lostCommand",
                    ("at", Timestamp(c.At, 'f')), ("faction", c.FactionId is { } f ? $"<@{f}>" : ""),
                    ("payload", Display.Truncate(CommandPayload.Describe(c.Payload), 300)))));
                var told = await new ResubmitService(_data, _factionChannels).SendLostCommandsAsync(result.LostCommands, state.Label);
                if (told == 1) lines.Add(Texts.Get("resubmit.gmSentOne"));
                else if (told > 1) lines.Add(Texts.Get("resubmit.gmSent", ("count", told)));
            }
            lines.Add(Texts.Get("gm.undo.resumeHint"));
            await _alerts.PostAsync(string.Join("\n", lines));
            return string.Join("\n", lines);
        }, Texts.Get("gm.undo.button"));

        await RespondAsync(
            Texts.Get("gm.undo.confirm", ("when", Timestamp(latest.TakenAt)), ("season", latest.Season), ("year", latest.Year)),
            components: confirm,
            ephemeral: true);
    }
}
