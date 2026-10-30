using Xunit;

namespace AureliaBot.Tests;

public class CabinetTests : IDisposable
{
    private readonly DatabaseFixture _fixture = new();
    private DatabaseService Db => _fixture.Db;

    public void Dispose() => _fixture.Dispose();

    private static GameConfig Config => DatabaseFixture.Config;

    private static readonly CabinetVoteCounter Counter = new(
        (votes, seats) => new VoteTally(
            votes.Where(v => v.Value == VoteChoice.Yes).Sum(v => seats.GetValueOrDefault(v.Key)),
            votes.Where(v => v.Value == VoteChoice.No).Sum(v => seats.GetValueOrDefault(v.Key)),
            votes.Where(v => v.Value == VoteChoice.Abstain).Sum(v => seats.GetValueOrDefault(v.Key))),
        tally => tally.YesSeats > tally.NoSeats);

    private void Start()
    {
        Db.StartGame(Config, DateTimeOffset.UtcNow);
        for (ulong id = 1; id <= 8; id++)
        {
            Db.CreateFaction(id, $"F{id}", "aurenten", 20);
            Db.SetFactionSeats(id, 10);
        }
    }

    private void AtResolution(long number) => Db.SaveGameState(Db.GetGameState() with { ResolutionNumber = number });

    private static Dictionary<Ministry, ulong> Posts(ulong treasury, ulong war, ulong interior, ulong commerce) => new()
    {
        [Ministry.Treasury] = treasury, [Ministry.War] = war, [Ministry.Interior] = interior, [Ministry.CommerceLabor] = commerce,
    };

    private void ImposeFirstCabinet() =>
        Assert.Null(Db.ImposeCabinet(1, new Dictionary<Ministry, ulong?>
        {
            [Ministry.Treasury] = 2, [Ministry.War] = 3, [Ministry.Interior] = 4, [Ministry.CommerceLabor] = 5,
        }).Refusal);

    private static HashSet<ulong> Set(params ulong[] ids) => [.. ids];

    [Fact]
    public void AnAppointmentToAVacantChancellorship_FillsTheSeatAtOnce()
    {
        Start();

        var result = Db.AppointChancellor(1);

        Assert.True(result.FilledSeat);
        Assert.Null(result.Nomination);
        var sitting = Db.GetSittingCabinet()!;
        Assert.Equal(1UL, sitting.ChancellorId);
        Assert.Equal(CabinetStatus.Caretaker, sitting.Status);
        Assert.All(GameEnumExtensions.CabinetMinistries, m => Assert.Null(sitting.HolderOf(m)));
        Assert.Equal(Set(1), sitting.Holders.ToHashSet());
    }

    [Fact]
    public void AnAppointmentWhileAChancellorSits_MakesADesignate_WithoutTheCabinetRole()
    {
        Start();
        ImposeFirstCabinet();

        var result = Db.AppointChancellor(6);

        Assert.False(result.FilledSeat);
        Assert.Equal(CabinetStatus.Forming, result.Nomination!.Status);
        Assert.Equal(6UL, Db.GetOpenNomination()!.ChancellorId);
        Assert.Equal(1UL, Db.GetSittingCabinet()!.ChancellorId);
        Assert.DoesNotContain(6UL, Db.GetSittingCabinet()!.Holders);
    }

    [Fact]
    public void AMinisterAppointedToAVacantChancellorship_LeavesTheirMinistry()
    {
        Start();
        ImposeFirstCabinet();
        Db.Resign(1);

        var result = Db.AppointChancellor(2);

        Assert.True(result.FilledSeat);
        Assert.Equal(Ministry.Treasury, result.VacatedMinistry);
        var sitting = Db.GetSittingCabinet()!;
        Assert.Equal(2UL, sitting.ChancellorId);
        Assert.Null(sitting.HolderOf(Ministry.Treasury));
    }

    [Fact]
    public void ANomination_KeepsToOneMinistryEach_AndTheChancellorHoldsNone()
    {
        Start();
        Db.AppointChancellor(1);

        Assert.NotNull(Db.Nominate(1, Posts(1, 3, 4, 5)).Refusal);
        Assert.NotNull(Db.Nominate(1, Posts(2, 2, 4, 5)).Refusal);
        Assert.NotNull(Db.Nominate(2, Posts(3, 4, 5, 6)).Refusal);
        Db.RetireFaction(6);
        Assert.NotNull(Db.Nominate(1, Posts(3, 4, 5, 6)).Refusal);

        var ok = Db.Nominate(1, Posts(2, 3, 4, 5));
        Assert.Null(ok.Refusal);
        Assert.Equal(CabinetStatus.Voting, ok.Nomination!.Status);
        Assert.Equal(2, ok.Nomination.DueResolution);
        Assert.NotNull(Db.Nominate(1, Posts(2, 3, 4, 5)).Refusal);
    }

