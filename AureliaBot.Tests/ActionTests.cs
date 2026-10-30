using Microsoft.Data.Sqlite;
using Xunit;

namespace AureliaBot.Tests;

public class ActionTests : IDisposable
{
    private readonly DatabaseFixture _fixture = new();
    private DatabaseService Db => _fixture.Db;

    public void Dispose() => _fixture.Dispose();

    private const int Max = 2;
    private const ulong Liberal1 = 1;
    private const ulong Liberal2 = 2;
    private const ulong Radical1 = 3;
    private const ulong Radical2 = 4;
    private const ulong Independent = 5;
    private const string Labour = "labour";
    private const string Tariffs = "tariffs";
    private const string Suffrage = "suffrage";

    private long _liberals;

    public ActionTests()
    {
        foreach (var id in new[] { Liberal1, Liberal2, Radical1, Radical2, Independent })
            Db.CreateFaction(id, $"Faction {id}", "aurenten", 10);
        Db.OpenIssue(Labour, "labour", "The Condition of Labour", "for", "against", 0.3, 2);
        Db.OpenIssue(Tariffs, "tariffs", "Tariffs", "for", "against", -0.2, 1);
        Db.OpenIssue(Suffrage, "suffrage", "Suffrage", "for", "against", 0, 1);
        _liberals = Db.CreateParty("Liberal Union", "#ffcc00", null, [Liberal1, Liberal2]).PartyId!.Value;
        var radicals = Db.CreateParty("Radicals", "#cc0000", null, [Radical1, Radical2]).PartyId!.Value;
        Db.RecordProgramme(_liberals, new Dictionary<string, Stance> { [Labour] = Stance.For }, null, DatabaseFixture.Constants);
        Db.RecordProgramme(radicals, new Dictionary<string, Stance> { [Labour] = Stance.StronglyAgainst, [Tariffs] = Stance.For }, null,
            DatabaseFixture.Constants);
    }

    private SubmitActionResult Poll(ulong faction, string issue = Labour) => Db.SubmitAction(faction, ActionKind.Poll, issue, Max);

    private SubmitActionResult Campaign(ulong faction, string issue, Stance? stance = null) =>
        Db.SubmitAction(faction, ActionKind.Campaign, issue, Max, stance);

    private SubmitActionResult Attack(ulong faction, ulong target, string issue) =>
        Db.SubmitAction(faction, ActionKind.Attack, issue, Max, targetFactionId: target);

    [Fact]
    public void ASecondActionOfTheSameKind_ReplacesTheFirst()
    {
        var first = Poll(Liberal1, Labour);
        var second = Poll(Liberal1, Tariffs);

        Assert.Equal(SubmitActionOutcome.Submitted, first.Outcome);
        Assert.Equal(SubmitActionOutcome.Replaced, second.Outcome);
        Assert.Equal(first.Action!.Id, second.Replaced!.Id);
        var pending = Assert.Single(Db.GetPendingActions(Liberal1));
        Assert.Equal(Tariffs, pending.IssueId);
    }

    [Fact]
    public void ReplacingIsAllowed_EvenWithTwoPending()
    {
        Poll(Liberal1);
        Campaign(Liberal1, Labour);

        var replaced = Campaign(Liberal1, Tariffs, Stance.Against);

        Assert.Equal(SubmitActionOutcome.Replaced, replaced.Outcome);
        Assert.Equal(2, Db.GetPendingActions(Liberal1).Count);
    }

    [Fact]
    public void AThirdKind_IsRefusedWhileTwoArePending_AndCancellingFreesASlot()
    {
        var poll = Poll(Liberal1);
        Campaign(Liberal1, Labour);

        var refused = Attack(Liberal1, Radical1, Labour);

        Assert.Equal(SubmitActionOutcome.TooMany, refused.Outcome);
        Assert.Equal(2, refused.Pending.Count);
        Assert.Equal(2, Db.GetPendingActions(Liberal1).Count);

        Assert.False(Db.CancelAction(poll.Action!.Id, Liberal2));
        Assert.True(Db.CancelAction(poll.Action!.Id, Liberal1));
        Assert.False(Db.CancelAction(poll.Action!.Id, Liberal1));

        var accepted = Attack(Liberal1, Radical1, Labour);
        Assert.Equal(SubmitActionOutcome.Submitted, accepted.Outcome);
        Assert.Equal(2, accepted.Pending.Count);
    }

    [Fact]
    public void PendingActions_AreKeptPerFaction()
    {
        Poll(Liberal1);
        Campaign(Liberal1, Labour);

        Assert.Equal(SubmitActionOutcome.Submitted, Poll(Liberal2).Outcome);
        Assert.Equal(2, Db.GetPendingActions(Liberal1).Count);
    }

