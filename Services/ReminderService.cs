using System.Text;

public class ReminderService
{
    public static readonly TimeSpan Lead = TimeSpan.FromHours(12);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(1);

    private readonly DatabaseService _data;
    private readonly ConstantsStore _constants;
    private readonly Func<ulong, string, Task<bool>> _send;
    private readonly TimeProvider _time;

    public ReminderService(DatabaseService data, FactionChannelService factionChannels, ConstantsStore constants)
        : this(data, constants, (factionId, text) => factionChannels.SendAsync(factionId, text), TimeProvider.System)
    {
    }

    internal ReminderService(DatabaseService data, ConstantsStore constants, Func<ulong, string, Task<bool>> send, TimeProvider time)
    {
        _data = data;
        _constants = constants;
        _send = send;
        _time = time;
    }

    public async Task RunAsync(CancellationToken cancellation)
    {
        using var timer = new PeriodicTimer(PollInterval, _time);
        do
        {
            try
            {
                await TickAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Reminders] Tick failed: {ex}");
            }
        } while (await timer.WaitForNextTickAsync(cancellation));
    }

    internal async Task<int> TickAsync()
    {
        var state = _data.GetGameState();
        if (!IsDue(state, _time.GetUtcNow())) return 0;

        var resolution = state.ResolutionNumber + 1;
        var sent = 0;
        foreach (var faction in _data.GetActiveFactions())
        {
            if (!_data.TryMarkReminderSent(faction.UserId, resolution)) continue;
            if (MessageFor(faction, state) is not { } message) continue;
            if (await _send(faction.UserId, message)) sent++;
        }
        return sent;
    }

    public static bool IsDue(GameState state, DateTimeOffset now) =>
        state.Started && !state.Paused && state.NextResolutionAt is { } next && now >= next - Lead && now < next;

    internal string? MessageFor(Faction faction, GameState state)
    {
        var outstanding = _data.Outstanding(faction.UserId, _constants.Current);
        var lines = new List<string>();

        if (outstanding.CanCampaign && outstanding.UnusedActions > 0)
            lines.Add(outstanding.UnusedActions == 1
                ? Texts.Get("reminders.actionUnused")
                : Texts.Get("reminders.actionsUnused", ("count", outstanding.UnusedActions)));
        if (outstanding.Opposition is { CanAct: true })
            lines.Add(Texts.Get("reminders.opposition"));
        foreach (var cabinet in outstanding.CabinetVotes)
            lines.Add(Texts.Get("me.cabinetVote",
                ("chancellor", cabinet.ChancellorId is { } c ? Display.Faction(_data, c) : Texts.Get("me.unknownTarget"))));
        foreach (var reading in outstanding.Readings)
            lines.Add(Texts.Get("me.readingVote", ("bill", _data.GetBill(reading.BillId)?.Title ?? reading.BillId)));
        foreach (var motion in outstanding.Motions)
            lines.Add(Texts.Get("me.motionVote", ("tabler", Display.Faction(_data, motion.TablerId))));
        foreach (var question in outstanding.Questions)
            lines.Add(Texts.Get("me.questionToAnswer", ("ministry", question.Ministry.DisplayName()), ("asker", Display.Faction(_data, question.AskerId))));
        foreach (var awaiting in outstanding.UnansweredCrises)
            lines.Add(Texts.Get("me.crisisAwaiting",
                ("crisis", awaiting.Crisis.Title), ("ministry", awaiting.Crisis.Ministry.DisplayName()), ("when", Display.NextResolution(state))));

        if (lines.Count == 0) return null;
        var sb = new StringBuilder();
        sb.AppendLine(Texts.Get("reminders.heading", ("faction", Display.Faction(faction)), ("when", Display.NextResolution(state))));
        foreach (var line in lines) sb.AppendLine($"- {line}");
        return sb.ToString().TrimEnd();
    }
}
