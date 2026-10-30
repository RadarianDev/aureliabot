using Discord;
using Discord.Interactions;

public partial class GmModule
{
    public partial class PartyGroup
    {
        private const int MaxPartyNameLength = 100;

        [SlashCommand("create", "Found a party with two to five independent founders")]
        public async Task CreateAsync(
            [Summary("name", "The party's name")] string name,
            [Summary("colour", "The party colour, #RRGGBB")] string colour,
            [Summary("founder1", "A founding faction")] IUser founder1,
            [Summary("founder2", "A founding faction")] IUser founder2,
            [Summary("founder3", "A founding faction")] IUser? founder3 = null,
            [Summary("founder4", "A founding faction")] IUser? founder4 = null,
            [Summary("founder5", "A founding faction")] IUser? founder5 = null,
            [Summary("emoji", "A custom emoji for the party, like <:name:id>")] string? emoji = null)
        {
            await DeferAsync(ephemeral: true);

            name = name.Trim();
            if (ValidatePartyName(name) is { } nameProblem)
            {
                await FollowupAsync(nameProblem, ephemeral: true);
                return;
            }
            if (NormalisePartyColour(colour) is not { } hex)
            {
                await FollowupAsync($"`{colour}` is not a colour; write it as `#RRGGBB`, for example `#C0392B`.", ephemeral: true);
                return;
            }
            ulong? emojiId = null;
            if (emoji is not null)
            {
                if (!TryParsePartyEmoji(emoji, out emojiId))
                {
                    await FollowupAsync($"`{emoji}` is not a custom emoji; pick one from this server's emoji list.", ephemeral: true);
                    return;
                }
            }

            var founders = new[] { founder1, founder2, founder3, founder4, founder5 }.OfType<IUser>().Select(u => u.Id).ToList();
            var result = _data.CreateParty(name, hex, emojiId, founders);
            if (result.Outcome != PartyCreateOutcome.Created || result.PartyId is not { } partyId)
            {
                await FollowupAsync(CreateRefusal(result, name), ephemeral: true, allowedMentions: AllowedMentions.None);
                return;
            }

            var warning = SimilarColourWarning(hex, partyId);
            var problems = (await _partySync.SyncAsync(partyId)).ToList();
            var party = _data.GetParty(partyId)!;

            var distinct = founders.Distinct().Select(id => Display.Faction(_data, id)).ToList();
            await PostPartyNewsAsync(
                $"**A new party takes the field.** {JoinNames(distinct)} have joined their fortunes together as the {Display.Party(party)}, "
                + "and will stand as one at the coming election.",
                "gm party create", problems);

            await FollowupAsync(
                Display.Truncate(
                    $"Founded **{party.Name}** (`{party.Colour}`) with {JoinNames(distinct)}."
                    + DiscordSummary(party)
                    + (warning is null ? "" : $"\n⚠️ {warning}")
                    + ProblemsText(problems)),
                ephemeral: true, allowedMentions: AllowedMentions.None);
        }

