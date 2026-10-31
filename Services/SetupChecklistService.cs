using Discord;
using Discord.WebSocket;

public enum CheckStatus { Ok, Warning, Missing }

public sealed record CheckLine(CheckStatus Status, string Text)
{
    public string Render() => Status switch
    {
        CheckStatus.Ok => Texts.Get("setup.ok", ("text", Text)),
        CheckStatus.Warning => Texts.Get("setup.warning", ("text", Text)),
        _ => Texts.Get("setup.missing", ("text", Text)),
    };
}

public sealed record SetupSuggestion(ChannelRole Role, ulong Id, string Mention);

public class SetupChecklistService
{
    public const string MessageKey = "setup_checklist_message";
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(3);

    private enum Kind { Text, Voice, Category, Role }

    private sealed record Item(ChannelRole Role, Kind Kind, string? MatchName)
    {
        public string Label => Role switch
        {
            ChannelRole.News => Texts.Get("setup.item.news"),
            ChannelRole.Dashboard => Texts.Get("setup.item.dashboard"),
            ChannelRole.Assembly => Texts.Get("setup.item.assembly"),
            ChannelRole.CampaignLog => Texts.Get("setup.item.campaignLog"),
            ChannelRole.Cabinet => Texts.Get("setup.item.cabinet"),
            ChannelRole.Gm => Texts.Get("setup.item.gm"),
            ChannelRole.TurnTracker => Texts.Get("setup.item.turnTracker"),
            ChannelRole.FactionCategory => Texts.Get("setup.item.factionCategory"),
            ChannelRole.PartyCategory => Texts.Get("setup.item.partyCategory"),
            ChannelRole.ArchiveCategory => Texts.Get("setup.item.archiveCategory"),
            ChannelRole.GmRole => Texts.Get("setup.item.gmRole"),
            ChannelRole.CabinetRole => Texts.Get("setup.item.cabinetRole"),
            _ => Role.ToString(),
        };
    }

    private static readonly IReadOnlyList<Item> Items =
    [
        new(ChannelRole.News, Kind.Text, "news"),
        new(ChannelRole.Dashboard, Kind.Text, "dashboard"),
        new(ChannelRole.Assembly, Kind.Text, "assembly"),
        new(ChannelRole.CampaignLog, Kind.Text, "campaign-log"),
        new(ChannelRole.Cabinet, Kind.Text, "cabinet"),
        new(ChannelRole.Gm, Kind.Text, "gm"),
        new(ChannelRole.TurnTracker, Kind.Voice, null),
        new(ChannelRole.FactionCategory, Kind.Category, "factions"),
        new(ChannelRole.PartyCategory, Kind.Category, "parties"),
        new(ChannelRole.ArchiveCategory, Kind.Category, "archive"),
        new(ChannelRole.GmRole, Kind.Role, "gm"),
        new(ChannelRole.CabinetRole, Kind.Role, "cabinet"),
    ];

    private static readonly HashSet<ChannelRole> PublicReadOnly =
        [ChannelRole.News, ChannelRole.Dashboard, ChannelRole.Assembly, ChannelRole.CampaignLog];

    private readonly DiscordSocketClient _client;
    private readonly DatabaseService _data;
    private readonly BotConfig _config;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _scheduled;

    public SetupChecklistService(DiscordSocketClient client, DatabaseService data, BotConfig config)
    {
        _client = client;
        _data = data;
        _config = config;
    }

    public SocketGuild? Guild =>
        _config.GuildId != 0 ? _client.GetGuild(_config.GuildId) : _client.Guilds.FirstOrDefault();

