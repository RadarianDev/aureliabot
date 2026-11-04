using Xunit;

namespace AureliaBot.Tests;

public class ActionPlannerTests
{
    private const long Resolution = 7;

    private const string Home = "home";

    private static IReadOnlyDictionary<ulong, double> Deltas(ActionPlan plan) =>
        plan.PopularityDeltas.GroupBy(d => d.FactionId).ToDictionary(g => g.Key, g => g.Sum(d => d.Change));

    private static PendingAction Campaign(long id, ulong faction, string issue, Stance stance, string province = Home) =>
        new(id, faction, ActionKind.Campaign, issue, province, stance, false, null, DateTimeOffset.UnixEpoch);

    private static PendingAction Attack(long id, ulong faction, ulong target, string issue) =>
        new(id, faction, ActionKind.Attack, issue, Home, null, false, target, DateTimeOffset.UnixEpoch);

    private static PendingAction Poll(long id, ulong faction, string issue) =>
        new(id, faction, ActionKind.Poll, issue, null, null, false, null, DateTimeOffset.UnixEpoch);

    private static ActionStartState Start(
        Func<PendingAction, AttackTarget>? targets = null,
        Func<string, long?>? raised = null,
        bool crowding = true) =>
        new(Resolution, crowding, issue => issue != "closed", (_, _) => 0.5, _ => 3, raised ?? (_ => null),
            targets ?? (_ => new AttackTarget(Stance.For, null)));

    private static ActionScoring CountingScoring(List<Stance>? attackedStances = null) =>
        new((_, _, _, m) => m,
            (stance, _, _) =>
            {
                attackedStances?.Add(stance);
                return stance.Sign() > 0 ? new AttackResult(false, 0, -2) : new AttackResult(true, -3, 0);
            },
            action => $"poll {action.IssueId}");

    [Fact]
    public void CampaignCrowding_CountsFactionsOnTheSameSideOfTheIssue()
    {
        var plan = ActionPlanner.Plan(
        [
            Campaign(1, 10, "labour", Stance.For),
            Campaign(2, 11, "labour", Stance.StronglyFor),
            Campaign(3, 12, "labour", Stance.Against),
            Campaign(4, 13, "tariffs", Stance.For),
        ], Start(), CountingScoring());

        Assert.Equal(2, Deltas(plan)[10]);
        Assert.Equal(2, Deltas(plan)[11]);
        Assert.Equal(1, Deltas(plan)[12]);
        Assert.Equal(1, Deltas(plan)[13]);
    }

    [Fact]
    public void CampaignCrowding_Off_CountsEveryCampaignAlone()
    {
        var plan = ActionPlanner.Plan(
        [
            Campaign(1, 10, "labour", Stance.For),
            Campaign(2, 11, "labour", Stance.For),
        ], Start(crowding: false), CountingScoring());

        Assert.Equal(1, Deltas(plan)[10]);
        Assert.Equal(1, Deltas(plan)[11]);
    }

    [Fact]
    public void CampaignCrowding_CountsOnlyCampaignsInTheSameProvince()
    {
        var plan = ActionPlanner.Plan(
        [
            Campaign(1, 10, "labour", Stance.For, "north"),
            Campaign(2, 11, "labour", Stance.For, "north"),
            Campaign(3, 12, "labour", Stance.For, "south"),
        ], Start(), CountingScoring());

        Assert.Equal(new RegionalDelta(10, "north", 2), plan.PopularityDeltas.Single(d => d.FactionId == 10));
        Assert.Equal(new RegionalDelta(11, "north", 2), plan.PopularityDeltas.Single(d => d.FactionId == 11));
        Assert.Equal(new RegionalDelta(12, "south", 1), plan.PopularityDeltas.Single(d => d.FactionId == 12));
    }

    [Fact]
    public void Campaigns_AreScoredOnTheOpinionOfTheirProvince()
    {
        var start = new ActionStartState(Resolution, true, _ => true, (_, province) => province == "north" ? 0.8 : -0.8, _ => 3, _ => null,
            _ => new AttackTarget(Stance.For, null));
        var scoring = new ActionScoring((_, opinion, _, _) => opinion, (_, _, _) => new AttackResult(false, 0, 0), _ => "");

        var plan = ActionPlanner.Plan([Campaign(1, 10, "labour", Stance.For, "north"), Campaign(2, 11, "labour", Stance.For, "south")], start, scoring);

        Assert.Equal(0.8, Deltas(plan)[10]);
        Assert.Equal(-0.8, Deltas(plan)[11]);
    }

