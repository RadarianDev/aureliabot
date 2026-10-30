using System.Text;
using Discord;
using Discord.WebSocket;

public class PartySyncService
{
    public const double SimilarColourThreshold = 20;

    private const ChannelPermission MemberPermissions =
        ChannelPermission.ViewChannel | ChannelPermission.SendMessages | ChannelPermission.ReadMessageHistory | ChannelPermission.PinMessages;

    private const ChannelPermission GmPermissions =
        ChannelPermission.ViewChannel | ChannelPermission.SendMessages | ChannelPermission.ReadMessageHistory;

    private const ChannelPermission BotPermissions =
        ChannelPermission.ViewChannel | ChannelPermission.SendMessages | ChannelPermission.ReadMessageHistory
        | ChannelPermission.ManageChannels | ChannelPermission.ManageMessages | ChannelPermission.PinMessages;

    private const ChannelPermission ArchiveReadPermissions = ChannelPermission.ViewChannel | ChannelPermission.ReadMessageHistory;

    private const ChannelPermission ArchiveDeniedPermissions =
        ChannelPermission.SendMessages | ChannelPermission.SendMessagesInThreads | ChannelPermission.CreatePublicThreads
        | ChannelPermission.CreatePrivateThreads | ChannelPermission.AddReactions | ChannelPermission.PinMessages;

    private readonly DiscordSocketClient _client;
    private readonly DatabaseService _data;
    private readonly AlertService _alerts;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public PartySyncService(DiscordSocketClient client, DatabaseService data, AlertService alerts)
    {
        _client = client;
        _data = data;
        _alerts = alerts;
    }

