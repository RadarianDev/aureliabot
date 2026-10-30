using System.Text;
using Discord;
using Discord.Net;
using Discord.WebSocket;

public class FactionChannelService
{
    private static readonly OverwritePermissions HiddenAccess = new(viewChannel: PermValue.Deny);

    private static readonly OverwritePermissions PlayerAccess = new(
        viewChannel: PermValue.Allow, sendMessages: PermValue.Allow, readMessageHistory: PermValue.Allow,
        addReactions: PermValue.Allow, embedLinks: PermValue.Allow, attachFiles: PermValue.Allow,
        useApplicationCommands: PermValue.Allow);

    private static readonly OverwritePermissions GmAccess = new(
        viewChannel: PermValue.Allow, sendMessages: PermValue.Allow, readMessageHistory: PermValue.Allow,
        addReactions: PermValue.Allow, embedLinks: PermValue.Allow, attachFiles: PermValue.Allow,
        manageMessages: PermValue.Allow);

    private static readonly OverwritePermissions BotAccess = new(
        viewChannel: PermValue.Allow, sendMessages: PermValue.Allow, readMessageHistory: PermValue.Allow,
        embedLinks: PermValue.Allow, attachFiles: PermValue.Allow, manageMessages: PermValue.Allow);

    private readonly DiscordSocketClient _client;
    private readonly DatabaseService _data;
    private readonly AlertService _alerts;

    public FactionChannelService(DiscordSocketClient client, DatabaseService data, AlertService alerts)
    {
        _client = client;
        _data = data;
        _alerts = alerts;
    }

    public async Task<ulong?> EnsureChannelAsync(ulong factionId)
    {
        if (_data.GetFaction(factionId) is not { } faction) return null;
        if (await FactionCategoryAsync() is not { } category)
        {
            await _alerts.PostAsync($"Could not open a private channel for <@{factionId}>: no Factions category is set. Run `/gm setup`.");
            return null;
        }
        var (channelId, problem) = await EnsureAsync(faction, category);
        if (problem is not null) await _alerts.PostAsync(problem);
        return channelId;
    }

    public async Task<IReadOnlyList<string>> SyncAllAsync()
    {
        var factions = _data.GetActiveFactions();
        if (factions.Count == 0) return [];
        if (await FactionCategoryAsync() is not { } category)
            return ["No Factions category is set, so no private faction channel could be checked."];

        var problems = new List<string>();
        foreach (var faction in factions)
        {
            var (_, problem) = await EnsureAsync(faction, category);
            if (problem is null) continue;
            problems.Add(problem);
            await _alerts.PostAsync(problem);
        }
        return problems;
    }

    public async Task<bool> SendAsync(ulong factionId, string text, MessageComponent? components = null, Embed? embed = null)
    {
        if (await EnsureChannelAsync(factionId) is not { } channelId) return false;
        try
        {
            if (await TextChannelAsync(channelId) is not { } channel) return false;
            var mentions = new AllowedMentions { UserIds = [factionId] };
            var first = true;
            foreach (var chunk in Publisher.Chunk(text))
            {
                var message = await channel.SendMessageAsync(
                    chunk,
                    embed: first ? embed : null,
                    components: first ? components : null,
                    allowedMentions: mentions);
                _data.LogPost(channelId, message.Id, "private");
                first = false;
            }
            return true;
        }
        catch (HttpException ex)
        {
            await _alerts.PostAsync($"Could not post in the private channel of <@{factionId}>: {ex.Message}");
            return false;
        }
    }

    private async Task<(ulong? ChannelId, string? Problem)> EnsureAsync(Faction faction, ICategoryChannel category)
    {
        if (_client.GetGuild(category.GuildId) is not { } guild)
            return (null, $"Could not open a private channel for <@{faction.UserId}>: the bot cannot see the Factions category's server.");

        var overwrites = Overwrites(guild, faction.UserId);
        try
        {
            if (faction.PrivateChannelId is { } existingId && await TextChannelAsync(existingId) is { } existing)
            {
                if (!SameOverwrites(existing.PermissionOverwrites, overwrites))
                    await existing.ModifyAsync(p => p.PermissionOverwrites = overwrites);
                Whitelist(guild, existing.Id);
                return (existing.Id, null);
            }

            var created = await guild.CreateTextChannelAsync(ChannelName(faction, guild), p =>
            {
                p.CategoryId = category.Id;
                p.Topic = string.IsNullOrWhiteSpace(faction.CharacterName)
                    ? "Private office: poll results, reminders and your commands."
                    : $"The private office of {faction.CharacterName}: poll results, reminders and your commands.";
                p.PermissionOverwrites = overwrites;
            });
            _data.SetFactionPrivateChannel(faction.UserId, created.Id);
            _data.Log(LogKinds.StateChange, "faction channel", $"Private channel {created.Id} for {faction.UserId}.", faction.UserId);
            Whitelist(guild, created.Id);
            return (created.Id, null);
        }
        catch (HttpException ex)
        {
            return (null, $"Discord refused the private channel of <@{faction.UserId}>: {ex.Message}");
        }
    }

    private List<Overwrite> Overwrites(SocketGuild guild, ulong factionId)
    {
        var overwrites = new List<Overwrite>
        {
            new(guild.EveryoneRole.Id, PermissionTarget.Role, HiddenAccess),
            new(factionId, PermissionTarget.User, PlayerAccess),
            new(_client.CurrentUser.Id, PermissionTarget.User, BotAccess),
        };
        if (_data.GetChannel(ChannelRole.GmRole) is { } gmRole && guild.GetRole(gmRole) is not null)
            overwrites.Add(new Overwrite(gmRole, PermissionTarget.Role, GmAccess));
        return overwrites;
    }

    private static bool SameOverwrites(IReadOnlyCollection<Overwrite> current, IReadOnlyCollection<Overwrite> wanted) =>
        current.Count == wanted.Count && wanted.All(w => current.Any(c =>
            c.TargetId == w.TargetId && c.TargetType == w.TargetType
            && c.Permissions.AllowValue == w.Permissions.AllowValue
            && c.Permissions.DenyValue == w.Permissions.DenyValue));

    private void Whitelist(SocketGuild guild, ulong channelId) =>
        _data.AddWhitelistedChannel(guild.Id, channelId, _client.CurrentUser.Id);

    private async Task<ICategoryChannel?> FactionCategoryAsync()
    {
        if (_data.GetChannel(ChannelRole.FactionCategory) is not { } categoryId) return null;
        if (_client.GetChannel(categoryId) is ICategoryChannel cached) return cached;
        return await _client.Rest.GetChannelAsync(categoryId) as ICategoryChannel;
    }

    private async Task<ITextChannel?> TextChannelAsync(ulong channelId)
    {
        if (_client.GetChannel(channelId) is ITextChannel cached) return cached;
        return await _client.Rest.GetChannelAsync(channelId) as ITextChannel;
    }

    private static string ChannelName(Faction faction, SocketGuild guild)
    {
        var name = Slug(faction.CharacterName);
        if (name.Length == 0 && guild.GetUser(faction.UserId) is { } member) name = Slug(member.Username);
        return name.Length == 0 ? $"faction-{faction.UserId}" : name;
    }

    public static string Slug(string text)
    {
        var slug = new StringBuilder();
        foreach (var c in text.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c)) slug.Append(c);
            else if (slug.Length > 0 && slug[^1] != '-') slug.Append('-');
        }
        var result = slug.ToString().Trim('-');
        return result.Length > 90 ? result[..90].Trim('-') : result;
    }
}
