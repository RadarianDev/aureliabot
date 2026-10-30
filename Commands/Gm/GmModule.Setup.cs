using Discord;
using Discord.Interactions;
using Discord.Net;
using Discord.WebSocket;

public partial class GmModule
{
    private enum SetupKind { Role, Category, Text }

    private sealed record SetupTarget(ChannelRole Role, string Name, SetupKind Kind)
    {
        public string Label => Kind switch
        {
            SetupKind.Role => $"{Name} role",
            SetupKind.Category => $"{Name} category",
            _ => $"#{Name}",
        };
    }

    private sealed record SetupItem(SetupTarget Target, ulong Id, IRole? Role, IGuildChannel? Channel)
    {
        public string Mention => Target.Kind == SetupKind.Role ? $"<@&{Id}>" : Target.Kind == SetupKind.Text ? $"<#{Id}>" : Target.Label;
    }

    private static readonly IReadOnlyList<SetupTarget> SetupTargets =
    [
        new(ChannelRole.GmRole, "GM", SetupKind.Role),
        new(ChannelRole.CabinetRole, "Cabinet", SetupKind.Role),
        new(ChannelRole.FactionCategory, "Factions", SetupKind.Category),
        new(ChannelRole.PartyCategory, "Parties", SetupKind.Category),
        new(ChannelRole.ArchiveCategory, "Archive", SetupKind.Category),
        new(ChannelRole.News, "news", SetupKind.Text),
        new(ChannelRole.Dashboard, "dashboard", SetupKind.Text),
        new(ChannelRole.Assembly, "assembly", SetupKind.Text),
        new(ChannelRole.CampaignLog, "campaign-log", SetupKind.Text),
        new(ChannelRole.Cabinet, "cabinet", SetupKind.Text),
        new(ChannelRole.Gm, "gm", SetupKind.Text),
    ];

    private static readonly OverwritePermissions SetupHidden = new(viewChannel: PermValue.Deny);

    private static readonly OverwritePermissions SetupReadOnly = new(
        viewChannel: PermValue.Allow, readMessageHistory: PermValue.Allow, sendMessages: PermValue.Deny,
        sendMessagesInThreads: PermValue.Deny, createPublicThreads: PermValue.Deny, createPrivateThreads: PermValue.Deny);

    private static readonly OverwritePermissions SetupMember = new(
        viewChannel: PermValue.Allow, sendMessages: PermValue.Allow, readMessageHistory: PermValue.Allow,
        addReactions: PermValue.Allow, embedLinks: PermValue.Allow, attachFiles: PermValue.Allow,
        useApplicationCommands: PermValue.Allow);

    private static readonly OverwritePermissions SetupGm = new(
        viewChannel: PermValue.Allow, sendMessages: PermValue.Allow, readMessageHistory: PermValue.Allow,
        addReactions: PermValue.Allow, embedLinks: PermValue.Allow, attachFiles: PermValue.Allow,
        useApplicationCommands: PermValue.Allow, manageMessages: PermValue.Allow);

    private static readonly OverwritePermissions SetupGmCategory = new(viewChannel: PermValue.Allow, readMessageHistory: PermValue.Allow);

    private static readonly OverwritePermissions SetupBot = new(
        viewChannel: PermValue.Allow, sendMessages: PermValue.Allow, readMessageHistory: PermValue.Allow,
        embedLinks: PermValue.Allow, attachFiles: PermValue.Allow, manageMessages: PermValue.Allow);

