using System.Text;
using Discord;
using Discord.WebSocket;

public enum ForeignGuestRefusal { NotInParty, Bot, OwnMember }

public enum ForeignGuestOutcome { Added, AlreadyGuest, Removed, NotGuest, NoChannel, Failed }

public sealed record ForeignGuestResult(ForeignGuestOutcome Outcome, ITextChannel? Channel = null, string? Error = null);

public class PartySyncService
{
    public const double SimilarColourThreshold = 20;

    private const int ChannelStemLimit = 80;

    public const ChannelPermission GuestPermissions =
        ChannelPermission.ViewChannel | ChannelPermission.SendMessages | ChannelPermission.ReadMessageHistory
        | ChannelPermission.AttachFiles | ChannelPermission.EmbedLinks | ChannelPermission.AddReactions;

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
            {
                await SyncPartyAsync(guild, partyId, problems);
                await OrderRolesAsync(guild, problems);
            }
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
            {
                foreach (var party in _data.GetAllParties())
                    await SyncPartyAsync(guild, party.Id, problems);
                await OrderRolesAsync(guild, problems);
            }
            return problems;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<string>> OrderRolesAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var problems = new List<string>();
            if (await GuildAsync(problems) is { } guild)
                await OrderRolesAsync(guild, problems);
            return problems;
        }
        finally
        {
            _lock.Release();
        }
    }

    public static IReadOnlyDictionary<ulong, int> RolePositionsBySeats(IReadOnlyList<(ulong RoleId, int Position, long Seats, long PartyId)> roles)
    {
        var slots = roles.Select(r => r.Position).OrderByDescending(p => p).ToList();
        return roles
            .OrderByDescending(r => r.Seats)
            .ThenBy(r => r.PartyId)
            .Select((r, i) => (r.RoleId, Position: slots[i]))
            .ToDictionary(r => r.RoleId, r => r.Position);
    }

    private async Task OrderRolesAsync(SocketGuild guild, List<string> problems)
    {
        var roles = _data.GetAllParties()
            .Select(p => (Party: p, Role: p.RoleId is { } id ? guild.GetRole(id) : null))
            .Where(x => x.Role is not null)
            .Select(x => (x.Role!.Id, x.Role.Position, _data.GetPartyMembers(x.Party.Id).Sum(f => f.Seats), x.Party.Id))
            .ToList();
        var targets = RolePositionsBySeats(roles);
        if (roles.All(r => targets[r.Item1] == r.Position)) return;

        await TryAsync(problems, Texts.Get("sync.party.reorderRoles"),
            () => guild.ReorderRolesAsync(targets.Select(t => new ReorderRoleProperties(t.Key, t.Value))));
    }

    public async Task SyncMemberAsync(ulong userId)
    {
        await _lock.WaitAsync();
        try
        {
            var problems = new List<string>();
            if (await GuildAsync(problems) is { } guild)
            {
                await SyncMemberAsync(guild, userId, problems);
                await OrderRolesAsync(guild, problems);
            }
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
        var name = Abbreviation(partyName);
        if (name.Length > ChannelStemLimit) name = name[..ChannelStemLimit].Trim('-');
        return name.Length == 0 ? Texts.Get("sync.party.channelNameFallback") : name;
    }

    public static IReadOnlyDictionary<long, string> ChatNames(IEnumerable<Party> parties)
    {
        var names = new Dictionary<long, string>();
        var taken = new HashSet<string>(StringComparer.Ordinal);
        foreach (var party in parties.OrderBy(p => p.Id))
        {
            var stem = ChannelName(party.Name);
            var name = stem;
            for (var n = 2; !taken.Add(name); n++)
                name = $"{stem}-{n}";
            names[party.Id] = name;
        }
        return names;
    }

    public static string ForeignChannelName(string chatName) => $"{chatName}-{Texts.Get("sync.party.foreignChannelSuffix")}";

    public static string ChannelStem(string channelName)
    {
        var foreignSuffix = "-" + Texts.Get("sync.party.foreignChannelSuffix");
        var name = channelName.EndsWith(foreignSuffix, StringComparison.OrdinalIgnoreCase) ? channelName[..^foreignSuffix.Length] : channelName;
        var dash = name.LastIndexOf('-');
        return dash > 0 && dash < name.Length - 1 && name[(dash + 1)..].All(char.IsAsciiDigit) ? name[..dash] : name;
    }

    public static string Abbreviation(string partyName)
    {
        var initials = new StringBuilder();
        var words = 0;
        var inWord = false;
        var initialTaken = false;
        foreach (var c in partyName.ToLowerInvariant())
        {
            if (IsWordBreak(c))
            {
                inWord = false;
                continue;
            }
            if (!inWord)
            {
                inWord = true;
                initialTaken = false;
            }
            if (initialTaken || !char.IsLetterOrDigit(c)) continue;
            initials.Append(c);
            initialTaken = true;
            words++;
        }
        return words == 1 ? ChannelSlug(partyName) : initials.ToString();
    }

    private static bool IsWordBreak(char c) =>
        char.IsWhiteSpace(c) || c == '/' || c == '_' || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.DashPunctuation;

    public static ForeignGuestRefusal? GuestRefusal(Faction? actor, bool targetIsBot, Faction? target)
    {
        if (actor is not { IsActive: true, PartyId: { } partyId }) return ForeignGuestRefusal.NotInParty;
        if (targetIsBot) return ForeignGuestRefusal.Bot;
        if (target is { IsActive: true } && target.PartyId == partyId) return ForeignGuestRefusal.OwnMember;
        return null;
    }

    public static IReadOnlyList<ulong> Guests(IEnumerable<Overwrite> overwrites, ulong botId) =>
        overwrites.Where(o => o.TargetType == PermissionTarget.User && o.TargetId != botId).Select(o => o.TargetId).ToList();

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
        var chatName = ChatNames(_data.GetAllParties()).TryGetValue(party.Id, out var assigned) ? assigned : ChannelName(party.Name);
        var categoryId = await PartyCategoryAsync(guild, problems);
        var overwrites = ChatOverwrites(guild, role);

        var channel = await EnsureChannelAsync(guild, party.ChannelId, chatName, categoryId, overwrites, problems,
            Texts.Get("sync.party.noCategory", ("party", party.Name)),
            Texts.Get("sync.party.createChat", ("party", party.Name)),
            id => Texts.Get("sync.party.updateChat", ("channel", id), ("party", party.Name)));
        var foreign = await EnsureChannelAsync(guild, party.ForeignChannelId, ForeignChannelName(chatName), categoryId, overwrites, problems,
            Texts.Get("sync.party.noCategoryForeign", ("party", party.Name)),
            Texts.Get("sync.party.createForeign", ("party", party.Name)),
            id => Texts.Get("sync.party.updateForeign", ("channel", id), ("party", party.Name)));

        var roleId = role?.Id ?? party.RoleId;
        var channelId = channel?.Id ?? party.ChannelId;
        var foreignId = foreign?.Id ?? party.ForeignChannelId;
        if (roleId != party.RoleId || channelId != party.ChannelId || foreignId != party.ForeignChannelId)
            _data.SetPartyDiscordIds(party.Id, roleId, channelId, foreignId);
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

    private async Task<ulong?> PartyCategoryAsync(SocketGuild guild, List<string> problems)
    {
        var categoryId = _data.GetChannel(ChannelRole.PartyCategory);
        if (categoryId is null || guild.GetCategoryChannel(categoryId.Value) is not null) return categoryId;
        problems.Add(Texts.Get("sync.party.categoryGone", ("category", categoryId)));
        await _alerts.PostAsync(problems[^1]);
        return null;
    }

    private async Task<ITextChannel?> EnsureChannelAsync(
        SocketGuild guild, ulong? storedId, string name, ulong? categoryId, List<Overwrite> overwrites, List<string> problems,
        string noCategory, string create, Func<ulong, string> update)
    {
        ITextChannel? channel = storedId is { } id ? guild.GetTextChannel(id) : null;

        if (channel is null)
        {
            if (categoryId is null)
            {
                problems.Add(noCategory);
                await _alerts.PostAsync(problems[^1]);
                return null;
            }
            return await TryAsync(problems, create,
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
            await TryAsync(problems, update(channel.Id),
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

        var channels = new[] { deleted.ChannelId, deleted.ForeignChannelId }
            .Select(id => id is { } channelId ? guild.GetTextChannel(channelId) : null)
            .OfType<SocketTextChannel>()
            .ToList();
        if (channels.Count == 0) return;

        var archiveId = _data.GetChannel(ChannelRole.ArchiveCategory);
        if (archiveId is not null && guild.GetCategoryChannel(archiveId.Value) is null) archiveId = null;
        if (archiveId is null)
        {
            problems.Add(Texts.Get("sync.party.noArchive",
                ("channels", string.Join(", ", channels.Select(c => $"<#{c.Id}>"))), ("party", deleted.Name)));
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

        foreach (var channel in channels)
            await TryAsync(problems, Texts.Get("sync.party.archiveChat", ("channel", channel.Id), ("party", deleted.Name)),
                () => channel.ModifyAsync(p =>
                {
                    if (archiveId is not null) p.CategoryId = archiveId.Value;
                    p.PermissionOverwrites = overwrites;
                }));
    }

    public async Task<ForeignGuestResult> AddForeignGuestAsync(long partyId, IUser guest) =>
        await WithForeignChannelAsync(partyId, Texts.Get("sync.party.addGuest", ("user", guest.Username)), async channel =>
        {
            if (channel.GetPermissionOverwrite(guest) is { } existing && existing.AllowValue == (ulong)GuestPermissions)
                return ForeignGuestOutcome.AlreadyGuest;
            await channel.AddPermissionOverwriteAsync(guest, new OverwritePermissions((ulong)GuestPermissions, 0));
            return ForeignGuestOutcome.Added;
        });

    public async Task<ForeignGuestResult> RemoveForeignGuestAsync(long partyId, IUser guest) =>
        await WithForeignChannelAsync(partyId, Texts.Get("sync.party.removeGuest", ("user", guest.Username)), async channel =>
        {
            if (channel.GetPermissionOverwrite(guest) is null) return ForeignGuestOutcome.NotGuest;
            await channel.RemovePermissionOverwriteAsync(guest);
            return ForeignGuestOutcome.Removed;
        });

    public async Task<ITextChannel?> ForeignChannelAsync(long partyId)
    {
        await _lock.WaitAsync();
        try
        {
            return await FindOrCreateForeignChannelAsync(partyId);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<ITextChannel?> FindOrCreateForeignChannelAsync(long partyId)
    {
        var problems = new List<string>();
        if (await GuildAsync(problems) is not { } guild) return null;
        if (ForeignChannel(guild, partyId) is { } found) return found;
        await SyncPartyAsync(guild, partyId, problems);
        return ForeignChannel(guild, partyId);
    }

    private async Task<ForeignGuestResult> WithForeignChannelAsync(long partyId, string what, Func<ITextChannel, Task<ForeignGuestOutcome>> action)
    {
        await _lock.WaitAsync();
        try
        {
            if (await FindOrCreateForeignChannelAsync(partyId) is not { } channel) return new ForeignGuestResult(ForeignGuestOutcome.NoChannel);

            try
            {
                return new ForeignGuestResult(await action(channel), channel);
            }
            catch (Exception ex)
            {
                await _alerts.PostAsync(Texts.Get("sync.discordRefused", ("what", what), ("error", ex.Message)));
                return new ForeignGuestResult(ForeignGuestOutcome.Failed, channel, ex.Message);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private SocketTextChannel? ForeignChannel(SocketGuild guild, long partyId) =>
        _data.GetParty(partyId)?.ForeignChannelId is { } id ? guild.GetTextChannel(id) : null;

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
