using Discord;
using Discord.Interactions;

public partial class GmModule
{
    [Group("faction", "Register, edit, retire and unretire factions")]
    [RequireUserPermission(GuildPermission.ManageGuild)]
    public class FactionGroup : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly DatabaseService _data;
        private readonly ConstantsStore _constants;
        private readonly FactionChannelService _factionChannels;
        private readonly PartySyncService _partySync;
        private readonly CabinetSyncService _cabinetSync;
        private readonly Confirmations _confirmations;
        private readonly Publisher _publisher;
        private readonly AlertService _alerts;

        public FactionGroup(
            DatabaseService data,
            ConstantsStore constants,
            FactionChannelService factionChannels,
            PartySyncService partySync,
            CabinetSyncService cabinetSync,
            Confirmations confirmations,
            Publisher publisher,
            AlertService alerts)
        {
            _data = data;
            _constants = constants;
            _factionChannels = factionChannels;
            _partySync = partySync;
            _cabinetSync = cabinetSync;
            _confirmations = confirmations;
            _publisher = publisher;
            _alerts = alerts;
        }

        [SlashCommand("create", "Register a player as a faction, with a private channel")]
        public async Task CreateAsync(
            [Summary("player", "The player's Discord account")] IUser player,
            [Summary("character", "The character's name"), MaxLength(80)] string character,
            [Summary("home", "Home province, the character's safe borough"), Autocomplete(typeof(ProvinceAutocomplete))] string home)
        {
            await DeferAsync(ephemeral: true);

            character = character.Trim();
            var config = _constants.Config;
            var province = ProvinceAutocomplete.Find(config, home);
            var refusal = (player, character, province, existing: _data.GetFaction(player.Id)) switch
            {
                ({ IsBot: true }, _, _, _) => Texts.Get("gm.faction.create.refusedBot"),
                (_, "", _, _) => Texts.Get("gm.faction.create.refusedNoName"),
                (_, _, null, _) => Texts.Get("gm.faction.noProvince", ("home", home)),
                (_, _, _, { IsActive: true }) => Texts.Get("gm.faction.create.alreadyHas", ("player", player.Mention)),
                (_, _, _, not null) => Texts.Get("gm.faction.create.alreadyRetired", ("player", player.Mention)),
                _ => null,
            };
            if (refusal is not null)
            {
                await FollowupAsync(refusal, ephemeral: true, allowedMentions: AllowedMentions.None);
                return;
            }

            if (!_data.CreateFaction(player.Id, character, province!.Id, config.Constants.Popularity.Start))
            {
                await FollowupAsync(Texts.Get("gm.faction.create.alreadyHas", ("player", player.Mention)), ephemeral: true, allowedMentions: AllowedMentions.None);
                return;
            }
            _data.Log(LogKinds.StateChange, "gm faction create", $"Registered {player.Id} as '{character}' of {province.Id}.", player.Id);

            var channelId = await _factionChannels.EnsureChannelAsync(player.Id);
            await _publisher.PostAsync(
                ChannelRole.News,
                Texts.Get("gm.faction.create.news", ("character", character), ("player", player.Mention), ("province", province.Name)),
                "gm faction create");
            var greeted = channelId is not null && await _factionChannels.SendAsync(
                player.Id,
                Texts.Get("gm.faction.create.greeting", ("player", player.Mention), ("character", character), ("province", province.Name)));

            var lines = new List<string>
            {
                Texts.Get("gm.faction.create.registered",
                    ("player", player.Mention), ("character", character), ("province", province.Name),
                    ("popularity", Display.Number(config.Constants.Popularity.Start))),
                channelId is { } id
                    ? Texts.Get("gm.faction.create.privateChannel", ("channel", id))
                    : Texts.Get("gm.faction.create.noPrivateChannel"),
            };
            if (channelId is not null && !greeted) lines.Add(Texts.Get("gm.faction.create.greetingFailed"));
            await FollowupAsync(string.Join("\n", lines), ephemeral: true, allowedMentions: AllowedMentions.None);
        }

