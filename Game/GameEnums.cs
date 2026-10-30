public enum Season { Spring, Summer, Autumn, Winter }

public enum Stance { StronglyAgainst = -2, Against = -1, Silent = 0, For = 1, StronglyFor = 2 }

public enum Lean { Against = -1, Neutral = 0, For = 1 }

public enum Ministry { War, Interior, CommerceLabor, Treasury, Navy }

public enum LedgerAccount { War, Interior, CommerceLabor, Treasury, Navy, Reserve }

public enum FactionStatus { Active, Retired }

public enum JoinRequestStatus { Pending, Accepted, Declined, Dropped }

public enum IssueStatus { Open, Closed }

public enum EventStatus { Queued, Fired, Cancelled }

public enum CrisisStatus { Live, Resolved, Cancelled }

public enum CrisisOutcome { Clean, Complicated, Default }

public enum BillStatus { Available, Voting, Cooldown, Passed, Withdrawn }

public enum ReadingKind { Government, Private }

public enum ReadingStatus { Scheduled, Open, Passed, Rejected, Cancelled }

public enum NoConfidenceStatus { Gathering, Voting, Passed, Failed, Lapsed, Cancelled }

public enum VoteSubject { Reading, NoConfidence, Cabinet }

public enum VoteChoice { Yes, No, Abstain }

public enum CabinetKind { Sitting, Nominated }

public enum CabinetStatus { Approved, Caretaker, Forming, Voting, Rejected, Cancelled, Replaced }

public enum LedgerKind
{
    Allocation, Grant, CarryOver, Interest, Upkeep, Purchase, Crackdown, Crisis, Project, Graft, Effect, Adjustment
}

public enum UnitType { Division, Gendarmerie }

public enum OrderKind { Raise, Train, Station, Crackdown }

public enum ProjectStatus { Active, Stalled, Finished, Halted }

public enum ActionKind { Campaign, Attack, Poll }

public enum OppositionActionKind { Question, Condemn, Bill }

public enum QuestionStatus { Open, Answered, Unanswered, Dropped }

public enum StanceSource { Programme, Campaign, Vote }

public enum TemplateKind { Issue, Bill, Project, Event, Crisis }

public enum TemplateStatus { Draft, Ready, Archived }

public enum ChannelRole
{
    News, Dashboard, Assembly, CampaignLog, PartyRegistry, Cabinet, Gm,
    FactionCategory, PartyCategory, ArchiveCategory, TurnTracker, GmRole, CabinetRole
}

public static class GameEnumExtensions
{
    public static Season Next(this Season season) => (Season)(((int)season + 1) % 4);

    public static int Sign(this Stance stance) => Math.Sign((int)stance);

    public static bool HoldsCrises(this Ministry ministry) => ministry != Ministry.Navy;

    public static IReadOnlyList<Ministry> CabinetMinistries { get; } =
        [Ministry.Treasury, Ministry.War, Ministry.Interior, Ministry.CommerceLabor];

    public static string DisplayName(this Ministry ministry) => ministry switch
    {
        Ministry.War => Texts.Get("names.ministry.war"),
        Ministry.Interior => Texts.Get("names.ministry.interior"),
        Ministry.CommerceLabor => Texts.Get("names.ministry.commerceLabor"),
        Ministry.Treasury => Texts.Get("names.ministry.treasury"),
        Ministry.Navy => Texts.Get("names.ministry.navy"),
        _ => ministry.ToString(),
    };

    public static string DisplayName(this Stance stance) => stance switch
    {
        Stance.StronglyFor => Texts.Get("names.stance.stronglyFor"),
        Stance.For => Texts.Get("names.stance.for"),
        Stance.Silent => Texts.Get("names.stance.silent"),
        Stance.Against => Texts.Get("names.stance.against"),
        Stance.StronglyAgainst => Texts.Get("names.stance.stronglyAgainst"),
        _ => stance.ToString(),
    };

    public static UnitType UnitTypeOf(Ministry ministry) => ministry switch
    {
        Ministry.War => UnitType.Division,
        Ministry.Interior => UnitType.Gendarmerie,
        _ => throw new ArgumentOutOfRangeException(nameof(ministry)),
    };

    public static Ministry MinistryOf(this UnitType type) =>
        type == UnitType.Division ? Ministry.War : Ministry.Interior;
}