    [Fact]
    public void OnlyTheDesignate_NominatesWhileOneIsForming()
    {
        Start();
        ImposeFirstCabinet();
        Db.AppointChancellor(6);

        Assert.NotNull(Db.Nominate(1, Posts(2, 3, 4, 5)).Refusal);
        Assert.Null(Db.Nominate(6, Posts(2, 3, 4, 5)).Refusal);
    }

    [Fact]
    public void AnApprovedCabinet_ReplacesTheSittingOne_AndItsMembersHoldTheRole()
    {
        Start();
        ImposeFirstCabinet();
        var previous = Db.GetSittingCabinet()!.Id;
        Db.AppointChancellor(6);
        var nomination = Db.Nominate(6, Posts(7, 8, 2, 3)).Nomination!;
        Assert.Null(Db.CastCabinetVote(nomination.Id, 7, VoteChoice.Yes).Refusal);
        Db.CastCabinetVote(nomination.Id, 8, VoteChoice.Yes);
        Db.CastCabinetVote(nomination.Id, 1, VoteChoice.No);

        Assert.Empty(Db.CountDueCabinetVotes(Counter).CabinetVotes);
        Assert.Equal(previous, Db.GetSittingCabinet()!.Id);

        AtResolution(1);
        var report = Db.CountDueCabinetVotes(Counter);

        var vote = Assert.Single(report.CabinetVotes);
        Assert.True(vote.Approved);
        Assert.Equal(20, vote.YesSeats);
        Assert.Equal(10, vote.NoSeats);
        var sitting = Db.GetSittingCabinet()!;
        Assert.Equal(nomination.Id, sitting.Id);
        Assert.Equal(CabinetStatus.Approved, sitting.Status);
        Assert.Equal(Set(6, 7, 8, 2, 3), sitting.Holders.ToHashSet());
        Assert.Equal(CabinetStatus.Replaced, Db.GetCabinet(previous)!.Status);
        Assert.Null(Db.GetOpenNomination());
    }

    [Fact]
    public void ARejectedCabinet_KeepsTheSittingOne_AndTheDesignateNominatesAgain()
    {
        Start();
        ImposeFirstCabinet();
        var previous = Db.GetSittingCabinet()!.Id;
        Db.AppointChancellor(6);
        var nomination = Db.Nominate(6, Posts(7, 8, 2, 3)).Nomination!;
        Db.CastCabinetVote(nomination.Id, 1, VoteChoice.No);
        Db.CastCabinetVote(nomination.Id, 7, VoteChoice.Yes);

        AtResolution(1);
        var report = Db.CountDueCabinetVotes(Counter);

        Assert.False(Assert.Single(report.CabinetVotes).Approved);
        Assert.Single(report.Alerts);
        Assert.Equal(CabinetStatus.Rejected, Db.GetCabinet(nomination.Id)!.Status);
        Assert.Equal(previous, Db.GetSittingCabinet()!.Id);
        Assert.Equal(Set(1, 2, 3, 4, 5), Db.GetSittingCabinet()!.Holders.ToHashSet());
        Assert.Equal(CabinetStatus.Forming, Db.GetOpenNomination()!.Status);
        var again = Db.Nominate(6, Posts(7, 8, 4, 5));
        Assert.Null(again.Refusal);
        Assert.Equal(3, again.Nomination!.DueResolution);
    }

    [Fact]
    public void ATie_FailsUnderTheTestCounter_AndAbstentionsDoNotCount()
    {
        Start();
        Db.AppointChancellor(1);
        var nomination = Db.Nominate(1, Posts(2, 3, 4, 5)).Nomination!;
        Db.CastCabinetVote(nomination.Id, 2, VoteChoice.Yes);
        Db.CastCabinetVote(nomination.Id, 6, VoteChoice.No);
        Db.CastCabinetVote(nomination.Id, 7, VoteChoice.Abstain);
        AtResolution(1);

        var vote = Assert.Single(Db.CountDueCabinetVotes(Counter).CabinetVotes);

        Assert.False(vote.Approved);
        Assert.Equal(10, vote.AbstainSeats);
        Assert.Null(Db.GetOpenNomination());
    }