    public void RequestRefresh()
    {
        if (Interlocked.Exchange(ref _scheduled, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(Debounce);
                Interlocked.Exchange(ref _scheduled, 0);
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                Interlocked.Exchange(ref _scheduled, 0);
                Console.WriteLine($"[SetupChecklist] Refresh failed: {ex.Message}");
            }
        });
    }

    public async Task<IUserMessage?> PostAsync(IMessageChannel channel)
    {
        await _gate.WaitAsync();
        try
        {
            var old = await ExistingMessageAsync();
            if (old is not null && old.Channel.Id == channel.Id)
            {
                await old.ModifyAsync(p => { p.Content = Build(); p.Components = Buttons(); p.AllowedMentions = AllowedMentions.None; });
                return old;
            }
            if (old is not null)
                await old.DeleteAsync();

            var message = await channel.SendMessageAsync(Build(), components: Buttons(), allowedMentions: AllowedMentions.None);
            _data.SetSetting(MessageKey, $"{channel.Id}:{message.Id}");
            return message;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RefreshAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (await ExistingMessageAsync() is not { } message) return;
            await message.ModifyAsync(p => { p.Content = Build(); p.Components = Buttons(); p.AllowedMentions = AllowedMentions.None; });
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IUserMessage?> ExistingMessageAsync()
    {
        var stored = _data.GetSetting(MessageKey)?.Split(':');
        if (stored is not [var channelText, var messageText]
            || !ulong.TryParse(channelText, out var channelId) || !ulong.TryParse(messageText, out var messageId)
            || _client.GetChannel(channelId) is not IMessageChannel channel)
            return null;
        try
        {
            return await channel.GetMessageAsync(messageId) as IUserMessage;
        }
        catch (Discord.Net.HttpException)
        {
            return null;
        }
    }

    public static MessageComponent Buttons() => new ComponentBuilder()
        .WithButton(Texts.Get("setup.button.register"), "setup:register", ButtonStyle.Primary)
        .WithButton(Texts.Get("setup.button.repair"), "setup:repair", ButtonStyle.Secondary)
        .WithButton(Texts.Get("setup.button.refresh"), "setup:refresh", ButtonStyle.Secondary)
        .Build();

    public IReadOnlyList<SetupSuggestion> Suggestions()
    {
        if (Guild is not { } guild) return [];
        var registered = _data.GetChannels();
        var suggestions = new List<SetupSuggestion>();
        foreach (var item in Items)
        {
            if (registered.TryGetValue(item.Role, out var id) && Resolve(guild, item, id) is not null) continue;
            if (FindByName(guild, item) is { } match) suggestions.Add(match);
        }
        return suggestions;
    }

    public string Build()
    {
        if (Guild is not { } guild) return Texts.Get("setup.noGuild");
        var registered = _data.GetChannels();
        var me = guild.CurrentUser;
        var sections = new List<(string Title, List<CheckLine> Lines)>();

        var channels = new List<CheckLine>();
        var categories = new List<CheckLine>();
        var roles = new List<CheckLine>();
        foreach (var item in Items)
        {
            var line = Check(guild, me, item, registered);
            (item.Kind switch { Kind.Category => categories, Kind.Role => roles, _ => channels }).Add(line);
        }
        sections.Add((Texts.Get("setup.section.channels"), channels));
        sections.Add((Texts.Get("setup.section.categories"), categories));
        sections.Add((Texts.Get("setup.section.roles"), roles));
        sections.Add((Texts.Get("setup.section.bot"), BotChecks(guild, me, registered)));
        sections.Add((Texts.Get("setup.section.game"), GameChecks(guild)));

        var all = sections.SelectMany(s => s.Lines).ToList();
        var ready = all.Count(l => l.Status == CheckStatus.Ok);
        var text = Texts.Get("setup.heading", ("ready", ready), ("total", all.Count)) + "\n" +
                   string.Join("\n", sections.Select(s =>
                       Texts.Get("setup.sectionTitle", ("title", s.Title)) + "\n" + string.Join("\n", s.Lines.Select(l => l.Render())))) +
                   "\n\n" + Texts.Get("setup.footer", ("when", Display.Timestamp(DateTimeOffset.UtcNow, 'R')));
        return Display.Truncate(text);
    }

    private CheckLine Check(SocketGuild guild, SocketGuildUser me, Item item, IReadOnlyDictionary<ChannelRole, ulong> registered)
    {
        if (!registered.TryGetValue(item.Role, out var id))
        {
            return new(CheckStatus.Missing, FindByName(guild, item) is { } match
                ? Texts.Get("setup.check.notSetFound", ("item", item.Label), ("match", match.Mention))
                : Texts.Get("setup.check.notSet", ("item", item.Label)));
        }

        var resolved = Resolve(guild, item, id);
        if (resolved is null)
            return new(CheckStatus.Missing, Texts.Get("setup.check.gone", ("item", item.Label), ("kind", KindWord(item.Kind))));

        var problems = item.Kind == Kind.Role
            ? RoleProblems(me, item, (SocketRole)resolved)
            : ChannelProblems(guild, me, item, (SocketGuildChannel)resolved, registered);
        var mention = item.Kind switch
        {
            Kind.Role => $"<@&{id}>",
            Kind.Category => $"**{((SocketGuildChannel)resolved).Name}**",
            _ => $"<#{id}>",
        };
        return problems.Count == 0
            ? new(CheckStatus.Ok, Texts.Get("setup.check.ok", ("item", item.Label), ("mention", mention)))
            : new(CheckStatus.Warning, Texts.Get("setup.check.problems", ("item", item.Label), ("mention", mention), ("problems", string.Join("; ", problems))));
    }

    private static object? Resolve(SocketGuild guild, Item item, ulong id) => item.Kind switch
    {
        Kind.Role => guild.GetRole(id),
        Kind.Text => guild.GetTextChannel(id) is { } text and not SocketVoiceChannel ? text : null,
        Kind.Voice => guild.GetVoiceChannel(id),
        Kind.Category => guild.GetCategoryChannel(id),
        _ => null,
    };

    private static SetupSuggestion? FindByName(SocketGuild guild, Item item)
    {
        if (item.MatchName is not { } name) return null;
        bool Matches(string candidate) =>
            string.Equals(Normalise(candidate), name, StringComparison.OrdinalIgnoreCase);
        return item.Kind switch
        {
            Kind.Role => guild.Roles.FirstOrDefault(r => !r.IsEveryone && Matches(r.Name)) is { } role
                ? new SetupSuggestion(item.Role, role.Id, $"<@&{role.Id}>") : null,
            Kind.Category => guild.CategoryChannels.FirstOrDefault(c => Matches(c.Name)) is { } category
                ? new SetupSuggestion(item.Role, category.Id, $"**{category.Name}**") : null,
            Kind.Text => guild.TextChannels.Where(c => c is not SocketVoiceChannel).FirstOrDefault(c => Matches(c.Name)) is { } channel
                ? new SetupSuggestion(item.Role, channel.Id, $"<#{channel.Id}>") : null,
            _ => null,
        };
    }

    private static string Normalise(string name) =>
        new string(name.Where(c => char.IsLetterOrDigit(c) || c == '-').ToArray()).Trim('-');

    private static string KindWord(Kind kind) => kind switch
    {
        Kind.Role => Texts.Get("setup.kind.role"),
        Kind.Category => Texts.Get("setup.kind.category"),
        Kind.Voice => Texts.Get("setup.kind.voice"),
        _ => Texts.Get("setup.kind.text"),
    };

    private static bool EveryoneCan(SocketGuild guild, IGuildChannel channel, ChannelPermission permission)
    {
        var overwrite = channel.GetPermissionOverwrite(guild.EveryoneRole);
        var bit = (ulong)permission;
        if (overwrite is { } o && (o.DenyValue & bit) != 0) return false;
        if (overwrite is { } a && (a.AllowValue & bit) != 0) return true;
        return (guild.EveryoneRole.Permissions.RawValue & bit) != 0;
    }

    private static bool RoleCanView(SocketGuild guild, IGuildChannel channel, IRole role)
    {
        if (role.Permissions.Administrator) return true;
        var canView = guild.EveryoneRole.Permissions.ViewChannel || role.Permissions.ViewChannel;
        var everyone = channel.GetPermissionOverwrite(guild.EveryoneRole);
        if (everyone is { ViewChannel: PermValue.Deny }) canView = false;
        if (everyone is { ViewChannel: PermValue.Allow }) canView = true;
        var own = channel.GetPermissionOverwrite(role);
        if (own is { ViewChannel: PermValue.Deny }) canView = false;
        if (own is { ViewChannel: PermValue.Allow }) canView = true;
        return canView;
    }

    private List<string> ChannelProblems(
        SocketGuild guild, SocketGuildUser me, Item item, SocketGuildChannel channel, IReadOnlyDictionary<ChannelRole, ulong> registered)
    {
        var problems = new List<string>();
        var mine = me.GetPermissions(channel);

        if (!mine.ViewChannel) problems.Add(Texts.Get("setup.problem.botCantSee"));
        if (item.Kind == Kind.Text)
        {
            if (!mine.SendMessages) problems.Add(Texts.Get("setup.problem.botCantPost"));
            if (item.Role == ChannelRole.Dashboard && !mine.ManageMessages) problems.Add(Texts.Get("setup.problem.dashboardNeedsManageMessages"));
            if (PublicReadOnly.Contains(item.Role))
            {
                if (!EveryoneCan(guild, channel, ChannelPermission.ViewChannel)) problems.Add(Texts.Get("setup.problem.playersCantRead"));
                if (EveryoneCan(guild, channel, ChannelPermission.SendMessages)) problems.Add(Texts.Get("setup.problem.playersCanPost"));
            }
            if (item.Role is ChannelRole.Gm or ChannelRole.Cabinet && EveryoneCan(guild, channel, ChannelPermission.ViewChannel))
                problems.Add(Texts.Get("setup.problem.shouldBePrivate"));
            if (item.Role == ChannelRole.Cabinet && registered.TryGetValue(ChannelRole.CabinetRole, out var cabinetRoleId)
                && guild.GetRole(cabinetRoleId) is { } cabinetRole && !RoleCanView(guild, channel, cabinetRole))
                problems.Add(Texts.Get("setup.problem.cabinetRoleCantSee"));
            if (item.Role is ChannelRole.Gm or ChannelRole.Cabinet && registered.TryGetValue(ChannelRole.GmRole, out var gmRoleId)
                && guild.GetRole(gmRoleId) is { } gmRole && !RoleCanView(guild, channel, gmRole))
                problems.Add(Texts.Get("setup.problem.gmRoleCantSee"));
        }
        if (item.Kind == Kind.Voice)
        {
            if (!mine.ManageChannel) problems.Add(Texts.Get("setup.problem.trackerNeedsManageChannels"));
            if (EveryoneCan(guild, channel, ChannelPermission.Connect)) problems.Add(Texts.Get("setup.problem.trackerConnect"));
        }
        if (item.Kind == Kind.Category)
        {
            if (!mine.ManageChannel) problems.Add(Texts.Get("setup.problem.categoryNeedsManageChannels"));
            if (item.Role == ChannelRole.ArchiveCategory && EveryoneCan(guild, channel, ChannelPermission.ViewChannel))
                problems.Add(Texts.Get("setup.problem.archiveVisible"));
        }
        return problems;
    }

    private static List<string> RoleProblems(SocketGuildUser me, Item item, SocketRole role)
    {
        var problems = new List<string>();
        if (item.Role == ChannelRole.CabinetRole)
        {
            if (role.Position >= me.Hierarchy) problems.Add(Texts.Get("setup.problem.roleAboveBot"));
            if (role.Colors.PrimaryColor.RawValue != 0) problems.Add(Texts.Get("setup.problem.roleColoured"));
        }
        return problems;
    }

    private List<CheckLine> BotChecks(SocketGuild guild, SocketGuildUser me, IReadOnlyDictionary<ChannelRole, ulong> registered)
    {
        var lines = new List<CheckLine>();
        var permissions = me.GuildPermissions;
        lines.Add(permissions.ManageChannels
            ? new(CheckStatus.Ok, Texts.Get("setup.bot.manageChannelsOk"))
            : new(CheckStatus.Missing, Texts.Get("setup.bot.manageChannelsMissing")));
        lines.Add(permissions.ManageRoles
            ? new(CheckStatus.Ok, Texts.Get("setup.bot.manageRolesOk"))
            : new(CheckStatus.Missing, Texts.Get("setup.bot.manageRolesMissing")));

        var partyRoles = _data.GetAllParties().Where(p => p.RoleId is not null)
            .Select(p => (Party: p, Role: guild.GetRole(p.RoleId!.Value))).Where(x => x.Role is not null).ToList();
        var tooHigh = partyRoles.Where(x => x.Role!.Position >= me.Hierarchy).Select(x => x.Party.Name).ToList();
        lines.Add(tooHigh.Count == 0
            ? new(CheckStatus.Ok, partyRoles.Count == 0 ? Texts.Get("setup.bot.rolePositionNoParties") : Texts.Get("setup.bot.rolePositionOk"))
            : new(CheckStatus.Missing, Texts.Get("setup.bot.rolePositionTooLow", ("roles", string.Join(", ", tooHigh)))));

        var coloured = guild.Roles
            .Where(r => !r.IsEveryone && r.Colors.PrimaryColor.RawValue != 0 && !r.IsManaged
                        && partyRoles.All(p => p.Role!.Id != r.Id)
                        && partyRoles.Any(p => r.Position > p.Role!.Position))
            .Select(r => r.Name).ToList();
        if (coloured.Count > 0)
            lines.Add(new(CheckStatus.Warning, Texts.Get("setup.bot.colouredRoles", ("roles", string.Join(", ", coloured.Take(5))))));

        lines.Add(guild.HasAllMembers
            ? new(CheckStatus.Ok, Texts.Get("setup.bot.membersOk"))
            : new(CheckStatus.Warning, Texts.Get("setup.bot.membersWarning")));
        lines.Add(new(CheckStatus.Ok, Texts.Get("setup.bot.messageContent")));
        return lines;
    }

    private List<CheckLine> GameChecks(SocketGuild guild)
    {
        var lines = new List<CheckLine>();
        var parties = _data.GetAllParties();
        var broken = parties.Where(p => p.RoleId is not { } r || guild.GetRole(r) is null
                                        || p.ChannelId is not { } c || guild.GetTextChannel(c) is null
                                        || p.ForeignChannelId is not { } f || guild.GetTextChannel(f) is null)
            .Select(p => p.Name).ToList();
        lines.Add(broken.Count == 0
            ? new(CheckStatus.Ok, Texts.Get("setup.game.partiesOk", ("count", parties.Count)))
            : new(CheckStatus.Warning, Texts.Get("setup.game.partiesBroken", ("parties", string.Join(", ", broken)))));

        var factions = _data.GetActiveFactions();
        var noChannel = factions.Count(f => f.PrivateChannelId is not { } id || guild.GetTextChannel(id) is null);
        lines.Add(noChannel == 0
            ? new(CheckStatus.Ok, Texts.Get("setup.game.factionsOk", ("count", factions.Count)))
            : new(CheckStatus.Warning, Texts.Get("setup.game.factionsNoChannel", ("count", noChannel))));

        var state = _data.GetGameState();
        var readyTemplates = _data.GetTemplates(status: TemplateStatus.Ready).Count;
        lines.Add(state.Started
            ? new(CheckStatus.Ok, Texts.Get("setup.game.started", ("label", state.Label)))
            : new(CheckStatus.Warning, Texts.Get("setup.game.notStarted",
                ("templates", readyTemplates), ("events", _data.GetQueuedEvents().Count(e => e.FireAtResolution <= 0)))));
        return lines;
    }
}
