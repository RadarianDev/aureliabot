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
            ChannelRole.News, ChannelRole.Notices, ChannelRole.Dashboard, ChannelRole.Assembly, ChannelRole.CampaignLog,
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
                    await RespondAsync(Texts.Get("gm.channel.set.needsRole", ("kind", kind)), ephemeral: true);
                    return;
                }
                _data.SetChannel(kind, role.Id);
                _checklist.RequestRefresh();
                _data.Log(LogKinds.GmOverride, "gm channel set", $"{kind} = role {role.Id}", Context.User.Id);
                await RespondAsync(Texts.Get("gm.channel.set.roleSet", ("kind", kind), ("role", role.Mention)), ephemeral: true, allowedMentions: AllowedMentions.None);
                return;
            }

            if (channel is null)
            {
                await RespondAsync(Texts.Get("gm.channel.set.needsChannel", ("kind", kind)), ephemeral: true);
                return;
            }

            var problem = kind switch
            {
                _ when Categories.Contains(kind) && channel is not ICategoryChannel => Texts.Get("gm.channel.set.notCategory", ("kind", kind)),
                ChannelRole.TurnTracker when channel is not IVoiceChannel => Texts.Get("gm.channel.set.notVoice", ("kind", kind)),
                _ when TextChannels.Contains(kind) && channel is not ITextChannel => Texts.Get("gm.channel.set.notText", ("kind", kind)),
                _ => null,
            };
            if (problem is not null)
            {
                await RespondAsync(problem, ephemeral: true);
                return;
            }

            _data.SetChannel(kind, channel.Id);
            _checklist.RequestRefresh();
            _data.Log(LogKinds.GmOverride, "gm channel set", $"{kind} = channel {channel.Id}", Context.User.Id);
            var whitelisted = TextChannels.Contains(kind) && _data.AddWhitelistedChannel(Context.Guild.Id, channel.Id, Context.User.Id);
            await RespondAsync(
                whitelisted
                    ? Texts.Get("gm.channel.set.channelSetWhitelisted", ("kind", kind), ("channel", channel.Id))
                    : Texts.Get("gm.channel.set.channelSet", ("kind", kind), ("channel", channel.Id)),
                ephemeral: true);
        }
    }
}
