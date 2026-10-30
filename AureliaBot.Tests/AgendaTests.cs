using Xunit;

namespace AureliaBot.Tests;

public class AgendaTests : AssemblyTestBase
{
    private const string Dock = """
        id: dock
        title: The Carden Dock Strike
        ministry: CommerceLabor
        province: north_arpal
        issue: labour
        answers:
          - lean: For
            label: Meet the strikers' demands
            text: The docks reopen.
          - lean: Against
            label: Declare the strike unlawful
            text: The strike collapses.
        default:
          text: The strike drags on.
        """;

    public AgendaTests()
    {
        Db.SaveTemplate("dock", TemplateKind.Crisis, Dock);
        Db.SetTemplateStatus("dock", TemplateStatus.Ready);
    }

    private string Agenda() => GmAgenda.Build(Db, Config);

    [Fact]
    public void AnEmptyQueue_IsFlagged_UntilAnEventFiresAtTheNextResolution()
    {
        Assert.Contains(Texts.Get("agenda.events.emptyNext"), Agenda());

        Db.QueueEvent(new EventTemplate("later", "A Later Event", "Text.", [], null), Db.GetGameState().ResolutionNumber + 3, false);
        var agenda = Agenda();
        Assert.Contains(Texts.Get("agenda.events.emptyNext"), agenda);
        Assert.Contains("A Later Event", agenda);

        Db.QueueEvent(new EventTemplate("soon", "A Sooner Event", "Text.", [], null), Db.GetGameState().ResolutionNumber + 1, false);
        agenda = Agenda();
        Assert.DoesNotContain(Texts.Get("agenda.events.emptyNext"), agenda);
        Assert.True(agenda.IndexOf("A Sooner Event", StringComparison.Ordinal) < agenda.IndexOf("A Later Event", StringComparison.Ordinal));
    }

    [Fact]
    public void ACrisis_IsListedUntilItsMinisterResponds()
    {
        SeatCabinet(Chancellor, (Ministry.Treasury, Minister), (Ministry.CommerceLabor, Backbencher));
        Db.ApplyEffects([new Effect("crisis.dock", null)], Config, new EffectSource("test", "gm", "test"));
        var crisis = Db.GetCrises(CrisisStatus.Live).Single();
        Assert.Contains("The Carden Dock Strike", Agenda());

        Assert.Null(Db.RespondToCrisis(crisis.Id, Backbencher, 1, "Feed them.").Refusal);

        var agenda = Agenda();
        Assert.DoesNotContain("The Carden Dock Strike", agenda);
        Assert.Contains(Texts.Get("agenda.crises.none"), agenda);
    }

    [Fact]
    public void VacantMinistries_SayWhetherTheChancellorAnswersForThem()
    {
        var agenda = Agenda();
        Assert.Contains(Texts.Get("agenda.vacant.covered", ("ministry", Ministry.War.DisplayName()), ("chancellor", Display.Faction(Db, Chancellor))), agenda);
        Assert.DoesNotContain(Texts.Get("agenda.vacant.chancellor"), agenda);

        SeatCabinet(null, (Ministry.Treasury, Minister));
        agenda = Agenda();
        Assert.Contains(Texts.Get("agenda.vacant.chancellor"), agenda);
        Assert.Contains(Texts.Get("agenda.vacant.uncovered", ("ministry", Ministry.War.DisplayName())), agenda);
        Assert.DoesNotContain(Texts.Get("agenda.vacant.uncovered", ("ministry", Ministry.Treasury.DisplayName())), agenda);
    }

    [Fact]
    public void ScheduledBills_JoinApplications_AndOpenVotes_AreListedWithTallies()
    {
        var reading = Propose(Chancellor, "ten_hours");
        var party = Db.GetPartyByName("Liberal Union")!;
        Assert.Equal(JoinRequestOutcome.Created, Db.CreateJoinRequest(Independent, party.Id).Outcome);

        var agenda = Agenda();
        Assert.Contains(Texts.Get("agenda.scheduled.government", ("bill", "ten_hours"), ("sponsor", Display.Faction(Db, Chancellor)),
            ("opens", GovernmentPublisher.Deadline(Db.GetGameState(), reading.OpensResolution))), agenda);
        Assert.Contains(Display.Faction(Db, Independent), agenda);
        Assert.Contains(Texts.Get("agenda.votes.none"), agenda);

        Resolve();
        Vote(VoteSubject.Reading, reading.Id, Opposer, VoteChoice.No);

        agenda = Agenda();
        Assert.Contains(Texts.Get("agenda.votes.tally", ("yes", 0), ("no", 50), ("abstain", 0)), agenda);
        Assert.Contains(Texts.Get("agenda.scheduled.none"), agenda);
    }
}