    [Fact]
    public void Slots_ReplaceTheSameKindInTheSameProvince_OrAPollOnTheSameIssue()
    {
        IReadOnlyList<PendingAction> pending = [Campaign(1, 10, "labour", Stance.For, "north"), Poll(2, 10, "labour")];

        Assert.Equal(1, ActionPlanner.SlotFor(pending, ActionKind.Campaign, "tariffs", "north", 2).Replaces?.Id);
        Assert.Equal(2, ActionPlanner.SlotFor(pending, ActionKind.Poll, "labour", null, 2).Replaces?.Id);
        Assert.False(ActionPlanner.SlotFor(pending, ActionKind.Campaign, "labour", "south", 2).Allowed);
        Assert.True(ActionPlanner.SlotFor(pending, ActionKind.Campaign, "labour", "south", 3).Allowed);
        Assert.Null(ActionPlanner.SlotFor(pending, ActionKind.Attack, "labour", "north", 3).Replaces);
    }

    [Fact]
    public void InThePreElection_CampaignsStackWithoutReplacing_ButOtherKindsStillDo()
    {
        IReadOnlyList<PendingAction> pending = [Campaign(1, 10, "labour", Stance.For, "north"), Poll(2, 10, "labour")];

        var campaign = ActionPlanner.SlotFor(pending, ActionKind.Campaign, "labour", "north", 5, campaignsStack: true);
        Assert.True(campaign.Allowed);
        Assert.Null(campaign.Replaces);
        Assert.False(ActionPlanner.SlotFor(pending, ActionKind.Campaign, "labour", "north", 2, campaignsStack: true).Allowed);
        Assert.Equal(2, ActionPlanner.SlotFor(pending, ActionKind.Poll, "labour", null, 5, campaignsStack: true).Replaces?.Id);
    }

    [Fact]
    public void Resolution_IsIndependentOfSubmissionOrder()
    {
        var specs = new (ulong Faction, ActionKind Kind, string Issue, Stance? Stance, ulong? Target)[]
        {
            (10, ActionKind.Campaign, "labour", Stance.For, null),
            (11, ActionKind.Campaign, "labour", Stance.StronglyFor, null),
            (12, ActionKind.Campaign, "labour", Stance.Against, null),
            (13, ActionKind.Attack, "labour", null, 10),
            (14, ActionKind.Attack, "tariffs", null, 12),
            (10, ActionKind.Attack, "tariffs", null, 14),
            (12, ActionKind.Campaign, "tariffs", Stance.StronglyAgainst, null),
        };
        var stances = new Dictionary<(ulong, string), Stance>
        {
            [(10, "labour")] = Stance.For,
            [(12, "tariffs")] = Stance.Against,
            [(14, "tariffs")] = Stance.StronglyFor,
        };
        var start = Start(a => new AttackTarget(stances[(a.TargetFactionId!.Value, a.IssueId)], null));

        IReadOnlyDictionary<ulong, double> Run(IEnumerable<int> order)
        {
            var actions = order.Select((spec, position) =>
            {
                var (faction, kind, issue, stance, target) = specs[spec];
                return new PendingAction(100 + position, faction, kind, issue, Home, stance, false, target, DateTimeOffset.UnixEpoch);
            });
            return Deltas(ActionPlanner.Plan(actions, start, CountingScoring()));
        }

        var baseline = Run(Enumerable.Range(0, specs.Length));
        var random = new Random(1234);
        for (var i = 0; i < 20; i++)
        {
            var shuffled = Enumerable.Range(0, specs.Length).OrderBy(_ => random.Next()).ToList();
            var deltas = Run(shuffled);
            Assert.Equal(baseline.OrderBy(kv => kv.Key), deltas.OrderBy(kv => kv.Key));
        }
    }

    [Fact]
    public void Attack_UsesTheTargetsStartOfResolutionStance_EvenIfItCampaignsTheOppositeWay()
    {
        var attacked = new List<Stance>();
        var plan = ActionPlanner.Plan(
        [
            Campaign(1, 20, "labour", Stance.Against),
            Attack(2, 21, 20, "labour"),
        ], Start(_ => new AttackTarget(Stance.For, null)), CountingScoring(attacked));

        Assert.Equal([Stance.For], attacked);
        var attack = plan.Outcomes.Single(o => o.Action.Kind == ActionKind.Attack);
        Assert.Equal(Stance.For, attack.TargetStance);
        Assert.Equal(-2, Deltas(plan)[21]);
        Assert.Contains(plan.StanceLogs, s => s.FactionId == 20 && s.Stance == Stance.Against);
    }

