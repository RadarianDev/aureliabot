using Xunit;

namespace AureliaBot.Tests;

public class ElectionCalculatorTests
{
    private const string Nation = "nation";
    private const string Abroad = "abroad";
    private const long A = 1, B = 2, C = 3;

    private static ElectionConstants Election => DatabaseFixture.Constants.Election;
    private static double Penalty => DatabaseFixture.Constants.StrongStancePenalty;

    private static ElectionParty Party(long id, params (string Issue, Stance Stance)[] programme) =>
        new(id, programme.ToDictionary(p => p.Issue, p => p.Stance));

    private static ElectionIssue Issue(string id, double salience, params (string Province, double Opinion)[] opinions) =>
        new(id, salience, opinions.ToDictionary(o => o.Province, o => o.Opinion));

    private static ElectionInput Input(int seats, ElectionProvince[] provinces, ElectionParty[] parties, ElectionFaction[] factions,
        ElectionIssue[] issues, ElectionConstants? constants = null) =>
        new(seats, provinces, parties, factions, issues, constants ?? Election, Penalty);

    private static ElectionInput WorkedExample(double homeMultiplier) => Input(30,
        [new ElectionProvince(Nation, 1)],
        [
            Party(A, ("one", Stance.StronglyFor), ("two", Stance.For)),
            Party(B, ("one", Stance.Against), ("two", Stance.Silent)),
            Party(C, ("one", Stance.For), ("two", Stance.StronglyFor)),
        ],
        [
            new ElectionFaction(11, A, 20, Nation), new ElectionFaction(12, A, 15, Nation), new ElectionFaction(13, A, 10, Nation),
            new ElectionFaction(21, B, 30, Nation), new ElectionFaction(22, B, 10, Nation),
            new ElectionFaction(31, C, 12, Nation),
        ],
        [Issue("one", 4, (Nation, 0.3)), Issue("two", 3, (Nation, 0.4))],
        Election with { Crowding = false, HomeMultiplier = homeMultiplier });

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void Worked_example_from_the_spec(double homeMultiplier)
    {
        var result = ElectionCalculator.Run(WorkedExample(homeMultiplier));

        var province = Assert.Single(result.Provinces);
        Assert.Equal(24, province.ContestedSeats);
        Assert.Equal(12, province.ContestedSeatsByParty[A]);
        Assert.Equal(8, province.ContestedSeatsByParty[B]);
        Assert.Equal(4, province.ContestedSeatsByParty[C]);

        Assert.Equal(15, result.PartySeats[A]);
        Assert.Equal(10, result.PartySeats[B]);
        Assert.Equal(5, result.PartySeats[C]);

        Assert.Equal(6, result.FactionSeats[11]);
        Assert.Equal(5, result.FactionSeats[12]);
        Assert.Equal(4, result.FactionSeats[13]);
        Assert.Equal(7, result.FactionSeats[21]);
        Assert.Equal(3, result.FactionSeats[22]);
        Assert.Equal(5, result.FactionSeats[31]);
    }

    [Fact]
    public void Worked_example_quotients_match_the_spec_without_the_home_multiplier()
    {
        var province = Assert.Single(ElectionCalculator.Run(WorkedExample(1.0)).Provinces);

        Assert.Equal(2.337, province.Strength[B] / 9, 3);
        Assert.Equal(2.361, province.Strength[A] / 12, 3);
    }

    [Theory]
    [InlineData(45, 35, 20, 5, 3, 2)]
    [InlineData(35, 45, 20, 3, 5, 2)]
    [InlineData(47, 33, 20, 5, 3, 2)]
    [InlineData(40, 40, 20, 4, 4, 2)]
    public void Contested_seats_are_apportioned_by_largest_remainder(double p1, double p2, double p3, int s1, int s2, int s3)
    {
        var result = ElectionCalculator.Run(Input(11,
            [new ElectionProvince("p1", p1), new ElectionProvince("p2", p2), new ElectionProvince("p3", p3)],
            [Party(A)],
            [new ElectionFaction(11, A, 10, "p1")],
            []));

        Assert.Equal([s1, s2, s3], result.Provinces.Select(p => p.ContestedSeats));
        Assert.Equal(11, result.PartySeats[A]);
    }

