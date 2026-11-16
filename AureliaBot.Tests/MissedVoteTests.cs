using Xunit;

namespace AureliaBot.Tests;

public class MissedVoteTests : AssemblyTestBase
{
    private double Popularity(ulong id) => Db.GetFaction(id)!.Popularity;

    private Reading OpenGovernmentBill()
    {
        var reading = Propose(Chancellor, "ten_hours");
        Resolve();
        return reading;
    }

    [Fact]
    public void SeatsLeftUncast_CostTheFullPenalty_FollowingCostsLess_AndVotingOrAbstainingNothing()
    {
        var reading = OpenGovernmentBill();
        Vote(VoteSubject.Reading, reading.Id, Chancellor, VoteChoice.Yes);
        Vote(VoteSubject.Reading, reading.Id, Opposer, VoteChoice.Abstain);
        var before = new[] { Chancellor, Minister, Backbencher, Opposer, SecondOpposer, Independent }.ToDictionary(id => id, Popularity);

        var report = Count();

        var uncast = report.MissedVotes.Where(m => !m.Followed).ToList();
        var followed = report.MissedVotes.Where(m => m.Followed).ToList();
        Assert.Equal([SecondOpposer, Independent], uncast.Select(m => m.FactionId).Order());
        Assert.Equal([Minister, Backbencher], followed.Select(m => m.FactionId).Order());
        Assert.All(uncast, m => Assert.Equal(-C.Assembly.MissedVotePenalty, m.PopularityChange));
        Assert.All(followed, m => Assert.Equal(-C.Assembly.FollowedVotePenalty, m.PopularityChange));
        Assert.Equal(before[SecondOpposer] - C.Assembly.MissedVotePenalty, Popularity(SecondOpposer));
        Assert.Equal(before[Independent] - C.Assembly.MissedVotePenalty, Popularity(Independent));
        Assert.Equal(before[Minister] - C.Assembly.FollowedVotePenalty, Popularity(Minister));
        Assert.Equal(before[Backbencher] - C.Assembly.FollowedVotePenalty, Popularity(Backbencher));
        Assert.Equal(before[Opposer], Popularity(Opposer));
        Assert.Contains(report.PrivateMessages[Independent], m => m.Contains("ten_hours"));
        Assert.Contains(report.PrivateMessages[Minister], m => m.Contains("ten_hours") && m.Contains(Display.Signed(-C.Assembly.FollowedVotePenalty)));
    }

    [Fact]
    public void ANoConfidenceMotion_IsNeverFollowed_SoEveryMemberMustVote()
    {
        var tabled = Db.TableNoConfidence(Opposer, C, Rules);
        Assert.Equal(NoConfidenceOutcome.WentToVote, tabled.Outcome);
        var motion = tabled.Motion!;
        Resolve();
        foreach (var voter in new[] { Chancellor, Minister, Backbencher, Opposer })
            Vote(VoteSubject.NoConfidence, motion.Id, voter, VoteChoice.Abstain);

        var report = Count();

        Assert.Equal([SecondOpposer, Independent], report.MissedVotes.Select(m => m.FactionId).Order());
    }
}
