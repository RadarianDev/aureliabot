using Discord;
using Discord.Interactions;

public partial class PartyModule
{
    [Group("foreign", "Let people outside your party into its foreign affairs channel")]
    public class ForeignGroup : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly DatabaseService _data;
        private readonly PartySyncService _partySync;

        public ForeignGroup(DatabaseService data, PartySyncService partySync)
        {
            _data = data;
            _partySync = partySync;
        }

        [SlashCommand("add", "Give someone outside your party access to its foreign affairs channel")]
        [RequirePartyMember]
        public async Task AddAsync([Summary("user", "The person to let in")] IUser user)
        {
            if (await RefuseAsync(user) is not { } party) return;
            await DeferAsync(ephemeral: true);

            var result = await _partySync.AddForeignGuestAsync(party.Id, user);
            if (result is { Outcome: ForeignGuestOutcome.Added, Channel: { } channel })
                await PostAsync(channel, Texts.Get("foreignAffairs.posted.added", ("user", user.Mention), ("by", Context.User.Mention)),
                    new AllowedMentions { UserIds = [user.Id] }, "party foreign add");

            await FollowupAsync(Reply(result, user), ephemeral: true, allowedMentions: AllowedMentions.None);
        }

        [SlashCommand("remove", "Take away someone's access to your party's foreign affairs channel")]
        [RequirePartyMember]
        public async Task RemoveAsync([Summary("user", "The person to remove")] IUser user)
        {
            if (await RefuseAsync(user) is not { } party) return;
            await DeferAsync(ephemeral: true);

            var result = await _partySync.RemoveForeignGuestAsync(party.Id, user);
            if (result is { Outcome: ForeignGuestOutcome.Removed, Channel: { } channel })
                await PostAsync(channel, Texts.Get("foreignAffairs.posted.removed", ("user", user.Mention), ("by", Context.User.Mention)),
                    AllowedMentions.None, "party foreign remove");

            await FollowupAsync(Reply(result, user), ephemeral: true, allowedMentions: AllowedMentions.None);
        }

        [SlashCommand("list", "See who outside your party has access to its foreign affairs channel")]
        [RequirePartyMember]
        public async Task ListAsync()
        {
            if (PartyOf(Context.User.Id) is not { } party)
            {
                await RespondAsync(Texts.Get("common.needsPartyMember"), ephemeral: true);
                return;
            }
            await DeferAsync(ephemeral: true);

            if (await _partySync.ForeignChannelAsync(party.Id) is not { } channel)
            {
                await FollowupAsync(Texts.Get("foreignAffairs.noChannel"), ephemeral: true);
                return;
            }
            var guests = PartySyncService.Guests(channel.PermissionOverwrites, Context.Client.CurrentUser.Id);
            await FollowupAsync(
                guests.Count == 0
                    ? Texts.Get("foreignAffairs.list.empty", ("channel", channel.Mention))
                    : Texts.Get("foreignAffairs.list.guests", ("channel", channel.Mention), ("count", guests.Count),
                        ("guests", string.Join(", ", guests.Select(id => $"<@{id}>")))),
                ephemeral: true, allowedMentions: AllowedMentions.None);
        }

        private Party? PartyOf(ulong userId) =>
            _data.GetFaction(userId) is { IsActive: true, PartyId: { } partyId } ? _data.GetParty(partyId) : null;

        private async Task<Party?> RefuseAsync(IUser user)
        {
            var actor = _data.GetFaction(Context.User.Id);
            var refusal = PartySyncService.GuestRefusal(actor, user.IsBot, _data.GetFaction(user.Id));
            var party = refusal is null ? PartyOf(Context.User.Id) : null;
            if (party is not null) return party;

            await RespondAsync(refusal switch
            {
                ForeignGuestRefusal.Bot => Texts.Get("foreignAffairs.refused.bot"),
                ForeignGuestRefusal.OwnMember => Texts.Get("foreignAffairs.refused.ownMember", ("user", user.Mention)),
                _ => Texts.Get("common.needsPartyMember"),
            }, ephemeral: true, allowedMentions: AllowedMentions.None);
            return null;
        }

        private static string Reply(ForeignGuestResult result, IUser user) => result.Outcome switch
        {
            ForeignGuestOutcome.Added => Texts.Get("foreignAffairs.added", ("user", user.Mention), ("channel", result.Channel!.Mention)),
            ForeignGuestOutcome.AlreadyGuest => Texts.Get("foreignAffairs.alreadyGuest", ("user", user.Mention), ("channel", result.Channel!.Mention)),
            ForeignGuestOutcome.Removed => Texts.Get("foreignAffairs.removed", ("user", user.Mention), ("channel", result.Channel!.Mention)),
            ForeignGuestOutcome.NotGuest => Texts.Get("foreignAffairs.notGuest", ("user", user.Mention), ("channel", result.Channel!.Mention)),
            ForeignGuestOutcome.Failed => Texts.Get("foreignAffairs.failed", ("error", result.Error)),
            _ => Texts.Get("foreignAffairs.noChannel"),
        };

        private async Task PostAsync(ITextChannel channel, string text, AllowedMentions mentions, string source)
        {
            try
            {
                var message = await channel.SendMessageAsync(text, allowedMentions: mentions);
                _data.LogPost(channel.Id, message.Id, source);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PartyForeign] Could not post in {channel.Id}: {ex.Message}");
            }
        }
    }
}
