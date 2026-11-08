using Xunit;

namespace AureliaBot.Tests;

public class WhipCountTests : AssemblyTestBase
{
    private Reading OpenGovernmentBill()
    {
        var reading = Propose(Chancellor, "ten_hours");
        Resolve();
        return reading;
    }

    private void Predict(ulong faction, Reading reading, ulong minister, VoteChoice side) =>
        Assert.Null(Db.MakeWhipCount(faction, reading.Id, minister, side, C).Refusal);

    private string? Refusal(ulong faction, Reading reading, ulong minister, VoteChoice side) =>
        Db.MakeWhipCount(faction, reading.Id, minister, side, C).Refusal;

    private double Popularity(ulong id) => Db.GetFaction(id)!.Popularity;

    [Fact]
    public void ARightCall_EarnsTheReward_AndSpendsTheOppositionAction()
    {
        var reading = OpenGovernmentBill();
        Predict(Opposer, reading, Minister, VoteChoice.No);
        Assert.False(Db.GetOppositionStanding(Opposer, C).CanAct);
        Vote(VoteSubject.Reading, reading.Id, Minister, VoteChoice.No);
        var before = Popularity(Opposer);

        var score = Assert.Single(Count().WhipCounts);

        Assert.Equal(VoteChoice.No, score.Actual);
        Assert.Equal(C.Opposition.WhipCountReward, score.PopularityChange);
        Assert.Equal(before + C.Opposition.WhipCountReward, Popularity(Opposer));
    }

    [Fact]
    public void AWrongCall_CostsThePenalty()
    {
        var reading = OpenGovernmentBill();
        Predict(Opposer, reading, Minister, VoteChoice.No);
        Vote(VoteSubject.Reading, reading.Id, Minister, VoteChoice.Yes);
        var before = Popularity(Opposer);

        Assert.Equal(-C.Opposition.WhipCountPenalty, Assert.Single(Count().WhipCounts).PopularityChange);
        Assert.Equal(before - C.Opposition.WhipCountPenalty, Popularity(Opposer));
    }

    [Fact]
    public void AMemberWhoFollowsTheirParty_IsScoredOnTheFollowedVote()
    {
        var reading = OpenGovernmentBill();
        Predict(Opposer, reading, Minister, VoteChoice.No);
        Vote(VoteSubject.Reading, reading.Id, Chancellor, VoteChoice.Yes);

        var score = Assert.Single(Count().WhipCounts);

        Assert.Equal(VoteChoice.Yes, score.Actual);
        Assert.Equal(-C.Opposition.WhipCountPenalty, score.PopularityChange);
    }

    [Fact]
    public void AnAbstentionOrNoVote_IsVoid()
    {
        var abstained = OpenGovernmentBill();
        Predict(Opposer, abstained, Minister, VoteChoice.No);
        Predict(SecondOpposer, abstained, Chancellor, VoteChoice.No);
        Vote(VoteSubject.Reading, abstained.Id, Minister, VoteChoice.Abstain);
        var before = Popularity(Opposer);

        var scores = Count().WhipCounts;

        Assert.All(scores, s => Assert.Equal(0, s.PopularityChange));
        Assert.Equal(before, Popularity(Opposer));
        Assert.False(Db.GetOppositionStanding(Opposer, C).CanAct);
    }

    [Fact]
    public void TheCallMustBreakFromTheLine_OnGovernmentAndPrivateBills()
    {
        var government = OpenGovernmentBill();
        Assert.NotNull(Refusal(Opposer, government, Minister, VoteChoice.Yes));
        Assert.Null(Refusal(Opposer, government, Minister, VoteChoice.No));

        Count();
        Resolve();
        var opposition = Propose(Opposer, "reform");
        Resolve();
        Assert.NotNull(Refusal(SecondOpposer, opposition, Minister, VoteChoice.No));
        Assert.Null(Refusal(SecondOpposer, opposition, Minister, VoteChoice.Yes));
    }

    [Fact]
    public void OnlyTheOppositionCalls_OnlyOnCabinetMembers_AndOnlyOnOpenVotes()
    {
        var scheduled = Propose(Chancellor, "ten_hours");
        Assert.NotNull(Refusal(Opposer, scheduled, Minister, VoteChoice.No));
        Resolve();

        Assert.NotNull(Refusal(Backbencher, scheduled, Minister, VoteChoice.No));
        Assert.NotNull(Refusal(Independent, scheduled, Minister, VoteChoice.No));
        Assert.NotNull(Refusal(Opposer, scheduled, Backbencher, VoteChoice.No));
        Assert.True(Db.GetOppositionStanding(Opposer, C).CanAct);
    }

    [Fact]
    public void ASecondCallInASeason_IsRefused()
    {
        var reading = OpenGovernmentBill();
        Predict(Opposer, reading, Minister, VoteChoice.No);

        Assert.NotNull(Refusal(Opposer, reading, Chancellor, VoteChoice.No));
        Assert.Null(Refusal(SecondOpposer, reading, Chancellor, VoteChoice.No));
    }
}
