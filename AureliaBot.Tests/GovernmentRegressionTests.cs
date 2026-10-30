using Xunit;

namespace AureliaBot.Tests;

public class GovernmentRegressionTests : AssemblyTestBase
{
    [Fact]
    public void AMotionStillOpenWhenThePreElectionEnds_IsCancelled_AndTheElectionStillRuns()
    {
        Db.SaveGameState(Db.GetGameState() with { PreElectionSeason = Season.Autumn, PreElectionYear = 1850 });
        var motion = Db.TableNoConfidence(Opposer, C).Motion!;
        Db.CosignNoConfidence(motion.Id, SecondOpposer, C);
        Assert.Equal(NoConfidenceStatus.Voting, Db.GetMotion(motion.Id)!.Status);
        foreach (var faction in new[] { Opposer, SecondOpposer, Backbencher, Independent })
            Db.CastAssemblyVote(VoteSubject.NoConfidence, motion.Id, faction, VoteChoice.Yes);

        Resolve();
        Assert.True(Db.GetGameState().IsPreElection);
        var report = Resolve();

        Assert.NotNull(report.Election);
        Assert.False(report.SnapElectionCalled);
        Assert.Equal(NoConfidenceStatus.Cancelled, Db.GetMotion(motion.Id)!.Status);
        Assert.False(Db.GetGameState().IsPreElection);
    }

    [Fact]
    public void APrivateBillThatClosesItsOwnIssue_StillCountsTowardsTheYearlyCap()
    {
        AddBill("settle", "franchise", Lean.For, "close.franchise");
        var reading = Propose(Opposer, "settle");
        Resolve();
        foreach (var faction in new[] { Opposer, SecondOpposer, Chancellor })
            Db.CastAssemblyVote(VoteSubject.Reading, reading.Id, faction, VoteChoice.Yes);

        Resolve();

        Assert.Equal(ReadingStatus.Passed, Db.GetReading(reading.Id)!.Status);
        Assert.Equal(IssueStatus.Closed, Db.GetIssue("franchise")!.Status);
        Assert.Equal(1, Db.GetOppositionStanding(Opposer, C).BillsThisYear);
    }
}
