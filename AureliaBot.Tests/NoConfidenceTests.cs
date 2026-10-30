using Xunit;

namespace AureliaBot.Tests;

public class NoConfidenceTests : AssemblyTestBase
{
    private NoConfidenceMotion TableToVote()
    {
        var result = Db.TableNoConfidence(Opposer, C, Rules);
        Assert.Equal(NoConfidenceOutcome.WentToVote, result.Outcome);
        return result.Motion!;
    }

    [Fact]
    public void AGoverningPartyBackbencher_CanTableAndCosign_WhileAMinisterCannot()
    {
        Db.SetFactionSeats(Backbencher, 10);
        Assert.Equal(NoConfidenceOutcome.InCabinet, Db.TableNoConfidence(Minister, C, Rules).Outcome);
        Assert.Equal(NoConfidenceOutcome.InCabinet, Db.TableNoConfidence(Chancellor, C, Rules).Outcome);

        var tabled = Db.TableNoConfidence(Backbencher, C, Rules);
        Assert.Equal(NoConfidenceOutcome.Tabled, tabled.Outcome);
        var motion = tabled.Motion!;
        Assert.Equal(NoConfidenceStatus.Gathering, motion.Status);
        Assert.Equal(NoConfidenceOutcome.MotionPending, Db.TableNoConfidence(Opposer, C, Rules).Outcome);

        Assert.Equal(NoConfidenceOutcome.InCabinet, Db.CosignNoConfidence(motion.Id, Minister, C, Rules).Outcome);
        Assert.Equal(NoConfidenceOutcome.IsTabler, Db.CosignNoConfidence(motion.Id, Backbencher, C, Rules).Outcome);
        Assert.Empty(Db.GetCosignableMotions(Minister));
        Assert.Contains(Db.GetCosignableMotions(Independent), m => m.Id == motion.Id);

        var cosigned = Db.CosignNoConfidence(motion.Id, Independent, C, Rules);
        Assert.Equal(NoConfidenceOutcome.Cosigned, cosigned.Outcome);
        Assert.Equal(20, cosigned.Supporters);
        Assert.Equal(NoConfidenceOutcome.AlreadySigned, Db.CosignNoConfidence(motion.Id, Independent, C, Rules).Outcome);
        Assert.Empty(Db.GetCosignableMotions(Independent));
        Assert.True(Db.GetOppositionStanding(Backbencher, C).CanAct);
    }

    [Fact]
    public void TheTablingThreshold_FollowsTheAssemblySize()
    {
        Db.SetFactionSeats(Opposer, 20);
        var tabled = Db.TableNoConfidence(Opposer, C, Rules);
        Assert.Equal(NoConfidenceOutcome.Tabled, tabled.Outcome);
        Assert.Equal(30, tabled.Threshold);
        Assert.Equal(20, tabled.Supporters);

        Db.SaveGameState(Db.GetGameState() with { Seats = 100 });
        var cosigned = Db.CosignNoConfidence(tabled.Motion!.Id, Independent, C, Rules);

        Assert.Equal(17, cosigned.Threshold);
        Assert.Equal(NoConfidenceOutcome.WentToVote, cosigned.Outcome);
        var motion = Db.GetMotion(tabled.Motion.Id)!;
        Assert.Equal(NoConfidenceStatus.Voting, motion.Status);
        Assert.Equal(Db.GetGameState().ResolutionNumber + 2, motion.DueResolution);
        Assert.Equal(NoConfidenceOutcome.NotGathering, Db.CosignNoConfidence(motion.Id, SecondOpposer, C, Rules).Outcome);
    }