        [SlashCommand("edit", "Change a faction's character name or home province")]
        public async Task EditAsync(
            [Summary("faction", "The faction"), Autocomplete(typeof(FactionAutocomplete))] string faction,
            [Summary("character", "New character name"), MaxLength(80)] string? character = null,
            [Summary("home", "New home province"), Autocomplete(typeof(ProvinceAutocomplete))] string? home = null)
        {
            if (FactionAutocomplete.Find(_data, faction) is not { } target)
            {
                await RespondAsync(Texts.Get("gm.faction.notFound"), ephemeral: true);
                return;
            }

            character = string.IsNullOrWhiteSpace(character) ? null : character.Trim();
            var config = _constants.Config;
            ProvinceDefinition? province = null;
            if (home is not null && (province = ProvinceAutocomplete.Find(config, home)) is null)
            {
                await RespondAsync(Texts.Get("gm.faction.noProvince", ("home", home)), ephemeral: true);
                return;
            }
            if (character is null && province is null)
            {
                await RespondAsync(Texts.Get("gm.faction.edit.nothing"), ephemeral: true);
                return;
            }

            _data.UpdateFaction(target.UserId, character, province?.Id);
            _data.Log(LogKinds.GmOverride, "gm faction edit",
                $"{target.UserId}: character '{target.CharacterName}' -> '{character ?? target.CharacterName}', home {target.HomeProvinceId} -> {province?.Id ?? target.HomeProvinceId}.",
                target.UserId);

            var changes = new List<string>();
            if (character is not null) changes.Add(Texts.Get("gm.faction.edit.changedCharacter", ("from", target.CharacterName), ("to", character)));
            if (province is not null) changes.Add(Texts.Get("gm.faction.edit.changedHome", ("from", Display.Province(config, target.HomeProvinceId)), ("to", province.Name)));
            await RespondAsync(Texts.Get("gm.faction.edit.done", ("user", target.UserId), ("changes", string.Join(", ", changes))), ephemeral: true, allowedMentions: AllowedMentions.None);
        }

        [SlashCommand("retire", "Retire a faction from politics")]
        public async Task RetireAsync(
            [Summary("faction", "The faction"), Autocomplete(typeof(FactionAutocomplete))] string faction)
        {
            if (FactionAutocomplete.Find(_data, faction) is not { } target)
            {
                await RespondAsync(Texts.Get("gm.faction.notFound"), ephemeral: true);
                return;
            }
            if (!target.IsActive)
            {
                await RespondAsync(Texts.Get("gm.faction.retire.alreadyRetired", ("faction", Display.Faction(target))), ephemeral: true, allowedMentions: AllowedMentions.None);
                return;
            }

            var lines = new List<string> { Texts.Get("gm.faction.retire.confirm", ("faction", Display.Faction(target))) };
            if (target.PartyId is { } partyId && _data.GetParty(partyId) is { } party)
            {
                var remaining = _data.GetPartyMembers(partyId).Count(m => m.UserId != target.UserId);
                lines.Add(remaining < 2
                    ? Texts.Get("gm.faction.retire.leavesDissolvingParty", ("party", Display.Party(party)), ("remaining", remaining))
                    : Texts.Get("gm.faction.retire.leavesParty", ("party", Display.Party(party))));
            }
            if (_data.GetSittingCabinet() is { } cabinet && cabinet.Holders.Contains(target.UserId))
                lines.Add(Texts.Get("gm.faction.retire.leavesCabinet"));
            lines.Add(Texts.Get("gm.faction.retire.dropsPending"));
            lines.Add(target.Seats > 0
                ? Texts.Get("gm.faction.retire.seatsVacantPreview", ("seats", target.Seats))
                : Texts.Get("gm.faction.retire.noSeats"));

            var userId = target.UserId;
            var buttons = _confirmations.Create(Context.User.Id, () => RetireConfirmedAsync(userId), Texts.Get("gm.faction.retire.button"));
            await RespondAsync(string.Join("\n", lines), components: buttons, ephemeral: true, allowedMentions: AllowedMentions.None);
        }

