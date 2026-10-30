using Discord;
using Discord.Interactions;

public partial class GmModule
{
    [Group("party", "Found, edit, move, delete parties and record programmes")]
    [RequireUserPermission(GuildPermission.ManageGuild)]
    public partial class PartyGroup : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly DatabaseService _data;
        private readonly ConstantsStore _constants;
        private readonly PartySyncService _partySync;
        private readonly FactionChannelService _factionChannels;
        private readonly Confirmations _confirmations;
        private readonly Publisher _publisher;
        private readonly AlertService _alerts;

        public PartyGroup(
            DatabaseService data,
            ConstantsStore constants,
            PartySyncService partySync,
            FactionChannelService factionChannels,
            Confirmations confirmations,
            Publisher publisher,
            AlertService alerts)
        {
            _data = data;
            _constants = constants;
            _partySync = partySync;
            _factionChannels = factionChannels;
            _confirmations = confirmations;
            _publisher = publisher;
            _alerts = alerts;
        }
    }
}
