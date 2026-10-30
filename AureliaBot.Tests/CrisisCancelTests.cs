using Xunit;

namespace AureliaBot.Tests;

public class CrisisCancelTests : AssemblyTestBase
{
    private const string Labour = """
        id: labour
        title: The Condition of Labour
        for: For
        against: Against
        opinion: 0.4
        salience: 3
        """;

    private const string Dock = """
        id: dock
        title: The Carden Dock Strike
        ministry: CommerceLabor
        province: north_arpal
        issue: labour
        answers:
          - lean: For
            label: Meet the strikers' demands
            price: 3
            text: The docks reopen within the week.
            effects: { economy: -1 }
          - lean: Against
            label: Declare the strike unlawful
            text: The strike collapses.
        default:
          text: The strike drags on unanswered.
          effects: { economy: -2 }
        """;

    private const string Works = """
        id: works
        title: The Works
        purse: CommerceLabor
        province: aurenten
        price: 6
        term: 3
        effects: { economy: 1 }
        bonus:
          within: 2
          text: Finished early.
          effects: { favour: 1 }
        """;

    public CrisisCancelTests()
    {
        SeatCabinet(Chancellor, (Ministry.Treasury, Minister), (Ministry.CommerceLabor, Backbencher));
        foreach (var (id, kind, yaml) in new[] { ("labour", TemplateKind.Issue, Labour), ("dock", TemplateKind.Crisis, Dock), ("works", TemplateKind.Project, Works) })
        {
            Db.SaveTemplate(id, kind, yaml);
            Db.SetTemplateStatus(id, TemplateStatus.Ready);
        }
    }

    private Crisis Post()
    {
        Assert.Empty(Db.ApplyEffects([new Effect("crisis.dock", null)], Config, new EffectSource("test", "gm", "test")).Warnings);
        return Db.GetCrises(CrisisStatus.Live).Last();
    }

    [Fact]
    public void CancellingALiveCrisis_MarksItCancelled_AndGivesBackTheCondemnersOppositionActions()
    {
        var crisis = Post();
        Assert.Null(Db.Condemn(Opposer, crisis.Id, Lean.Against, C).Refusal);
        Assert.False(Db.GetOppositionStanding(Opposer, C).CanAct);

        var result = Db.CancelCrisis(crisis.Id);

        Assert.Null(result.Refusal);
        Assert.Equal(CrisisStatus.Cancelled, Db.GetCrisis(crisis.Id)!.Status);
        Assert.Equal([Opposer], result.Refunded);
        Assert.True(Db.GetOppositionStanding(Opposer, C).CanAct);
        Assert.NotNull(Db.CancelCrisis(crisis.Id).Refusal);
    }

    [Fact]
    public void ACancelledCrisis_NeverResolves()
    {
        var crisis = Post();
        var economy = Db.GetGameState().Economy;
        Db.CancelCrisis(crisis.Id);

        var report = Db.ResolveDueCrises(Config, CrisisRules.Standard);

        Assert.Empty(report.CrisisOutcomes);
        Assert.Equal(economy, Db.GetGameState().Economy);
        Assert.Equal(CrisisStatus.Cancelled, Db.GetCrisis(crisis.Id)!.Status);
    }

    [Fact]
    public void ALiveCrisisEdit_ChangesTheLiveCopyOnly_UntilTheMinisterResponds()
    {
        var crisis = Post();
        var target = ContentRules.FindLiveCrisis(Db, crisis.Id, out var refusal);
        Assert.Null(refusal);
        var yaml = TemplateCodec.Serialize(target!.Current).Replace("The Carden Dock Strike", "The Great Dock Strike");

        var result = ContentRules.ApplyLiveEdit(Db, Config, target, yaml);

        Assert.True(result.Ok, result.Message);
        Assert.Equal(crisis.Id, result.AmendedCrisisId);
        Assert.Equal("The Great Dock Strike", Db.GetCrisis(crisis.Id)!.Title);
        Assert.Equal("The Carden Dock Strike", Db.GetParsedTemplate("dock")!.Title);

        Assert.Null(Db.RespondToCrisis(crisis.Id, Backbencher, 1, "Feed them.").Refusal);
        Assert.Null(ContentRules.FindLiveCrisis(Db, crisis.Id, out refusal));
        Assert.NotNull(refusal);
        Assert.False(ContentRules.ApplyLiveEdit(Db, Config, target, yaml.Replace("Great", "Grand")).Ok);
        Assert.Equal("The Great Dock Strike", Db.GetCrisis(crisis.Id)!.Title);
    }

    [Fact]
    public void AResponseWrittenBeforeAnEdit_IsRefused()
    {
        var crisis = Post();
        var stamp = DatabaseService.AnswersStamp(crisis);
        var target = ContentRules.FindLiveCrisis(Db, crisis.Id, out _)!;
        var yaml = TemplateCodec.Serialize(target.Current).Replace("Declare the strike unlawful", "Break the strike");
        Assert.True(ContentRules.ApplyLiveEdit(Db, Config, target, yaml).Ok);

        Assert.NotNull(Db.RespondToCrisis(crisis.Id, Backbencher, 1, "Feed them.", stamp).Refusal);
        Assert.Null(Db.RespondToCrisis(crisis.Id, Backbencher, 1, "Feed them.", DatabaseService.AnswersStamp(Db.GetCrisis(crisis.Id)!)).Refusal);
    }

    [Fact]
    public void ALiveCrisisEdit_KeepsItsMinistryAndIssue()
    {
        var crisis = Post();
        var target = ContentRules.FindLiveCrisis(Db, crisis.Id, out _)!;
        var yaml = TemplateCodec.Serialize(target.Current).Replace("ministry: CommerceLabor", "ministry: Interior");

        Assert.False(ContentRules.ApplyLiveEdit(Db, Config, target, yaml).Ok);
        Assert.Equal(Ministry.CommerceLabor, Db.GetCrisis(crisis.Id)!.Ministry);
    }

    [Fact]
    public void ContentEditLive_FindsTheOpenCrisisAndTheLiveProject()
    {
        var crisis = Post();
        Assert.Equal(crisis.Id, ContentRules.FindLive(Db, "dock", out _)!.RowId);

        Assert.Null(ContentRules.FindLive(Db, "works", out var refusal));
        Assert.NotNull(refusal);
        Db.ApplyEffects([new Effect("project.works", null)], Config, new EffectSource("test", "gm", "test"));
        var project = Db.GetLiveProjects().Single();
        var target = ContentRules.FindLive(Db, "works", out _)!;
        var yaml = TemplateCodec.Serialize(target.Current);

        Assert.True(ContentRules.ApplyLiveEdit(Db, Config, target, yaml.Replace("economy: 1", "economy: 2")).Ok);
        Assert.Equal(2.0, Db.GetProject(project.Id)!.ParsedEffects.Single().Value);
        Assert.False(ContentRules.ApplyLiveEdit(Db, Config, target, yaml.Replace("price: 6", "price: 9")).Ok);
        Assert.Equal(6m, Db.GetProject(project.Id)!.Price);
    }
}
