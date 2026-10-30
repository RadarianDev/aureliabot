using Xunit;

namespace AureliaBot.Tests;

public class JoinTests : IDisposable
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

    private long Apply(long party)
    {
        var result = Db.CreateJoinRequest(Applicant, party);
        Assert.Equal(JoinRequestOutcome.Created, result.Outcome);
        return result.RequestId!.Value;
    }

    [Fact]
    public void Applying_IsAllowedDuringThePreElection()
    {
        var state = Db.GetGameState();
        Assert.True(state.IsPreElection);
        Assert.Null(PartyModule.ApplyTimingRefusal(state));
    }

    [Fact]
    public void Applying_IsRefusedOutsideThePreElection_NamingTheNextOne()
    {
        var state = Db.GetGameState() with
        {
            Season = Season.Summer, Year = 1850, PreElectionSeason = Season.Spring, PreElectionYear = 1854,
        };

        var refusal = PartyModule.ApplyTimingRefusal(state);

        Assert.NotNull(refusal);
        Assert.Contains("Spring 1854", refusal);
    }

    [Fact]
    public void Applying_RefusalGivesTheTime_WhenThePreElectionIsNext()
    {
        var next = new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);
        var state = Db.GetGameState() with
        {
            Season = Season.Winter, Year = 1853, PreElectionSeason = Season.Spring, PreElectionYear = 1854, NextResolutionAt = next,
        };

        var refusal = PartyModule.ApplyTimingRefusal(state)!;

        Assert.Contains("Spring 1854", refusal);
        Assert.Contains($"<t:{next.ToUnixTimeSeconds()}:R>", refusal);
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    [InlineData(4, 3)]
    [InlineData(5, 3)]
    public void Majority_IsMoreThanHalf(int members, int majority)
    {
        Assert.Equal(majority, PartyModule.MajorityOf(members));
        Assert.Equal($"Accepts 1 of {majority} needed · Declines 0", PartyModule.Tally(1, 0, members));
    }

    [Fact]
    public void Join_NeedsAMajorityOfAccepts()
    {
        var party = SetUpParty(4);
        var request = Apply(party);

        var first = Db.AnswerJoinRequest(request, 1, true);
        var second = Db.AnswerJoinRequest(request, 2, true);
        Assert.Equal(JoinAnswerOutcome.Recorded, first.Outcome);
        Assert.Equal(JoinAnswerOutcome.Recorded, second.Outcome);
        Assert.True(Db.GetFaction(Applicant)!.IsIndependent);

        var third = Db.AnswerJoinRequest(request, 3, true);

        Assert.Equal(JoinAnswerOutcome.Accepted, third.Outcome);
        Assert.Equal(3, third.Accepts);
        Assert.Equal(4, third.Members);
        Assert.Equal(party, Db.GetFaction(Applicant)!.PartyId);
        Assert.Equal(JoinRequestStatus.Accepted, Db.GetJoinRequest(request)!.Status);
    }

    [Fact]
    public void Join_AMajorityOfDeclinesRefusesIt()
    {
        var party = SetUpParty(3);
        var request = Apply(party);

        Assert.Equal(JoinAnswerOutcome.Recorded, Db.AnswerJoinRequest(request, 1, false).Outcome);
        var second = Db.AnswerJoinRequest(request, 2, false);

        Assert.Equal(JoinAnswerOutcome.Declined, second.Outcome);
        Assert.True(Db.GetFaction(Applicant)!.IsIndependent);
        Assert.Equal(JoinRequestStatus.Declined, Db.GetJoinRequest(request)!.Status);
        Assert.Equal(JoinAnswerOutcome.NotPending, Db.AnswerJoinRequest(request, 3, true).Outcome);
    }

    [Fact]
    public void Join_AnswersCanChange()
    {
        var party = SetUpParty(3);
        var request = Apply(party);

        Db.AnswerJoinRequest(request, 1, false);
        var changed = Db.AnswerJoinRequest(request, 1, true);
        Assert.Equal(JoinAnswerOutcome.Recorded, changed.Outcome);
        Assert.Equal(1, changed.Accepts);
        Assert.Equal(0, changed.Declines);
        Assert.Single(Db.GetJoinAnswers(request));

        var decided = Db.AnswerJoinRequest(request, 2, true);

        Assert.Equal(JoinAnswerOutcome.Accepted, decided.Outcome);
        Assert.Equal(party, Db.GetFaction(Applicant)!.PartyId);
    }

    [Fact]
    public void Join_ANonMemberCannotAnswer()
    {
        var party = SetUpParty(2);
        Db.CreateFaction(50, "Outsider", "aurenten", 10);
        var request = Apply(party);

        var outsider = Db.AnswerJoinRequest(request, 50, true);
        var self = Db.AnswerJoinRequest(request, Applicant, true);

        Assert.Equal(JoinAnswerOutcome.NotAMember, outsider.Outcome);
        Assert.Equal(JoinAnswerOutcome.NotAMember, self.Outcome);
        Assert.Empty(Db.GetJoinAnswers(request));
        Assert.Equal(JoinRequestStatus.Pending, Db.GetJoinRequest(request)!.Status);
    }

    [Fact]
    public void Apply_OnlyAnIndependentCanApply_AndOnlyOnceAtATime()
    {
        var party = SetUpParty(2);
        Apply(party);

        Assert.Equal(JoinRequestOutcome.AlreadyPending, Db.CreateJoinRequest(Applicant, party).Outcome);
        Assert.Equal(JoinRequestOutcome.NotIndependent, Db.CreateJoinRequest(1, party).Outcome);
    }

    [Fact]
    public void FindParty_MatchesTheNameIgnoringCase()
    {
        var party = SetUpParty(2);

        Assert.Equal(party, PartyModule.FindParty(Db, "  liberal UNION ")!.Id);
        Assert.Null(PartyModule.FindParty(Db, "Tory"));
    }

    [Fact]
    public void Leave_KeepsSeats_AndALoneMemberDissolvesTheParty()
    {
        var party = SetUpParty(2);

        var result = Db.MoveFaction(1, null, "party leave")!;

        Assert.Equal(party, result.FromPartyId);
        Assert.NotNull(result.DissolvedParty);
        Assert.True(Db.GetFaction(1)!.IsIndependent);
        Assert.True(Db.GetFaction(2)!.IsIndependent);
        Assert.Null(Db.GetParty(party));
    }
}
