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
            await FollowupAsync("Run this in a text channel, ideally #gm.", ephemeral: true);
            return;
        }

        var checklist = await _setupChecklist.PostAsync(channel);
        await FollowupAsync(
            checklist is null
                ? "I couldn't post the checklist here."
                : $"The setup checklist is in {checklist.GetJumpUrl()}. It updates itself whenever a channel or role changes, " +
                  "or you register something with `/gm channel set`. Nothing was created.",
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
                ? "There was nothing to register: no unset item has a channel or role with a matching name."
                : "Registered: " + string.Join(", ", suggestions.Select(s => $"{s.Role} → {s.Mention}")),
            ephemeral: true,
            allowedMentions: AllowedMentions.None);
    }

    [ComponentInteraction("setup:repair", ignoreGroupNames: true)]
    public async Task RepairAsync()
    {
        await DeferAsync(ephemeral: true);
        var problems = new List<string>();
        problems.AddRange(await Guarded("faction channels", () => _factionChannels.SyncAllAsync()));
        problems.AddRange(await Guarded("parties", () => _partySync.SyncAllAsync()));
        problems.AddRange(await Guarded("the Cabinet role", () => _cabinetSync.SyncAsync()));
        await _checklist.RefreshAsync();
        await FollowupAsync(
            Display.Truncate(problems.Count == 0
                ? "Party roles and chats, faction channels and the Cabinet role all match the game."
                : "Repaired what I could. Still wrong:\n" + string.Join("\n", problems.Select(p => $"- {p}"))),
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
            return [$"{what}: {ex.Message}"];
        }
    }
}