    [Fact]
    public void AGatheringMotion_LapsesWithoutACooldown()
    {
        Db.SetFactionSeats(Opposer, 10);
        var motion = Db.TableNoConfidence(Opposer, C, Rules).Motion!;

        Assert.Empty(Resolve().NoConfidenceMotions);
        Assert.Equal(NoConfidenceStatus.Gathering, Db.GetMotion(motion.Id)!.Status);
        var report = Resolve();

        Assert.Equal(NoConfidenceStatus.Lapsed, Assert.Single(report.NoConfidenceMotions).Outcome);
        Assert.Equal(NoConfidenceStatus.Lapsed, Db.GetMotion(motion.Id)!.Status);
        Assert.Null(Db.GetGameState().NoConfidenceCooldownUntil);
        Assert.Equal(NoConfidenceOutcome.Tabled, Db.TableNoConfidence(Independent, C, Rules).Outcome);
    }

    [Fact]
    public void APassedMotion_MakesTheNextSeasonThePreElection()
    {
        var motion = TableToVote();
        Resolve();
        Vote(VoteSubject.NoConfidence, motion.Id, Opposer, VoteChoice.Yes);
        Vote(VoteSubject.NoConfidence, motion.Id, SecondOpposer, VoteChoice.Yes);
        Vote(VoteSubject.NoConfidence, motion.Id, Backbencher, VoteChoice.Yes);
        Vote(VoteSubject.NoConfidence, motion.Id, Chancellor, VoteChoice.No);
        Assert.Contains(Db.GetVotingMotionsNotVotedBy(Minister), m => m.Id == motion.Id);

        var counted = Count();

        Assert.True(counted.SnapElectionCalled);
        var outcome = Assert.Single(counted.NoConfidenceMotions);
        Assert.Equal((NoConfidenceStatus.Passed, 100, 40, 180), (outcome.Outcome, outcome.YesSeats, outcome.NoSeats, outcome.FilledSeats));
        Assert.Equal(CabinetStatus.Caretaker, Db.GetSittingCabinet()!.Status);
        var state = Db.GetGameState();
        Assert.Equal(GameCalendar.Next(state.Season, state.Year), (state.PreElectionSeason, state.PreElectionYear));

        var next = Resolve();

        Assert.True(next.PreElectionOpened);
        Assert.True(Db.GetGameState().IsPreElection);
        Assert.Equal(NoConfidenceOutcome.PreElection, Db.TableNoConfidence(Backbencher, C, Rules).Outcome);
    }

    [Fact]
    public void AFailedMotion_StartsTheCooldown()
    {
        var motion = TableToVote();
        Resolve();
        Vote(VoteSubject.NoConfidence, motion.Id, Opposer, VoteChoice.Yes);
        Vote(VoteSubject.NoConfidence, motion.Id, SecondOpposer, VoteChoice.Yes);
        var resolution = Db.GetGameState().ResolutionNumber + 1;

        var counted = Count();

        Assert.False(counted.SnapElectionCalled);
        Assert.Equal(NoConfidenceStatus.Failed, Assert.Single(counted.NoConfidenceMotions).Outcome);
        Assert.Equal(CabinetStatus.Approved, Db.GetSittingCabinet()!.Status);
        Assert.Equal(resolution + C.Assembly.NoConfidenceCooldownSeasons, Db.GetGameState().NoConfidenceCooldownUntil);
        Assert.Equal(NoConfidenceOutcome.Cooldown, Db.TableNoConfidence(Backbencher, C, Rules).Outcome);

        Resolve();
        for (var i = 0; i < C.Assembly.NoConfidenceCooldownSeasons; i++)
        {
            Assert.Equal(NoConfidenceOutcome.Cooldown, Db.TableNoConfidence(Backbencher, C, Rules).Outcome);
            Resolve();
        }
        Assert.NotEqual(NoConfidenceOutcome.Cooldown, Db.TableNoConfidence(Backbencher, C, Rules).Outcome);
    }

    [Fact]
    public void NoMotion_IsTabledWithoutASittingCabinet()
    {
        Sql("UPDATE cabinets SET status = 'Replaced';");

        Assert.Equal(NoConfidenceOutcome.NoCabinet, Db.TableNoConfidence(Opposer, C, Rules).Outcome);
    }
}
