using Xunit;

namespace AureliaBot.Tests;

public class FormulasTests
{
    private const double Tolerance = 1e-9;

    private static readonly GameConstants Constants = DatabaseFixture.Constants with
    {
        StrongStancePenalty = 0.5,
        Campaign = new CampaignConstants { Factor = 4, ActionsPerTurn = 2, Crowding = true },
        Attack = new AttackConstants { Factor = 3, Backfire = 2 },
    };

    private static ConditionDefinition Condition(string id, double rest = 0, params (string Issue, double Bias)[] biases) => new()
    {
        Id = id,
        Name = id,
        DiscontentRest = rest,
        Biases = biases.ToDictionary(b => b.Issue, b => b.Bias),
    };

    [Theory]
    [InlineData(Stance.For, 0.4, 0.4)]
    [InlineData(Stance.For, -0.4, -0.4)]
    [InlineData(Stance.Against, 0.4, -0.4)]
    [InlineData(Stance.Against, -0.4, 0.4)]
    [InlineData(Stance.StronglyFor, 0.4, 0.3)]
    [InlineData(Stance.StronglyFor, -0.4, -1.3)]
    [InlineData(Stance.StronglyAgainst, 0.4, -1.3)]
    [InlineData(Stance.StronglyAgainst, -0.4, 0.3)]
    [InlineData(Stance.Silent, 0.9, 0)]
    [InlineData(Stance.Silent, -0.9, 0)]
    public void StanceScore_covers_every_branch(Stance stance, double opinion, double expected) =>
        Assert.Equal(expected, Formulas.StanceScore(stance, opinion, 0.5), Tolerance);

    [Fact]
    public void StanceScore_strong_stance_pays_penalty_even_at_zero_opinion() =>
        Assert.Equal(-0.5, Formulas.StanceScore(Stance.StronglyFor, 0, 0.5), Tolerance);

    [Fact]
    public void SalienceFactor_is_a_third_of_salience()
    {
        Assert.Equal(1, Formulas.SalienceFactor(3), Tolerance);
        Assert.Equal(0, Formulas.SalienceFactor(0), Tolerance);
        Assert.Equal(5.0 / 3, Formulas.SalienceFactor(5), Tolerance);
    }

    [Fact]
    public void ProvincialOpinion_adds_condition_biases_for_the_issue_only()
    {
        var strike = Condition("strike", 0, ("labour", 0.3), ("free_trade", -0.5));
        var boom = Condition("boom", 0, ("labour", -0.1));
        Assert.Equal(0.4, Formulas.ProvincialOpinion(0.2, [strike, boom], "labour"), Tolerance);
        Assert.Equal(0.2, Formulas.ProvincialOpinion(0.2, [], "labour"), Tolerance);
        Assert.Equal(0.2, Formulas.ProvincialOpinion(0.2, [boom], "franchise"), Tolerance);
    }

    [Fact]
    public void ProvincialOpinion_is_clamped()
    {
        var strong = Condition("strong", 0, ("labour", 0.8));
        var weak = Condition("weak", 0, ("labour", -0.8));
        Assert.Equal(1, Formulas.ProvincialOpinion(0.5, [strong], "labour"), Tolerance);
        Assert.Equal(-1, Formulas.ProvincialOpinion(-0.5, [weak], "labour"), Tolerance);
    }

    [Fact]
    public void NationalOpinion_is_population_weighted()
    {
        Assert.Equal(0.5, Formulas.NationalOpinion([(3, 1.0), (1, -1.0)]), Tolerance);
        Assert.Equal(-0.2, Formulas.NationalOpinion([(1, 0.4), (4, -0.35)]), Tolerance);
    }

    [Fact]
    public void NationalOpinion_with_no_population_is_zero()
    {
        Assert.Equal(0, Formulas.NationalOpinion([]), Tolerance);
        Assert.Equal(0, Formulas.NationalOpinion([(0, 0.7)]), Tolerance);
    }

    [Fact]
    public void CampaignGain_alone_uses_factor_score_and_salience()
    {
        Assert.Equal(4 * 0.5 * 1, Formulas.CampaignGain(Constants, Stance.For, 0.5, 3, 1), Tolerance);
        Assert.Equal(4 * -0.5 * 2.0 / 3, Formulas.CampaignGain(Constants, Stance.Against, 0.5, 2, 1), Tolerance);
    }

    [Fact]
    public void CampaignGain_is_divided_by_root_of_crowd()
    {
        var alone = Formulas.CampaignGain(Constants, Stance.For, 0.5, 3, 1);
        Assert.Equal(alone / 2, Formulas.CampaignGain(Constants, Stance.For, 0.5, 3, 4), Tolerance);
        Assert.Equal(alone / Math.Sqrt(3), Formulas.CampaignGain(Constants, Stance.For, 0.5, 3, 3), Tolerance);
    }

    [Fact]
    public void CampaignGain_treats_crowd_below_one_as_one() =>
        Assert.Equal(
            Formulas.CampaignGain(Constants, Stance.For, 0.5, 3, 1),
            Formulas.CampaignGain(Constants, Stance.For, 0.5, 3, 0),
            Tolerance);