    [Fact]
    public void Remainder_ties_between_equal_populations_go_to_the_first_province()
    {
        var result = ElectionCalculator.Run(Input(2,
            [new ElectionProvince("p1", 10), new ElectionProvince("p2", 10)],
            [Party(A)],
            [new ElectionFaction(11, A, 10, "p1")],
            []));

        Assert.Equal([1, 0], result.Provinces.Select(p => p.ContestedSeats));
    }

    [Theory]
    [InlineData(false, A)]
    [InlineData(true, C)]
    public void Crowding_weakens_a_stance_shared_by_several_parties(bool crowding, long winner)
    {
        var result = ElectionCalculator.Run(Input(4,
            [new ElectionProvince(Nation, 1)],
            [Party(A, ("one", Stance.For)), Party(B, ("one", Stance.For)), Party(C, ("two", Stance.For))],
            [new ElectionFaction(11, A, 10, Abroad), new ElectionFaction(21, B, 10, Abroad), new ElectionFaction(31, C, 9.5, Abroad)],
            [Issue("one", 1, (Nation, 1)), Issue("two", 1, (Nation, 1))],
            Election with { Crowding = crowding }));

        var province = Assert.Single(result.Provinces);
        Assert.Equal(winner, province.WinnerPartyId);
        Assert.Equal(1, province.ContestedSeatsByParty[winner]);
        Assert.Equal(2, result.PartySeats[winner]);
    }

    [Fact]
    public void Equal_quotients_go_to_the_more_popular_party()
    {
        var result = ElectionCalculator.Run(Input(3,
            [new ElectionProvince(Nation, 1)],
            [Party(A), Party(B)],
            [new ElectionFaction(11, A, 10, Nation), new ElectionFaction(21, B, 15, Abroad)],
            [],
            Election with { HomeMultiplier = 1.5 }));

        var province = Assert.Single(result.Provinces);
        Assert.Equal(province.Strength[A], province.Strength[B]);
        Assert.Equal(1, province.ContestedSeatsByParty[B]);
        Assert.Equal(B, province.WinnerPartyId);
    }

    [Fact]
    public void Equal_quotients_and_popularity_go_to_the_older_party()
    {
        var result = ElectionCalculator.Run(Input(3,
            [new ElectionProvince(Nation, 1)],
            [Party(B), Party(A)],
            [new ElectionFaction(21, B, 10, Nation), new ElectionFaction(11, A, 10, Nation)],
            []));

        var province = Assert.Single(result.Provinces);
        Assert.Equal(1, province.ContestedSeatsByParty[A]);
        Assert.Equal(0, province.ContestedSeatsByParty[B]);
        Assert.Equal(A, province.WinnerPartyId);
    }

    [Fact]
    public void A_party_without_popularity_wins_only_its_boroughs()
    {
        var result = ElectionCalculator.Run(Input(10,
            [new ElectionProvince(Nation, 1)],
            [Party(A), Party(B)],
            [new ElectionFaction(11, A, 10, Nation), new ElectionFaction(21, B, 0, Nation), new ElectionFaction(22, B, 0, Nation)],
            []));

        var province = Assert.Single(result.Provinces);
        Assert.Equal(0, province.Strength[B]);
        Assert.Equal(0, province.ContestedSeatsByParty[B]);
        Assert.Equal(7, province.ContestedSeatsByParty[A]);
        Assert.Equal(2, result.PartySeats[B]);
        Assert.Equal(1, result.FactionSeats[21]);
        Assert.Equal(1, result.FactionSeats[22]);
        Assert.Equal(0, result.VoteShare[B]);
    }

