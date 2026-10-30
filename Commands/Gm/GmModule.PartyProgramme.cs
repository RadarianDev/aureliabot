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
                await FollowupAsync($"There is no party called \"{party}\".", ephemeral: true, allowedMentions: AllowedMentions.None);
                return;
            }

            if (!ProgrammeParser.TryParseMessageLink(message, out var link))
            {
                await FollowupAsync(
                    "That is not a message link. Use **Copy Message Link** in Discord: `https://discord.com/channels/<server>/<channel>/<message>`.",
                    ephemeral: true);
                return;
            }
            if (link.GuildId != Context.Guild.Id)
            {
                await FollowupAsync("That message is on another server.", ephemeral: true);
                return;
            }
            if (Context.Client.GetChannel(link.ChannelId) is not IMessageChannel channel)
            {
                await FollowupAsync($"I cannot see the channel <#{link.ChannelId}>.", ephemeral: true);
                return;
            }

            var source = await channel.GetMessageAsync(link.MessageId);
            if (source is null)
            {
                await FollowupAsync($"I cannot find that message in <#{link.ChannelId}>.", ephemeral: true);
                return;
            }

            var openIssues = _data.GetOpenIssues();
            var parsed = ProgrammeParser.Parse(source.Content, openIssues, _data.GetAllIssues());
            if (!parsed.IsValid)
            {
                var errors = parsed.Errors.Select(e => $"Line {e.LineNumber}: `{e.Text.Replace("`", "'")}` — {e.Reason}");
                await FollowupAsync(
                    Display.Truncate($"The programme of {Display.Party(target)} has {parsed.Errors.Count} bad line(s). Nothing was recorded.\n" +
                                     string.Join("\n", errors)),
                    ephemeral: true);
                return;
            }

            var state = _data.GetGameState();
            var before = _data.GetProgramme(target.Id);
            var flips = DatabaseService.ProgrammeFlips(before, parsed.Stances).ToHashSet();
            var penalty = state.IsPreElection ? 0 : flips.Count * _constants.Current.Programme.FlipPenalty;
            var members = _data.GetPartyMembers(target.Id);

            var preview = new List<string> { $"**Programme of {Display.Party(target)}** ({source.GetJumpUrl()})" };
            if (openIssues.Count == 0) preview.Add("There are no open issues.");
            foreach (var issue in openIssues)
            {
                var stance = parsed.Stances.GetValueOrDefault(issue.Id, Stance.Silent);
                var line = $"{issue.Title}: {(stance == Stance.Silent ? "*no stance*" : stance.DisplayName())}";
                if (flips.Contains(issue.Id))
                    line += $" ⚠️ flips from {before[issue.Id].DisplayName()}";
                preview.Add(line);
            }

            preview.Add("");
            if (state.IsPreElection)
                preview.Add("This is the pre-election, so the programme is a manifesto: it changes freely and is posted to #news.");
            else
            {
                preview.Add(flips.Count == 0
                    ? "No stance flips sign, so the change is free."
                    : $"{flips.Count} stance(s) flip sign: each of the {members.Count} members loses {Display.Number(penalty)} popularity, and each flip is announced in #news.");
                preview.Add("The programme is posted to #campaign-log.");
            }
            preview.Add("I will reply to the original message with the recorded programme.");

            var partyId = target.Id;
            var stances = parsed.Stances;
            var url = source.GetJumpUrl();
            var components = _confirmations.Create(
                Context.User.Id,
                () => RecordProgrammeAsync(partyId, stances, url, link),
                "Record",
                ButtonStyle.Success);
            await FollowupAsync(Display.Truncate(string.Join("\n", preview)), components: components, ephemeral: true);
        }

        private async Task<string> RecordProgrammeAsync(long partyId, IReadOnlyDictionary<string, Stance> stances, string url, MessageLink link)
        {
            if (_data.GetParty(partyId) is not { } party) return "That party no longer exists. Nothing was recorded.";

            var state = _data.GetGameState();
            var result = _data.RecordProgramme(partyId, stances, url, _constants.Current);
            var openIssues = _data.GetOpenIssues();
            var titles = _data.GetAllIssues().ToDictionary(i => i.Id, i => i.Title);
            var programme = ProgrammeText(stances, openIssues);
            var problems = new List<string>();

            try
            {
                if (state.IsPreElection)
                    await _publisher.PostAsync(ChannelRole.News, $"## Manifesto of {TheParty(party.Name)}\n{programme}", ProgrammeSource);
                else
                {
                    await _publisher.PostAsync(ChannelRole.CampaignLog, $"## Programme of {TheParty(party.Name)}\n{programme}", ProgrammeSource);
                    foreach (var issueId in result.FlippedIssueIds)
                        await _publisher.PostAsync(ChannelRole.News,
                            $"{Capitalise(TheParty(party.Name))} changes its line on {titles.GetValueOrDefault(issueId, issueId)}.",
                            ProgrammeSource);
                }
            }
            catch (Exception ex)
            {
                problems.Add($"posting it publicly failed: {ex.Message}");
            }

            try
            {
                if (Context.Client.GetChannel(link.ChannelId) is IMessageChannel channel)
                    await channel.SendMessageAsync(
                        Display.Truncate($"Recorded as the programme of {Display.Party(party)}:\n{programme}"),
                        allowedMentions: AllowedMentions.None,
                        messageReference: new MessageReference(link.MessageId, link.ChannelId, link.GuildId, false));
                else
                    problems.Add("I could not reach the original message's channel to reply");
            }
            catch (Exception ex)
            {
                problems.Add($"replying to the original message failed: {ex.Message}");
            }

            if (problems.Count > 0)
                await _alerts.PostAsync($"The programme of {party.Name} was recorded, but {string.Join("; ", problems)}.");

            var reply = $"Recorded the programme of {Display.Party(party)}: {stances.Count(kv => kv.Value != Stance.Silent)} stance(s).";
            reply += state.IsPreElection
                ? " Posted to #news as its manifesto."
                : result.FlippedIssueIds.Count == 0
                    ? " Posted to #campaign-log; no stance flipped."
                    : $" Posted to #campaign-log. Flipped: {string.Join(", ", result.FlippedIssueIds.Select(id => titles.GetValueOrDefault(id, id)))}; " +
                      $"{result.PenalisedFactions.Count} member(s) lost {Display.Number(result.PenaltyPerMember)} popularity each.";
            if (problems.Count > 0) reply += $"\n⚠️ But {string.Join("; ", problems)}.";
            return reply;
        }

        private static string ProgrammeText(IReadOnlyDictionary<string, Stance> stances, IReadOnlyList<Issue> openIssues)
        {
            var stanced = openIssues
                .Where(i => stances.GetValueOrDefault(i.Id, Stance.Silent) != Stance.Silent)
                .Select(i => $"{i.Title}: *{stances[i.Id].DisplayName()}*")
                .ToList();
            var silent = openIssues.Where(i => stances.GetValueOrDefault(i.Id, Stance.Silent) == Stance.Silent).Select(i => i.Title).ToList();

            List<string> lines = stanced.Count == 0 ? ["No stance on any issue."] : stanced;
            if (stanced.Count > 0 && silent.Count > 0)
                lines.Add($"No stance on {string.Join(", ", silent)}.");
            return string.Join("\n", lines);
        }

        private static string TheParty(string name) =>
            name.StartsWith("the ", StringComparison.OrdinalIgnoreCase) ? name : $"the {name}";

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