        [SlashCommand("edit", "Rename, recolour or change a party's emoji")]
        public async Task EditAsync(
            [Summary("party", "The party"), Autocomplete(typeof(PartyAutocomplete))] long party,
            [Summary("name", "A new name")] string? name = null,
            [Summary("colour", "A new colour, #RRGGBB")] string? colour = null,
            [Summary("emoji", "A new custom emoji, or 'none' to remove it")] string? emoji = null)
        {
            await DeferAsync(ephemeral: true);

            if (_data.GetParty(party) is not { } current)
            {
                await FollowupAsync("That party does not exist.", ephemeral: true);
                return;
            }
            if (name is null && colour is null && emoji is null)
            {
                await FollowupAsync("Nothing to change: give a new name, colour or emoji.", ephemeral: true);
                return;
            }

            var newName = name?.Trim() ?? current.Name;
            if (ValidatePartyName(newName) is { } nameProblem)
            {
                await FollowupAsync(nameProblem, ephemeral: true);
                return;
            }
            if (_data.GetPartyByName(newName) is { } other && other.Id != current.Id)
            {
                await FollowupAsync($"A party named **{other.Name}** already exists.", ephemeral: true);
                return;
            }

            var newColour = current.Colour;
            if (colour is not null)
            {
                if (NormalisePartyColour(colour) is not { } hex)
                {
                    await FollowupAsync($"`{colour}` is not a colour; write it as `#RRGGBB`, for example `#C0392B`.", ephemeral: true);
                    return;
                }
                newColour = hex;
            }

            var newEmoji = current.EmojiId;
            if (emoji is not null)
            {
                if (emoji.Trim().Equals("none", StringComparison.OrdinalIgnoreCase))
                    newEmoji = null;
                else if (TryParsePartyEmoji(emoji, out var parsed))
                    newEmoji = parsed;
                else
                {
                    await FollowupAsync($"`{emoji}` is not a custom emoji; pick one from this server's emoji list, or write `none`.", ephemeral: true);
                    return;
                }
            }

            if (!_data.UpdateParty(current.Id, newName, newColour, newEmoji))
            {
                await FollowupAsync($"Could not update **{current.Name}**: the name **{newName}** may already be taken.", ephemeral: true);
                return;
            }
            _data.Log(LogKinds.StateChange, "gm party edit",
                $"Party {current.Id}: '{current.Name}' {current.Colour} {current.EmojiId} -> '{newName}' {newColour} {newEmoji}.", Context.User.Id);

            var warning = colour is null ? null : SimilarColourWarning(newColour, current.Id);
            var problems = await _partySync.SyncAsync(current.Id);
            var updated = _data.GetParty(current.Id)!;

            await FollowupAsync(
                Display.Truncate(
                    $"Updated **{updated.Name}** (`{updated.Colour}`"
                    + (updated.EmojiId is null ? ", no emoji" : $", {Display.Party(updated)}") + ")."
                    + DiscordSummary(updated)
                    + (warning is null ? "" : $"\n⚠️ {warning}")
                    + ProblemsText(problems)),
                ephemeral: true, allowedMentions: AllowedMentions.None);
        }

        [SlashCommand("move", "Move a faction into a party, or make it independent, at any time")]
        public async Task MoveAsync(
            [Summary("faction", "The player whose faction moves")] IUser faction,
            [Summary("party", "The party to join; leave empty to make the faction independent"), Autocomplete(typeof(PartyAutocomplete))] long? party = null)
        {
            await DeferAsync(ephemeral: true);

            if (_data.GetFaction(faction.Id) is not { IsActive: true })
            {
                await FollowupAsync($"<@{faction.Id}> is not a registered, active faction.", ephemeral: true, allowedMentions: AllowedMentions.None);
                return;
            }
            Party? target = null;
            if (party is { } partyId && (target = _data.GetParty(partyId)) is null)
            {
                await FollowupAsync("That party does not exist.", ephemeral: true);
                return;
            }

            if (_data.MoveFaction(faction.Id, party, "gm party move") is not { } result)
            {
                await FollowupAsync("The move could not be made; the faction or the party changed meanwhile.", ephemeral: true);
                return;
            }
            if (result.FromPartyId == party)
            {
                await FollowupAsync(
                    $"{Display.Faction(result.Faction)} is already {(target is null ? "independent" : $"in **{target.Name}**")}.",
                    ephemeral: true, allowedMentions: AllowedMentions.None);
                return;
            }

            var problems = new List<string>();
            if (party is { } joined)
                problems.AddRange(await _partySync.SyncAsync(joined));
            await _partySync.SyncMemberAsync(faction.Id);
            if (result.DissolvedParty is { } dissolved)
                await _partySync.HandleDissolutionAsync(dissolved, faction.Id);
            else if (result.FromPartyId is { } from)
                problems.AddRange(await _partySync.SyncAsync(from));

            var fromName = result.DissolvedParty?.Name ?? (result.FromPartyId is { } fromId ? _data.GetParty(fromId)?.Name : null);
            await FollowupAsync(
                Display.Truncate(
                    $"Moved {Display.Faction(result.Faction)} from {(fromName is null ? "independent" : $"**{fromName}**")} "
                    + $"to {(target is null ? "independent" : $"**{target.Name}**")}."
                    + (result.DissolvedParty is null ? "" : $"\n**{result.DissolvedParty.Name}** was left with fewer than two members and dissolved; it was announced in #news.")
                    + ProblemsText(problems)),
                ephemeral: true, allowedMentions: AllowedMentions.None);
        }

        [SlashCommand("delete", "Delete a party: its members become independent, its role is deleted and its chat archived")]
        public async Task DeleteAsync(
            [Summary("party", "The party"), Autocomplete(typeof(PartyAutocomplete))] long party)
        {
            if (_data.GetParty(party) is not { } target)
            {
                await RespondAsync("That party does not exist.", ephemeral: true);
                return;
            }

            var memberCount = _data.GetPartyMembers(target.Id).Count;
            var components = _confirmations.Create(Context.User.Id, () => DeletePartyConfirmedAsync(target.Id));
            await RespondAsync(
                $"Delete **{target.Name}**? Its {memberCount} member(s) become independent and keep their seats, its role is deleted, "
                + "its chat is archived, and the dissolution is announced in #news.",
                components: components, ephemeral: true);
        }

