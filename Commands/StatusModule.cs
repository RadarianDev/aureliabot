using System.Text;
using Discord;
using Discord.Interactions;

public enum HelpTopic { Basics, Parties, Campaigning, Opposition, Voting, Ministries, Elections }

public enum FactionRole { Government, Opposition, Independent }

public static class FactionRoles
{
    public static FactionRole Of(DatabaseService data, Faction faction)
    {
        var cabinet = data.GetSittingCabinet();
        if (cabinet is not null && cabinet.Holders.Contains(faction.UserId)) return FactionRole.Government;
        if (faction.PartyId is not { } partyId) return FactionRole.Independent;
        return data.GetGoverningPartyIds().Contains(partyId) ? FactionRole.Government : FactionRole.Opposition;
    }
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
        sb.AppendLine($"{state.Label}{(state.IsPreElection ? " · Pre-election" : "")} · next resolution {Display.NextResolution(state)}");
        sb.AppendLine($"Popularity {Display.Number(faction.Popularity)} · {faction.Seats} seats · " +
                      $"{(party is null ? "independent" : Display.Party(party))} · {role.ToString().ToLowerInvariant()}");

        var buttons = new ComponentBuilder();
        var waiting = new List<string>();
        var row = 0;
        if (party is not null)
        {
            foreach (var request in _data.GetPendingJoinRequests().Where(r => r.PartyId == party.Id))
            {
                var answers = _data.GetJoinAnswers(request.Id);
                var mine = answers.TryGetValue(faction.UserId, out var accept) ? (accept ? "you accepted" : "you declined") : "not answered yet";
                waiting.Add($"Application from {Display.Faction(_data, request.FactionId)} to join {party.Name} ({mine})");
                if (row < 5)
                {
                    buttons.WithButton($"Accept #{request.Id}", $"join:accept:{request.Id}", ButtonStyle.Success, row: row);
                    buttons.WithButton($"Decline #{request.Id}", $"join:decline:{request.Id}", ButtonStyle.Danger, row: row);
                    row++;
                }
            }
        }
        foreach (var request in _data.GetPendingJoinRequests().Where(r => r.FactionId == faction.UserId))
            if (_data.GetParty(request.PartyId) is { } target)
                waiting.Add($"Your application to {target.Name} is waiting for its members' answers.");

        var pending = _data.GetPendingActions(faction.UserId);
        if (pending.Count > 0)
        {
            var titles = _data.GetAllIssues().ToDictionary(i => i.Id, i => i.Title);
            sb.AppendLine();
            sb.AppendLine($"**Pending actions** (resolve {Display.NextResolution(state)})");
            foreach (var action in pending)
            {
                var issue = titles.GetValueOrDefault(action.IssueId, action.IssueId);
                var text = action.Kind switch
                {
                    ActionKind.Campaign => $"Campaign *{action.Stance?.DisplayName()}* on {issue}{(action.OffProgramme ? " (off-programme)" : "")}",
                    ActionKind.Attack => $"Attack {(action.TargetFactionId is { } t ? Display.Faction(_data, t) : "?")} on {issue}",
                    _ => $"Poll {issue}{(action.ProvinceId is { } p ? $" in {Display.Province(_constants.Config, p)}" : " nationally")}",
                };
                sb.AppendLine($"- {text}");
                if (row < 5)
                {
                    buttons.WithButton($"Cancel {action.Kind.ToString().ToLowerInvariant()}", $"action:cancel:{action.Id}", ButtonStyle.Secondary, row: row);
                    row++;
                }
            }
        }
        else
        {
            sb.AppendLine();
            sb.AppendLine($"You have {_constants.Current.Campaign.ActionsPerTurn} actions this turn, each of a different kind: `/campaign`, `/attack`, `/poll`.");
        }

        sb.AppendLine();
        if (waiting.Count > 0)
        {
            sb.AppendLine("**Waiting on you**");
            foreach (var line in waiting) sb.AppendLine($"- {line}");
        }
        else
        {
            sb.AppendLine("**Nothing is waiting on you.**");
            sb.AppendLine(NextSteps(faction, state));
        }

        await RespondAsync(Display.Truncate(sb.ToString()), components: row > 0 ? buttons.Build() : null,
            ephemeral: true, allowedMentions: AllowedMentions.None);
    }

    private static string NextSteps(Faction faction, GameState state)
    {
        if (faction.IsIndependent)
            return state.IsPreElection
                ? "You are independent, and only parties stand in elections: `/party apply` to join one this season."
                : $"You are independent. Parties can be joined in the pre-election, {GameCalendar.Label(state.PreElectionSeason, state.PreElectionYear)}.";
        return "Talk with your party, and see `/help` for what you can do.";
    }

    [SlashCommand("help", "Explain how part of the game works")]
    public async Task HelpAsync([Summary("topic", "What to explain")] HelpTopic topic = HelpTopic.Basics)
    {
        var c = _constants.Current;
        var text = topic switch
        {
            HelpTopic.Basics =>
                "## How Aurelia works\n" +
                "Each turn is one season and lasts one real day; it ends at the daily resolution. " +
                $"The pre-election turn lasts {c.PreElectionDays} days and ends in an election.\n" +
                "Money, popularity, seats and forces are public numbers. The people's mood is told in words.\n" +
                "`/me` shows what is waiting on you, `/info` shows anything in the game.",
            HelpTopic.Parties =>
                "## Parties\n" +
                "The GM founds parties at the request of two or more factions. Only parties stand in elections.\n" +
                "- `/party apply` joins a party, only during the pre-election, with the agreement of a majority of its members.\n" +
                "- `/party leave` makes you independent; you keep your seats. A party left with one faction dissolves.\n" +
                "- A party writes its programme as a message and sends the GM its link. Outside the pre-election, " +
                $"flipping a stance between For and Against costs every member {Display.Number(c.Programme.FlipPenalty)} popularity.",
            HelpTopic.Campaigning =>
                "## Campaigning\n" +
                $"Each turn you take up to {c.Campaign.ActionsPerTurn} actions of different kinds (per day in the pre-election):\n" +
                "- **Campaign** on an issue: gain popularity if the nation agrees with you.\n" +
                "- **Attack** a rival's stance: they lose popularity if the nation disagrees with them; otherwise it backfires.\n" +
                "- **Poll** an issue, nationally or in one province.\n" +
                "All actions are revealed together when the turn ends. *(Coming in a later update.)*",
            HelpTopic.Opposition =>
                "## Opposition\n" +
                "A faction in a party with no cabinet seat is in opposition, and has one opposition action a season: " +
                "question a minister, condemn a crisis, or propose a bill (once a year). *(Coming in a later update.)*",
            HelpTopic.Voting =>
                "## Voting\n" +
                "Each faction votes all its seats one way. A bill passes when more seats vote for it than against. " +
                "Cabinet and no-confidence votes stay open until the end of the next turn. *(Coming in a later update.)*",
            HelpTopic.Ministries =>
                "## Ministries\n" +
                "The Chancellor names four ministers: Treasury, War, Interior, and Commerce and Labor. " +
                "Ministers answer crises, manage forces and run projects. *(Coming in a later update.)*",
            HelpTopic.Elections =>
                "## Elections\n" +
                "Seats are counted province by province, by population. A party's strength is its factions' combined popularity, " +
                "weighed by how well its programme fits the local mood. Every faction in a standing party also wins a safe borough " +
                "seat in its home province. *(Coming in a later update.)*",
            _ => "Unknown topic.",
        };
        await RespondAsync(text, ephemeral: true);
    }
}
