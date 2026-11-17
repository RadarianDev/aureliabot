using Discord;
using Discord.Interactions;

public partial class GmModule
{
    [Group("agendas", "See, deal and re-deal the factions' secret agendas")]
    [RequireUserPermission(GuildPermission.ManageGuild)]
    public class AgendasGroup : InteractionModuleBase<SocketInteractionContext>
    {
        private readonly DatabaseService _data;
        private readonly ConstantsStore _constants;
        private readonly AgendaService _agendas;

        public AgendasGroup(DatabaseService data, ConstantsStore constants, AgendaService agendas)
        {
            _data = data;
            _constants = constants;
            _agendas = agendas;
        }

        [SlashCommand("list", "Privately show every faction's secret agenda and how it stands")]
        public async Task ListAsync()
        {
            var config = _constants.Config.Agendas;
            var now = _data.GetTrackValues();
            var hands = _data.GetActiveAgendas();
            var lines = new List<string> { Texts.Get("secretAgenda.gm.heading") };
            foreach (var hand in hands)
            {
                if (config.Find(hand.AgendaId) is not { } agenda) continue;
                var progress = agenda.Wishes.Select(w =>
                {
                    var start = hand.Start.GetValueOrDefault(w.Track, now[w.Track]);
                    var met = AgendaRules.Met(w, start, now[w.Track], config.MarginOf(w.Track));
                    return Texts.Get("secretAgenda.gm.progress",
                        ("track", AgendaWording.Track(w.Track)), ("start", Display.Number(start)), ("now", Display.Number(now[w.Track])),
                        ("mark", met ? Texts.Get("secretAgenda.gm.on") : Texts.Get("secretAgenda.gm.off")));
                });
                lines.Add(Texts.Get("secretAgenda.gm.line",
                    ("faction", Display.Faction(_data, hand.FactionId)), ("agenda", agenda.Name),
                    ("season", GameCalendar.Label(hand.Season, hand.Year)), ("progress", string.Join(Texts.Get("secretAgenda.wish.separator"), progress))));
            }
            if (hands.Count == 0) lines.Add(Texts.Get("secretAgenda.gm.none"));

            var assignments = _data.GetAgendaAssignments();
            if (assignments.Count > 0)
            {
                lines.Add("");
                lines.Add(Texts.Get("secretAgenda.gm.assignedHeading"));
                lines.AddRange(assignments.Select(kv => Texts.Get("secretAgenda.gm.assignedLine",
                    ("faction", Display.Faction(_data, kv.Key)), ("agenda", config.Find(kv.Value)?.Name ?? kv.Value))));
            }

            var futures = _data.GetFutureAgendas();
            if (futures.Count > 0)
            {
                lines.Add("");
                lines.Add(Texts.Get("secretAgenda.gm.futureHeading"));
                lines.AddRange(futures.Select(kv => Texts.Get("secretAgenda.gm.assignedLine",
                    ("faction", Display.Faction(_data, kv.Key)), ("agenda", config.Find(kv.Value)?.Name ?? kv.Value))));
            }

            lines.Add("");
            lines.Add(Texts.Get("secretAgenda.gm.balance", ("counts", string.Join(Texts.Get("secretAgenda.wish.separator"), config.Agendas.Select(a =>
                Texts.Get("secretAgenda.gm.count", ("agenda", a.Name), ("held", hands.Count(h => h.AgendaId == a.Id)),
                    ("assigned", assignments.Values.Count(id => id == a.Id))))))));
            var chunks = Publisher.Chunk(string.Join("\n", lines)).ToList();
            await RespondAsync(chunks[0], ephemeral: true, allowedMentions: AllowedMentions.None);
            foreach (var chunk in chunks.Skip(1))
                await FollowupAsync(chunk, ephemeral: true, allowedMentions: AllowedMentions.None);
        }

        [SlashCommand("deal", "Deal a secret agenda to every active faction that has none, scored at the next election")]
        public async Task DealAsync()
        {
            await DeferAsync(ephemeral: true);
            if (Refusal() is { } refusal)
            {
                await FollowupAsync(refusal, ephemeral: true);
                return;
            }
            var (dealt, unsent) = await _agendas.DealMissingAsync();
            var lines = new List<string>
            {
                dealt.Count == 0
                    ? Texts.Get("secretAgenda.gm.dealtNone")
                    : Texts.Get("secretAgenda.gm.dealt", ("count", dealt.Count), ("factions", string.Join(", ", dealt.Select(id => Display.Faction(_data, id))))),
            };
            if (unsent.Count > 0)
                lines.Add(Texts.Get("secretAgenda.gm.sendFailed", ("factions", string.Join(", ", unsent.Select(id => Display.Faction(_data, id))))));
            await FollowupAsync(string.Join("\n", lines), ephemeral: true, allowedMentions: AllowedMentions.None);
        }