    [Fact]
    public void Attack_SuccessHitsTheTarget_AndBackfireHitsTheAttacker()
    {
        var plan = ActionPlanner.Plan(
        [
            Attack(1, 30, 31, "labour"),
            Attack(2, 32, 33, "tariffs"),
        ], Start(a => new AttackTarget(a.TargetFactionId == 31 ? Stance.Against : Stance.For, null)), CountingScoring());

        Assert.Equal(-3, Deltas(plan)[31]);
        Assert.False(Deltas(plan).ContainsKey(30));
        Assert.Equal(-2, Deltas(plan)[32]);
        Assert.False(Deltas(plan).ContainsKey(33));
    }

    [Fact]
    public void Attack_OnATargetThatLostItsStance_Fizzles()
    {
        var plan = ActionPlanner.Plan(
            [Attack(1, 30, 31, "labour")],
            Start(_ => new AttackTarget(null, "the target is not an active faction")),
            CountingScoring());

        var outcome = Assert.Single(plan.Outcomes);
        Assert.True(outcome.Fizzled);
        Assert.Empty(plan.PopularityDeltas);
        Assert.Contains("not an active faction", outcome.PrivateMessage);
    }

    [Fact]
    public void Salience_RisesOncePerIssue_AndNotAgainInTheSameSeason()
    {
        var plan = ActionPlanner.Plan(
        [
            Campaign(1, 10, "labour", Stance.For),
            Campaign(2, 11, "labour", Stance.Against),
            Campaign(3, 12, "labour", Stance.StronglyFor),
            Campaign(4, 13, "tariffs", Stance.For),
            Campaign(5, 14, "suffrage", Stance.For),
        ], Start(raised: issue => issue == "tariffs" ? Resolution : Resolution - 1), CountingScoring());

        Assert.Equal(["labour", "suffrage"], plan.SalienceRaises);
    }

    [Fact]
    public void Polls_AreDeliveredPrivately_AndCampaignsAreLogged()
    {
        var plan = ActionPlanner.Plan(
        [
            Poll(1, 10, "labour"),
            Campaign(2, 10, "labour", Stance.For),
        ], Start(), CountingScoring());

        Assert.Equal("poll labour", plan.Outcomes.Single(o => o.Action.Kind == ActionKind.Poll).PrivateMessage);
        Assert.Single(plan.StanceLogs);
        Assert.Equal(1, Deltas(plan)[10]);
    }

    [Fact]
    public void ActionsOnAClosedIssue_Fizzle()
    {
        var plan = ActionPlanner.Plan(
        [
            Campaign(1, 10, "closed", Stance.For),
            Poll(2, 10, "closed"),
        ], Start(), CountingScoring());

        Assert.All(plan.Outcomes, o => Assert.True(o.Fizzled));
        Assert.Empty(plan.StanceLogs);
        Assert.Empty(plan.SalienceRaises);
        Assert.Empty(plan.PopularityDeltas);
    }

    [Theory]
    [InlineData(null, null, CampaignStanceOutcome.StanceRequired, null, false)]
    [InlineData(Stance.Silent, null, CampaignStanceOutcome.StanceRequired, null, false)]
    [InlineData(null, Stance.Against, CampaignStanceOutcome.Ok, Stance.Against, false)]
    [InlineData(Stance.For, null, CampaignStanceOutcome.Ok, Stance.For, false)]
    [InlineData(Stance.For, Stance.For, CampaignStanceOutcome.Ok, Stance.For, false)]
    [InlineData(Stance.For, Stance.StronglyFor, CampaignStanceOutcome.Ok, Stance.StronglyFor, true)]
    [InlineData(Stance.For, Stance.Against, CampaignStanceOutcome.Ok, Stance.Against, true)]
    [InlineData(Stance.For, Stance.Silent, CampaignStanceOutcome.SilentRefused, null, false)]
    public void CampaignStance_DefaultsToTheProgramme_AndFlagsOffProgramme(
        Stance? programme, Stance? given, CampaignStanceOutcome outcome, Stance? stance, bool offProgramme)
    {
        var decision = ActionPlanner.DecideCampaignStance(programme, given);

        Assert.Equal(outcome, decision.Outcome);
        Assert.Equal(stance, decision.Stance);
        Assert.Equal(offProgramme, decision.OffProgramme);
    }
}
