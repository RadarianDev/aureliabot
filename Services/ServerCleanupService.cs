using Discord;
using Discord.WebSocket;

public enum CleanupChannelKind { PartyChat, FactionChat }

public enum CleanupRefusal { Everyone, Managed, GmRole, CabinetRole, AboveBot }

public sealed record GuildChannelInfo(ulong Id, string Name, ulong? CategoryId);

public sealed record GuildRoleInfo(ulong Id, string Name, int Position, bool IsEveryone, bool IsManaged);

public sealed record CleanupChannel(ulong Id, string Name, CleanupChannelKind Kind);

public sealed record CleanupRole(ulong Id, string Name);

public sealed record RefusedRole(ulong Id, string Name, CleanupRefusal Reason);

public sealed record CleanupPlan(IReadOnlyList<CleanupChannel> Channels, IReadOnlyList<CleanupRole> Roles, IReadOnlyList<RefusedRole> Refused)
{
    public bool IsEmpty => Channels.Count == 0 && Roles.Count == 0;
}

public sealed record CleanupFailure(string Name, string Error);

public sealed record CleanupResult(
    IReadOnlyList<ulong> DeletedChannels,
    IReadOnlyList<ulong> DeletedRoles,
    IReadOnlyList<CleanupFailure> Failures,
    ClearedDiscordIds Cleared);

public class ServerCleanupService
{
    private readonly DatabaseService _data;
    private readonly AlertService _alerts;

    public ServerCleanupService(DatabaseService data, AlertService alerts)
    {
        _data = data;
        _alerts = alerts;
    }

    public static CleanupPlan BuildPlan(
        IReadOnlyCollection<GuildChannelInfo> channels,
        IReadOnlyCollection<GuildRoleInfo> roles,
        int botTopPosition,
        IReadOnlyDictionary<ChannelRole, ulong> registered,
        IReadOnlyCollection<Party> parties,
        IReadOnlyCollection<Faction> factions)
    {
        var registeredIds = registered.Values.ToHashSet();
        var partyCategories = new[] { ChannelRole.PartyCategory, ChannelRole.ArchiveCategory }
            .Where(registered.ContainsKey).Select(r => registered[r]).ToHashSet();
        ulong? factionCategory = registered.TryGetValue(ChannelRole.FactionCategory, out var factionCategoryId) ? factionCategoryId : null;
        var partyChatIds = parties.SelectMany(p => new[] { p.ChannelId, p.ForeignChannelId }).OfType<ulong>().ToHashSet();
        var factionChatIds = factions.Where(f => f.PrivateChannelId is not null).Select(f => f.PrivateChannelId!.Value).ToHashSet();

        var chats = new List<CleanupChannel>();
        foreach (var channel in channels.Where(c => !registeredIds.Contains(c.Id)))
        {
            CleanupChannelKind? kind =
                partyChatIds.Contains(channel.Id) ? CleanupChannelKind.PartyChat
                : factionChatIds.Contains(channel.Id) ? CleanupChannelKind.FactionChat
                : channel.CategoryId is { } category && partyCategories.Contains(category) ? CleanupChannelKind.PartyChat
                : channel.CategoryId is { } other && other == factionCategory ? CleanupChannelKind.FactionChat
                : null;
            if (kind is { } found) chats.Add(new CleanupChannel(channel.Id, channel.Name, found));
        }

        var partyRoleIds = parties.Where(p => p.RoleId is not null).Select(p => p.RoleId!.Value).ToHashSet();
        var partyNames = parties.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var partyChatNames = chats.Where(c => c.Kind == CleanupChannelKind.PartyChat)
            .SelectMany(c => new[] { c.Name, PartySyncService.ChannelStem(c.Name) })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        bool IsPartyRole(GuildRoleInfo role) =>
            partyRoleIds.Contains(role.Id)
            || partyNames.Contains(role.Name)
            || new[] { PartySyncService.ChannelSlug(role.Name), PartySyncService.Abbreviation(role.Name) }
                .Any(name => name.Length > 0 && partyChatNames.Contains(name));

        var deletable = new List<CleanupRole>();
        var refused = new List<RefusedRole>();
        foreach (var role in roles.Where(IsPartyRole))
        {
            CleanupRefusal? refusal =
                role.IsEveryone ? CleanupRefusal.Everyone
                : registered.TryGetValue(ChannelRole.GmRole, out var gm) && gm == role.Id ? CleanupRefusal.GmRole
                : registered.TryGetValue(ChannelRole.CabinetRole, out var cabinet) && cabinet == role.Id ? CleanupRefusal.CabinetRole
                : role.IsManaged ? CleanupRefusal.Managed
                : role.Position >= botTopPosition ? CleanupRefusal.AboveBot
                : null;
            if (refusal is { } reason) refused.Add(new RefusedRole(role.Id, role.Name, reason));
            else deletable.Add(new CleanupRole(role.Id, role.Name));
        }

        return new CleanupPlan(chats, deletable, refused);
    }

    public CleanupPlan Plan(SocketGuild guild) => BuildPlan(
        guild.Channels
            .Where(c => c is not SocketCategoryChannel and not SocketThreadChannel)
            .Select(c => new GuildChannelInfo(c.Id, c.Name, (c as INestedChannel)?.CategoryId))
            .ToList(),
        guild.Roles.Select(r => new GuildRoleInfo(r.Id, r.Name, r.Position, r.IsEveryone, r.IsManaged)).ToList(),
        guild.CurrentUser.Hierarchy,
        _data.GetChannels(),
        _data.GetAllParties(),
        _data.GetAllFactions());

    public async Task<CleanupResult> RunAsync(SocketGuild guild, CleanupPlan plan, ulong gmId)
    {
        var deletedChannels = new List<ulong>();
        var deletedRoles = new List<ulong>();
        var failures = new List<CleanupFailure>();

        foreach (var chat in plan.Channels)
        {
            try
            {
                if (guild.GetChannel(chat.Id) is { } channel) await channel.DeleteAsync();
                deletedChannels.Add(chat.Id);
            }
            catch (Exception ex)
            {
                failures.Add(new CleanupFailure(Texts.Get("cleanup.channelName", ("name", chat.Name)), ex.Message));
            }
        }

        foreach (var party in plan.Roles)
        {
            try
            {
                if (guild.GetRole(party.Id) is { } role) await role.DeleteAsync();
                deletedRoles.Add(party.Id);
            }
            catch (Exception ex)
            {
                failures.Add(new CleanupFailure(Texts.Get("cleanup.roleName", ("name", party.Name)), ex.Message));
            }
        }

        var cleared = _data.ClearDiscordIds(deletedRoles, deletedChannels);
        _data.Log(LogKinds.GmOverride, "gm cleanup",
            $"Deleted {deletedChannels.Count} channels and {deletedRoles.Count} roles, {failures.Count} failed; cleared ids on {cleared.Parties} parties and {cleared.Factions} factions.",
            gmId);
        await _alerts.PostAsync(Texts.Get("cleanup.alert",
            ("gm", gmId), ("channels", deletedChannels.Count), ("roles", deletedRoles.Count), ("failures", failures.Count)));

        return new CleanupResult(deletedChannels, deletedRoles, failures, cleared);
    }
}
