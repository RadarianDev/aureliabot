using Discord;
using Discord.Interactions;
using Discord.WebSocket;

[Group("party", "Join or leave a party")]
public class PartyModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly DatabaseService _data;
    private readonly PartySyncService _partySync;
    private readonly FactionChannelService _factionChannels;
    private readonly Confirmations _confirmations;
    private readonly AlertService _alerts;

    public PartyModule(
        DatabaseService data,
        PartySyncService partySync,
        FactionChannelService factionChannels,
        Confirmations confirmations,
        AlertService alerts)
    {
        _data = data;
        _partySync = partySync;
        _factionChannels = factionChannels;
        _confirmations = confirmations;
        _alerts = alerts;
    }

    public static Party? FindParty(DatabaseService data, string name) =>
        data.GetAllParties().FirstOrDefault(p => string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    public static string? ApplyTimingRefusal(GameState state)
    {
        if (state.IsPreElection) return null;
        var preElection = (state.PreElectionSeason, state.PreElectionYear);
        var opens = GameCalendar.Next(state.Season, state.Year) == preElection && state.NextResolutionAt is not null
            ? $", which opens {Display.NextResolution(state)}"
            : "";
        return $"You can apply to join a party only during a pre-election. The next pre-election is " +
               $"{GameCalendar.Label(state.PreElectionSeason, state.PreElectionYear)}{opens}.";
    }

    public static int MajorityOf(int members) => members / 2 + 1;

    public static string Tally(int accepts, int declines, int members) =>
        $"Accepts {accepts} of {MajorityOf(members)} needed · Declines {declines}";

    [SlashCommand("apply", "Apply to join a party (pre-election only)")]
    [RequireFaction]
    public async Task ApplyAsync(
        [Summary("party", "The party to join"), Autocomplete(typeof(PartyNameAutocomplete))] string party)
    {
        var state = _data.GetGameState();
        if (ApplyTimingRefusal(state) is { } refusal)
        {
            await RespondAsync(refusal, ephemeral: true);
            return;
        }

        if (FindParty(_data, party) is not { } target)
        {
            await RespondAsync($"There is no party called \"{party}\".", ephemeral: true, allowedMentions: AllowedMentions.None);
            return;
        }

        var faction = _data.GetFaction(Context.User.Id)!;
        if (!faction.IsIndependent)
        {
            var current = faction.PartyId is { } id ? _data.GetParty(id) : null;
            await RespondAsync(
                $"You are already a member of {(current is null ? "a party" : Display.Party(current))}. Only an independent can apply; leave first with `/party leave`.",
                ephemeral: true);
            return;
        }

        if (target.ChannelId is not { } chatId || Context.Client.GetChannel(chatId) is not IMessageChannel chat)
        {
            await _alerts.PostAsync(
                $"{Context.User.Mention} tried to apply to {target.Name}, but the party has no chat I can reach. Repair it with `/gm setup`.");
            await RespondAsync(
                $"{Display.Party(target)} has no party chat to post your application in. The GM has been told; try again once it is repaired.",
                ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);
        var result = _data.CreateJoinRequest(Context.User.Id, target.Id);
        switch (result.Outcome)
        {
            case JoinRequestOutcome.AlreadyPending:
                await FollowupAsync($"You already have an application pending with {Display.Party(target)}. Its members are still answering.", ephemeral: true);
                return;
            case JoinRequestOutcome.NotIndependent:
                await FollowupAsync("Only an independent can apply; leave your party first with `/party leave`.", ephemeral: true);
                return;
            case JoinRequestOutcome.NoSuchParty:
                await FollowupAsync($"There is no party called \"{party}\" any more.", ephemeral: true, allowedMentions: AllowedMentions.None);
                return;
            case JoinRequestOutcome.NotRegistered:
                await FollowupAsync(FactionGate.NotRegistered, ephemeral: true);
                return;
        }

        var requestId = result.RequestId!.Value;
        var members = _data.GetPartyMembers(target.Id).Count;
        try
        {
            var post = await chat.SendMessageAsync(
                JoinPostText(Display.Faction(faction), target, Tally(0, 0, members), null),
                components: JoinButtons(requestId, disabled: false),
                allowedMentions: AllowedMentions.None);
            _data.SetJoinRequestMessage(requestId, post.Id);
        }
        catch (Exception ex)
        {
            await _alerts.PostAsync(
                $"Could not post {Context.User.Mention}'s application (request {requestId}) in {target.Name}'s chat: {ex.Message}");
            await FollowupAsync(
                $"Your application to {Display.Party(target)} is recorded, but I could not post it in the party chat. The GM has been told.",
                ephemeral: true);
            return;
        }

        var majority = MajorityOf(members);
        await FollowupAsync(
            $"Your application to join {Display.Party(target)} is posted in its chat. " +
            $"You join as soon as {majority} of its {members} members accept, and it is declined as soon as {majority} decline. " +
            $"Joining does not change your seats. An application still pending when the election runs, at the end of {state.Label} " +
            $"({Display.NextResolution(state)}), is dropped.",
            ephemeral: true);
    }

    [SlashCommand("leave", "Leave your party and become independent")]
    [RequirePartyMember]
    public async Task LeaveAsync()
    {
        var faction = _data.GetFaction(Context.User.Id)!;
        var party = _data.GetParty(faction.PartyId!.Value);
        var partyName = party is null ? "your party" : Display.Party(party);
        var remaining = _data.GetPartyMembers(faction.PartyId.Value).Count(m => m.UserId != faction.UserId);

        var text = $"You will leave {partyName} and become independent. You keep your {faction.Seats} seats; seats only change at an election.";
        if (remaining < 2)
            text += $"\n{partyName} would be left with {remaining} active member{(remaining == 1 ? "" : "s")}, so it dissolves at once, and the dissolution is announced in #news.";
        text += "\nYou could only join a party again during a pre-election.";

        var userId = Context.User.Id;
        await RespondAsync(
            text,
            components: _confirmations.Create(userId, () => LeaveConfirmedAsync(userId), "Leave the party"),
            ephemeral: true);
    }

    private async Task<string> LeaveConfirmedAsync(ulong userId)
    {
        var result = _data.MoveFaction(userId, null, "party leave");
        if (result is null) return FactionGate.NotRegistered;
        if (result.FromPartyId is not { } fromId) return "You are already independent. Nothing was changed.";

        var party = _data.GetParty(fromId) ?? result.DissolvedParty;
        var partyName = party is null ? "your party" : Display.Party(party);

        try
        {
            await _partySync.SyncMemberAsync(userId);
        }
        catch (Exception ex)
        {
            await _alerts.PostAsync($"Party sync failed after <@{userId}> left {party?.Name ?? fromId.ToString()}: {ex.Message}");
        }

        if (result.DissolvedParty is { } dissolved)
        {
            try
            {
                await _partySync.HandleDissolutionAsync(dissolved);
            }
            catch (Exception ex)
            {
                await _alerts.PostAsync($"Handling the dissolution of {dissolved.Name} failed: {ex.Message}");
            }
            return $"You have left {partyName} and are now independent, keeping your {result.Faction.Seats} seats. " +
                   $"With fewer than two active members left, {partyName} has dissolved.";
        }

        if (party?.ChannelId is { } chatId && Context.Client.GetChannel(chatId) is IMessageChannel chat)
        {
            try
            {
                await chat.SendMessageAsync($"{Display.Faction(result.Faction)} has left the party.", allowedMentions: AllowedMentions.None);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PartyModule] Could not announce a leave in {party.Name}'s chat: {ex.Message}");
            }
        }

        return $"You have left {partyName} and are now independent, keeping your {result.Faction.Seats} seats.";
    }

    [ComponentInteraction("join:accept:*", ignoreGroupNames: true)]
    public Task AcceptAsync(string requestId) => AnswerAsync(requestId, true);

    [ComponentInteraction("join:decline:*", ignoreGroupNames: true)]
    public Task DeclineAsync(string requestId) => AnswerAsync(requestId, false);

    private async Task AnswerAsync(string rawId, bool accept)
    {
        if (!long.TryParse(rawId, out var requestId) || _data.GetJoinRequest(requestId) is not { } request)
        {
            await RespondAsync("This application no longer exists.", ephemeral: true);
            return;
        }

        var party = _data.GetParty(request.PartyId);
        var partyName = party is null ? "this party" : Display.Party(party);
        var voter = _data.GetFaction(Context.User.Id);
        if (voter is not { IsActive: true } || voter.PartyId != request.PartyId)
        {
            await RespondAsync($"Only active members of {partyName} can answer its applications.", ephemeral: true);
            return;
        }

        var answer = _data.AnswerJoinRequest(requestId, Context.User.Id, accept);
        var applicant = Display.Faction(_data, request.FactionId);
        var component = (SocketMessageComponent)Context.Interaction;

        if (answer.Outcome == JoinAnswerOutcome.NotAMember)
        {
            await RespondAsync($"Only active members of {partyName} can answer its applications.", ephemeral: true);
            return;
        }

        if (answer.Outcome == JoinAnswerOutcome.NotPending)
        {
            var status = answer.Request?.Status ?? request.Status;
            await component.UpdateAsync(p => p.Components = JoinButtons(requestId, disabled: true));
            await FollowupAsync($"This application is already closed ({StatusWord(status)}). Nothing was changed.", ephemeral: true);
            return;
        }

        var tally = Tally(answer.Accepts, answer.Declines, answer.Members);
        var closing = answer.Outcome switch
        {
            JoinAnswerOutcome.Accepted => "**Accepted.**",
            JoinAnswerOutcome.Declined => "**Declined.**",
            JoinAnswerOutcome.Dropped => "**Dropped:** the applicant is no longer an independent.",
            _ => null,
        };
        var open = answer.Outcome == JoinAnswerOutcome.Recorded;
        if (party is not null)
            await component.UpdateAsync(p =>
            {
                p.Content = JoinPostText(applicant, party, tally, closing);
                p.Components = JoinButtons(requestId, disabled: !open);
                p.AllowedMentions = AllowedMentions.None;
            });
        else
            await component.UpdateAsync(p => p.Components = JoinButtons(requestId, disabled: !open));

        var verb = accept ? "accepted" : "declined";
        switch (answer.Outcome)
        {
            case JoinAnswerOutcome.Accepted:
                await JoinedAsync(request, partyName, applicant);
                await FollowupAsync($"You {verb}. That makes a majority: {applicant} has joined {partyName}.", ephemeral: true, allowedMentions: AllowedMentions.None);
                break;
            case JoinAnswerOutcome.Declined:
                await TellApplicantAsync(request.FactionId,
                    $"{partyName} has declined your application. You remain independent.");
                await FollowupAsync($"You {verb}. That makes a majority: the application from {applicant} is declined.", ephemeral: true, allowedMentions: AllowedMentions.None);
                break;
            case JoinAnswerOutcome.Dropped:
                await FollowupAsync($"You {verb}, but {applicant} is no longer an independent, so the application is dropped.", ephemeral: true, allowedMentions: AllowedMentions.None);
                break;
            default:
                await FollowupAsync(
                    $"You {verb} the application from {applicant}. {tally}. You can change your answer while it is open.",
                    ephemeral: true, allowedMentions: AllowedMentions.None);
                break;
        }
    }

    private async Task JoinedAsync(JoinRequest request, string partyName, string applicant)
    {
        try
        {
            await _partySync.SyncMemberAsync(request.FactionId);
        }
        catch (Exception ex)
        {
            await _alerts.PostAsync($"Party sync failed after <@{request.FactionId}> joined party {request.PartyId}: {ex.Message}");
        }

        try
        {
            await Context.Channel.SendMessageAsync($"{applicant} joins {partyName}. Welcome to the party.", allowedMentions: AllowedMentions.None);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PartyModule] Could not announce a join: {ex.Message}");
        }

        await TellApplicantAsync(request.FactionId,
            $"{partyName} has accepted your application: you are now a member. Your seats are unchanged.");
    }

    private async Task TellApplicantAsync(ulong factionId, string text)
    {
        try
        {
            await _factionChannels.SendAsync(factionId, text);
        }
        catch (Exception ex)
        {
            await _alerts.PostAsync($"Could not tell <@{factionId}> about their party application: {ex.Message}");
        }
    }

    private static string StatusWord(JoinRequestStatus status) => status switch
    {
        JoinRequestStatus.Accepted => "accepted",
        JoinRequestStatus.Declined => "declined",
        JoinRequestStatus.Dropped => "dropped",
        _ => "pending",
    };

    private static string JoinPostText(string applicant, Party party, string tally, string? closing)
    {
        var text = $"**Application to join {Display.Party(party)}**\n{applicant} applies to join the party.\n{tally}";
        return closing is null ? text : $"{text}\n{closing}";
    }

    private static MessageComponent JoinButtons(long requestId, bool disabled) =>
        new ComponentBuilder()
            .WithButton("Accept", $"join:accept:{requestId}", ButtonStyle.Success, disabled: disabled)
            .WithButton("Decline", $"join:decline:{requestId}", ButtonStyle.Danger, disabled: disabled)
            .Build();

    private sealed class PartyNameAutocomplete : AutocompleteHandler
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
