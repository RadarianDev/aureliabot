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
                    await RespondAsync("The game hasn't started yet; open it with `/gm start`.", ephemeral: true);
                    return;
                }
                await DeferAsync(ephemeral: true);
                var report = await _resolution.ResolveNowAsync();
                var after = _data.GetGameState();
                await FollowupAsync(
                    report is null
                        ? "⚠️ The resolution failed and was rolled back. The details are in #gm, and the schedule is paused."
                        : $"Resolved {GameCalendar.Label(report.FromSeason, report.FromYear)}. It is now {after.Label}. " +
                          $"Next resolution: {NextResolutionText(after)}.",
                    ephemeral: true);
                return;

            case ResolveAction.Delay:
                if (hours is not > 0)
                {
                    await RespondAsync("Give a positive number of `hours` to delay by.", ephemeral: true);
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
                    await FollowupAsync("No resolution is scheduled yet, so there is nothing to delay.", ephemeral: true);
                    return;
                }
                _data.Log(LogKinds.GmOverride, "gm resolve delay", $"Delayed the next resolution by {hours} h.", Context.User.Id);
                await FollowupAsync($"The next resolution is delayed by {hours} h, to {NextResolutionText(delayed)}.", ephemeral: true);
                return;

            case ResolveAction.Pause:
                var toPause = _data.GetGameState();
                if (toPause.Paused)
                {
                    await RespondAsync("The schedule is already paused.", ephemeral: true);
                    return;
                }
                _data.SaveGameState(toPause with { Paused = true });
                _data.Log(LogKinds.GmOverride, "gm resolve pause", "Paused the schedule.", Context.User.Id);
                _turnTracker.RequestCurrent();
                await RespondAsync("The schedule is paused. Nothing resolves until you resume it.", ephemeral: true);
                return;

            case ResolveAction.Resume:
                var toResume = _data.GetGameState();
                if (!toResume.Paused)
                {
                    await RespondAsync("The schedule isn't paused.", ephemeral: true);
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
                var overdue = resumed.NextResolutionAt <= DateTimeOffset.UtcNow
                    ? " It is already due, so it runs within a minute."
                    : "";
                await RespondAsync($"The schedule is running again. Next resolution: {NextResolutionText(resumed)}.{overdue}", ephemeral: true);
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
            ? $"Config reloaded from `{_gameFiles.ConfigFolder}`. Values already in play are unchanged."
            : $"⚠️ The config was not reloaded; the old one stays in force.\n{error}";
        var contentText = content.Changed
            ? GameFilesService.Describe(content)
            : $"Content: all {content.Unchanged} file(s) in `{_gameFiles.ContentFolder}` are already loaded.";
        await FollowupAsync(Display.Truncate($"{configText}\n{contentText}"), ephemeral: true);
    }

    [SlashCommand("undo", "Restore the snapshot taken before the last resolution, and pause")]
    public async Task UndoAsync()
    {
        var latest = _snapshots.List().FirstOrDefault();
        if (latest is null)
        {
            await RespondAsync("There is no snapshot to restore.", ephemeral: true);
            return;
        }

        var confirm = _confirmations.Create(Context.User.Id, async () =>
        {
            var result = await _resolution.ExclusiveAsync(() => _snapshots.UndoAsync());
            if (result is null) return "There is no snapshot to restore.";

            _turnTracker.RequestCurrent();
            await _publisher.UpdateDashboardAsync();
            var state = _data.GetGameState();
            var lines = new List<string>
            {
                $"Restored the snapshot from {Timestamp(result.Restored.TakenAt)}. It is {state.Label} again, and the schedule is **paused**.",
                $"Posts deleted: {result.PostsDeleted}" + (result.PostsNotDeleted > 0 ? $", {result.PostsNotDeleted} could not be deleted." : "."),
            };
            if (result.LostCommands.Count == 0)
                lines.Add("No player commands were lost.");
            else
            {
                lines.Add($"Commands lost ({result.LostCommands.Count}):");
                lines.AddRange(result.LostCommands.Take(30).Select(c =>
                    $"- {Timestamp(c.At, 'f')} {(c.FactionId is { } f ? $"<@{f}>" : "")} `{c.Payload}`"));
            }
            lines.Add("Use `/gm resolve resume` to re-run the resolution with the same seed.");
            await _alerts.PostAsync(string.Join("\n", lines));
            return string.Join("\n", lines);
        }, "Undo");

        await RespondAsync(
            $"Restore the snapshot from {Timestamp(latest.TakenAt)} ({latest.Season} {latest.Year})? " +
            "Everything since then is lost, that resolution's posts are deleted, and the schedule is paused.",
            components: confirm,
            ephemeral: true);
    }
}