        [SlashCommand("redeal", "Void a faction's secret agenda and deal it a different one")]
        public async Task RedealAsync([Summary("faction", "The faction"), Autocomplete(typeof(FactionAutocomplete))] string faction)
        {
            await DeferAsync(ephemeral: true);
            if (Refusal() is { } refusal)
            {
                await FollowupAsync(refusal, ephemeral: true);
                return;
            }
            if (FactionAutocomplete.Find(_data, faction) is not { } target)
            {
                await FollowupAsync(Texts.Get("gm.faction.notFound"), ephemeral: true);
                return;
            }
            var (hand, sent) = await _agendas.RedealAsync(target.UserId);
            var name = Display.Faction(target);
            if (hand is null || _constants.Config.Agendas.Find(hand.AgendaId) is not { } agenda)
            {
                await FollowupAsync(Texts.Get("secretAgenda.gm.redealFailed", ("faction", name)), ephemeral: true, allowedMentions: AllowedMentions.None);
                return;
            }
            var text = Texts.Get("secretAgenda.gm.redealt", ("faction", name), ("agenda", agenda.Name));
            if (!sent) text += "\n" + Texts.Get("secretAgenda.gm.sendFailed", ("factions", name));
            await FollowupAsync(text, ephemeral: true, allowedMentions: AllowedMentions.None);
        }

        [SlashCommand("assign", "Choose a faction's secret agenda, for the next deal or at once")]
        public async Task AssignAsync(
            [Summary("faction", "The faction"), Autocomplete(typeof(FactionAutocomplete))] string faction,
            [Summary("agenda", "The agenda"), Autocomplete(typeof(AgendaAutocomplete))] string agenda,
            [Summary("now", "Replace their current agenda at once (default: hold it for the next deal)")] bool now = false)
        {
            await DeferAsync(ephemeral: true);
            if (FactionAutocomplete.Find(_data, faction) is not { } target)
            {
                await FollowupAsync(Texts.Get("gm.faction.notFound"), ephemeral: true);
                return;
            }
            var (result, sent) = await _agendas.AssignAsync(target.UserId, agenda, now);
            var name = Display.Faction(target);
            var agendaName = _constants.Config.Agendas.Find(agenda)?.Name ?? agenda;
            var lines = new List<string>
            {
                result.Refusal switch
                {
                    AssignAgendaRefusal.UnknownAgenda => Texts.Get("secretAgenda.gm.unknownAgenda", ("agenda", agenda)),
                    AssignAgendaRefusal.NotActive => Texts.Get("secretAgenda.gm.notActive", ("faction", name)),
                    AssignAgendaRefusal.NotStarted => Texts.Get("secretAgenda.gm.notStarted"),
                    AssignAgendaRefusal.NowInPreElection => Texts.Get("secretAgenda.gm.nowInPreElection", ("faction", name)),
                    _ when now => Texts.Get("secretAgenda.gm.assignedNow", ("faction", name), ("agenda", agendaName)),
                    _ when result.Future is not null => Texts.Get("secretAgenda.gm.assignedFuture", ("faction", name), ("agenda", agendaName)),
                    _ => Texts.Get("secretAgenda.gm.assignedNext", ("faction", name), ("agenda", agendaName)),
                },
            };
            if (result.Refusal is null && result.Repeats) lines.Add(Texts.Get("secretAgenda.gm.repeats", ("faction", name)));
            if ((result.Dealt is not null || result.Future is not null) && !sent) lines.Add(Texts.Get("secretAgenda.gm.sendFailed", ("factions", name)));
            await FollowupAsync(string.Join("\n", lines), ephemeral: true, allowedMentions: AllowedMentions.None);
        }

        [SlashCommand("cancel", "Drop the agenda you chose for a faction's next deal")]
        public async Task CancelAsync([Summary("faction", "The faction"), Autocomplete(typeof(FactionAutocomplete))] string faction)
        {
            if (FactionAutocomplete.Find(_data, faction) is not { } target)
            {
                await RespondAsync(Texts.Get("gm.faction.notFound"), ephemeral: true);
                return;
            }
            var name = Display.Faction(target);
            await RespondAsync(_data.CancelAgendaAssignment(target.UserId)
                ? Texts.Get("secretAgenda.gm.cancelled", ("faction", name))
                : Texts.Get("secretAgenda.gm.nothingAssigned", ("faction", name)), ephemeral: true, allowedMentions: AllowedMentions.None);
        }

        private string? Refusal() =>
            _constants.Config.Agendas.Agendas.Count == 0 ? Texts.Get("secretAgenda.gm.noAgendas")
            : !_data.GetGameState().Started ? Texts.Get("secretAgenda.gm.notStarted")
            : null;
    }
}
