using System.Collections.Generic;
using System.Linq;

public sealed record FactionOutstanding(
    IReadOnlyList<PendingAction> PendingActions,
    int UnusedActions,
    bool CanCampaign,
    OppositionStanding? Opposition,
    IReadOnlyList<Cabinet> CabinetVotes,
    IReadOnlyList<Reading> Readings,
    IReadOnlyList<NoConfidenceMotion> Motions,
    IReadOnlyList<Question> Questions,
    IReadOnlyList<AwaitingCrisis> Crises)
{
    public IEnumerable<AwaitingCrisis> UnansweredCrises => Crises.Where(c => !c.Responded);
}

public partial class DatabaseService
{
    public FactionOutstanding Outstanding(ulong factionId, GameConstants constants)
    {
        var faction = GetFaction(factionId);
        var seated = faction is { Seats: > 0 };
        var inTheSenate = faction is { SenateSeats: > 0 } && GetGameState().SenateEstablished;
        var pending = GetPendingActions(factionId);
        return new FactionOutstanding(
            pending,
            Math.Max(0, constants.ActionsAllowed(GetGameState()) - pending.Count),
            GetAllIssues().Any(i => i.Status == IssueStatus.Open),
            RoleOf(factionId) == FactionRole.Opposition ? GetOppositionStanding(factionId, constants) : null,
            CabinetVotesAwaiting(factionId),
            seated ? GetOpenReadingsNotVotedBy(factionId)
                : inTheSenate ? GetOpenReadingsNotVotedBy(factionId).Where(r => r.Kind != ReadingKind.Override).ToList()
                : [],
            seated ? GetVotingMotionsNotVotedBy(factionId) : [],
            QuestionsToAnswer(factionId),
            CrisesAwaiting(factionId));
    }
}
