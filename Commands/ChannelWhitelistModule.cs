using Discord;
using Discord.Interactions;
using System.Linq;

[Group("whitelist", "Manage which channels the bot responds to commands in")]
public class ChannelWhitelistModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly DatabaseService data;

    public ChannelWhitelistModule(DatabaseService db)
    {
        data = db;
    }

    [SlashCommand("add", "Allow bot commands in a channel (server managers only)")]
    [RequireUserPermission(GuildPermission.ManageGuild)]
    public async Task AddAsync(
        [Summary("channel", "Channel to whitelist")] IChannel channel)
    {
        var added = data.AddWhitelistedChannel(Context.Guild.Id, channel.Id, Context.User.Id);
        await RespondAsync(
            added
                ? Texts.Get("whitelist.added", ("channel", channel.Id))
                : Texts.Get("whitelist.alreadyListed", ("channel", channel.Id)),
            ephemeral: true);
    }

    [SlashCommand("remove", "Disallow bot commands in a channel (server managers only)")]
    [RequireUserPermission(GuildPermission.ManageGuild)]
    public async Task RemoveAsync(
        [Summary("channel", "Channel to remove from the whitelist")] IChannel channel)
    {
        var removed = data.RemoveWhitelistedChannel(Context.Guild.Id, channel.Id);
        await RespondAsync(
            removed
                ? Texts.Get("whitelist.removed", ("channel", channel.Id))
                : Texts.Get("whitelist.notListed", ("channel", channel.Id)),
            ephemeral: true);
    }

    [SlashCommand("list", "List channels the bot is whitelisted in (server managers only)")]
    [RequireUserPermission(GuildPermission.ManageGuild)]
    public async Task ListAsync()
    {
        var channels = data.GetWhitelistedChannels(Context.Guild.Id);
        if (channels.Count == 0)
        {
            await RespondAsync(Texts.Get("whitelist.empty"), ephemeral: true);
            return;
        }

        var lines = channels.Select(c => $"<#{c}>");
        await RespondAsync(Texts.Get("whitelist.list", ("channels", string.Join("\n", lines))), ephemeral: true);
    }
}
