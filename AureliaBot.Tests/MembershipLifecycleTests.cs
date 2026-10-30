using Xunit;

namespace AureliaBot.Tests;

public class MembershipLifecycleTests : IDisposable
{
    private readonly DatabaseFixture _fixture = new();
    private DatabaseService Db => _fixture.Db;

    public void Dispose() => _fixture.Dispose();

    private const ulong Applicant = 99;

    private long SetUpParty(int members)
    {
        for (ulong id = 1; id <= (ulong)members; id++)
            Db.CreateFaction(id, $"Member {id}", "aurenten", 10);
        Db.CreateFaction(Applicant, "Applicant", "aurenten", 10);
        var founders = Enumerable.Range(1, members).Select(i => (ulong)i).ToList();
        return Db.CreateParty("Liberal Union", "#ffcc00", null, founders).PartyId!.Value;
    }

    [Fact]
    public void PendingApplications_AreDroppedWhenThePreElectionResolves()
    {
        var party = SetUpParty(3);
        var request = Db.CreateJoinRequest(Applicant, party).RequestId!.Value;

        Db.Resolve(DatabaseFixture.Constants, new ScriptedRandomSource(), 1, DateTimeOffset.UtcNow);

        Assert.Equal(JoinRequestStatus.Dropped, Db.GetJoinRequest(request)!.Status);
    }

    [Fact]
    public void Answering_OutsideThePreElection_DropsTheApplication()
    {
        var party = SetUpParty(2);
        var request = Db.CreateJoinRequest(Applicant, party).RequestId!.Value;
        Db.SaveGameState(Db.GetGameState() with { Season = Season.Summer });

        var answer = Db.AnswerJoinRequest(request, 1, accept: true);

        Assert.Equal(JoinAnswerOutcome.NotPending, answer.Outcome);
        Assert.True(Db.GetFaction(Applicant)!.IsIndependent);
        Assert.Equal(JoinRequestStatus.Dropped, Db.GetJoinRequest(request)!.Status);
    }

    [Fact]
    public void AMemberLeaving_CanTurnExistingAcceptsIntoAMajority()
    {
        var party = SetUpParty(4);
        var request = Db.CreateJoinRequest(Applicant, party).RequestId!.Value;
        Db.AnswerJoinRequest(request, 1, accept: true);
        Db.AnswerJoinRequest(request, 2, accept: true);
        Assert.Equal(JoinRequestStatus.Pending, Db.GetJoinRequest(request)!.Status);

        Db.MoveFaction(4, null, "test");

        Assert.Equal(JoinRequestStatus.Accepted, Db.GetJoinRequest(request)!.Status);
        Assert.Equal(party, Db.GetFaction(Applicant)!.PartyId);
    }

    [Fact]
    public void AJoiningFaction_TakesThePartyProgrammeAsItsStance()
    {
        var party = SetUpParty(2);
        Db.OpenIssue("labour", "labour", "The Condition of Labour", "For", "Against", 0.2, 3);
        Db.RecordProgramme(party, new Dictionary<string, Stance> { ["labour"] = Stance.StronglyFor }, null, DatabaseFixture.Constants);

        Db.MoveFaction(Applicant, party, "test");

        Assert.Equal(Stance.StronglyFor, Db.GetLatestLoggedStance(Applicant, "labour"));
    }

    [Fact]
    public void LatestStances_KeepsOneEntryPerIssue()
    {
        var party = SetUpParty(2);
        Db.OpenIssue("labour", "labour", "Labour", "For", "Against", 0.2, 3);
        Db.OpenIssue("army", "army", "Army", "For", "Against", 0.2, 3);
        Db.RecordProgramme(party, new Dictionary<string, Stance> { ["labour"] = Stance.For, ["army"] = Stance.Against }, null, DatabaseFixture.Constants);
        for (var i = 0; i < 25; i++)
            Db.RecordProgramme(party, new Dictionary<string, Stance> { ["labour"] = Stance.StronglyFor, ["army"] = Stance.Against }, null, DatabaseFixture.Constants);

        var stances = Db.GetLatestStances(1);

        Assert.Equal(2, stances.Count);
        Assert.Contains(stances, s => s.IssueId == "labour" && s.Stance == Stance.StronglyFor);
        Assert.Contains(stances, s => s.IssueId == "army" && s.Stance == Stance.Against);
    }
}
