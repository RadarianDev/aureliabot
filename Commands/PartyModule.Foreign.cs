using Discord;
using Discord.Interactions;

public partial class PartyModule
{
    [Group("foreign", "Let people outside your party into its foreign affairs channel")]
    public class ForeignGroup : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly DatabaseService _data;
        private readonly PartySyncService _partySync;
        private readonly Confirmations _confirmations;

        public ForeignGroup(DatabaseService data, PartySyncService partySync, Confirmations confirmations)
        {
            _data = data;
            _partySync = partySync;
            _confirmations = confirmations;
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

        [SlashCommand("clear", "Delete every message in your party's foreign affairs channel")]
        [RequirePartyMember]
        public async Task ClearAsync()
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
            var userId = Context.User.Id;
            await FollowupAsync(Texts.Get("foreignAffairs.clear.confirm", ("channel", channel.Mention)),
                components: _confirmations.CreateWithResult(userId, () => ClearConfirmedAsync(channel, userId), Texts.Get("foreignAffairs.clear.button")),
                ephemeral: true, allowedMentions: AllowedMentions.None);
        }

        private async Task<ConfirmedResult> ClearConfirmedAsync(ITextChannel channel, ulong userId)
        {
            List<IMessage> history;
            try
            {
                history = await ReadHistoryAsync(channel);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PartyForeign] Could not read {channel.Id}: {ex.Message}");
                return new ConfirmedResult(Texts.Get("foreignAffairs.clear.unreadable", ("error", ex.Message)));
            }

            var now = DateTimeOffset.UtcNow;
            var clearer = Context.Guild.GetUser(userId)?.DisplayName ?? Context.User.Username;
            var transcript = ChatTranscript.Render(channel.Name, clearer, now, history.Select(ChatTranscript.From));
            var fileName = ChatTranscript.FileName(channel.Name, now);

            try
            {
                await DeleteAsync(channel, history);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PartyForeign] Could not clear {channel.Id}: {ex.Message}");
                return new ConfirmedResult(Texts.Get("foreignAffairs.clear.partly", ("error", ex.Message)), fileName, transcript);
            }
            _data.Log(LogKinds.Command, "party foreign clear", $"Cleared {history.Count} messages from {channel.Id}.", userId);
            await PostAsync(channel, Texts.Get("foreignAffairs.posted.cleared", ("by", $"<@{userId}>")), AllowedMentions.None, "party foreign clear");
            return new ConfirmedResult(Texts.Get("foreignAffairs.clear.done", ("count", history.Count), ("channel", channel.Mention)), fileName, transcript);
        }

        private static async Task<List<IMessage>> ReadHistoryAsync(ITextChannel channel)
        {
            var history = new List<IMessage>();
            var batch = (await channel.GetMessagesAsync(100).FlattenAsync()).ToList();
            while (batch.Count > 0)
            {
                history.AddRange(batch);
                var oldest = batch.MinBy(m => m.Timestamp)!;
                batch = (await channel.GetMessagesAsync(oldest.Id, Direction.Before, 100).FlattenAsync()).ToList();
            }
            return history;
        }

        private static async Task DeleteAsync(ITextChannel channel, IReadOnlyList<IMessage> messages)
        {
            var bulkCutoff = DateTimeOffset.UtcNow - TimeSpan.FromDays(14) + TimeSpan.FromHours(1);
            var recent = messages.Where(m => m.Timestamp > bulkCutoff).ToList();
            foreach (var chunk in recent.Chunk(100))
            {
                if (chunk.Length > 1) await channel.DeleteMessagesAsync(chunk);
                else await chunk[0].DeleteAsync();
            }
            foreach (var message in messages.Except(recent))
                await message.DeleteAsync();
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
