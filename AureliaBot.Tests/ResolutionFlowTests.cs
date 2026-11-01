using Xunit;

namespace AureliaBot.Tests;

public class ResolutionFlowTests : IDisposable
{
    private readonly DatabaseFixture _fixture = new();
    private DatabaseService Db => _fixture.Db;

    public void Dispose() => _fixture.Dispose();

    private static GameConfig Config => DatabaseFixture.Config;

    private void StartWithIssue(double opinion = 0.4, double salience = 3)
    {
        Db.StartGame(Config, DateTimeOffset.UtcNow);
        Db.OpenIssue("labour", "labour", "The Condition of Labour", "For", "Against", opinion, salience);
    }

    private ResolutionReport Resolve(IRandomSource? random = null) =>
        Db.Resolve(Config, random ?? new ScriptedRandomSource([], Enumerable.Repeat(0.5, 20)), 1, DateTimeOffset.UtcNow);

    [Fact]
    public void ACampaignWithTheNation_GainsPopularity_AndRaisesSalienceOnce()
    {
        StartWithIssue();
        Db.CreateFaction(1, "A", "aurenten", 10);
        Db.CreateFaction(2, "B", "aurenten", 10);
        Db.SubmitAction(1, ActionKind.Campaign, "labour", 2, Stance.For);
        Db.SubmitAction(2, ActionKind.Campaign, "labour", 2, Stance.For);

        var report = Resolve();

        Assert.Equal(2, report.Actions.Count);
        Assert.All(report.Actions, a => Assert.True(a.ActorPopularityChange > 0));
        var a = Db.GetFaction(1)!;
        Assert.True(a.Popularity > 10 - 1);
        Assert.Equal(4, Db.GetIssue("labour")!.Salience);
        Assert.Equal(Stance.For, Db.GetLatestLoggedStance(1, "labour"));
        Assert.Empty(Db.GetPendingActions(1));
    }

    [Fact]
    public void AnIssueNobodyCampaignsOn_LosesSalience()
    {
        StartWithIssue();
        Resolve();
        Assert.Equal(2, Db.GetIssue("labour")!.Salience);
    }

    [Fact]
    public void AnAttackOnAStanceTheNationOpposes_CostsTheTarget()
    {
        StartWithIssue(opinion: 0.5);
        Db.CreateFaction(1, "Attacker", "aurenten", 10);
        Db.CreateFaction(2, "Target", "aurenten", 10);
        Db.SubmitAction(2, ActionKind.Campaign, "labour", 2, Stance.Against);
        Resolve();
        var before = Db.GetFaction(2)!.Popularity;

        Db.SubmitAction(1, ActionKind.Attack, "labour", 2, targetFactionId: 2);
        var report = Resolve();

        var attack = Assert.Single(report.Actions);
        Assert.True(attack.TargetPopularityChange < 0);
        Assert.True(Db.GetFaction(2)!.Popularity < before);
    }

    [Fact]
    public void APoll_IsDeliveredPrivatelyInWords()
    {
        StartWithIssue();
        Db.CreateFaction(1, "Pollster", "aurenten", 10);
        Db.SubmitAction(1, ActionKind.Poll, "labour", 2, provinceId: "north_arpal");

        var report = Resolve();

        var message = Assert.Single(report.PrivateMessages[1]);
        Assert.Contains("North Arpal", message);
        Assert.Contains("*", message);
        Assert.Empty(report.Actions);
    }

    [Fact]
    public void LeavingThePreElection_StartsTheFirstTerm()
    {
        StartWithIssue();
        Resolve();
        var state = Db.GetGameState();
        Assert.Equal(Season.Summer, state.Season);
        Assert.Equal(1, state.TermNumber);
        Assert.Equal(Season.Spring, state.PreElectionSeason);
        Assert.Equal(1854, state.PreElectionYear);
        Assert.False(state.IsPreElection);
    }

    [Fact]
    public void ThePendingActionsPreview_MatchesWhatTheResolutionDoes_AndChangesNothing()
    {
        StartWithIssue();
        foreach (var id in new ulong[] { 1, 2, 3, 4 })
            Db.CreateFaction(id, $"F{id}", "aurenten", 30);
        var against = Db.CreateParty("Against", "#cc0000", null, [3, 4]).PartyId!.Value;
        Db.RecordProgramme(against, new Dictionary<string, Stance> { ["labour"] = Stance.Against }, null, Config.Constants);
        Db.SubmitAction(1, ActionKind.Campaign, "labour", 2, Stance.For);
        Db.SubmitAction(3, ActionKind.Campaign, "labour", 2);
        Db.SubmitAction(2, ActionKind.Attack, "labour", 2, targetFactionId: 3);
        Db.SubmitAction(2, ActionKind.Poll, "labour", 2);
        var before = Enumerable.Range(1, 4).ToDictionary(i => (ulong)i, i => Db.GetFaction((ulong)i)!.Popularity);

        var preview = Db.PreviewActions(Config);

        Assert.Equal(4, preview.Plan.Outcomes.Count);
        Assert.All(preview.Plan.Outcomes, o => Assert.False(o.Fizzled));
        Assert.True(preview.Plan.Outcomes.Single(o => o.Action.Kind == ActionKind.Attack).Succeeded);
        Assert.Equal(before, Enumerable.Range(1, 4).ToDictionary(i => (ulong)i, i => Db.GetFaction((ulong)i)!.Popularity));

        Db.ResolveCampaignDay(Config, new ScriptedRandomSource([], Enumerable.Repeat(0.5, 4)), 1, DateTimeOffset.UtcNow);

        foreach (var (id, popularity) in before)
            Assert.Equal(popularity + preview.Plan.PopularityDeltas.GetValueOrDefault(id), Db.GetFaction(id)!.Popularity, 6);
        Assert.True(preview.Plan.PopularityDeltas[3] < 0);
    }

    [Fact]
    public void ACampaignDay_ResolvesActionsWithoutDriftOrAdvancing()
    {
        StartWithIssue();
        Db.CreateFaction(1, "A", "aurenten", 30);
        Db.SubmitAction(1, ActionKind.Campaign, "labour", 2, Stance.For);

        var report = Db.ResolveCampaignDay(Config, new ScriptedRandomSource(), 1, DateTimeOffset.UtcNow);

        var state = Db.GetGameState();
        Assert.True(report.CampaignDay);
        Assert.Equal(Season.Spring, state.Season);
        Assert.Equal(1, state.CampaignDaysRun);
        Assert.Equal(30 + report.Actions.Single().ActorPopularityChange, Db.GetFaction(1)!.Popularity, 6);
    }

    [Fact]
    public void PopularityDriftsTowardTheStartEverySeason()
    {
        StartWithIssue();
        Db.CreateFaction(1, "High", "aurenten", 50);
        Resolve();
        Assert.Equal(48, Db.GetFaction(1)!.Popularity, 6);
    }
}