    [Fact]
    public void CampaignGain_adds_the_baseline_share_of_salience_before_crowding()
    {
        var heard = Constants with { Campaign = Constants.Campaign with { Baseline = 0.5 } };

        Assert.Equal(0.5 * 4, Formulas.CampaignGain(heard, Stance.For, 0, 4, 1), Tolerance);
        Assert.Equal(-1.2, Formulas.CampaignGain(Constants, Stance.StronglyFor, 0.1, 3, 1), Tolerance);
        Assert.Equal(0.3, Formulas.CampaignGain(heard, Stance.StronglyFor, 0.1, 3, 1), Tolerance);
        Assert.Equal((0.5 * 3 + 4 * 0.5) / 2, Formulas.CampaignGain(heard, Stance.For, 0.5, 3, 4), Tolerance);
    }

    [Fact]
    public void ShiftInProvince_MovesOnlyThatProvince_AndTheNationalFigureByItsShare()
    {
        var range = new PopularityConstants { Min = 0, Max = 100, Start = 10 };
        var populations = new Dictionary<string, double> { ["big"] = 3, ["small"] = 1 };

        var shift = Formulas.ShiftInProvince(10, new Dictionary<string, double>(), populations, "small", 4, range);

        Assert.Equal(1, shift.NationalChange, Tolerance);
        Assert.Equal(4, shift.RegionalChange, Tolerance);
        Assert.Equal(14, 11 + shift.Deviations["small"], Tolerance);
        Assert.Equal(10, 11 + shift.Deviations["big"], Tolerance);
        Assert.Equal(0, 3 * shift.Deviations["big"] + shift.Deviations["small"], Tolerance);
    }

    [Fact]
    public void ShiftInProvince_StopsAtTheBottomOfTheRange()
    {
        var range = new PopularityConstants { Min = 0, Max = 100, Start = 10 };
        var populations = new Dictionary<string, double> { ["big"] = 3, ["small"] = 1 };

        var shift = Formulas.ShiftInProvince(1, new Dictionary<string, double>(), populations, "small", -5, range);

        Assert.Equal(-1, shift.RegionalChange, Tolerance);
        Assert.Equal(0, 1 + shift.NationalChange + shift.Deviations["small"], Tolerance);
    }

    [Fact]
    public void CampaignGain_ignores_crowd_when_crowding_is_off()
    {
        var uncrowded = Constants with { Campaign = Constants.Campaign with { Crowding = false } };
        Assert.Equal(
            Formulas.CampaignGain(uncrowded, Stance.For, 0.5, 3, 1),
            Formulas.CampaignGain(uncrowded, Stance.For, 0.5, 3, 9),
            Tolerance);
    }

    [Fact]
    public void Attack_succeeds_when_target_is_out_of_step()
    {
        var result = Formulas.Attack(Constants, Stance.For, -0.4, 3);
        Assert.True(result.Succeeded);
        Assert.Equal(-3 * 0.4 * 1, result.TargetChange, Tolerance);
        Assert.Equal(0, result.AttackerChange, Tolerance);
    }

    [Fact]
    public void Attack_on_strong_stance_counts_the_penalty()
    {
        var result = Formulas.Attack(Constants, Stance.StronglyFor, 0.1, 3);
        Assert.True(result.Succeeded);
        Assert.Equal(-3 * 0.3, result.TargetChange, Tolerance);
    }

    [Theory]
    [InlineData(Stance.For, 0.4)]
    [InlineData(Stance.Against, -0.4)]
    [InlineData(Stance.For, 0)]
    public void Attack_backfires_when_target_is_in_step_or_level(Stance stance, double opinion)
    {
        var result = Formulas.Attack(Constants, stance, opinion, 3);
        Assert.False(result.Succeeded);
        Assert.Equal(0, result.TargetChange, Tolerance);
        Assert.Equal(-2, result.AttackerChange, Tolerance);
    }

    [Fact]
    public void Popularity_drifts_toward_start_from_above_and_below()
    {
        Assert.Equal(48, Formulas.DriftToward(50, 10, 0.05), Tolerance);
        Assert.Equal(0.5, Formulas.DriftToward(0, 10, 0.05), Tolerance);
        Assert.Equal(10, Formulas.DriftToward(10, 10, 0.05), Tolerance);
    }

    [Theory]
    [InlineData(3, 7, 0.5, 3.5)]
    [InlineData(7, 3, 0.5, 6.5)]
    [InlineData(3.2, 3, 0.5, 3)]
    [InlineData(2.9, 3, 0.5, 3)]
    [InlineData(3, 3, 0.5, 3)]
    [InlineData(40, 60, 1, 41)]
    public void StepToward_moves_by_step_without_overshooting(double value, double target, double step, double expected) =>
        Assert.Equal(expected, Formulas.StepToward(value, target, step), Tolerance);

    [Fact]
    public void RestingDiscontent_adds_condition_rests()
    {
        var discontent = new DiscontentConstants { Rest = 3 };
        Assert.Equal(3, Formulas.RestingDiscontent(discontent, []), Tolerance);
        Assert.Equal(4.5, Formulas.RestingDiscontent(discontent, [Condition("strike", 2), Condition("boom", -0.5)]), Tolerance);
    }
}