    [Fact]
    public void Members_without_popularity_split_seats_equally()
    {
        var split = ElectionCalculator.SplitAmongMembers(
            [new ElectionFaction(3, A, 0, Nation), new ElectionFaction(1, A, 0, Nation), new ElectionFaction(2, A, 0, Nation)], 4);

        Assert.Equal(2, split[1]);
        Assert.Equal(1, split[2]);
        Assert.Equal(1, split[3]);
    }

    [Fact]
    public void Remainder_ties_between_members_go_to_the_more_popular()
    {
        var split = ElectionCalculator.SplitAmongMembers(
            [new ElectionFaction(1, A, 10, Nation), new ElectionFaction(2, A, 30, Nation)], 2);

        Assert.Equal(0, split[1]);
        Assert.Equal(2, split[2]);
    }

    [Fact]
    public void Borough_seats_land_in_home_provinces()
    {
        var result = ElectionCalculator.Run(Input(10,
            [new ElectionProvince("p1", 50), new ElectionProvince("p2", 50)],
            [Party(A), Party(B)],
            [
                new ElectionFaction(11, A, 10, "p1"), new ElectionFaction(12, A, 10, "p2"),
                new ElectionFaction(21, B, 10, "p2"), new ElectionFaction(22, B, 10, Abroad),
            ],
            []));

        var p1 = result.Provinces.Single(p => p.ProvinceId == "p1");
        var p2 = result.Provinces.Single(p => p.ProvinceId == "p2");
        Assert.Equal(1, p1.BoroughSeatsByParty[A]);
        Assert.Equal(0, p1.BoroughSeatsByParty[B]);
        Assert.Equal(1, p2.BoroughSeatsByParty[A]);
        Assert.Equal(1, p2.BoroughSeatsByParty[B]);
        Assert.Equal(6, p1.ContestedSeats + p2.ContestedSeats);
        Assert.Equal(10, result.PartySeats.Values.Sum());
        Assert.All(result.FactionSeats.Values, seats => Assert.True(seats >= 1));
    }

    [Fact]
    public void Each_province_is_won_by_its_strongest_party()
    {
        var result = ElectionCalculator.Run(Input(20,
            [new ElectionProvince("p1", 50), new ElectionProvince("p2", 50), new ElectionProvince("p3", 0)],
            [Party(A, ("one", Stance.For)), Party(B, ("one", Stance.Against))],
            [new ElectionFaction(11, A, 10, "p1"), new ElectionFaction(21, B, 10, "p2")],
            [Issue("one", 2, ("p1", 0.8), ("p2", -0.8))]));

        Assert.Equal(A, result.Provinces.Single(p => p.ProvinceId == "p1").WinnerPartyId);
        Assert.Equal(B, result.Provinces.Single(p => p.ProvinceId == "p2").WinnerPartyId);
        Assert.Equal(A, result.Provinces.Single(p => p.ProvinceId == "p3").WinnerPartyId);
        Assert.Equal(0, result.Provinces.Single(p => p.ProvinceId == "p3").ContestedSeats);
    }

    [Fact]
    public void Borough_seats_can_win_a_province_against_a_stronger_party()
    {
        var result = ElectionCalculator.Run(Input(8,
            [new ElectionProvince(Nation, 1)],
            [Party(A), Party(B)],
            [
                new ElectionFaction(11, A, 60, Abroad),
                new ElectionFaction(21, B, 5, Nation), new ElectionFaction(22, B, 5, Nation),
                new ElectionFaction(23, B, 5, Nation), new ElectionFaction(24, B, 5, Nation),
            ],
            [],
            Election with { HomeMultiplier = 1 }));

        var province = Assert.Single(result.Provinces);
        Assert.True(province.Strength[A] > province.Strength[B]);
        Assert.Equal(4, province.BoroughSeatsByParty[B]);
        Assert.True(result.PartySeats[B] > province.ContestedSeatsByParty[A]);
        Assert.Equal(B, province.WinnerPartyId);
    }