    [SlashCommand("setup", "Create or repair every game channel, category and role, and report what still needs a hand")]
    public async Task SetupAsync()
    {
        await DeferAsync(ephemeral: true);
        var guild = Context.Guild;

        var existed = new List<string>();
        var adopted = new List<string>();
        var created = new List<string>();
        var problems = new List<string>();
        var items = new Dictionary<ChannelRole, SetupItem>();

        foreach (var target in SetupTargets)
        {
            try
            {
                var (item, outcome) = await EnsureSetupTargetAsync(guild, target);
                items[target.Role] = item;
                (outcome switch { "created" => created, "adopted" => adopted, _ => existed }).Add(item.Mention);
            }
            catch (HttpException ex)
            {
                problems.Add($"{target.Label}: Discord refused ({ex.Message}).");
            }
        }

        if (items.TryGetValue(ChannelRole.CabinetRole, out var cabinetRole) && cabinetRole.Role is { } role
            && (role.Colors.PrimaryColor != Color.Default || role.IsHoisted))
        {
            try
            {
                await role.ModifyAsync(p =>
                {
                    p.Color = Color.Default;
                    p.Hoist = false;
                });
            }
            catch (HttpException ex)
            {
                problems.Add($"Cabinet role: could not clear its colour or hoist ({ex.Message}).");
            }
        }

        foreach (var item in items.Values.Where(i => i.Channel is not null))
        {
            try
            {
                await ApplySetupPermissionsAsync(guild, item, items);
            }
            catch (HttpException ex)
            {
                problems.Add($"{item.Target.Label}: could not set its permissions ({ex.Message}).");
            }
        }

        var syncs = new List<string>
        {
            await SetupSyncAsync("Faction channels", () => _factionChannels.SyncAllAsync()),
            await SetupSyncAsync("Party roles and chats", () => _partySync.SyncAllAsync()),
            await SetupSyncAsync("Cabinet role", () => _cabinetSync.SyncAsync()),
        };

        var needs = SetupNeeds(guild);
        _data.Log(LogKinds.GmOverride, "gm setup",
            $"Existed {existed.Count}, adopted {adopted.Count}, created {created.Count}, problems {problems.Count}.", Context.User.Id);

        var lines = new List<string> { "**Game setup**" };
        if (existed.Count > 0) lines.Add($"✅ Already set: {string.Join(", ", existed)}");
        if (adopted.Count > 0) lines.Add($"✅ Found by name and registered: {string.Join(", ", adopted)}");
        if (created.Count > 0) lines.Add($"🆕 Created: {string.Join(", ", created)}");
        lines.AddRange(problems.Select(p => $"⚠️ {p}"));
        lines.Add("**Sync**");
        lines.AddRange(syncs);
        lines.Add(SetupIntentsLine(guild));
        lines.Add("**Still needs a hand**");
        lines.AddRange(needs.Count > 0 ? needs.Select(n => $"⚠️ {n}") : ["✅ Nothing."]);

        await FollowupAsync(Display.Truncate(string.Join("\n", lines)), ephemeral: true, allowedMentions: AllowedMentions.None);
    }

    private async Task<(SetupItem Item, string Outcome)> EnsureSetupTargetAsync(SocketGuild guild, SetupTarget target)
    {
        if (_data.GetChannel(target.Role) is { } registeredId && FindSetupTarget(guild, target, registeredId) is { } registered)
        {
            RegisterSetupTarget(guild, registered);
            return (registered, "existed");
        }

        if (FindSetupTargetByName(guild, target) is { } named)
        {
            RegisterSetupTarget(guild, named);
            return (named, "adopted");
        }

        var made = target.Kind switch
        {
            SetupKind.Role => RoleItem(target, await guild.CreateRoleAsync(target.Name, GuildPermissions.None, color: null, isHoisted: false, isMentionable: false)),
            SetupKind.Category => ChannelItem(target, await guild.CreateCategoryChannelAsync(target.Name)),
            _ => ChannelItem(target, await guild.CreateTextChannelAsync(target.Name)),
        };
        RegisterSetupTarget(guild, made);
        return (made, "created");
    }

    private static SetupItem RoleItem(SetupTarget target, IRole role) => new(target, role.Id, role, null);

    private static SetupItem ChannelItem(SetupTarget target, IGuildChannel channel) => new(target, channel.Id, null, channel);

    private static SetupItem? FindSetupTarget(SocketGuild guild, SetupTarget target, ulong id) => target.Kind switch
    {
        SetupKind.Role => guild.GetRole(id) is { } r ? RoleItem(target, r) : null,
        SetupKind.Category => guild.GetCategoryChannel(id) is { } c ? ChannelItem(target, c) : null,
        _ => guild.GetTextChannel(id) is { } t && IsPlainText(t) ? ChannelItem(target, t) : null,
    };

    private static SetupItem? FindSetupTargetByName(SocketGuild guild, SetupTarget target)
    {
        bool Named(string name) => name.Equals(target.Name, StringComparison.OrdinalIgnoreCase);
        return target.Kind switch
        {
            SetupKind.Role => guild.Roles.FirstOrDefault(r => Named(r.Name) && !r.IsManaged && r.Id != guild.EveryoneRole.Id)
                is { } r ? RoleItem(target, r) : null,
            SetupKind.Category => guild.CategoryChannels.OrderBy(c => c.Position).FirstOrDefault(c => Named(c.Name))
                is { } c ? ChannelItem(target, c) : null,
            _ => guild.TextChannels.Where(IsPlainText).OrderBy(t => t.Position).FirstOrDefault(t => Named(t.Name))
                is { } t ? ChannelItem(target, t) : null,
        };
    }

    private static bool IsPlainText(SocketTextChannel channel) =>
        channel is not IVoiceChannel and not SocketThreadChannel;