        private async Task<string> RetireConfirmedAsync(ulong userId)
        {
            var cabinet = _data.GetSittingCabinet();
            var vacated = new List<string>();
            if (cabinet?.ChancellorId == userId) vacated.Add(Texts.Get("gm.faction.retire.chancellorship"));
            if (cabinet is not null)
                vacated.AddRange(cabinet.Posts.Where(p => p.Value == userId)
                    .Select(p => Texts.Get("gm.faction.retire.ministry", ("portfolio", p.Key.DisplayName()))));

            if (_data.RetireFaction(userId) is not { } result)
                return Texts.Get("gm.faction.retire.notActive", ("user", userId));

            var lines = new List<string> { Texts.Get("gm.faction.retire.retired", ("faction", Display.Faction(result.Faction))) };
            if (result.DissolvedParty is { } dissolved)
            {
                lines.Add(Texts.Get("gm.faction.retire.dissolved", ("party", Display.Party(dissolved))));
                lines.Add(await TryAsync(Texts.Get("gm.faction.retire.syncPartyDissolution"),
                    () => _partySync.HandleDissolutionAsync(dissolved, null, result.DissolvedLastMemberId)));
            }
            else
            {
                lines.Add(await TryAsync(Texts.Get("gm.faction.retire.syncPartyRole"), () => _partySync.SyncMemberAsync(userId)));
                if (result.Faction.PartyId is { } formerParty)
                    lines.Add(await TryAsync(Texts.Get("gm.faction.retire.syncParty"), () => _partySync.SyncAsync(formerParty)));
            }
            if (vacated.Count > 0)
            {
                var vacancy = Texts.Get("gm.faction.retire.vacancy",
                    ("faction", Display.Faction(result.Faction)), ("posts", string.Join(Texts.Get("gm.faction.retire.and"), vacated)));
                lines.Add(vacancy);
                await _alerts.PostAsync(Texts.Get("gm.faction.retire.vacancyAlert", ("vacancy", vacancy)));
            }
            lines.Add(await TryAsync(Texts.Get("gm.faction.retire.syncCabinetRole"), () => _cabinetSync.SyncAsync()));
            if (result.Faction.Seats > 0)
                lines.Add(Texts.Get("gm.faction.retire.seatsVacant", ("seats", result.Faction.Seats)));
            return string.Join("\n", lines.Where(l => l.Length > 0));
        }

        [SlashCommand("unretire", "Return a retired faction to politics")]
        public async Task UnretireAsync(
            [Summary("faction", "The faction"), Autocomplete(typeof(RetiredFactionAutocomplete))] string faction)
        {
            if (FactionAutocomplete.Find(_data, faction) is not { } target)
            {
                await RespondAsync(Texts.Get("gm.faction.notFound"), ephemeral: true);
                return;
            }
            if (target.IsActive)
            {
                await RespondAsync(Texts.Get("gm.faction.unretire.alreadyActive", ("faction", Display.Faction(target))), ephemeral: true, allowedMentions: AllowedMentions.None);
                return;
            }

            var lines = new List<string>
            {
                Texts.Get("gm.faction.unretire.confirm", ("faction", Display.Faction(target))),
                Texts.Get("gm.faction.unretire.independent", ("popularity", Display.Number(target.Popularity))),
                target.Seats > 0
                    ? Texts.Get("gm.faction.unretire.seatsCountAgain", ("seats", target.Seats))
                    : Texts.Get("gm.faction.unretire.noSeats"),
                Texts.Get("gm.faction.unretire.nothingRestored"),
            };

            var userId = target.UserId;
            var buttons = _confirmations.Create(Context.User.Id, () => UnretireConfirmedAsync(userId), Texts.Get("gm.faction.unretire.button"));
            await RespondAsync(string.Join("\n", lines), components: buttons, ephemeral: true, allowedMentions: AllowedMentions.None);
        }

        private async Task<string> UnretireConfirmedAsync(ulong userId)
        {
            if (_data.UnretireFaction(userId) is not { } faction)
                return Texts.Get("gm.faction.unretire.notRetired", ("user", userId));

            await _publisher.PostAsync(
                ChannelRole.News,
                Texts.Get("gm.faction.unretire.news", ("character", faction.CharacterName), ("player", MentionUtils.MentionUser(userId))),
                "gm faction unretire");
            return Texts.Get("gm.faction.unretire.returned", ("faction", Display.Faction(faction)));
        }

        private static async Task<string> TryAsync(string what, Func<Task> sync)
        {
            try
            {
                await sync();
                return "";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GmFaction] {what} sync failed: {ex}");
                return Texts.Get("gm.faction.retire.syncFailed", ("what", what), ("error", ex.Message));
            }
        }
    }
}
