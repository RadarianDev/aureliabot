using Discord;
using Discord.Interactions;

public partial class GmModule
{
    [Group("cabinet", "Appoint the Chancellor or impose a cabinet")]
    [RequireUserPermission(GuildPermission.ManageGuild)]
    public class CabinetGroup : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly DatabaseService _data;
        private readonly GovernmentPublisher _government;
        private readonly CabinetSyncService _cabinetSync;
        private readonly FactionChannelService _factionChannels;

        public CabinetGroup(DatabaseService data, GovernmentPublisher government, CabinetSyncService cabinetSync, FactionChannelService factionChannels)
        {
            _data = data;
            _government = government;
            _cabinetSync = cabinetSync;
            _factionChannels = factionChannels;
        }

        [SlashCommand("chancellor", "Appoint the Chancellor, announced as a royal proclamation")]
        public async Task ChancellorAsync(
            [Summary("faction", "The new Chancellor"), Autocomplete(typeof(FactionAutocomplete))] string faction)
        {
            if (FactionAutocomplete.Find(_data, faction) is not { IsActive: true } appointee)
            {
                await RespondAsync(Texts.Get("gm.cabinet.noActiveFaction", ("text", faction)), ephemeral: true, allowedMentions: AllowedMentions.None);
                return;
            }

            await DeferAsync(ephemeral: true);
            var result = _data.AppointChancellor(appointee.UserId);
            if (result.Refusal is not null)
            {
                await FollowupAsync(result.Refusal, ephemeral: true, allowedMentions: AllowedMentions.None);
                return;
            }

            var who = Display.Faction(appointee);
            await _government.CloseNominationsAsync(result.Cancelled);
            await _government.ProclaimAsync(result.FilledSeat
                ? Texts.Get("cabinet.proclaim.appointedSitting", ("faction", who))
                : Texts.Get("cabinet.proclaim.appointedDesignate", ("faction", who)), "gm cabinet chancellor");
            var problems = await _cabinetSync.SyncAsync();
            if (result.FilledSeat || result.Nomination is not null)
                await _factionChannels.SendAsync(appointee.UserId, Texts.Get("cabinet.private.appointed"));

            var lines = new List<string>
            {
                result.FilledSeat
                    ? Texts.Get("gm.cabinet.appointedSitting", ("faction", who))
                    : result.Nomination is null
                        ? Texts.Get("gm.cabinet.alreadyChancellor", ("faction", who))
                        : Texts.Get("gm.cabinet.appointedDesignate", ("faction", who)),
            };
            if (result.VacatedMinistry is { } vacated)
                lines.Add(Texts.Get("gm.cabinet.vacatedMinistry", ("ministry", vacated.DisplayName())));
            if (result.Cancelled.Count > 0)
                lines.Add(Texts.Get("gm.cabinet.cancelled", ("count", result.Cancelled.Count)));
            lines.AddRange(problems.Select(p => Texts.Get("gm.warning", ("warning", p))));
            await FollowupAsync(string.Join("\n", lines), ephemeral: true, allowedMentions: AllowedMentions.None);
        }

        [SlashCommand("impose", "Impose a caretaker cabinet without a vote; unnamed ministries are left vacant")]
        public async Task ImposeAsync(
            [Summary("chancellor", "The Chancellor; defaults to the current one or the Chancellor-designate"), Autocomplete(typeof(FactionAutocomplete))] string? chancellor = null,
            [Summary("treasury", "Minister of the Treasury"), Autocomplete(typeof(FactionAutocomplete))] string? treasury = null,
            [Summary("war", "Minister of War"), Autocomplete(typeof(FactionAutocomplete))] string? war = null,
            [Summary("interior", "Minister of the Interior"), Autocomplete(typeof(FactionAutocomplete))] string? interior = null,
            [Summary("commerce", "Minister of Commerce and Labor"), Autocomplete(typeof(FactionAutocomplete))] string? commerce = null)
        {
            ulong? chancellorId = null;
            if (chancellor is not null)
            {
                if (FactionAutocomplete.Find(_data, chancellor) is not { } found)
                {
                    await RespondAsync(Texts.Get("gm.cabinet.noActiveFaction", ("text", chancellor)), ephemeral: true, allowedMentions: AllowedMentions.None);
                    return;
                }
                chancellorId = found.UserId;
            }

            var posts = new Dictionary<Ministry, ulong?>();
            foreach (var (ministry, text) in new (Ministry, string?)[]
                     { (Ministry.Treasury, treasury), (Ministry.War, war), (Ministry.Interior, interior), (Ministry.CommerceLabor, commerce) })
            {
                if (text is null)
                {
                    posts[ministry] = null;
                    continue;
                }
                if (FactionAutocomplete.Find(_data, text) is not { } found)
                {
                    await RespondAsync(Texts.Get("gm.cabinet.noActiveFaction", ("text", text)), ephemeral: true, allowedMentions: AllowedMentions.None);
                    return;
                }
                posts[ministry] = found.UserId;
            }

            await DeferAsync(ephemeral: true);
            var result = _data.ImposeCabinet(chancellorId, posts);
            if (result.Cabinet is not { } cabinet)
            {
                await FollowupAsync(result.Refusal ?? Texts.Get("gm.cabinet.failed"), ephemeral: true, allowedMentions: AllowedMentions.None);
                return;
            }

            await _government.CloseNominationsAsync(result.Cancelled);
            var lines = _government.CabinetLines(cabinet);
            await _government.ProclaimAsync(Texts.Get("cabinet.proclaim.imposed", ("cabinet", lines)), "gm cabinet impose");
            var problems = await _cabinetSync.SyncAsync();

            var reply = new List<string> { Texts.Get("gm.cabinet.imposed", ("cabinet", lines)) };
            if (result.Cancelled.Count > 0)
                reply.Add(Texts.Get("gm.cabinet.cancelled", ("count", result.Cancelled.Count)));
            reply.AddRange(problems.Select(p => Texts.Get("gm.warning", ("warning", p))));
            await FollowupAsync(string.Join("\n", reply), ephemeral: true, allowedMentions: AllowedMentions.None);
        }
    }
}
