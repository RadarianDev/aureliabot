using Xunit;
using Xunit.Abstractions;

namespace AureliaBot.Tests;

public class CampaignScenarioTests : IDisposable
{
    private readonly DatabaseFixture _fixture = new();
    private readonly ITestOutputHelper _output;
    private DatabaseService Db => _fixture.Db;
    private static GameConfig Config => DatabaseFixture.Config;
    private static PopularityConstants Range => Config.Constants.Popularity;

    private const ulong Strong = 1;
    private const ulong Plain = 2;
    private const ulong Idle = 3;
    private const string Target = "provencie_gares";

    public CampaignScenarioTests(ITestOutputHelper output) => _output = output;

    public void Dispose() => _fixture.Dispose();

    private Dictionary<string, double> Regional(ulong id) => Db.GetRegionalPopularity(id, Range);

    private double WeightedMean(ulong id)
    {
        var regional = Regional(id);
        var populations = Config.Provinces.ToDictionary(p => p.Id, p => p.Population);
        return regional.Sum(kv => kv.Value * populations[kv.Key]) / populations.Values.Sum();
    }

    private void Report(string moment)
    {
        foreach (var id in new[] { Strong, Plain, Idle })
        {
            var regional = Regional(id);
            _output.WriteLine($"{moment,-22} faction {id}: national {Db.GetFaction(id)!.Popularity,6:0.00} · " +
                              $"{Target} {regional[Target],6:0.00} · aurenten {regional["aurenten"],6:0.00}");
        }
    }

    private void AssertNationalIsTheWeightedMean()
    {
        foreach (var id in new[] { Strong, Plain, Idle })
            Assert.Equal(Db.GetFaction(id)!.Popularity, WeightedMean(id), 6);
    }

    [Fact]
    public void APreElectionTour_BuildsARegionalLead_ThatSpreadsAcrossTheKingdomAfterwards()
    {
        Db.StartGame(Config, DateTimeOffset.UtcNow);
        Db.OpenIssue("labour", "labour", "The Condition of Labour", "For", "Against", 0.1, 3);
        Db.CreateFaction(Strong, "Strong", "aurenten", 10);
        Db.CreateFaction(Plain, "Plain", "aurenten", 10);
        Db.CreateFaction(Idle, "Idle", "aurenten", 10);
        var opinion = Db.LoadOpinionSnapshot(Config).Provincial("labour", Target);
        _output.WriteLine($"Opinion on labour in {Target}: {opinion:+0.000;-0.000}");
        Report("start");

        var max = Config.Constants.Campaign.ActionsPerPreElectionDay;
        for (var day = 1; day <= Config.Constants.PreElectionDays - 1; day++)
        {
            var snapshot = Db.LoadOpinionSnapshot(Config);
            var salience = snapshot.Salience("labour");
            var strongEach = Formulas.CampaignGain(Config.Constants, Stance.StronglyFor, opinion, salience, 2 * max);
            var plainOnce = Formulas.CampaignGain(Config.Constants, Stance.For, opinion, salience, 2);
            _output.WriteLine($"Day {day}: salience {salience}; expected Strong {max} × {strongEach:0.000} = {max * strongEach:0.000}, Plain {plainOnce:0.000}");
            for (var i = 0; i < max; i++)
                Assert.Equal(SubmitActionOutcome.Submitted,
                    Db.SubmitAction(Strong, ActionKind.Campaign, "labour", max, Stance.StronglyFor, Target).Outcome);
            Db.SubmitAction(Plain, ActionKind.Campaign, "labour", max, Stance.For, Target);
            Assert.Equal(max, Db.GetPendingActions(Strong).Count);

            var report = Db.ResolveCampaignDay(Config, new ScriptedRandomSource(), 1, DateTimeOffset.UtcNow);
            Assert.All(report.Actions.Where(a => a.FactionId == Strong), a => Assert.Equal(strongEach, a.ActorPopularityChange, 6));
            Assert.Equal(plainOnce, report.Actions.Single(a => a.FactionId == Plain).ActorPopularityChange, 6);
            Assert.True(strongEach > 0);
            Report($"campaign day {day}");
            AssertNationalIsTheWeightedMean();
        }

        var strongLead = Regional(Strong)[Target] - Db.GetFaction(Strong)!.Popularity;
        Assert.True(strongLead > 0);
        Assert.Equal(10, Regional(Idle)[Target], 6);
        Assert.True(Db.GetFaction(Strong)!.Popularity > 10);

        var leads = new List<double> { strongLead };
        for (var season = 1; season <= 4; season++)
        {
            Db.Resolve(Config, new ScriptedRandomSource([], Enumerable.Repeat(0.5, 50)), 1, DateTimeOffset.UtcNow);
            leads.Add(Regional(Strong)[Target] - Db.GetFaction(Strong)!.Popularity);
            Report($"after season {season}");
            AssertNationalIsTheWeightedMean();
        }

        for (var i = 1; i < leads.Count; i++)
            Assert.Equal(leads[i - 1] * (1 - Range.Spread), leads[i], 6);
        _output.WriteLine($"Strong's lead in {Target}: {string.Join(" → ", leads.Select(l => l.ToString("0.00")))}");
    }
}
