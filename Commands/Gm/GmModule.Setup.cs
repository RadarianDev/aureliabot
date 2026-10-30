using Discord;
using Discord.Interactions;

public partial class GmModule
{
    [SlashCommand("setup", "Post the setup checklist here; it keeps itself up to date and creates nothing")]
    public async Task SetupAsync()
    {
        await DeferAsync(ephemeral: true);
        if (Context.Channel is not IMessageChannel channel)
        {
            await FollowupAsync(Texts.Get("gm.setup.notTextChannel"), ephemeral: true);
            return;
        }

        var checklist = await _setupChecklist.PostAsync(channel);
        await FollowupAsync(
            checklist is null
                ? Texts.Get("gm.setup.couldNotPost")
                : Texts.Get("gm.setup.posted", ("url", checklist.GetJumpUrl())),
            ephemeral: true);
    }
}

[RequireUserPermission(GuildPermission.ManageGuild)]
public class SetupChecklistModule : InteractionModuleBase<SocketInteractionContext>
{
    private static readonly HashSet<ChannelRole> TextChannels =
    [
        ChannelRole.News, ChannelRole.Dashboard, ChannelRole.Assembly, ChannelRole.CampaignLog, ChannelRole.Cabinet, ChannelRole.Gm,
    ];

    private readonly DatabaseService _data;
    private readonly SetupChecklistService _checklist;
    private readonly PartySyncService _partySync;
    private readonly CabinetSyncService _cabinetSync;
    private readonly FactionChannelService _factionChannels;

    public SetupChecklistModule(
        DatabaseService data,
        SetupChecklistService checklist,
        PartySyncService partySync,
        CabinetSyncService cabinetSync,
        FactionChannelService factionChannels)
    {
        _data = data;
        _checklist = checklist;
        _partySync = partySync;
        _cabinetSync = cabinetSync;
        _factionChannels = factionChannels;
    }

    [ComponentInteraction("setup:register", ignoreGroupNames: true)]
    public async Task RegisterAsync()
    {
        await DeferAsync(ephemeral: true);
        var suggestions = _checklist.Suggestions();
        foreach (var suggestion in suggestions)
        {
            _data.SetChannel(suggestion.Role, suggestion.Id);
            if (TextChannels.Contains(suggestion.Role))
                _data.AddWhitelistedChannel(Context.Guild.Id, suggestion.Id, Context.User.Id);
            _data.Log(LogKinds.GmOverride, "gm setup", $"{suggestion.Role} = {suggestion.Id} (registered from the checklist)", Context.User.Id);
        }
        await _checklist.RefreshAsync();
        await FollowupAsync(
            suggestions.Count == 0
                ? Texts.Get("setup.register.nothing")
                : Texts.Get("setup.register.done",
                    ("items", string.Join(", ", suggestions.Select(s => Texts.Get("setup.register.item", ("role", s.Role), ("mention", s.Mention)))))),
            ephemeral: true,
            allowedMentions: AllowedMentions.None);
    }

    [ComponentInteraction("setup:repair", ignoreGroupNames: true)]
    public async Task RepairAsync()
    {
        await DeferAsync(ephemeral: true);
        var problems = new List<string>();
        problems.AddRange(await Guarded(Texts.Get("setup.repair.factionChannels"), () => _factionChannels.SyncAllAsync()));
        problems.AddRange(await Guarded(Texts.Get("setup.repair.parties"), () => _partySync.SyncAllAsync()));
        problems.AddRange(await Guarded(Texts.Get("setup.repair.cabinetRole"), () => _cabinetSync.SyncAsync()));
        await _checklist.RefreshAsync();
        await FollowupAsync(
            Display.Truncate(problems.Count == 0
                ? Texts.Get("setup.repair.allGood")
                : Texts.Get("setup.repair.stillWrong") + "\n" + string.Join("\n", problems.Select(p => $"- {p}"))),
            ephemeral: true,
            allowedMentions: AllowedMentions.None);
    }

    [ComponentInteraction("setup:refresh", ignoreGroupNames: true)]
    public async Task RefreshAsync()
    {
        await DeferAsync();
        await _checklist.RefreshAsync();
    }

    private static async Task<IReadOnlyList<string>> Guarded(string what, Func<Task<IReadOnlyList<string>>> sync)
    {
        try
        {
            return await sync();
        }
        catch (Exception ex)
        {
            return [Texts.Get("setup.repair.failed", ("what", what), ("error", ex.Message))];
        }
    }
}