    [Fact]
    public void Equal_seats_go_to_the_stronger_party_before_the_more_popular()
    {
        var result = ElectionCalculator.Run(Input(4,
            [new ElectionProvince(Nation, 1)],
            [Party(A), Party(B, ("one", Stance.For))],
            [new ElectionFaction(11, A, 11, Abroad), new ElectionFaction(21, B, 10, Abroad)],
            [Issue("one", 1, (Nation, 1))]));

        var province = Assert.Single(result.Provinces);
        Assert.Equal(1, province.ContestedSeatsByParty[A]);
        Assert.Equal(1, province.ContestedSeatsByParty[B]);
        Assert.True(province.Strength[B] > province.Strength[A]);
        Assert.Equal(B, province.WinnerPartyId);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.5)]
    public void Explain_shows_the_working_behind_each_strength(double homeMultiplier)
    {
        var input = WorkedExample(homeMultiplier) with { Constants = Election with { Crowding = true, HomeMultiplier = homeMultiplier } };
        var result = ElectionCalculator.Run(input);

        var working = ElectionCalculator.Explain(input, Nation);

        var province = Assert.Single(result.Provinces);
        Assert.Equal([A, B, C], working.Select(b => b.PartyId));
        foreach (var b in working)
        {
            Assert.Equal(province.Strength[b.PartyId], b.Strength, 9);
            Assert.Equal(b.BaseStrength * b.Multiplier, b.Strength, 9);
            Assert.Equal(b.Members.Sum(m => m.Weighted), b.Weighted, 9);
            Assert.Equal(b.Issues.Sum(t => t.Contribution), b.ProgrammeScore, 9);
        }
        var c = working.Single(b => b.PartyId == C);
        Assert.Equal(2, c.Issues.Single(t => t.IssueId == "one").Crowd);
        Assert.Single(working.Single(b => b.PartyId == B).Issues);
    }

    [Fact]
    public void A_province_where_no_party_has_strength_has_no_winner()
    {
        var result = ElectionCalculator.Run(Input(5,
            [new ElectionProvince(Nation, 1)],
            [Party(A)],
            [new ElectionFaction(11, A, 0, Nation)],
            []));

        Assert.Null(Assert.Single(result.Provinces).WinnerPartyId);
    }

    [Fact]
    public void Vote_shares_sum_to_one()
    {
        var result = ElectionCalculator.Run(WorkedExample(1.5) with
        {
            Provinces = [new ElectionProvince("p1", 60), new ElectionProvince(Nation, 40)],
        });

        Assert.Equal(1, result.VoteShare.Values.Sum(), 9);
        Assert.All(result.VoteShare.Values, share => Assert.InRange(share, 0, 1));
    }

    [Fact]
    public void The_senate_worked_example_matches_the_spec()
    {
        var result = ElectionCalculator.RunSenate(WorkedExample(1.0), new Dictionary<string, int> { [Nation] = 30 });

        var province = Assert.Single(result.Provinces);
        Assert.Equal(24, province.ContestedSeats);
        Assert.Equal(15, result.PartySeats[A]);
        Assert.Equal(10, result.PartySeats[B]);
        Assert.Equal(5, result.PartySeats[C]);
        Assert.Equal(7, result.FactionSeats[21]);
    }

    [Fact]
    public void Senate_boroughs_come_out_of_each_provinces_own_seats()
    {
        var result = ElectionCalculator.RunSenate(Input(0,
            [new ElectionProvince("p1", 90), new ElectionProvince("p2", 10)],
            [Party(A), Party(B)],
            [new ElectionFaction(11, A, 10, "p1"), new ElectionFaction(12, A, 10, "p1"), new ElectionFaction(21, B, 10, "p2")],
            []), new Dictionary<string, int> { ["p1"] = 9, ["p2"] = 9 });

        Assert.Equal(7, result.Provinces.Single(p => p.ProvinceId == "p1").ContestedSeats);
        Assert.Equal(8, result.Provinces.Single(p => p.ProvinceId == "p2").ContestedSeats);
        Assert.Equal(18, result.PartySeats.Values.Sum());
    }

    [Fact]
    public void The_senate_has_no_threshold()
    {
        var result = ElectionCalculator.RunSenate(LopsidedContest(senateEstablished: true), new Dictionary<string, int> { [Nation] = 90 });

        Assert.True(result.FactionSeats[21] >= 1);
        Assert.Null(result.BelowThreshold);
    }

    private static ElectionInput LopsidedContest(bool senateEstablished) => Input(100,
        [new ElectionProvince(Nation, 1)],
        [Party(A), Party(B)],
        [new ElectionFaction(11, A, 90, Abroad), new ElectionFaction(21, B, 1, Abroad)],
        []) with { SenateEstablished = senateEstablished };

    [Fact]
    public void Before_the_senate_small_parties_keep_their_boroughs_and_face_no_threshold()
    {
        var result = ElectionCalculator.Run(LopsidedContest(senateEstablished: false));

        Assert.Equal(98, Assert.Single(result.Provinces).ContestedSeats);
        Assert.Equal(1, result.FactionSeats[21] - Assert.Single(result.Provinces).ContestedSeatsByParty[B]);
        Assert.Empty(result.BelowThreshold!);
    }

    [Fact]
    public void Once_the_senate_exists_the_assembly_has_no_boroughs_and_a_threshold()
    {
        var result = ElectionCalculator.Run(LopsidedContest(senateEstablished: true));

        var province = Assert.Single(result.Provinces);
        Assert.Equal(100, province.ContestedSeats);
        Assert.Equal(0, province.BoroughSeatsByParty[B]);
        Assert.Equal(100, result.PartySeats[A]);
        Assert.Equal(0, result.PartySeats[B]);
        Assert.Equal(0, result.FactionSeats[21]);
        Assert.Contains(B, result.BelowThreshold!);
    }

    [Fact]
    public void The_threshold_is_ignored_when_no_party_clears_it()
    {
        var input = LopsidedContest(senateEstablished: true) with { Constants = Election with { Threshold = 0.99 } };

        var result = ElectionCalculator.Run(input);

        Assert.Empty(result.BelowThreshold!);
        Assert.Equal(100, result.PartySeats.Values.Sum());
    }

    [Fact]
    public void The_national_vote_counts_each_province_by_its_population()
    {
        var result = ElectionCalculator.Run(Input(10,
            [new ElectionProvince("p1", 90), new ElectionProvince("p2", 10)],
            [Party(A), Party(B)],
            [
                new ElectionFaction(11, A, 10, Abroad, new Dictionary<string, double> { ["p1"] = 10, ["p2"] = 0 }),
                new ElectionFaction(21, B, 10, Abroad, new Dictionary<string, double> { ["p1"] = 0, ["p2"] = 10 }),
            ],
            []));

        Assert.Equal(0.9, result.VoteShare[A], 9);
        Assert.Equal(0.1, result.VoteShare[B], 9);
    }

    [Fact]
    public void No_parties_gives_an_empty_result()
    {
        var result = ElectionCalculator.Run(Input(10, [new ElectionProvince(Nation, 1)], [], [], []));

        Assert.Empty(result.Provinces);
        Assert.Empty(result.PartySeats);
        Assert.Empty(result.FactionSeats);
        Assert.Empty(result.VoteShare);
    }

    [Fact]
    public void Without_provinces_every_seat_is_a_borough()
    {
        var result = ElectionCalculator.Run(Input(10, [], [Party(A)], [new ElectionFaction(11, A, 10, Nation), new ElectionFaction(12, A, 5, Nation)], []));

        Assert.Empty(result.Provinces);
        Assert.Equal(2, result.PartySeats[A]);
        Assert.Equal(1, result.FactionSeats[11]);
        Assert.Equal(1, result.FactionSeats[12]);
        Assert.Equal(0, result.VoteShare[A]);
    }
}
