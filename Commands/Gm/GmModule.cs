using Discord;
using Discord.Interactions;

[Group("gm", "Run the game (GM only)")]
[RequireUserPermission(GuildPermission.ManageGuild)]
public partial class GmModule : InteractionModuleBase<SocketInteractionContext>
{
}
