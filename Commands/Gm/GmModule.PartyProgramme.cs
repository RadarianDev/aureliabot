using Discord;
using Discord.Interactions;

public partial class GmModule
{
    public partial class PartyGroup
    {
        private const string ProgrammeSource = "gm party programme";

        [SlashCommand("programme", "Record a party's programme from a linked message")]
        public async Task ProgrammeAsync(
            [Summary("party", "The party"), Autocomplete(typeof(ProgrammePartyAutocomplete))] string party,
            [Summary("message", "Link to the programme message (Copy Message Link)")] string message)
        {
            await DeferAsync(ephemeral: true);

            if (PartyModule.FindParty(_data, party) is not { } target)
            {
                await FollowupAsync(Texts.Get("gm.party.programme.noParty", ("party", party)), ephemeral: true, allowedMentions: AllowedMentions.None);
                return;
            }

            if (!ProgrammeParser.TryParseMessageLink(message, out var link))
            {
                await FollowupAsync(Texts.Get("gm.party.programme.notLink"), ephemeral: true);
                return;
            }
            if (link.GuildId != Context.Guild.Id)
            {
                await FollowupAsync(Texts.Get("gm.party.programme.otherServer"), ephemeral: true);
                return;
            }
            if (Context.Client.GetChannel(link.ChannelId) is not IMessageChannel channel)
            {
                await FollowupAsync(Texts.Get("gm.party.programme.cantSeeChannel", ("channel", link.ChannelId)), ephemeral: true);
                return;
            }

            IMessage? source;
            try
            {
                source = await channel.GetMessageAsync(link.MessageId);
            }
            catch (Discord.Net.HttpException ex)
            {
                await FollowupAsync(
                    Texts.Get("gm.party.programme.cantRead", ("channel", link.ChannelId), ("reason", ex.Reason ?? ex.Message)),
                    ephemeral: true);
                return;
            }
            if (source is null)
            {
                await FollowupAsync(Texts.Get("gm.party.programme.messageNotFound", ("channel", link.ChannelId)), ephemeral: true);
                return;
            }

            var openIssues = _data.GetOpenIssues();
            var parsed = ProgrammeParser.Parse(source.Content, openIssues, _data.GetAllIssues());
            if (!parsed.IsValid)
            {
                var errors = parsed.Errors.Select(e => Texts.Get("gm.party.programme.badLine",
                    ("line", e.LineNumber), ("text", e.Text.Replace("`", "'")), ("reason", e.Reason)));
                await FollowupAsync(
                    Display.Truncate(Texts.Get("gm.party.programme.badLines", ("party", Display.Party(target)), ("count", parsed.Errors.Count)) + "\n" +
                                     string.Join("\n", errors)),
                    ephemeral: true);
                return;
            }

            var state = _data.GetGameState();
            var before = _data.GetProgramme(target.Id);
            var flips = DatabaseService.ProgrammeFlips(before, parsed.Stances).ToHashSet();
            var penalty = state.IsPreElection ? 0 : flips.Count * _constants.Current.Programme.FlipPenalty;
            var members = _data.GetPartyMembers(target.Id);

            var preview = new List<string> { Texts.Get("gm.party.programme.previewTitle", ("party", Display.Party(target)), ("url", source.GetJumpUrl())) };
            if (openIssues.Count == 0) preview.Add(Texts.Get("gm.party.programme.noOpenIssues"));
            foreach (var issue in openIssues)
            {
                var stance = parsed.Stances.GetValueOrDefault(issue.Id, Stance.Silent);
                var stanceText = stance == Stance.Silent ? Texts.Get("gm.party.programme.noStance") : stance.DisplayName();
                preview.Add(flips.Contains(issue.Id)
                    ? Texts.Get("gm.party.programme.previewLineFlip", ("issue", issue.Title), ("stance", stanceText), ("from", before[issue.Id].DisplayName()))
                    : Texts.Get("gm.party.programme.previewLine", ("issue", issue.Title), ("stance", stanceText)));
            }

            preview.Add("");
            if (state.IsPreElection)
                preview.Add(Texts.Get("gm.party.programme.preElection"));
            else
            {
                preview.Add(flips.Count == 0
                    ? Texts.Get("gm.party.programme.noFlips")
                    : Texts.Get("gm.party.programme.flips", ("flips", flips.Count), ("members", members.Count), ("penalty", Display.Number(penalty))));
                preview.Add(Texts.Get("gm.party.programme.postedToCampaignLog"));
            }
            preview.Add(Texts.Get("gm.party.programme.willReply"));

            var partyId = target.Id;
            var stances = parsed.Stances;
            var url = source.GetJumpUrl();
            var components = _confirmations.Create(
                Context.User.Id,
                () => RecordProgrammeAsync(partyId, stances, url, link),
                Texts.Get("gm.party.programme.button"),
                ButtonStyle.Success);
            await FollowupAsync(Display.Truncate(string.Join("\n", preview)), components: components, ephemeral: true);
        }