    [Fact]
    public void Votes_NeedSeats_AndCanBeChangedUntilTheCount()
    {
        Start();
        Db.AppointChancellor(1);
        var nomination = Db.Nominate(1, Posts(2, 3, 4, 5)).Nomination!;
        Db.SetFactionSeats(8, 0);

        Assert.NotNull(Db.CastCabinetVote(nomination.Id, 8, VoteChoice.Yes).Refusal);
        Assert.Null(Db.CastCabinetVote(nomination.Id, 7, VoteChoice.Yes).Previous);
        Assert.Equal(VoteChoice.Yes, Db.CastCabinetVote(nomination.Id, 7, VoteChoice.No).Previous);
        Assert.DoesNotContain(Db.CabinetVotesAwaiting(7), c => c.Id == nomination.Id);
        Assert.Contains(Db.CabinetVotesAwaiting(6), c => c.Id == nomination.Id);
        Assert.Empty(Db.CabinetVotesAwaiting(8));

        AtResolution(1);
        Db.CountDueCabinetVotes(Counter);
        Assert.NotNull(Db.CastCabinetVote(nomination.Id, 6, VoteChoice.Yes).Refusal);
    }

    [Fact]
    public void AnImposedCabinet_SitsAsCaretaker_WithUnnamedMinistriesVacant_AndCancelsNominations()
    {
        Start();
        ImposeFirstCabinet();
        var previous = Db.GetSittingCabinet()!.Id;
        Db.AppointChancellor(6);
        Db.Nominate(6, Posts(7, 8, 2, 3));

        var result = Db.ImposeCabinet(null, new Dictionary<Ministry, ulong?> { [Ministry.War] = 7 });

        Assert.Null(result.Refusal);
        Assert.Single(result.Cancelled);
        var sitting = Db.GetSittingCabinet()!;
        Assert.Equal(CabinetStatus.Caretaker, sitting.Status);
        Assert.Equal(1UL, sitting.ChancellorId);
        Assert.Equal(7UL, sitting.HolderOf(Ministry.War));
        Assert.Null(sitting.HolderOf(Ministry.Treasury));
        Assert.Equal(Set(1, 7), sitting.Holders.ToHashSet());
        Assert.Equal(CabinetStatus.Replaced, Db.GetCabinet(previous)!.Status);
        Assert.Null(Db.GetOpenNomination());
    }

    [Fact]
    public void AnImposedCabinet_RefusesAChancellorHoldingAMinistry()
    {
        Start();

        Assert.NotNull(Db.ImposeCabinet(1, new Dictionary<Ministry, ulong?> { [Ministry.War] = 1 }).Refusal);
        Assert.Null(Db.GetSittingCabinet());
    }

    [Fact]
    public void AMinistersResignation_VacatesThePost_AndCancelsANominationIncludingThem()
    {
        Start();
        ImposeFirstCabinet();
        Db.AppointChancellor(6);
        var nomination = Db.Nominate(6, Posts(7, 8, 2, 3)).Nomination!;

        var result = Db.Resign(2);

        Assert.Null(result.Refusal);
        Assert.Equal(Ministry.Treasury, result.Ministry);
        Assert.Equal(nomination.Id, Assert.Single(result.Cancelled).Id);
        Assert.Equal(CabinetStatus.Cancelled, Db.GetCabinet(nomination.Id)!.Status);
        var sitting = Db.GetSittingCabinet()!;
        Assert.Null(sitting.HolderOf(Ministry.Treasury));
        Assert.Equal(Set(1, 3, 4, 5), sitting.Holders.ToHashSet());
    }

    [Fact]
    public void AChancellorsResignation_LeavesTheMinistersInOffice()
    {
        Start();
        ImposeFirstCabinet();

        var result = Db.Resign(1);

        Assert.True(result.WasChancellor);
        var sitting = Db.GetSittingCabinet()!;
        Assert.Null(sitting.ChancellorId);
        Assert.Equal(Set(2, 3, 4, 5), sitting.Holders.ToHashSet());
        Assert.NotNull(Db.Resign(6).Refusal);
    }
}
