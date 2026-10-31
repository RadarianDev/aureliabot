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
                await FollowupAsync(Texts.Get("gm.party.badColour", ("colour", colour)), ephemeral: true);
                return;
            }
            ulong? emojiId = null;
            if (emoji is not null)
            {
                if (!TryParsePartyEmoji(emoji, out emojiId))
                {
                    await FollowupAsync(Texts.Get("gm.party.create.badEmoji", ("emoji", emoji)), ephemeral: true);
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
                Texts.Get("gm.party.create.news", ("founders", JoinNames(distinct)), ("party", Display.Party(party))),
                "gm party create", problems);

            await FollowupAsync(
                Display.Truncate(
                    Texts.Get("gm.party.create.founded", ("name", party.Name), ("colour", party.Colour), ("founders", JoinNames(distinct)))
                    + DiscordSummary(party)
                    + WarningText(warning)
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
                await FollowupAsync(Texts.Get("gm.party.notFound"), ephemeral: true);
                return;
            }
            if (name is null && colour is null && emoji is null)
            {
                await FollowupAsync(Texts.Get("gm.party.edit.nothing"), ephemeral: true);
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
                await FollowupAsync(Texts.Get("gm.party.nameTaken", ("name", other.Name)), ephemeral: true);
                return;
            }

            var newColour = current.Colour;
            if (colour is not null)
            {
                if (NormalisePartyColour(colour) is not { } hex)
                {
                    await FollowupAsync(Texts.Get("gm.party.badColour", ("colour", colour)), ephemeral: true);
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
                    await FollowupAsync(Texts.Get("gm.party.edit.badEmoji", ("emoji", emoji)), ephemeral: true);
                    return;
                }
            }

            if (!_data.UpdateParty(current.Id, newName, newColour, newEmoji))
            {
                await FollowupAsync(Texts.Get("gm.party.edit.failed", ("name", current.Name), ("newName", newName)), ephemeral: true);
                return;
            }
            _data.Log(LogKinds.StateChange, "gm party edit",
                $"Party {current.Id}: '{current.Name}' {current.Colour} {current.EmojiId} -> '{newName}' {newColour} {newEmoji}.", Context.User.Id);

            var warning = colour is null ? null : SimilarColourWarning(newColour, current.Id);
            var problems = await _partySync.SyncAsync(current.Id);
            var updated = _data.GetParty(current.Id)!;

            await FollowupAsync(
                Display.Truncate(
                    (updated.EmojiId is null
                        ? Texts.Get("gm.party.edit.updatedNoEmoji", ("name", updated.Name), ("colour", updated.Colour))
                        : Texts.Get("gm.party.edit.updatedWithEmoji", ("name", updated.Name), ("colour", updated.Colour), ("party", Display.Party(updated))))
                    + DiscordSummary(updated)
                    + WarningText(warning)
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
                await FollowupAsync(Texts.Get("gm.party.move.notActive", ("user", faction.Id)), ephemeral: true, allowedMentions: AllowedMentions.None);
                return;
            }
            Party? target = null;
            if (party is { } partyId && (target = _data.GetParty(partyId)) is null)
            {
                await FollowupAsync(Texts.Get("gm.party.notFound"), ephemeral: true);
                return;
            }

            if (_data.MoveFaction(faction.Id, party, "gm party move") is not { } result)
            {
                await FollowupAsync(Texts.Get("gm.party.move.failed"), ephemeral: true);
                return;
            }
            if (result.FromPartyId == party)
            {
                await FollowupAsync(
                    target is null
                        ? Texts.Get("gm.party.move.alreadyIndependent", ("faction", Display.Faction(result.Faction)))
                        : Texts.Get("gm.party.move.alreadyIn", ("faction", Display.Faction(result.Faction)), ("party", target.Name)),
                    ephemeral: true, allowedMentions: AllowedMentions.None);
                return;
            }

            var problems = new List<string>();
            if (party is { } joined)
                problems.AddRange(await _partySync.SyncAsync(joined));
            await _partySync.SyncMemberAsync(faction.Id);
            if (result.DissolvedParty is { } dissolved)
                await _partySync.HandleDissolutionAsync(dissolved, faction.Id, result.DissolvedLastMemberId);
            else if (result.FromPartyId is { } from)
                problems.AddRange(await _partySync.SyncAsync(from));

            var fromName = result.DissolvedParty?.Name ?? (result.FromPartyId is { } fromId ? _data.GetParty(fromId)?.Name : null);
            await FollowupAsync(
                Display.Truncate(
                    Texts.Get("gm.party.move.moved",
                        ("faction", Display.Faction(result.Faction)), ("from", PartyOrIndependent(fromName)), ("to", PartyOrIndependent(target?.Name)))
                    + (result.DissolvedParty is null ? "" : "\n" + Texts.Get("gm.party.move.dissolved", ("party", result.DissolvedParty.Name)))
                    + ProblemsText(problems)),
                ephemeral: true, allowedMentions: AllowedMentions.None);
        }

        [SlashCommand("delete", "Delete a party: its members become independent, its role is deleted and its chat archived")]
        public async Task DeleteAsync(
            [Summary("party", "The party"), Autocomplete(typeof(PartyAutocomplete))] long party)
        {
            if (_data.GetParty(party) is not { } target)
            {
                await RespondAsync(Texts.Get("gm.party.notFound"), ephemeral: true);
                return;
            }

            var memberCount = _data.GetPartyMembers(target.Id).Count;
            var components = _confirmations.Create(Context.User.Id, () => DeletePartyConfirmedAsync(target.Id));
            await RespondAsync(
                Texts.Get("gm.party.delete.confirm", ("name", target.Name), ("members", memberCount)),
                components: components, ephemeral: true);
        }

        private async Task<string> DeletePartyConfirmedAsync(long partyId)
        {
            var members = _data.GetPartyMembers(partyId).Select(f => f.UserId).ToList();
            if (_data.DeleteParty(partyId, "gm party delete") is not { } deleted)
                return Texts.Get("gm.party.delete.gone");

            await _partySync.ArchiveAsync(deleted);
            foreach (var member in members)
                await _partySync.SyncMemberAsync(member);

            var problems = new List<string>();
            var names = members.Select(id => Display.Faction(_data, id)).ToList();
            await PostPartyNewsAsync(
                names.Count switch
                {
                    0 => Texts.Get("gm.party.delete.news", ("name", deleted.Name)),
                    1 => Texts.Get("gm.party.delete.newsOneMember", ("name", deleted.Name), ("names", JoinNames(names))),
                    _ => Texts.Get("gm.party.delete.newsMembers", ("name", deleted.Name), ("names", JoinNames(names))),
                },
                "gm party delete", problems);

            return Texts.Get("gm.party.delete.deleted", ("name", deleted.Name), ("count", names.Count)) + ProblemsText(problems);
        }

        private static string? ValidatePartyName(string name) =>
            name.Length == 0 ? Texts.Get("gm.party.nameMissing")
            : name.Length > MaxPartyNameLength ? Texts.Get("gm.party.nameTooLong", ("max", MaxPartyNameLength))
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
                .Select(p => Texts.Get("gm.party.similarColourEntry", ("name", p.Name), ("colour", p.Colour)))
                .ToList();
            return close.Count == 0
                ? null
                : Texts.Get("gm.party.similarColour", ("colour", colour), ("parties", JoinNames(close)));
        }

        private string CreateRefusal(PartyCreateResult result, string name) => result.Outcome switch
        {
            PartyCreateOutcome.NameTaken => Texts.Get("gm.party.nameTaken", ("name", name)),
            PartyCreateOutcome.TooFewFounders => Texts.Get("gm.party.create.tooFewFounders"),
            PartyCreateOutcome.FounderNotRegistered => Texts.Get("gm.party.create.founderNotRegistered", ("user", result.OffendingFaction)),
            PartyCreateOutcome.FounderNotIndependent => Texts.Get("gm.party.create.founderNotIndependent",
                ("faction", Display.Faction(_data, result.OffendingFaction ?? 0)),
                ("party", (_data.GetFaction(result.OffendingFaction ?? 0)?.PartyId is { } p ? _data.GetParty(p)?.Name : null)
                          ?? Texts.Get("gm.party.create.aParty"))),
            _ => Texts.Get("gm.party.create.failed"),
        };

        private async Task PostPartyNewsAsync(string text, string source, List<string> problems)
        {
            try
            {
                if (await _publisher.PostAsync(ChannelRole.News, text, source) is null)
                    problems.Add(Texts.Get("gm.party.newsNotSet"));
            }
            catch (Exception ex)
            {
                problems.Add(Texts.Get("gm.party.newsFailed", ("error", ex.Message)));
            }
        }

        private static string PartyOrIndependent(string? name) =>
            name is null ? Texts.Get("gm.party.move.independent") : Texts.Get("gm.party.move.partyName", ("name", name));

        private static string DiscordSummary(Party party) =>
            (party.RoleId is { } role ? Texts.Get("gm.party.summary.role", ("role", role)) : Texts.Get("gm.party.summary.noRole"))
            + (party.ChannelId is { } channel ? Texts.Get("gm.party.summary.chat", ("channel", channel)) : Texts.Get("gm.party.summary.noChat"))
            + (party.ForeignChannelId is { } foreign ? Texts.Get("gm.party.summary.foreign", ("channel", foreign)) : Texts.Get("gm.party.summary.noForeign"));

        private static string WarningText(string? warning) =>
            warning is null ? "" : "\n" + Texts.Get("gm.warning", ("warning", warning));

        private static string ProblemsText(IReadOnlyCollection<string> problems) =>
            problems.Count == 0 ? "" : "\n" + Texts.Get("gm.party.problemsHeading") + "\n" + string.Join("\n", problems.Select(p => $"- {p}"));

        private static string JoinNames(IReadOnlyList<string> names) => names.Count switch
        {
            0 => "",
            1 => names[0],
            _ => string.Join(", ", names.Take(names.Count - 1)) + Texts.Get("gm.party.and") + names[^1],
        };
    }
}