    private void RegisterSetupTarget(SocketGuild guild, SetupItem item)
    {
        if (_data.GetChannel(item.Target.Role) != item.Id)
        {
            _data.SetChannel(item.Target.Role, item.Id);
            _data.Log(LogKinds.GmOverride, "gm setup", $"{item.Target.Role} = {item.Id}", Context.User.Id);
        }
        if (item.Target.Kind == SetupKind.Text)
            _data.AddWhitelistedChannel(guild.Id, item.Id, Context.User.Id);
    }

    private async Task ApplySetupPermissionsAsync(SocketGuild guild, SetupItem item, IReadOnlyDictionary<ChannelRole, SetupItem> items)
    {
        var channel = item.Channel!;
        var gm = items.GetValueOrDefault(ChannelRole.GmRole)?.Role;
        var cabinet = items.GetValueOrDefault(ChannelRole.CabinetRole)?.Role;

        var roles = new List<(IRole Role, OverwritePermissions Permissions)>();
        switch (item.Target.Role)
        {
            case ChannelRole.FactionCategory or ChannelRole.PartyCategory or ChannelRole.ArchiveCategory:
                roles.Add((guild.EveryoneRole, SetupHidden));
                if (gm is not null) roles.Add((gm, SetupGmCategory));
                break;
            case ChannelRole.Gm:
                roles.Add((guild.EveryoneRole, SetupHidden));
                if (gm is not null) roles.Add((gm, SetupGm));
                break;
            case ChannelRole.Cabinet:
                roles.Add((guild.EveryoneRole, SetupHidden));
                if (cabinet is not null) roles.Add((cabinet, SetupMember));
                if (gm is not null) roles.Add((gm, SetupGm));
                break;
            default:
                roles.Add((guild.EveryoneRole, SetupReadOnly));
                break;
        }

        if (!Same(channel.GetPermissionOverwrite(guild.CurrentUser), SetupBot))
            await channel.AddPermissionOverwriteAsync(guild.CurrentUser, SetupBot);
        foreach (var (role, permissions) in roles)
            if (!Same(channel.GetPermissionOverwrite(role), permissions))
                await channel.AddPermissionOverwriteAsync(role, permissions);
    }

    private static bool Same(OverwritePermissions? current, OverwritePermissions wanted) =>
        current is { } c && c.AllowValue == wanted.AllowValue && c.DenyValue == wanted.DenyValue;

    private static async Task<string> SetupSyncAsync(string what, Func<Task<IReadOnlyList<string>>> sync)
    {
        try
        {
            var problems = await sync();
            return problems.Count == 0
                ? $"✅ {what}: in step with the database."
                : $"⚠️ {what}: {problems.Count} problem(s), see #gm. First: {Display.Truncate(problems[0], 200)}";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GmSetup] {what} sync failed: {ex}");
            return $"⚠️ {what}: sync failed ({Display.Truncate(ex.Message, 200)}).";
        }
    }

    private List<string> SetupNeeds(SocketGuild guild)
    {
        var needs = new List<string>();
        if (_data.GetChannel(ChannelRole.TurnTracker) is not { } tracker || guild.GetVoiceChannel(tracker) is null)
            needs.Add("Turn tracker: make a voice channel, deny Connect to @everyone, then `/gm channel set kind:TurnTracker`.");

        var bot = guild.CurrentUser;
        var missing = new List<string>();
        if (!bot.GuildPermissions.ManageChannels) missing.Add("Manage Channels");
        if (!bot.GuildPermissions.ManageRoles) missing.Add("Manage Roles");
        if (missing.Count > 0) needs.Add($"The bot's role lacks {string.Join(" and ", missing)}.");

        var above = _data.GetAllParties()
            .Where(p => p.RoleId is not null)
            .Select(p => guild.GetRole(p.RoleId!.Value))
            .Append(_data.GetChannel(ChannelRole.CabinetRole) is { } cabinetId ? guild.GetRole(cabinetId) : null)
            .OfType<SocketRole>()
            .Where(r => r.Position >= bot.Hierarchy)
            .Select(r => r.Mention)
            .ToList();
        if (above.Count > 0)
            needs.Add($"Move the bot's role above {string.Join(", ", above)} in Server Settings → Roles, or it can't assign them.");

        return needs;
    }

    private static string SetupIntentsLine(SocketGuild guild) =>
        guild.HasAllMembers
            ? "✅ Privileged intents (Server Members, Message Content): enabled, since the bot connected while requesting both."
            : $"⚠️ Privileged intents: only {guild.DownloadedMemberCount} of {guild.MemberCount} members are loaded; check Server Members and Message Content in the Developer Portal.";
}
