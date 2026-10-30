using Discord;
using Discord.Interactions;

[Group("gm", "Run the game (GM only)")]
[RequireUserPermission(GuildPermission.ManageGuild)]
public partial class GmModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly DatabaseService _data;
    private readonly ConstantsStore _constants;
    private readonly ResolutionService _resolution;
    private readonly SnapshotService _snapshots;
    private readonly TurnTrackerService _turnTracker;
    private readonly Confirmations _confirmations;
    private readonly AlertService _alerts;
    private readonly Publisher _publisher;

    public GmModule(
        DatabaseService data,
        ConstantsStore constants,
        ResolutionService resolution,
        SnapshotService snapshots,
        TurnTrackerService turnTracker,
        Confirmations confirmations,
        AlertService alerts,
        Publisher publisher)
    {
        _data = data;
        _constants = constants;
        _resolution = resolution;
        _snapshots = snapshots;
        _turnTracker = turnTracker;
        _confirmations = confirmations;
        _alerts = alerts;
        _publisher = publisher;
    }

    private static string Timestamp(DateTimeOffset at, char style = 'F') => $"<t:{at.ToUnixTimeSeconds()}:{style}>";

    private string NextResolutionText(GameState state) =>
        state.NextResolutionAt is { } next
            ? $"{Timestamp(next)} ({Timestamp(next, 'R')})"
            : "not scheduled";
}
