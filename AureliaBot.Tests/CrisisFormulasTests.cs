using Xunit;

namespace AureliaBot.Tests;

public class CrisisFormulasTests
{
    private const double Tolerance = 1e-9;

    private static GameConstants Constants => DatabaseFixture.Constants;

    [Fact]
    public void Constants_MatchTheValuesTheseTestsAssume()
    {
        Assert.Equal(4, Constants.Campaign.Factor);
        Assert.Equal(2, Constants.Opposition.CondemnReward);
        Assert.Equal(1, Constants.Opposition.CondemnPenalty);
    }

    [Theory]
    [InlineData(0.5, 3)]
    [InlineData(-0.5, 3)]
    [InlineData(0.9, 5)]
    public void CrisisGain_NeutralAnswer_GainsNothing(double opinion, double salience) =>
        Assert.Equal(0, Formulas.CrisisGain(Constants, Lean.Neutral, opinion, salience), Tolerance);

    [Theory]
    [InlineData(Lean.For, 0.5, 3, 2)]
    [InlineData(Lean.For, -0.5, 3, -2)]
    [InlineData(Lean.Against, 0.5, 3, -2)]
    [InlineData(Lean.Against, -0.5, 3, 2)]
    [InlineData(Lean.For, 0.5, 6, 4)]
    [InlineData(Lean.Against, -0.75, 1, 1)]
    public void CrisisGain_IsSignedByOpinionAndScaledBySalience(Lean answer, double opinion, double salience, double expected) =>
        Assert.Equal(expected, Formulas.CrisisGain(Constants, answer, opinion, salience), Tolerance);

    [Theory]
    [InlineData(3, 4)]
    [InlineData(6, 8)]
    [InlineData(1.5, 2)]
    public void CrisisDefaultLoss_IsAPositiveMagnitudeScaledBySalience(double salience, double expected) =>
        Assert.Equal(expected, Formulas.CrisisDefaultLoss(Constants, salience), Tolerance);

    [Theory]
    [InlineData(Lean.For, Lean.Against, 0.4, 2)]
    [InlineData(Lean.Against, Lean.For, -0.4, 2)]
    [InlineData(Lean.For, Lean.For, 0.4, 0)]
    [InlineData(Lean.Against, Lean.Against, -0.4, 0)]
    [InlineData(Lean.Against, Lean.For, 0.4, -1)]
    [InlineData(Lean.For, Lean.Against, -0.4, -1)]
    [InlineData(Lean.Against, Lean.Against, 0.4, 0)]
    [InlineData(Lean.For, Lean.For, -0.4, 0)]
    public void CondemnationChange_RewardsRightOverWrongAndPenalisesWrongOverRight(Lean side, Lean minister, double opinion, double expected) =>
        Assert.Equal(expected, Formulas.CondemnationChange(Constants, side, minister, opinion), Tolerance);

    [Fact]
    public void CondemnationChange_NeutralMinisterAnswer_IsNotOnTheRightSide()
    {
        Assert.Equal(2, Formulas.CondemnationChange(Constants, Lean.For, Lean.Neutral, 0.4), Tolerance);
        Assert.Equal(0, Formulas.CondemnationChange(Constants, Lean.Against, Lean.Neutral, 0.4), Tolerance);
    }

    [Fact]
    public void CondemnationChange_NoMinisterResponse_IsNotOnTheRightSide()
    {
        Assert.Equal(2, Formulas.CondemnationChange(Constants, Lean.Against, null, -0.4), Tolerance);
        Assert.Equal(0, Formulas.CondemnationChange(Constants, Lean.For, null, -0.4), Tolerance);
    }

    [Theory]
    [InlineData(Lean.For, Lean.For)]
    [InlineData(Lean.For, Lean.Against)]
    [InlineData(Lean.Against, Lean.For)]
    [InlineData(Lean.Against, null)]
    [InlineData(Lean.For, Lean.Neutral)]
    public void CondemnationChange_ZeroOpinion_GivesNobodyAnything(Lean side, Lean? minister) =>
        Assert.Equal(0, Formulas.CondemnationChange(Constants, side, minister, 0), Tolerance);
}
