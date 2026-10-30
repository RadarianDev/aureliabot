using Xunit;

namespace AureliaBot.Tests;

public class VoteCountingTests
{
    private const double Tolerance = 1e-9;

    private static readonly GameConstants Constants = DatabaseFixture.Constants with
    {
        Law = new LawConstants { SponsorFactor = 4 },
    };

    private static VoteTally Tally(params (ulong Faction, VoteChoice Choice, int Seats)[] votes) => VoteCounting.Tally(
        votes.ToDictionary(v => v.Faction, v => v.Choice),
        votes.ToDictionary(v => v.Faction, v => v.Seats));

    [Fact]
    public void Tally_weights_votes_by_seats() =>
        Assert.Equal(
            new VoteTally(45, 30, 12),
            Tally((1, VoteChoice.Yes, 40), (2, VoteChoice.Yes, 5), (3, VoteChoice.No, 30), (4, VoteChoice.Abstain, 12)));

    [Fact]
    public void Tally_ignores_votes_from_factions_without_seats()
    {
        var votes = new Dictionary<ulong, VoteChoice> { [1] = VoteChoice.Yes, [2] = VoteChoice.No, [3] = VoteChoice.No };
        var seats = new Dictionary<ulong, int> { [1] = 10, [2] = 8, [4] = 50 };

        Assert.Equal(new VoteTally(10, 8, 0), VoteCounting.Tally(votes, seats));
    }

    [Fact]
    public void One_large_faction_outweighs_several_small_ones() =>
        Assert.True(VoteCounting.MajorityPasses(Tally((1, VoteChoice.Yes, 50), (2, VoteChoice.No, 20), (3, VoteChoice.No, 20))));

    [Fact]
    public void Majority_ignores_abstentions() =>
        Assert.True(VoteCounting.MajorityPasses(new VoteTally(10, 9, 100)));

    [Fact]
    public void Majority_tie_fails() =>
        Assert.False(VoteCounting.MajorityPasses(new VoteTally(30, 30, 5)));

    [Fact]
    public void Majority_with_no_votes_fails() =>
        Assert.False(VoteCounting.MajorityPasses(new VoteTally(0, 0, 40)));

    [Fact]
    public void Majority_with_more_no_fails() =>
        Assert.False(VoteCounting.MajorityPasses(new VoteTally(20, 21, 0)));

    [Theory]
    [InlineData(91, 180, true)]
    [InlineData(90, 180, false)]
    [InlineData(89, 180, false)]
    [InlineData(88, 175, true)]
    [InlineData(87, 175, false)]
    public void No_confidence_needs_more_than_half_of_filled_seats(int yes, int filled, bool passes) =>
        Assert.Equal(passes, VoteCounting.NoConfidencePasses(new VoteTally(yes, 0, 0), filled));

    [Fact]
    public void No_confidence_counts_abstentions_and_non_voters_against()
    {
        Assert.False(VoteCounting.NoConfidencePasses(new VoteTally(80, 10, 60), 180));
        Assert.False(VoteCounting.NoConfidencePasses(new VoteTally(85, 0, 0), 180));
    }

    [Theory]
    [InlineData(180, 0.1667, 30)]
    [InlineData(210, 0.1667, 35)]
    [InlineData(150, 0.1, 15)]
    [InlineData(5, 0.1, 1)]
    [InlineData(3, 0.1, 1)]
    public void Tabling_threshold_rounds_to_nearest_seat(int seats, double share, int expected) =>
        Assert.Equal(expected, VoteCounting.TablingThreshold(seats, share));

    [Theory]
    [InlineData(Lean.For, VoteChoice.Yes, Stance.For)]
    [InlineData(Lean.For, VoteChoice.No, Stance.Against)]
    [InlineData(Lean.For, VoteChoice.Abstain, null)]
    [InlineData(Lean.Against, VoteChoice.Yes, Stance.Against)]
    [InlineData(Lean.Against, VoteChoice.No, Stance.For)]
    [InlineData(Lean.Against, VoteChoice.Abstain, null)]
    public void Logged_stance_follows_lean_and_choice(Lean lean, VoteChoice choice, Stance? expected) =>
        Assert.Equal(expected, VoteCounting.LoggedStance(lean, choice));

    [Theory]
    [InlineData(Lean.For, 0.5, 3, 2)]
    [InlineData(Lean.For, -0.5, 3, -2)]
    [InlineData(Lean.Against, 0.5, 3, -2)]
    [InlineData(Lean.Against, -0.5, 3, 2)]
    [InlineData(Lean.For, 0.3, 6, 2.4)]
    public void Sponsor_reward_is_signed_by_lean(Lean lean, double opinion, double salience, double expected) =>
        Assert.Equal(expected, VoteCounting.SponsorReward(Constants, lean, opinion, salience), Tolerance);

    [Fact]
    public void Passed_against_bill_with_public_for_costs_the_sponsor() =>
        Assert.True(VoteCounting.SponsorReward(Constants, Lean.Against, 0.6, 4) < 0);
}