    public async Task<IReadOnlyList<string>> SyncAsync(long partyId)
    {
        await _lock.WaitAsync();
        try
        {
            var problems = new List<string>();
            if (await GuildAsync(problems) is { } guild)
                await SyncPartyAsync(guild, partyId, problems);
            return problems;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<string>> SyncAllAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var problems = new List<string>();
            if (await GuildAsync(problems) is { } guild)
                foreach (var party in _data.GetAllParties())
                    await SyncPartyAsync(guild, party.Id, problems);
            return problems;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SyncMemberAsync(ulong userId)
    {
        await _lock.WaitAsync();
        try
        {
            var problems = new List<string>();
            if (await GuildAsync(problems) is { } guild)
                await SyncMemberAsync(guild, userId, problems);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ArchiveAsync(Party deleted)
    {
        await _lock.WaitAsync();
        try
        {
            var problems = new List<string>();
            if (await GuildAsync(problems) is { } guild)
                await ArchiveAsync(guild, deleted, problems);
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task HandleDissolutionAsync(Party dissolved) => HandleDissolutionAsync(dissolved, null, null);

    public async Task HandleDissolutionAsync(Party dissolved, ulong? departedId, ulong? lastMemberId = null)
    {
        var problems = new List<string>();
        var formerHolders = new List<ulong>();

        await _lock.WaitAsync();
        try
        {
            if (await GuildAsync(problems) is { } guild)
            {
                if (dissolved.RoleId is { } roleId)
                    formerHolders.AddRange(guild.Users.Where(u => u.Roles.Any(r => r.Id == roleId)).Select(u => u.Id));
                await ArchiveAsync(guild, dissolved, problems);
                foreach (var userId in formerHolders)
                    await SyncMemberAsync(guild, userId, problems);
            }
        }
        finally
        {
            _lock.Release();
        }

        var lastMembers = formerHolders
            .Where(id => id != departedId)
            .Select(id => _data.GetFaction(id))
            .OfType<Faction>()
            .Where(f => f.IsActive && f.IsIndependent)
            .ToList();
        var last = lastMemberId is { } knownLast ? _data.GetFaction(knownLast) : lastMembers.Count == 1 ? lastMembers[0] : null;

        var news = last is null
            ? Texts.Get("news.party.dissolvedEmpty", ("party", dissolved.Name))
            : Texts.Get("news.party.dissolvedLast", ("party", dissolved.Name), ("faction", Display.Faction(last)), ("seats", SeatsText(last.Seats)));
        var newsProblems = new List<string>();
        await PostNewsAsync(news, "party dissolution", newsProblems);

        await _alerts.PostAsync(
            Texts.Get("alerts.party.dissolved", ("party", dissolved.Name), ("id", dissolved.Id),
                ("last", last is null ? Texts.Get("alerts.party.lastUnknown") : Texts.Get("alerts.party.lastKnown", ("faction", Display.Faction(last)))),
                ("outcome", problems.Count == 0 ? Texts.Get("alerts.party.cleanedUp") : Texts.Get("alerts.party.changesFailed", ("count", problems.Count))))
            + (newsProblems.Count > 0 ? "\n" + string.Join("\n", newsProblems) : ""));
    }

    public static double ColourDistance(string a, string b)
    {
        var (l1, a1, b1) = ToLab(a);
        var (l2, a2, b2) = ToLab(b);
        return Math.Sqrt((l1 - l2) * (l1 - l2) + (a1 - a2) * (a1 - a2) + (b1 - b2) * (b1 - b2));
    }

    public static bool AreSimilarColours(string a, string b) => ColourDistance(a, b) < SimilarColourThreshold;

    public static string ChannelName(string partyName)
    {
        var name = ChannelSlug(partyName);
        return name.Length == 0 ? Texts.Get("sync.party.channelNameFallback") : name;
    }

    public static string ChannelSlug(string partyName)
    {
        var slug = new StringBuilder();
        foreach (var c in partyName.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c) || c == '_')
                slug.Append(c);
            else if (slug.Length > 0 && slug[^1] != '-')
                slug.Append('-');
        }
        var name = slug.ToString().Trim('-');
        return name.Length > 100 ? name[..100].Trim('-') : name;
    }

    public static Color ParseColour(string hex) =>
        ConstantsStore.IsHexColour(hex) ? new Color(Convert.ToUInt32(hex[1..], 16)) : Color.Default;

    private static (double L, double A, double B) ToLab(string hex)
    {
        var rgb = ParseColour(hex);
        var r = Linear(rgb.R / 255.0);
        var g = Linear(rgb.G / 255.0);
        var b = Linear(rgb.B / 255.0);

        var x = (0.4124564 * r + 0.3575761 * g + 0.1804375 * b) / 0.95047;
        var y = 0.2126729 * r + 0.7151522 * g + 0.0721750 * b;
        var z = (0.0193339 * r + 0.1191920 * g + 0.9503041 * b) / 1.08883;

        var fx = LabF(x);
        var fy = LabF(y);
        var fz = LabF(z);
        return (116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
    }

    private static double Linear(double channel) =>
        channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);

    private static double LabF(double t) =>
        t > 216.0 / 24389 ? Math.Cbrt(t) : (24389.0 / 27 * t + 16) / 116;

    private static string SeatsText(long seats) => seats == 1 ? Texts.Get("news.party.oneSeat") : Texts.Get("news.party.seats", ("seats", seats));

    private async Task SyncPartyAsync(SocketGuild guild, long partyId, List<string> problems)
    {
        if (_data.GetParty(partyId) is not { } party)
        {
            problems.Add(Texts.Get("sync.party.missing", ("id", partyId)));
            return;
        }

        var role = await EnsureRoleAsync(guild, party, problems);
        if (role is not null)
            await SyncRoleMembersAsync(guild, party, role, problems);
        var channel = await EnsureChannelAsync(guild, party, role, problems);

        var roleId = role?.Id ?? party.RoleId;
        var channelId = channel?.Id ?? party.ChannelId;
        if (roleId != party.RoleId || channelId != party.ChannelId)
            _data.SetPartyDiscordIds(party.Id, roleId, channelId);
    }

    private async Task<IRole?> EnsureRoleAsync(SocketGuild guild, Party party, List<string> problems)
    {
        var colour = ParseColour(party.Colour);
        var colours = new RoleColors(colour, null, null);
        IRole? role = party.RoleId is { } id ? guild.GetRole(id) : null;

        if (role is null)
            return await TryAsync(problems, Texts.Get("sync.party.createRole", ("party", party.Name)),
                async () => (IRole)await guild.CreateRoleAsync(party.Name, GuildPermissions.None, colours, isHoisted: true, isMentionable: false));

        if (role.Name != party.Name || role.Colors.PrimaryColor.RawValue != colour.RawValue
            || role.Colors.SecondaryColor is not null || !role.IsHoisted)
            await TryAsync(problems, Texts.Get("sync.party.updateRole", ("role", role.Name), ("party", party.Name)),
                () => role.ModifyAsync(p =>
                {
                    p.Name = party.Name;
                    p.Colors = colours;
                    p.Hoist = true;
                }));
        return role;
    }

    private async Task SyncRoleMembersAsync(SocketGuild guild, Party party, IRole role, List<string> problems)
    {
        var members = _data.GetPartyMembers(party.Id).Select(f => f.UserId).ToHashSet();

        foreach (var userId in members)
        {
            if (guild.GetUser(userId) is not { } user || user.Roles.Any(r => r.Id == role.Id)) continue;
            await TryAsync(problems, Texts.Get("sync.party.giveRole", ("user", user.Username), ("party", party.Name)), () => user.AddRoleAsync(role));
        }

        foreach (var user in guild.Users.Where(u => !members.Contains(u.Id) && u.Roles.Any(r => r.Id == role.Id)).ToList())
            await TryAsync(problems, Texts.Get("sync.party.takeRole", ("user", user.Username), ("party", party.Name)), () => user.RemoveRoleAsync(role));
    }

    private async Task<ITextChannel?> EnsureChannelAsync(SocketGuild guild, Party party, IRole? role, List<string> problems)
    {
        var categoryId = _data.GetChannel(ChannelRole.PartyCategory);
        if (categoryId is not null && guild.GetCategoryChannel(categoryId.Value) is null)
        {
            problems.Add(Texts.Get("sync.party.categoryGone", ("category", categoryId)));
            await _alerts.PostAsync(problems[^1]);
            categoryId = null;
        }

        var overwrites = ChatOverwrites(guild, role);
        var name = ChannelName(party.Name);
        ITextChannel? channel = party.ChannelId is { } id ? guild.GetTextChannel(id) : null;

        if (channel is null)
        {
            if (categoryId is null)
            {
                problems.Add(Texts.Get("sync.party.noCategory", ("party", party.Name)));
                await _alerts.PostAsync(problems[^1]);
                return null;
            }
            return await TryAsync(problems, Texts.Get("sync.party.createChat", ("party", party.Name)),
                async () => (ITextChannel)await guild.CreateTextChannelAsync(name, p =>
                {
                    p.CategoryId = categoryId.Value;
                    p.PermissionOverwrites = overwrites;
                }));
        }

        var needsName = channel.Name != name;
        var needsCategory = categoryId is not null && channel.CategoryId != categoryId;
        var needsOverwrites = overwrites.Any(o => channel.PermissionOverwrites.All(e =>
            e.TargetId != o.TargetId || e.TargetType != o.TargetType
            || e.Permissions.AllowValue != o.Permissions.AllowValue || e.Permissions.DenyValue != o.Permissions.DenyValue));

        if (needsName || needsCategory || needsOverwrites)
        {
            var kept = channel.PermissionOverwrites.Where(e => overwrites.All(o => o.TargetId != e.TargetId));
            await TryAsync(problems, Texts.Get("sync.party.updateChat", ("channel", channel.Id), ("party", party.Name)),
                () => channel.ModifyAsync(p =>
                {
                    if (needsName) p.Name = name;
                    if (needsCategory) p.CategoryId = categoryId!.Value;
                    if (needsOverwrites) p.PermissionOverwrites = kept.Concat(overwrites).ToList();
                }));
        }
        return channel;
    }

    private List<Overwrite> ChatOverwrites(SocketGuild guild, IRole? partyRole)
    {
        var overwrites = new List<Overwrite>
        {
            new(guild.EveryoneRole.Id, PermissionTarget.Role, new OverwritePermissions(0, (ulong)ChannelPermission.ViewChannel)),
            new(guild.CurrentUser.Id, PermissionTarget.User, new OverwritePermissions((ulong)BotPermissions, 0)),
        };
        if (partyRole is not null)
            overwrites.Add(new(partyRole.Id, PermissionTarget.Role, new OverwritePermissions((ulong)MemberPermissions, 0)));
        if (GmRole(guild) is { } gmRole)
            overwrites.Add(new(gmRole.Id, PermissionTarget.Role, new OverwritePermissions((ulong)GmPermissions, 0)));
        return overwrites;
    }

    private SocketRole? GmRole(SocketGuild guild) =>
        _data.GetChannel(ChannelRole.GmRole) is { } id ? guild.GetRole(id) : null;

    private async Task SyncMemberAsync(SocketGuild guild, ulong userId, List<string> problems)
    {
        if (guild.GetUser(userId) is not { } user) return;
        var faction = _data.GetFaction(userId);
        var target = faction is { IsActive: true } ? faction.PartyId : null;

        foreach (var party in _data.GetAllParties())
        {
            var role = party.RoleId is { } roleId ? guild.GetRole(roleId) : null;
            var holds = role is not null && user.Roles.Any(r => r.Id == role.Id);

            if (party.Id == target)
            {
                if (role is null)
                    await SyncPartyAsync(guild, party.Id, problems);
                else if (!holds)
                    await TryAsync(problems, Texts.Get("sync.party.giveRole", ("user", user.Username), ("party", party.Name)), () => user.AddRoleAsync(role));
            }
            else if (holds)
            {
                await TryAsync(problems, Texts.Get("sync.party.takeRole", ("user", user.Username), ("party", party.Name)), () => user.RemoveRoleAsync(role!));
            }
        }
    }

    private async Task ArchiveAsync(SocketGuild guild, Party deleted, List<string> problems)
    {
        if (deleted.RoleId is { } roleId && guild.GetRole(roleId) is { } role)
            await TryAsync(problems, Texts.Get("sync.party.deleteRole", ("party", deleted.Name)), () => role.DeleteAsync());

        if (deleted.ChannelId is not { } channelId || guild.GetTextChannel(channelId) is not { } channel) return;

        var archiveId = _data.GetChannel(ChannelRole.ArchiveCategory);
        if (archiveId is not null && guild.GetCategoryChannel(archiveId.Value) is null) archiveId = null;
        if (archiveId is null)
        {
            problems.Add(Texts.Get("sync.party.noArchive", ("channel", channel.Id), ("party", deleted.Name)));
            await _alerts.PostAsync(problems[^1]);
        }

        var overwrites = new List<Overwrite>
        {
            new(guild.EveryoneRole.Id, PermissionTarget.Role, new OverwritePermissions(0, (ulong)ChannelPermission.ViewChannel)),
            new(guild.CurrentUser.Id, PermissionTarget.User, new OverwritePermissions((ulong)BotPermissions, 0)),
        };
        if (GmRole(guild) is { } gmRole)
            overwrites.Add(new(gmRole.Id, PermissionTarget.Role,
                new OverwritePermissions((ulong)ArchiveReadPermissions, (ulong)ArchiveDeniedPermissions)));

        await TryAsync(problems, Texts.Get("sync.party.archiveChat", ("channel", channel.Id), ("party", deleted.Name)),
            () => channel.ModifyAsync(p =>
            {
                if (archiveId is not null) p.CategoryId = archiveId.Value;
                p.PermissionOverwrites = overwrites;
            }));
    }

    private async Task PostNewsAsync(string text, string source, List<string> problems)
    {
        try
        {
            if (_data.GetChannel(ChannelRole.News) is { } newsId && _client.GetChannel(newsId) is IMessageChannel news)
            {
                var message = await news.SendMessageAsync(text, allowedMentions: AllowedMentions.None);
                _data.LogPost(newsId, message.Id, source);
            }
            else
            {
                problems.Add(Texts.Get("sync.party.noNews"));
            }
        }
        catch (Exception ex)
        {
            problems.Add(Texts.Get("sync.party.newsFailed", ("error", ex.Message)));
        }
    }

    private async Task<SocketGuild?> GuildAsync(List<string> problems)
    {
        var guild = FindGuild();
        if (guild is null)
        {
            problems.Add(Texts.Get("sync.party.noGuild"));
            return null;
        }
        if (!guild.HasAllMembers)
        {
            try { await guild.DownloadUsersAsync(); }
            catch (Exception ex) { Console.WriteLine($"[PartySync] Could not download members: {ex.Message}"); }
        }
        return guild;
    }

    private SocketGuild? FindGuild()
    {
        foreach (var kind in new[] { ChannelRole.PartyCategory, ChannelRole.News, ChannelRole.Gm, ChannelRole.ArchiveCategory })
            if (_data.GetChannel(kind) is { } id && _client.GetChannel(id) is SocketGuildChannel channel)
                return channel.Guild;
        return _client.Guilds.FirstOrDefault();
    }

    private async Task TryAsync(List<string> problems, string what, Func<Task> action) =>
        await TryAsync<object?>(problems, what, async () =>
        {
            await action();
            return null;
        });

    private async Task<T?> TryAsync<T>(List<string> problems, string what, Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex)
        {
            var problem = Texts.Get("sync.discordRefused", ("what", what), ("error", ex.Message));
            problems.Add(problem);
            await _alerts.PostAsync(problem);
            return default;
        }
    }
}
