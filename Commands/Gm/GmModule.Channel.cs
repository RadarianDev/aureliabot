using Discord;
using Discord.Interactions;

public partial class GmModule
{
    [Group("channel", "Register the game's channels, categories and roles")]
    [RequireUserPermission(GuildPermission.ManageGuild)]
    public class ChannelGroup : InteractionModuleBase<SocketInteractionContext>
    {
        private static readonly HashSet<ChannelRole> TextChannels =
        [
            ChannelRole.News, ChannelRole.Dashboard, ChannelRole.Assembly, ChannelRole.CampaignLog,
            ChannelRole.PartyRegistry, ChannelRole.Cabinet, ChannelRole.Gm,
        ];

        private static readonly HashSet<ChannelRole> Categories =
            [ChannelRole.FactionCategory, ChannelRole.PartyCategory, ChannelRole.ArchiveCategory];

        private static readonly HashSet<ChannelRole> Roles = [ChannelRole.GmRole, ChannelRole.CabinetRole];

        private readonly DatabaseService _data;
        private readonly SetupChecklistService _checklist;

        public ChannelGroup(DatabaseService data, SetupChecklistService checklist)
        {
            _data = data;
            _checklist = checklist;
        }

        [SlashCommand("set", "Set a game channel, category, the turn tracker, the GM role or the Cabinet role")]
        public async Task SetAsync(
            [Summary("kind", "What this is")] ChannelRole kind,
            [Summary("channel", "The channel or category (for every kind except the two roles)")] IGuildChannel? channel = null,
            [Summary("role", "The role (GM role or Cabinet role)")] IRole? role = null)
        {
            if (Roles.Contains(kind))
            {
                if (role is null)
                {
                    await RespondAsync($"{kind} needs the `role` option.", ephemeral: true);
                    return;
                }
                _data.SetChannel(kind, role.Id);
                _checklist.RequestRefresh();
                _data.Log(LogKinds.GmOverride, "gm channel set", $"{kind} = role {role.Id}", Context.User.Id);
                await RespondAsync($"{kind} is now {role.Mention}.", ephemeral: true, allowedMentions: AllowedMentions.None);
                return;
            }

            if (channel is null)
            {
                await RespondAsync($"{kind} needs the `channel` option.", ephemeral: true);
                return;
            }

            var problem = kind switch
            {
                _ when Categories.Contains(kind) && channel is not ICategoryChannel => "must be a category",
                ChannelRole.TurnTracker when channel is not IVoiceChannel => "must be a voice channel",
                _ when TextChannels.Contains(kind) && channel is not ITextChannel => "must be a text channel",
                _ => null,
            };
            if (problem is not null)
            {
                await RespondAsync($"{kind} {problem}.", ephemeral: true);
                return;
            }

            _data.SetChannel(kind, channel.Id);
            _checklist.RequestRefresh();
            _data.Log(LogKinds.GmOverride, "gm channel set", $"{kind} = channel {channel.Id}", Context.User.Id);
            var whitelisted = TextChannels.Contains(kind) && _data.AddWhitelistedChannel(Context.Guild.Id, channel.Id, Context.User.Id);
            await RespondAsync(
                $"{kind} is now <#{channel.Id}>." + (whitelisted ? " It was added to the command whitelist." : ""),
                ephemeral: true);
        }
    }
}
