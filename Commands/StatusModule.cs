using System.Text;
using Discord;
using Discord.Interactions;

public enum HelpTopic { Basics, Parties, Campaigning, Opposition, Voting, Ministries, Elections }

public enum FactionRole { Government, Opposition, Independent }

public static class FactionRoles
{
    public static FactionRole Of(DatabaseService data, Faction faction) => data.RoleOf(faction.UserId);
}

public class StatusModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly DatabaseService _data;
    private readonly ConstantsStore _constants;

    public StatusModule(DatabaseService data, ConstantsStore constants)
    {
        _data = data;
        _constants = constants;
    }

    [SlashCommand("me", "Your turn at a glance: what is pending and what you can do")]
    [RequireFaction]
    public async Task MeAsync()
    {
        var faction = _data.GetFaction(Context.User.Id)!;
        var state = _data.GetGameState();
        var party = faction.PartyId is { } partyId ? _data.GetParty(partyId) : null;
        var role = FactionRoles.Of(_data, faction);

        var sb = new StringBuilder();
        sb.AppendLine($"## {Display.Faction(faction)}");
        sb.AppendLine(state.IsPreElection
            ? Texts.Get("me.statusPreElection", ("season", state.Label), ("when", Display.NextResolution(state)))
            : Texts.Get("me.status", ("season", state.Label), ("when", Display.NextResolution(state))));
        sb.AppendLine(Texts.Get("me.summary", ("popularity", Display.Number(faction.Popularity)), ("seats", faction.Seats),
            ("party", party is null ? Display.Independent : Display.Party(party)), ("role", role.ToString().ToLowerInvariant())));

        var buttons = new ComponentBuilder();
        var waiting = new List<string>();
        var row = 0;
        if (party is not null)
        {
            foreach (var request in _data.GetPendingJoinRequests().Where(r => r.PartyId == party.Id))
            {
                var answers = _data.GetJoinAnswers(request.Id);
                var mine = answers.TryGetValue(faction.UserId, out var accept)
                    ? (accept ? Texts.Get("me.youAccepted") : Texts.Get("me.youDeclined"))
                    : Texts.Get("me.notAnswered");
                waiting.Add(Texts.Get("me.application",
                    ("applicant", Display.Faction(_data, request.FactionId)), ("party", party.Name), ("answer", mine)));
                if (row < 5)
                {
                    buttons.WithButton(Texts.Get("me.acceptButton", ("request", request.Id)), $"join:accept:{request.Id}", ButtonStyle.Success, row: row);
                    buttons.WithButton(Texts.Get("me.declineButton", ("request", request.Id)),$"join:decline:{request.Id}", ButtonStyle.Danger, row: row);
                    row++;
                }
            }
        }
        foreach (var request in _data.GetPendingJoinRequests().Where(r => r.FactionId == faction.UserId))
            if (_data.GetParty(request.PartyId) is { } target)
                waiting.Add(Texts.Get("me.ownApplication", ("party", target.Name)));

        void VoteRow(Func<VoteChoice, string> id)
        {
            if (row >= 5) return;
            buttons.WithButton(Texts.Get("assembly.choice.yes"), id(VoteChoice.Yes), ButtonStyle.Success, row: row);
            buttons.WithButton(Texts.Get("assembly.choice.no"), id(VoteChoice.No), ButtonStyle.Danger, row: row);
            buttons.WithButton(Texts.Get("assembly.choice.abstain"), id(VoteChoice.Abstain), ButtonStyle.Secondary, row: row);
            row++;
        }

        void SingleButton(string label, string id, ButtonStyle style)
        {
            if (row >= 5) return;
            buttons.WithButton(label, id, style, row: row);
            row++;
        }

        foreach (var cabinet in _data.CabinetVotesAwaiting(faction.UserId))
        {
            waiting.Add(Texts.Get("me.cabinetVote", ("chancellor", cabinet.ChancellorId is { } c ? Display.Faction(_data, c) : Texts.Get("me.unknownTarget"))));
            VoteRow(choice => GovernmentPublisher.CabinetVoteButtonId(choice, cabinet.Id));
        }
        var canVote = faction.Seats > 0;
        foreach (var reading in canVote ? _data.GetOpenReadingsNotVotedBy(faction.UserId) : [])
        {
            waiting.Add(Texts.Get("me.readingVote", ("bill", _data.GetBill(reading.BillId)?.Title ?? reading.BillId)));
            VoteRow(choice => AssemblyPublisher.ReadingVoteButtonId(choice, reading.Id));
        }
        foreach (var motion in canVote ? _data.GetVotingMotionsNotVotedBy(faction.UserId) : [])
        {
            waiting.Add(Texts.Get("me.motionVote", ("tabler", Display.Faction(_data, motion.TablerId))));
            VoteRow(choice => AssemblyPublisher.MotionVoteButtonId(choice, motion.Id));
        }
        foreach (var motion in _data.GetCosignableMotions(faction.UserId))
        {
            waiting.Add(Texts.Get("me.motionCosign", ("tabler", Display.Faction(_data, motion.TablerId))));
            SingleButton(Texts.Get("me.cosignButton"), AssemblyPublisher.CosignButtonId(motion.Id), ButtonStyle.Danger);
        }
        foreach (var reading in _data.GetScheduledReadingsBy(faction.UserId))
        {
            waiting.Add(Texts.Get("me.scheduledBill", ("bill", _data.GetBill(reading.BillId)?.Title ?? reading.BillId)));
            SingleButton(Texts.Get("me.withdrawButton"), AssemblyPublisher.WithdrawButtonId(reading.Id), ButtonStyle.Secondary);
        }
        foreach (var question in _data.QuestionsToAnswer(faction.UserId))
        {
            waiting.Add(Texts.Get("me.questionToAnswer", ("ministry", question.Ministry.DisplayName()), ("asker", Display.Faction(_data, question.AskerId))));
            SingleButton(Texts.Get("me.answerButton"), GovernmentPublisher.AnswerButtonId(question.Id), ButtonStyle.Primary);
        }

        if (role == FactionRole.Opposition)
        {
            var standing = _data.GetOppositionStanding(faction.UserId, _constants.Current);
            sb.AppendLine(standing.CanAct
                ? standing.CanProposeBill ? Texts.Get("me.oppositionFreeWithBill") : Texts.Get("me.oppositionFree")
                : Texts.Get("me.oppositionUsed"));
        }

        var pending = _data.GetPendingActions(faction.UserId);
        if (pending.Count > 0)
        {
            var titles = _data.GetAllIssues().ToDictionary(i => i.Id, i => i.Title);
            sb.AppendLine();
            sb.AppendLine(Texts.Get("me.pendingActions", ("when", Display.NextResolution(state))));
            foreach (var action in pending)
            {
                var issue = titles.GetValueOrDefault(action.IssueId, action.IssueId);
                var text = action.Kind switch
                {
                    ActionKind.Campaign => action.OffProgramme
                        ? Texts.Get("me.campaignOff", ("stance", action.Stance?.DisplayName()), ("issue", issue))
                        : Texts.Get("me.campaign", ("stance", action.Stance?.DisplayName()), ("issue", issue)),
                    ActionKind.Attack => Texts.Get("me.attack",
                        ("target", action.TargetFactionId is { } t ? Display.Faction(_data, t) : Texts.Get("me.unknownTarget")), ("issue", issue)),
                    _ => action.ProvinceId is { } p
                        ? Texts.Get("me.pollProvince", ("issue", issue), ("province", Display.Province(_constants.Config, p)))
                        : Texts.Get("me.pollNational", ("issue", issue)),
                };
                sb.AppendLine($"- {text}");
                if (row < 5)
                {
                    buttons.WithButton(Texts.Get("me.cancelButton", ("kind", action.Kind.ToString().ToLowerInvariant())),$"action:cancel:{action.Id}", ButtonStyle.Secondary, row: row);
                    row++;
                }
            }
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine(Texts.Get("me.noActions", ("count", _constants.Current.Campaign.ActionsPerTurn)));
        }

        sb.AppendLine();
        if (waiting.Count > 0)
        {
            sb.AppendLine(Texts.Get("me.waiting"));
            foreach (var line in waiting) sb.AppendLine($"- {line}");
        }
        else
        {
            sb.AppendLine(Texts.Get("me.nothingWaiting"));
            sb.AppendLine(NextSteps(faction, state));
        }

        await RespondAsync(Display.Truncate(sb.ToString()), components: row > 0 ? buttons.Build() : null,
            ephemeral: true, allowedMentions: AllowedMentions.None);
    }

    private static string NextSteps(Faction faction, GameState state)
    {
        if (faction.IsIndependent)
            return state.IsPreElection
                ? Texts.Get("me.independentPreElection")
                : Texts.Get("me.independent", ("preElection", GameCalendar.Label(state.PreElectionSeason, state.PreElectionYear)));
        return Texts.Get("me.member");
    }

    [SlashCommand("help", "Explain how part of the game works")]
    public async Task HelpAsync([Summary("topic", "What to explain")] HelpTopic topic = HelpTopic.Basics)
    {
        var c = _constants.Current;
        var text = topic switch
        {
            HelpTopic.Basics => Texts.Get("help.basics", ("preElectionDays", c.PreElectionDays)),
            HelpTopic.Parties => Texts.Get("help.parties", ("flipPenalty", Display.Number(c.Programme.FlipPenalty))),
            HelpTopic.Campaigning => Texts.Get("help.campaigning", ("actionsPerTurn", c.Campaign.ActionsPerTurn)),
            HelpTopic.Opposition => Texts.Get("help.opposition"),
            HelpTopic.Voting => Texts.Get("help.voting"),
            HelpTopic.Ministries => Texts.Get("help.ministries"),
            HelpTopic.Elections => Texts.Get("help.elections"),
            _ => Texts.Get("help.unknown"),
        };
        await RespondAsync(text, ephemeral: true);
    }
}
