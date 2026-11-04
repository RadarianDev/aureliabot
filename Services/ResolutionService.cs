public class ResolutionService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    private readonly DatabaseService _data;
    private readonly ConstantsStore _constants;
    private readonly SnapshotService _snapshots;
    private readonly Publisher _publisher;
    private readonly TurnTrackerService _turnTracker;
    private readonly AlertService _alerts;
    private readonly PartySyncService _partySync;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ResolutionService(
        DatabaseService data,
        ConstantsStore constants,
        SnapshotService snapshots,
        Publisher publisher,
        TurnTrackerService turnTracker,
        AlertService alerts,
        PartySyncService partySync)
    {
        _data = data;
        _constants = constants;
        _snapshots = snapshots;
        _publisher = publisher;
        _turnTracker = turnTracker;
        _alerts = alerts;
        _partySync = partySync;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            try
            {
                await TickAsync(DateTimeOffset.UtcNow);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Resolution] Poller tick failed: {ex}");
            }
        } while (await timer.WaitForNextTickAsync(cancellationToken));
    }

    private async Task TickAsync(DateTimeOffset now)
    {
        var state = _data.GetGameState();
        if (!state.Started || state.Paused || state.NextResolutionAt is not { } next) return;

        var constants = _constants.Current;
        var length = GameCalendar.SeasonLengthDays(state, constants);
        var campaignDays = GameCalendar.CampaignDays(next, constants, length);

        if (now >= next)
        {
            if (now - next > PollInterval * 2)
                await _alerts.PostAsync(
                    Texts.Get("alerts.resolution.missed", ("time", next.ToUnixTimeSeconds())));
            await ResolveAsync(campaignDay: false);
        }
        else if (state.CampaignDaysRun < campaignDays.Count && now >= campaignDays[state.CampaignDaysRun])
        {
            await ResolveAsync(campaignDay: true);
        }
    }

    public Task<ResolutionReport?> ResolveNowAsync() =>
        _data.GetGameState().Started ? ResolveAsync(campaignDay: false) : Task.FromResult<ResolutionReport?>(null);

    public async Task<T> ExclusiveAsync<T>(Func<Task<T>> work)
    {
        await _gate.WaitAsync();
        try
        {
            return await work();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ResolutionReport?> ResolveAsync(bool campaignDay)
    {
        await _gate.WaitAsync();
        try
        {
            var constants = _constants.Current;
            var state = _data.GetGameState();
            var seed = state.RngSeed ?? SeededRandomSource.NewSeed();
            _data.SaveGameState(state with { RngSeed = seed });
            _snapshots.Backup();

            ResolutionReport report;
            try
            {
                var random = new SeededRandomSource(seed);
                report = campaignDay
                    ? _data.ResolveCampaignDay(_constants.Config, random, seed, DateTimeOffset.UtcNow)
                    : _data.Resolve(_constants.Config, random, seed, DateTimeOffset.UtcNow);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Resolution] Resolution failed and was rolled back: {ex}");
                _data.SaveGameState(_data.GetGameState() with { Paused = true });
                _turnTracker.RequestCurrent();
                await _alerts.PostAsync(
                    Texts.Get("alerts.resolution.failed", ("label", state.Label), ("seed", seed), ("error", ex.Message)));
                return null;
            }

            await PublishAsync(report);
            return report;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task PublishAsync(ResolutionReport report)
    {
        try
        {
            await _publisher.PublishAsync(report);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Resolution] Publishing failed: {ex}");
            await _alerts.PostAsync(Texts.Get("alerts.resolution.publishFailed", ("number", report.ResolutionNumber), ("error", ex.Message)));
        }
        if (report.Election is not null)
            await _partySync.OrderRolesAsync();
        _turnTracker.RequestCurrent();
        await _publisher.PostGmDigestAsync(report);
    }
}
