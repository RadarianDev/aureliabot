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
        var label = GameCalendar.Label(state.PreElectionSeason, state.PreElectionYear);
        return GameCalendar.Next(state.Season, state.Year) == preElection && state.NextResolutionAt is not null
            ? Texts.Get("party.apply.notPreElectionOpens", ("preElection", label), ("opens", Display.NextResolution(state)))
            : Texts.Get("party.apply.notPreElection", ("preElection", label));
    }

    public static int MajorityOf(int members) => members / 2 + 1;

    public static string Tally(int accepts, int declines, int members) =>
        Texts.Get("party.tally", ("accepts", accepts), ("needed", MajorityOf(members)), ("declines", declines));

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
            await RespondAsync(Texts.Get("party.apply.noSuchParty", ("party", party)), ephemeral: true, allowedMentions: AllowedMentions.None);
            return;
        }

        var faction = _data.GetFaction(Context.User.Id)!;
        if (!faction.IsIndependent)
        {
            var current = faction.PartyId is { } id ? _data.GetParty(id) : null;
            await RespondAsync(
                Texts.Get("party.apply.alreadyMember",
                    ("party", current is null ? Texts.Get("party.apply.aParty") : Display.Party(current))),
                ephemeral: true);
            return;
        }

        if (target.ChannelId is not { } chatId || Context.Client.GetChannel(chatId) is not IMessageChannel chat)
        {
            await _alerts.PostAsync(
                Texts.Get("party.alert.noChat", ("user", Context.User.Mention), ("party", target.Name)));
            await RespondAsync(
                Texts.Get("party.apply.noChat", ("party", Display.Party(target))),
                ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);
        var result = _data.CreateJoinRequest(Context.User.Id, target.Id);
        switch (result.Outcome)
        {
            case JoinRequestOutcome.AlreadyPending:
                await FollowupAsync(Texts.Get("party.apply.alreadyPending", ("party", Display.Party(target))), ephemeral: true);
                return;
            case JoinRequestOutcome.NotIndependent:
                await FollowupAsync(Texts.Get("party.apply.notIndependent"), ephemeral: true);
                return;
            case JoinRequestOutcome.NoSuchParty:
                await FollowupAsync(Texts.Get("party.apply.partyGone", ("party", party)), ephemeral: true, allowedMentions: AllowedMentions.None);
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
            _data.LogPost(chat.Id, post.Id, "party apply");
        }
        catch (Exception ex)
        {
            await _alerts.PostAsync(
                Texts.Get("party.alert.postFailed", ("user", Context.User.Mention), ("request", requestId), ("party", target.Name), ("error", ex.Message)));
            await FollowupAsync(
                Texts.Get("party.apply.postFailed", ("party", Display.Party(target))),
                ephemeral: true);
            return;
        }

        var majority = MajorityOf(members);
        await FollowupAsync(
            Texts.Get("party.apply.posted", ("party", Display.Party(target)), ("majority", majority), ("members", members),
                ("season", state.Label), ("when", Display.NextResolution(state))),
            ephemeral: true);
    }

    [SlashCommand("leave", "Leave your party and become independent")]
    [RequirePartyMember]
    public async Task LeaveAsync()
    {
        var faction = _data.GetFaction(Context.User.Id)!;
        var party = _data.GetParty(faction.PartyId!.Value);
        var partyName = party is null ? Texts.Get("party.leave.yourParty") : Display.Party(party);
        var remaining = _data.GetPartyMembers(faction.PartyId.Value).Count(m => m.UserId != faction.UserId);

        var text = Texts.Get("party.leave.confirm", ("party", partyName), ("seats", faction.Seats));
        if (remaining < 2)
            text += "\n" + (remaining == 1
                ? Texts.Get("party.leave.dissolvesOne", ("party", partyName), ("remaining", remaining))
                : Texts.Get("party.leave.dissolvesMany", ("party", partyName), ("remaining", remaining)));
        text += "\n" + Texts.Get("party.leave.rejoin");

        var userId = Context.User.Id;
        await RespondAsync(
            text,
            components: _confirmations.Create(userId, () => LeaveConfirmedAsync(userId), Texts.Get("party.leave.button")),
            ephemeral: true);
    }

    private async Task<string> LeaveConfirmedAsync(ulong userId)
    {
        var result = _data.MoveFaction(userId, null, "party leave");
        if (result is null) return FactionGate.NotRegistered;
        if (result.FromPartyId is not { } fromId) return Texts.Get("party.leave.alreadyIndependent");

        var party = _data.GetParty(fromId) ?? result.DissolvedParty;
        var partyName = party is null ? Texts.Get("party.leave.yourParty") : Display.Party(party);

        try
        {
            await _partySync.SyncMemberAsync(userId);
            if (result.DissolvedParty is null)
                await _partySync.SyncAsync(fromId);
        }
        catch (Exception ex)
        {
            await _alerts.PostAsync(Texts.Get("party.alert.leaveSyncFailed",
                ("user", userId), ("party", party?.Name ?? fromId.ToString()), ("error", ex.Message)));
        }

        if (result.DissolvedParty is { } dissolved)
        {
            try
            {
                await _partySync.HandleDissolutionAsync(dissolved, Context.User.Id, result.DissolvedLastMemberId);
            }
            catch (Exception ex)
            {
                await _alerts.PostAsync(Texts.Get("party.alert.dissolutionFailed", ("party", dissolved.Name), ("error", ex.Message)));
            }
            return Texts.Get("party.leave.doneDissolved", ("party", partyName), ("seats", result.Faction.Seats));
        }

        await PostInPartyChatAsync(party, Texts.Get("party.leave.posted", ("faction", Display.Faction(result.Faction))), "party leave");

        return Texts.Get("party.leave.done", ("party", partyName), ("seats", result.Faction.Seats));
    }

    [ComponentInteraction("join:accept:*", ignoreGroupNames: true)]
    public Task AcceptAsync(string requestId) => AnswerAsync(requestId, true);

    [ComponentInteraction("join:decline:*", ignoreGroupNames: true)]
    public Task DeclineAsync(string requestId) => AnswerAsync(requestId, false);

    private async Task AnswerAsync(string rawId, bool accept)
    {
        if (!long.TryParse(rawId, out var requestId) || _data.GetJoinRequest(requestId) is not { } request)
        {
            await RespondAsync(Texts.Get("party.join.gone"), ephemeral: true);
            return;
        }

        var party = _data.GetParty(request.PartyId);
        var partyName = party is null ? Texts.Get("party.join.thisParty") : Display.Party(party);
        var voter = _data.GetFaction(Context.User.Id);
        if (voter is not { IsActive: true } || voter.PartyId != request.PartyId)
        {
            await RespondAsync(Texts.Get("party.join.notMember", ("party", partyName)), ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);
        var answer = _data.AnswerJoinRequest(requestId, Context.User.Id, accept);
        var applicant = Display.Faction(_data, request.FactionId);

        if (answer.Outcome == JoinAnswerOutcome.NotAMember)
        {
            await FollowupAsync(Texts.Get("party.join.notMember", ("party", partyName)), ephemeral: true);
            return;
        }

        if (answer.Outcome == JoinAnswerOutcome.NotPending)
        {
            var status = answer.Request?.Status ?? request.Status;
            await UpdateApplicationPostAsync(request, party, null, disabled: true);
            await FollowupAsync(Texts.Get("party.join.closed", ("status", StatusWord(status))), ephemeral: true);
            return;
        }

        var tally = Tally(answer.Accepts, answer.Declines, answer.Members);
        var closing = answer.Outcome switch
        {
            JoinAnswerOutcome.Accepted => Texts.Get("party.join.closingAccepted"),
            JoinAnswerOutcome.Declined => Texts.Get("party.join.closingDeclined"),
            JoinAnswerOutcome.Dropped => Texts.Get("party.join.closingDropped"),
            _ => null,
        };
        var open = answer.Outcome == JoinAnswerOutcome.Recorded;
        await UpdateApplicationPostAsync(request, party,
            party is null ? null : JoinPostText(applicant, party, tally, closing), disabled: !open);

        var verb = accept ? Texts.Get("party.join.verbAccepted") : Texts.Get("party.join.verbDeclined");
        switch (answer.Outcome)
        {
            case JoinAnswerOutcome.Accepted:
                await JoinedAsync(request, partyName, applicant);
                await FollowupAsync(Texts.Get("party.join.joinedMajority", ("verb", verb), ("applicant", applicant), ("party", partyName)),
                    ephemeral: true, allowedMentions: AllowedMentions.None);
                break;
            case JoinAnswerOutcome.Declined:
                await TellApplicantAsync(request.FactionId,
                    Texts.Get("party.join.declinedApplicant", ("party", partyName)));
                await FollowupAsync(Texts.Get("party.join.declinedMajority", ("verb", verb), ("applicant", applicant)),
                    ephemeral: true, allowedMentions: AllowedMentions.None);
                break;
            case JoinAnswerOutcome.Dropped:
                await FollowupAsync(Texts.Get("party.join.dropped", ("verb", verb), ("applicant", applicant)),
                    ephemeral: true, allowedMentions: AllowedMentions.None);
                break;
            default:
                await FollowupAsync(
                    Texts.Get("party.join.recorded", ("verb", verb), ("applicant", applicant), ("tally", tally)),
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
            await _alerts.PostAsync(Texts.Get("party.alert.joinSyncFailed",
                ("user", request.FactionId), ("party", request.PartyId), ("error", ex.Message)));
        }

        await PostInPartyChatAsync(_data.GetParty(request.PartyId),
            Texts.Get("party.join.welcome", ("applicant", applicant), ("party", partyName)), "party join");

        await TellApplicantAsync(request.FactionId,
            Texts.Get("party.join.acceptedApplicant", ("party", partyName)));
    }

    private async Task UpdateApplicationPostAsync(JoinRequest request, Party? party, string? content, bool disabled)
    {
        if (party?.ChannelId is not { } chatId || request.MessageId is not { } messageId) return;
        try
        {
            if (Context.Client.GetChannel(chatId) is not IMessageChannel chat
                || await chat.GetMessageAsync(messageId) is not IUserMessage post) return;
            await post.ModifyAsync(p =>
            {
                if (content is not null) p.Content = content;
                p.Components = JoinButtons(request.Id, disabled);
                p.AllowedMentions = AllowedMentions.None;
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PartyModule] Could not update application {request.Id}'s post: {ex.Message}");
        }
    }

    private async Task PostInPartyChatAsync(Party? party, string text, string source)
    {
        if (party?.ChannelId is not { } chatId || Context.Client.GetChannel(chatId) is not IMessageChannel chat) return;
        try
        {
            var message = await chat.SendMessageAsync(text, allowedMentions: AllowedMentions.None);
            _data.LogPost(chatId, message.Id, source);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[PartyModule] Could not post in {party.Name}'s chat: {ex.Message}");
        }
    }

    private async Task TellApplicantAsync(ulong factionId, string text)
    {
        try
        {
            await _factionChannels.SendAsync(factionId, text);
        }
        catch (Exception ex)
        {
            await _alerts.PostAsync(Texts.Get("party.alert.tellFailed", ("user", factionId), ("error", ex.Message)));
        }
    }

    private static string StatusWord(JoinRequestStatus status) => status switch
    {
        JoinRequestStatus.Accepted => Texts.Get("party.status.accepted"),
        JoinRequestStatus.Declined => Texts.Get("party.status.declined"),
        JoinRequestStatus.Dropped => Texts.Get("party.status.dropped"),
        _ => Texts.Get("party.status.pending"),
    };

    private static string JoinPostText(string applicant, Party party, string tally, string? closing)
    {
        var text = Texts.Get("party.join.post", ("party", Display.Party(party)), ("applicant", applicant), ("tally", tally));
        return closing is null ? text : $"{text}\n{closing}";
    }

    private static MessageComponent JoinButtons(long requestId, bool disabled) =>
        new ComponentBuilder()
            .WithButton(Texts.Get("party.join.accept"), $"join:accept:{requestId}", ButtonStyle.Success, disabled: disabled)
            .WithButton(Texts.Get("party.join.decline"),$"join:decline:{requestId}", ButtonStyle.Danger, disabled: disabled)
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
