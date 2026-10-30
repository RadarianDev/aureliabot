using Discord;
using Discord.Interactions;

[Group("gm", "Run the game (GM only)")]
[DefaultMemberPermissions(GuildPermission.ManageGuild)]
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
    private readonly PartySyncService _partySync;
    private readonly CabinetSyncService _cabinetSync;
    private readonly FactionChannelService _factionChannels;
    private readonly SetupChecklistService _setupChecklist;
    private readonly GameFilesService _gameFiles;
    private readonly ServerCleanupService _cleanup;

    public GmModule(
        DatabaseService data,
        ConstantsStore constants,
        ResolutionService resolution,
        SnapshotService snapshots,
        TurnTrackerService turnTracker,
        Confirmations confirmations,
        AlertService alerts,
        Publisher publisher,
        PartySyncService partySync,
        CabinetSyncService cabinetSync,
        FactionChannelService factionChannels,
        SetupChecklistService setupChecklist,
        GameFilesService gameFiles,
        ServerCleanupService cleanup)
    {
        _data = data;
        _constants = constants;
        _resolution = resolution;
        _snapshots = snapshots;
        _turnTracker = turnTracker;
        _confirmations = confirmations;
        _alerts = alerts;
        _publisher = publisher;
        _partySync = partySync;
        _cabinetSync = cabinetSync;
        _factionChannels = factionChannels;
        _setupChecklist = setupChecklist;
        _gameFiles = gameFiles;
        _cleanup = cleanup;
    }

    private static string Timestamp(DateTimeOffset at, char style = 'F') => $"<t:{at.ToUnixTimeSeconds()}:{style}>";

    private string NextResolutionText(GameState state) =>
        state.NextResolutionAt is { } next
            ? Texts.Get("gm.nextResolution", ("at", Timestamp(next)), ("relative", Timestamp(next, 'R')))
            : Texts.Get("gm.notScheduled");
}