        private async Task<string> RecordProgrammeAsync(long partyId, IReadOnlyDictionary<string, Stance> stances, string url, MessageLink link)
        {
            if (_data.GetParty(partyId) is not { } party) return Texts.Get("gm.party.programme.gone");

            var state = _data.GetGameState();
            var result = _data.RecordProgramme(partyId, stances, url, _constants.Current);
            var openIssues = _data.GetOpenIssues();
            var titles = _data.GetAllIssues().ToDictionary(i => i.Id, i => i.Title);
            var programme = ProgrammeText(stances, openIssues);
            var problems = new List<string>();

            try
            {
                if (state.IsPreElection)
                    await _publisher.PostAsync(ChannelRole.News,
                        Texts.Get("gm.party.programme.manifestoPost", ("party", TheParty(party.Name)), ("programme", programme)), ProgrammeSource);
                else
                {
                    await _publisher.PostAsync(ChannelRole.CampaignLog,
                        Texts.Get("gm.party.programme.programmePost", ("party", TheParty(party.Name)), ("programme", programme)), ProgrammeSource);
                    foreach (var issueId in result.FlippedIssueIds)
                        await _publisher.PostAsync(ChannelRole.News,
                            Texts.Get("gm.party.programme.flipNews",
                                ("party", Capitalise(TheParty(party.Name))), ("issue", titles.GetValueOrDefault(issueId, issueId))),
                            ProgrammeSource);
                }
            }
            catch (Exception ex)
            {
                problems.Add(Texts.Get("gm.party.programme.postFailed", ("error", ex.Message)));
            }

            try
            {
                if (Context.Client.GetChannel(link.ChannelId) is IMessageChannel channel)
                {
                    var recordedReply = await channel.SendMessageAsync(
                        Display.Truncate(Texts.Get("gm.party.programme.recordedReply", ("party", Display.Party(party)), ("programme", programme))),
                        allowedMentions: AllowedMentions.None,
                        messageReference: new MessageReference(link.MessageId, link.ChannelId, link.GuildId, false));
                    _data.LogPost(link.ChannelId, recordedReply.Id, "gm party programme");
                }
                else
                    problems.Add(Texts.Get("gm.party.programme.cantReachChannel"));
            }
            catch (Exception ex)
            {
                problems.Add(Texts.Get("gm.party.programme.replyFailed", ("error", ex.Message)));
            }

            if (problems.Count > 0)
                await _alerts.PostAsync(Texts.Get("gm.party.programme.alert", ("party", party.Name), ("problems", string.Join("; ", problems))));

            var partyText = Display.Party(party);
            var count = stances.Count(kv => kv.Value != Stance.Silent);
            var reply = state.IsPreElection
                ? Texts.Get("gm.party.programme.recordedManifesto", ("party", partyText), ("count", count))
                : result.FlippedIssueIds.Count == 0
                    ? Texts.Get("gm.party.programme.recordedNoFlip", ("party", partyText), ("count", count))
                    : Texts.Get("gm.party.programme.recordedFlips", ("party", partyText), ("count", count),
                        ("issues", string.Join(", ", result.FlippedIssueIds.Select(id => titles.GetValueOrDefault(id, id)))),
                        ("members", result.PenalisedFactions.Count), ("penalty", Display.Number(result.PenaltyPerMember)));
            if (problems.Count > 0) reply += "\n" + Texts.Get("gm.party.programme.butProblems", ("problems", string.Join("; ", problems)));
            return reply;
        }

        private static string ProgrammeText(IReadOnlyDictionary<string, Stance> stances, IReadOnlyList<Issue> openIssues)
        {
            var stanced = openIssues
                .Where(i => stances.GetValueOrDefault(i.Id, Stance.Silent) != Stance.Silent)
                .Select(i => Texts.Get("gm.party.programme.stanceLine", ("issue", i.Title), ("stance", stances[i.Id].DisplayName())))
                .ToList();
            var silent = openIssues.Where(i => stances.GetValueOrDefault(i.Id, Stance.Silent) == Stance.Silent).Select(i => i.Title).ToList();

            List<string> lines = stanced.Count == 0 ? [Texts.Get("gm.party.programme.noStanceAtAll")] : stanced;
            if (stanced.Count > 0 && silent.Count > 0)
                lines.Add(Texts.Get("gm.party.programme.noStanceOn", ("issues", string.Join(", ", silent))));
            return string.Join("\n", lines);
        }

        private static string TheParty(string name) =>
            name.StartsWith("the ", StringComparison.OrdinalIgnoreCase) ? name : Texts.Get("gm.party.programme.theParty", ("name", name));

        private static string Capitalise(string text) =>
            text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

        private sealed class ProgrammePartyAutocomplete : AutocompleteHandler
        {
            public override Task<AutocompletionResult> GenerateSuggestionsAsync(
                IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
            {
                var data = (DatabaseService)services.GetService(typeof(DatabaseService))!;
                var typed = autocompleteInteraction.Data.Current.Value?.ToString() ?? "";
                var results = data.GetAllParties()
                    .Where(p => p.Name.Contains(typed, StringComparison.OrdinalIgnoreCase))
                    .Take(25)
                    .Select(p => new AutocompleteResult(p.Name, p.Name));
                return Task.FromResult(AutocompletionResult.FromSuccess(results));
            }
        }
    }
}