    [Fact]
    public void Attack_OnAMemberOfYourOwnParty_IsRefused()
    {
        var result = Attack(Liberal1, Liberal2, Labour);

        Assert.Equal(SubmitActionOutcome.AttackRefused, result.Outcome);
        Assert.Contains("own party", result.Refusal);
        Assert.Empty(Db.GetPendingActions(Liberal1));
    }

    [Fact]
    public void Attack_OnAFactionWithNoStance_IsRefused()
    {
        var result = Attack(Liberal1, Radical1, Suffrage);

        Assert.Equal(SubmitActionOutcome.AttackRefused, result.Outcome);
        Assert.Contains("no stance", result.Refusal);
        Assert.Null(Db.CheckAttack(Liberal1, Independent, Labour).Stance);
    }

    [Fact]
    public void Attack_OnYourself_OrARetiredFaction_IsRefused()
    {
        Assert.NotNull(Db.CheckAttack(Radical1, Radical1, Labour).Refusal);
        Db.RetireFaction(Radical2);
        Assert.Contains("not an active", Db.CheckAttack(Liberal1, Radical2, Labour).Refusal);
    }

    [Fact]
    public void Attack_UsesTheTargetsProgrammeStance()
    {
        var check = Db.CheckAttack(Liberal1, Radical1, Labour);
        Assert.Null(check.Refusal);
        Assert.Equal(Stance.StronglyAgainst, check.Stance);

        var result = Attack(Independent, Liberal1, Labour);
        Assert.Equal(SubmitActionOutcome.Submitted, result.Outcome);
        Assert.Equal(Liberal1, result.Action!.TargetFactionId);
    }

    [Fact]
    public void IndependentCampaign_RequiresAStance_AndIsNotOffProgramme()
    {
        Assert.Equal(SubmitActionOutcome.StanceRequired, Campaign(Independent, Labour).Outcome);
        Assert.Empty(Db.GetPendingActions(Independent));

        var result = Campaign(Independent, Labour, Stance.StronglyAgainst);

        Assert.Equal(SubmitActionOutcome.Submitted, result.Outcome);
        Assert.Equal(Stance.StronglyAgainst, result.Action!.Stance);
        Assert.False(result.Action.OffProgramme);
    }

    [Fact]
    public void PartyCampaign_DefaultsToTheProgramme()
    {
        var result = Campaign(Liberal1, Labour);

        Assert.Equal(Stance.For, result.Action!.Stance);
        Assert.False(result.Action.OffProgramme);
    }

    [Fact]
    public void PartyCampaign_AgainstTheProgramme_IsOffProgramme()
    {
        var against = Campaign(Liberal1, Labour, Stance.Against);
        Assert.True(against.Action!.OffProgramme);

        var stronger = Campaign(Liberal1, Labour, Stance.StronglyFor);
        Assert.True(stronger.Action!.OffProgramme);

        var same = Campaign(Liberal1, Labour, Stance.For);
        Assert.False(same.Action!.OffProgramme);
    }

    [Fact]
    public void PartyCampaign_WhereTheProgrammeIsSilent_RequiresAStance_AndIsNotOffProgramme()
    {
        Assert.Equal(SubmitActionOutcome.StanceRequired, Campaign(Liberal1, Tariffs).Outcome);

        var result = Campaign(Liberal1, Tariffs, Stance.Against);

        Assert.Equal(SubmitActionOutcome.Submitted, result.Outcome);
        Assert.False(result.Action!.OffProgramme);
    }

    [Fact]
    public void Actions_OnAnIssueThatIsNotOpen_AreRefused()
    {
        Assert.Equal(SubmitActionOutcome.IssueNotOpen, Poll(Liberal1, "no_such_issue").Outcome);
    }

    [Fact]
    public void Polls_KeepTheirProvince_AndRefuseOneNotInPlay()
    {
        Assert.Equal(SubmitActionOutcome.UnknownProvince,
            Db.SubmitAction(Liberal1, ActionKind.Poll, Labour, Max, provinceId: "aurenten").Outcome);
        using (var conn = new SqliteConnection(_fixture.ConnectionString))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO provinces (id, name, population, discontent) VALUES ('aurenten', 'Aurenten', 1, 3);";
            cmd.ExecuteNonQuery();
        }

        var result = Db.SubmitAction(Liberal1, ActionKind.Poll, Labour, Max, provinceId: "aurenten");

        Assert.Equal("aurenten", result.Action!.ProvinceId);
    }

    [Fact]
    public void RetiredFactions_CannotSubmit()
    {
        Db.RetireFaction(Independent);

        Assert.Equal(SubmitActionOutcome.NotActive, Poll(Independent).Outcome);
    }
}