        private async Task<string> DeletePartyConfirmedAsync(long partyId)
        {
            var members = _data.GetPartyMembers(partyId).Select(f => f.UserId).ToList();
            if (_data.DeleteParty(partyId, "gm party delete") is not { } deleted)
                return "That party no longer exists; nothing was changed.";

            await _partySync.ArchiveAsync(deleted);
            foreach (var member in members)
                await _partySync.SyncMemberAsync(member);

            var problems = new List<string>();
            var names = members.Select(id => Display.Faction(_data, id)).ToList();
            await PostPartyNewsAsync(
                $"**The {deleted.Name} is no more.** The party is struck from the register"
                + (names.Count == 0 ? "." : $", and {JoinNames(names)} {(names.Count == 1 ? "sits" : "sit")} on as independent{(names.Count == 1 ? "" : "s")}."),
                "gm party delete", problems);

            return $"Deleted **{deleted.Name}**. {names.Count} member(s) are now independent; its role was deleted and its chat archived "
                + "(any Discord refusal was posted to #gm)." + ProblemsText(problems);
        }

        private static string? ValidatePartyName(string name) =>
            name.Length == 0 ? "The party needs a name."
            : name.Length > MaxPartyNameLength ? $"The name is too long: Discord allows {MaxPartyNameLength} characters."
            : null;

        private static string? NormalisePartyColour(string colour)
        {
            var value = colour.Trim();
            if (!value.StartsWith('#')) value = "#" + value;
            return ConstantsStore.IsHexColour(value) ? value.ToUpperInvariant() : null;
        }

        private static bool TryParsePartyEmoji(string text, out ulong? emojiId)
        {
            var value = text.Trim();
            if (Emote.TryParse(value, out var emote))
            {
                emojiId = emote.Id;
                return true;
            }
            if (ulong.TryParse(value, out var id))
            {
                emojiId = id;
                return true;
            }
            emojiId = null;
            return false;
        }

        private string? SimilarColourWarning(string colour, long partyId)
        {
            var close = _data.GetAllParties()
                .Where(p => p.Id != partyId && PartySyncService.AreSimilarColours(colour, p.Colour))
                .Select(p => $"**{p.Name}** (`{p.Colour}`)")
                .ToList();
            return close.Count == 0
                ? null
                : $"`{colour}` is close to {JoinNames(close)}; the seat chart and map may be hard to read. Change it with `/gm party edit` if needed.";
        }

        private string CreateRefusal(PartyCreateResult result, string name) => result.Outcome switch
        {
            PartyCreateOutcome.NameTaken => $"A party named **{name}** already exists.",
            PartyCreateOutcome.TooFewFounders => "A party needs at least two different founders.",
            PartyCreateOutcome.FounderNotRegistered =>
                $"<@{result.OffendingFaction}> is not a registered, active faction; register them with `/gm faction create` first.",
            PartyCreateOutcome.FounderNotIndependent =>
                $"{Display.Faction(_data, result.OffendingFaction ?? 0)} already belongs to "
                + $"**{(_data.GetFaction(result.OffendingFaction ?? 0)?.PartyId is { } p ? _data.GetParty(p)?.Name : null) ?? "a party"}**; "
                + "every founder must be independent. Move them out first with `/gm party move`.",
            _ => "The party could not be founded.",
        };

        private async Task PostPartyNewsAsync(string text, string source, List<string> problems)
        {
            try
            {
                if (await _publisher.PostAsync(ChannelRole.News, text, source) is null)
                    problems.Add("No #news channel is set, so the announcement was not posted.");
            }
            catch (Exception ex)
            {
                problems.Add($"Could not post to #news: {ex.Message}");
            }
        }

        private static string DiscordSummary(Party party) =>
            (party.RoleId is { } role ? $" Role <@&{role}>." : " No role yet.")
            + (party.ChannelId is { } channel ? $" Chat <#{channel}>." : " No chat yet.");

        private static string ProblemsText(IReadOnlyCollection<string> problems) =>
            problems.Count == 0 ? "" : "\nProblems:\n" + string.Join("\n", problems.Select(p => $"- {p}"));

        private static string JoinNames(IReadOnlyList<string> names) => names.Count switch
        {
            0 => "",
            1 => names[0],
            _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1],
        };
    }
}
