public sealed record FiredEventReport(long EventId, string Title, string Text, IReadOnlyList<Effect> Effects);

public sealed record CrisisPostedReport(long CrisisId, string Title);

public sealed record CondemnationScore(ulong FactionId, Lean Side, double PopularityChange);

public sealed record CrisisOutcomeReport(
    long CrisisId,
    string Title,
    Ministry Ministry,
    ulong? ResponsibleFactionId,
    CrisisOutcome Outcome,
    string? AnswerLabel,
    string? ResponseText,
    IReadOnlyList<string> Texts,
    IReadOnlyList<Effect> Effects,
    double PopularityChange,
    IReadOnlyList<CondemnationScore> Condemnations);

public sealed record VoteResultReport(VoteSubject Subject, long SubjectId, string Title, bool Passed, int YesSeats, int NoSeats, int AbstainSeats);

public sealed record LawReport(
    string BillId,
    string Title,
    ulong SponsorFactionId,
    int YesSeats,
    int NoSeats,
    double SponsorPopularityChange,
    string PassText,
    IReadOnlyList<Effect> Effects);

public sealed record ReadingOpenedReport(long ReadingId, string BillId, string Title);

public sealed record ReadingCancelledReport(long ReadingId, string BillId, string Title, string Reason);

public sealed record ProjectStartedReport(long ProjectId, string Title);

public sealed record ProjectFinishedReport(long ProjectId, string Title, ulong? MinisterFactionId, bool BonusEarned, string? BonusText, IReadOnlyList<Effect> Effects);

public sealed record ActionReport(
    long ActionId,
    ulong FactionId,
    ActionKind Kind,
    string IssueId,
    Stance? Stance,
    bool OffProgramme,
    ulong? TargetFactionId,
    double ActorPopularityChange,
    double TargetPopularityChange,
    bool AttackSucceeded = false);

public sealed record CrackdownReport(string ProvinceId, bool Success, int GendarmerieTotal, int DieSides, int DieRoll);

public enum QuestionOutcomeKind { Unanswered, Dropped, Extended }

public sealed record QuestionReport(long QuestionId, Ministry Ministry, ulong AskerId, ulong? MinisterId, QuestionOutcomeKind Outcome, double MinisterChange, double AskerChange);

public sealed record CabinetVoteReport(long CabinetId, ulong? ChancellorId, bool Approved, int YesSeats, int NoSeats, int AbstainSeats);

public sealed record NoConfidenceReport(long MotionId, ulong TablerId, NoConfidenceStatus Outcome, int YesSeats, int NoSeats, int FilledSeats);

public sealed record ElectionPartySnapshot(long Id, string Name, string Colour, ulong? EmojiId);

public sealed record ElectionReport(int TermNumber, ElectionResult Result, IReadOnlyList<ElectionPartySnapshot> Parties);

public sealed record ResolutionReport
{
    public long ResolutionNumber { get; init; }
    public Season FromSeason { get; init; }
    public int FromYear { get; init; }
    public Season ToSeason { get; set; }
    public int ToYear { get; set; }
    public int Seed { get; init; }
    public bool CampaignDay { get; init; }
    public bool SnapElectionCalled { get; set; }
    public bool PreElectionOpened { get; set; }
    public ElectionReport? Election { get; set; }

    public List<string> News { get; } = [];
    public List<FiredEventReport> FiredEvents { get; } = [];
    public List<CrisisPostedReport> NewCrises { get; } = [];
    public List<CrisisOutcomeReport> CrisisOutcomes { get; } = [];
    public List<VoteResultReport> VoteResults { get; } = [];
    public List<LawReport> Laws { get; } = [];
    public List<ReadingOpenedReport> ReadingsOpened { get; } = [];
    public List<ReadingCancelledReport> ReadingsCancelled { get; } = [];
    public List<ProjectStartedReport> ProjectsStarted { get; } = [];
    public List<ProjectFinishedReport> ProjectsFinished { get; } = [];
    public List<ActionReport> Actions { get; } = [];
    public List<CrackdownReport> Crackdowns { get; } = [];
    public List<QuestionReport> Questions { get; } = [];
    public List<CabinetVoteReport> CabinetVotes { get; } = [];
    public List<NoConfidenceReport> NoConfidenceMotions { get; } = [];
    public Dictionary<ulong, List<string>> PrivateMessages { get; } = [];
    public List<string> CabinetReport { get; } = [];
    public List<string> Alerts { get; } = [];

    public void AddPrivate(ulong factionId, string message)
    {
        if (!PrivateMessages.TryGetValue(factionId, out var list))
            PrivateMessages[factionId] = list = [];
        list.Add(message);
    }
}
