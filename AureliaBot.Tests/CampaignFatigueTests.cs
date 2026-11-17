using Xunit;

namespace AureliaBot.Tests;

public class CampaignFatigueTests : IDisposable
{
    private readonly DatabaseFixture _fixture = new();
    private DatabaseService Db => _fixture.Db;
    private static GameConfig Config => DatabaseFixture.Config;
    private static CampaignConstants Campaign => Config.Constants.Campaign;
    private const double Tolerance = 1e-9;

    public void Dispose() => _fixture.Dispose();

    private ResolutionReport Resolve() =>
        Db.Resolve(Config, new ScriptedRandomSource([], Enumerable.Repeat(0.5, 50)), 1, DateTimeOffset.UtcNow);

    private void StartATerm()
    {
        Db.StartGame(Config, DateTimeOffset.UtcNow);
        Db.CreateFaction(1, "A", "aurenten", 10);
        Resolve();
        Db.OpenIssue("labour", "labour", "The Condition of Labour", "For", "Against", 0.5, 3);
    }

    private double CampaignOnce()
    {
        Assert.Equal(SubmitActionOutcome.Submitted,
            Db.SubmitAction(1, ActionKind.Campaign, "labour", 2, Stance.For, "aurenten").Outcome);
        return Resolve().Actions.Single(a => a.FactionId == 1).ActorPopularityChange;
    }

    private double Expected(double tiredness)
    {
        var snapshot = Db.LoadOpinionSnapshot(Config);
        return Formulas.CampaignGain(Config.Constants, Stance.For, snapshot.Provincial("labour", "aurenten"), snapshot.Salience("labour"), 1, tiredness);
    }

    [Fact]
    public void CampaigningOnTheSameIssueInTheSameProvince_TiresIt_TurnAfterTurn()
    {
        StartATerm();
        double tired = 0;
        for (var turn = 0; turn < 3; turn++)
        {
            tired *= Campaign.Fatigue.Fade;
            var expected = Expected(tired);
            var fresh = Expected(0);
            var actual = CampaignOnce();
            Assert.Equal(expected, actual, 6);
            Assert.True(actual > 0);
            if (turn > 0) Assert.True(actual < fresh);
            tired += 1;
        }
    }

    [Fact]
    public void Tiredness_FadesEachTurn_AndIsKeptPerFactionProvinceAndIssue()
    {
        StartATerm();
        CampaignOnce();
        Assert.Equal(1, Db.GetCampaignFatigue()[(1UL, "aurenten", "labour")], 6);

        Resolve();
        Assert.Equal(Campaign.Fatigue.Fade, Db.GetCampaignFatigue()[(1UL, "aurenten", "labour")], 6);

        Db.CreateFaction(2, "B", "aurenten", 10);
        Assert.False(Db.GetCampaignFatigue().ContainsKey((2UL, "aurenten", "labour")));
        Assert.False(Db.GetCampaignFatigue().ContainsKey((1UL, "provencie_gares", "labour")));
    }

    [Fact]
    public void TheMultiplier_RewardsFreshCampaigns_AndCutsTiredAndOffProgrammeOnes()
    {
        Assert.Equal(Campaign.Fresh, Formulas.CampaignMultiplier(Campaign, 0, false), Tolerance);
        Assert.Equal(Campaign.Fresh / (1 + Campaign.Fatigue.Weight), Formulas.CampaignMultiplier(Campaign, 1, false), Tolerance);
        Assert.Equal(Campaign.Fresh * Campaign.OffProgramme, Formulas.CampaignMultiplier(Campaign, 0, true), Tolerance);
    }

    [Fact]
    public void ALosingCampaign_IsNeverSoftened()
    {
        var plain = Formulas.CampaignGain(Config.Constants, Stance.StronglyFor, -0.9, 3, 1);
        Assert.True(plain < 0);
        Assert.Equal(plain, Formulas.CampaignGain(Config.Constants, Stance.StronglyFor, -0.9, 3, 1, fatigue: 4, offProgramme: true), Tolerance);
    }

    [Fact]
    public void TheDefaults_KeepTheReferenceCampaignAtTodaysGain()
    {
        var everyOtherTurn = Campaign.Fatigue.Fade * Campaign.Fatigue.Fade / (1 - Campaign.Fatigue.Fade * Campaign.Fatigue.Fade);
        var onProgramme = Formulas.CampaignMultiplier(Campaign, everyOtherTurn, false);
        var offProgramme = Formulas.CampaignMultiplier(Campaign, everyOtherTurn, true);
        var average = 0.9 * onProgramme + 0.1 * offProgramme;
        Assert.InRange(average, 0.97, 1.03);
    }
}
