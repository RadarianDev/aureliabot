using Discord;
using Discord.Interactions;

public partial class GmModule
{
    [Group("faction", "Register, edit and retire factions")]
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
                ({ IsBot: true }, _, _, _) => "A bot can't be a faction.",
                (_, "", _, _) => "The character needs a name.",
                (_, _, null, _) => $"There is no province `{home}`. Pick one from the list.",
                (_, _, _, { IsActive: true }) => $"{player.Mention} already has a faction.",
                (_, _, _, not null) => $"{player.Mention} already has a faction, now retired. A player can't register twice.",
                _ => null,
            };
            if (refusal is not null)
            {
                await FollowupAsync(refusal, ephemeral: true, allowedMentions: AllowedMentions.None);
                return;
            }

            if (!_data.CreateFaction(player.Id, character, province!.Id, config.Constants.Popularity.Start))
            {
                await FollowupAsync($"{player.Mention} already has a faction.", ephemeral: true, allowedMentions: AllowedMentions.None);
                return;
            }
            _data.Log(LogKinds.StateChange, "gm faction create", $"Registered {player.Id} as '{character}' of {province.Id}.", player.Id);

            var channelId = await _factionChannels.EnsureChannelAsync(player.Id);
            await _publisher.PostAsync(
                ChannelRole.News,
                $"**{character}** ({player.Mention}) enters politics, standing for {province.Name}.",
                "gm faction create");
            var greeted = channelId is not null && await _factionChannels.SendAsync(
                player.Id,
                $"Welcome to politics, {player.Mention}. This is the private office of **{character}**, member for {province.Name}. " +
                "Poll results and reminders arrive here, and you can run your commands here.");

            var lines = new List<string>
            {
                $"Registered {player.Mention} as **{character}**, home province {province.Name}, at popularity {Display.Number(config.Constants.Popularity.Start)}, independent, with no seats until the next election.",
                channelId is { } id ? $"Private channel: <#{id}>." : "⚠️ The private channel could not be made; see #gm, then run `/gm setup`.",
            };
            if (channelId is not null && !greeted) lines.Add("⚠️ The greeting could not be posted.");
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
                await RespondAsync("No such faction. Pick one from the list.", ephemeral: true);
                return;
            }

            character = string.IsNullOrWhiteSpace(character) ? null : character.Trim();
            var config = _constants.Config;
            ProvinceDefinition? province = null;
            if (home is not null && (province = ProvinceAutocomplete.Find(config, home)) is null)
            {
                await RespondAsync($"There is no province `{home}`. Pick one from the list.", ephemeral: true);
                return;
            }
            if (character is null && province is null)
            {
                await RespondAsync("Give a new `character` or `home`.", ephemeral: true);
                return;
            }

            _data.UpdateFaction(target.UserId, character, province?.Id);
            _data.Log(LogKinds.GmOverride, "gm faction edit",
                $"{target.UserId}: character '{target.CharacterName}' -> '{character ?? target.CharacterName}', home {target.HomeProvinceId} -> {province?.Id ?? target.HomeProvinceId}.",
                target.UserId);

            var changes = new List<string>();
            if (character is not null) changes.Add($"character **{target.CharacterName}** → **{character}**");
            if (province is not null) changes.Add($"home {Display.Province(config, target.HomeProvinceId)} → {province.Name}");
            await RespondAsync($"<@{target.UserId}>: {string.Join(", ", changes)}.", ephemeral: true, allowedMentions: AllowedMentions.None);
        }

        [SlashCommand("retire", "Retire a faction from politics")]
        public async Task RetireAsync(
            [Summary("faction", "The faction"), Autocomplete(typeof(FactionAutocomplete))] string faction)
        {
            if (FactionAutocomplete.Find(_data, faction) is not { } target)
            {
                await RespondAsync("No such faction. Pick one from the list.", ephemeral: true);
                return;
            }
            if (!target.IsActive)
            {
                await RespondAsync($"{Display.Faction(target)} is already retired.", ephemeral: true, allowedMentions: AllowedMentions.None);
                return;
            }

            var lines = new List<string> { $"Retire {Display.Faction(target)}?" };
            if (target.PartyId is { } partyId && _data.GetParty(partyId) is { } party)
            {
                var remaining = _data.GetPartyMembers(partyId).Count(m => m.UserId != target.UserId);
                lines.Add(remaining < 2
                    ? $"- They leave {Display.Party(party)}, which dissolves: only {remaining} active member would remain."
                    : $"- They leave {Display.Party(party)}.");
            }
            if (_data.GetSittingCabinet() is { } cabinet && cabinet.Holders.Contains(target.UserId))
                lines.Add("- They leave the cabinet; their post falls vacant.");
            lines.Add("- Their pending actions and join applications are dropped.");
            lines.Add(target.Seats > 0
                ? $"- Their {target.Seats} seat(s) stay vacant until the next election."
                : "- They hold no seats.");

            var userId = target.UserId;
            var buttons = _confirmations.Create(Context.User.Id, () => RetireConfirmedAsync(userId), "Retire");
            await RespondAsync(string.Join("\n", lines), components: buttons, ephemeral: true, allowedMentions: AllowedMentions.None);
        }

        private async Task<string> RetireConfirmedAsync(ulong userId)
        {
            var cabinet = _data.GetSittingCabinet();
            var vacated = new List<string>();
            if (cabinet?.ChancellorId == userId) vacated.Add("the Chancellorship");
            if (cabinet is not null)
                vacated.AddRange(cabinet.Posts.Where(p => p.Value == userId).Select(p => $"the Ministry of {p.Key.DisplayName()}"));

            if (_data.RetireFaction(userId) is not { } result)
                return $"<@{userId}> is not an active faction; nothing was changed.";

            var lines = new List<string> { $"{Display.Faction(result.Faction)} has retired from politics." };
            if (result.DissolvedParty is { } dissolved)
            {
                lines.Add($"{Display.Party(dissolved)} dissolved.");
                lines.Add(await TryAsync("party dissolution", () => _partySync.HandleDissolutionAsync(dissolved, null, result.DissolvedLastMemberId)));
            }
            else
            {
                lines.Add(await TryAsync("party role", () => _partySync.SyncMemberAsync(userId)));
                if (result.Faction.PartyId is { } formerParty)
                    lines.Add(await TryAsync("party sync", () => _partySync.SyncAsync(formerParty)));
            }
            if (vacated.Count > 0)
            {
                var vacancy = $"{Display.Faction(result.Faction)} retired, leaving {string.Join(" and ", vacated)} vacant.";
                lines.Add(vacancy);
                await _alerts.PostAsync($"A cabinet seat is empty: {vacancy}");
            }
            lines.Add(await TryAsync("cabinet role", () => _cabinetSync.SyncAsync()));
            if (result.Faction.Seats > 0)
                lines.Add($"Their {result.Faction.Seats} seat(s) stay vacant until the next election.");
            return string.Join("\n", lines.Where(l => l.Length > 0));
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
                return $"⚠️ The {what} sync failed: {ex.Message}";
            }
        }
    }
}
