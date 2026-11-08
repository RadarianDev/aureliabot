using System.Threading.Channels;
using Discord;
using Discord.WebSocket;

public class TurnTrackerService
{
    private static readonly TimeSpan ErrorBackoff = TimeSpan.FromSeconds(30);

    private readonly DiscordSocketClient _client;
    private readonly DatabaseService _data;
    private readonly TimeProvider _time;
    private readonly RenameRateLimiter _limiter = new();
    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    private readonly object _pendingLock = new();
    private string? _pending;

    public TurnTrackerService(DiscordSocketClient client, DatabaseService data)
        : this(client, data, TimeProvider.System)
    {
    }

    internal TurnTrackerService(DiscordSocketClient client, DatabaseService data, TimeProvider time)
    {
        _client = client;
        _data = data;
        _time = time;
    }

    public static string NameFor(GameState state)
    {
        var name = state.Label;
        if (state.IsPreElection) name = Texts.Get("news.turnTracker.preElection", ("name", name));
        else if (state.IsSenateCampaign) name = Texts.Get("news.turnTracker.senateCampaign", ("name", name));
        if (state.Paused) name = Texts.Get("news.turnTracker.paused", ("name", name));
        return name;
    }

    public void Request(string name)
    {
        lock (_pendingLock)
            _pending = name;
        _wake.Writer.TryWrite(true);
    }

    public void RequestCurrent() => Request(NameFor(_data.GetGameState()));

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await StepAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TurnTracker] Unhandled exception: {ex}");
                try
                {
                    await Task.Delay(ErrorBackoff, _time, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    public DiscordSocketClient Client => _client;

    internal string? PendingName
    {
        get
        {
            lock (_pendingLock)
                return _pending;
        }
    }

    private bool TakePending(string name)
    {
        lock (_pendingLock)
        {
            if (_pending != name) return false;
            _pending = null;
            return true;
        }
    }

    private async Task StepAsync(CancellationToken cancellationToken)
    {
        var name = PendingName;
        if (name is null)
        {
            await _wake.Reader.ReadAsync(cancellationToken);
            return;
        }

        var channel = ResolveChannel();
        if (channel is null)
        {
            TakePending(name);
            return;
        }

        if (channel.Name == name)
        {
            TakePending(name);
            return;
        }

        var delay = _limiter.DelayBeforeNext(_time.GetUtcNow());
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, _time, cancellationToken);
            return;
        }

        if (!TakePending(name)) return;

        _limiter.Record(_time.GetUtcNow());
        try
        {
            await channel.ModifyAsync(p => p.Name = name);
            Console.WriteLine($"[TurnTracker] Renamed the turn tracker to \"{name}\".");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TurnTracker] Failed to rename the turn tracker to \"{name}\": {ex.Message}");
        }
    }

    private IGuildChannel? ResolveChannel()
    {
        var channelId = _data.GetChannel(ChannelRole.TurnTracker);
        if (channelId is null)
        {
            Console.WriteLine("[TurnTracker] No turn tracker channel is set; skipping the rename.");
            return null;
        }

        if (_client.GetChannel(channelId.Value) is IGuildChannel channel) return channel;

        Console.WriteLine($"[TurnTracker] Turn tracker channel {channelId.Value} was not found; skipping the rename.");
        return null;
    }
}

internal sealed class RenameRateLimiter
{
    internal const int MaxRenames = 2;
    internal static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    private readonly List<DateTimeOffset> _recent = new();

    internal IReadOnlyList<DateTimeOffset> Recent => _recent;

    internal void Record(DateTimeOffset at)
    {
        _recent.Add(at);
        _recent.RemoveAll(t => at - t >= Window);
    }

    internal TimeSpan DelayBeforeNext(DateTimeOffset now) => DelayBeforeNext(_recent, now);

    internal static TimeSpan DelayBeforeNext(IReadOnlyList<DateTimeOffset> recent, DateTimeOffset now)
    {
        var inWindow = recent.Where(t => now - t < Window).OrderBy(t => t).ToList();
        if (inWindow.Count < MaxRenames) return TimeSpan.Zero;
        return inWindow[inWindow.Count - MaxRenames] + Window - now;
    }
}
