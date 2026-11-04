using Xunit;

namespace AureliaBot.Tests;

public class ContentRulesTests : IDisposable
{
    private readonly DatabaseFixture _fixture = new();
    private DatabaseService Db => _fixture.Db;
    private static GameConfig Config => DatabaseFixture.Config;

    public void Dispose() => _fixture.Dispose();

    private void Import(params string[] names)
    {
        var files = TemplateCodecTests.StartingFiles().Where(f => names.Contains(f.Name)).ToList();
        Assert.Equal(names.Length, files.Count);
        var outcomes = ContentRules.Import(Db, ContentRules.ImportContext(Db, Config), files);
        Assert.All(outcomes, o => Assert.True(o.Imported, string.Join("\n", o.Errors)));
    }

    private void ImportYaml(string name, string yaml)
    {
        var outcome = ContentRules.ImportOne(Db, ContentRules.ImportContext(Db, Config), new ImportFile(name, yaml));
        Assert.True(outcome.Imported, string.Join("\n", outcome.Errors));
    }

    [Fact]
    public void Bill_IsNotReady_UntilItsIssueIs()
    {
        Import("issues/social_reform.yaml", "bills/ten_hours_bill.yaml", "events/grimble_returns.yaml");
        Assert.True(ContentRules.MakeReady(Db, Config, "grimble_returns").Ok);

        var refused = ContentRules.MakeReady(Db, Config, "ten_hours_bill");
        Assert.False(refused.Ok);
        Assert.Contains("Its issue template `social_reform` (issue) is Draft", refused.Message);
        Assert.Equal(TemplateStatus.Draft, Db.GetTemplate("ten_hours_bill")!.Status);

        Assert.True(ContentRules.MakeReady(Db, Config, "social_reform").Ok);
        Assert.True(ContentRules.MakeReady(Db, Config, "ten_hours_bill").Ok);
        Assert.Equal(TemplateStatus.Ready, Db.GetTemplate("ten_hours_bill")!.Status);
    }

    [Fact]
    public void Bill_WithAMissingIssue_IsRefused()
    {
        Import("bills/ten_hours_bill.yaml");
        var refused = ContentRules.MakeReady(Db, Config, "ten_hours_bill");
        Assert.False(refused.Ok);
        Assert.Contains("no issue template `social_reform`", refused.Message);
    }

    [Fact]
    public void Event_ReferencingADraftBill_IsRefused()
    {
        Import("issues/social_reform.yaml", "bills/ten_hours_bill.yaml", "events/grimble_returns.yaml");
        Assert.True(ContentRules.MakeReady(Db, Config, "grimble_returns").Ok);
        ImportYaml("events/e.yaml", "id: e\ntitle: T\ntext: S\neffects: { open.social_reform: true, bill.ten_hours_bill: true }\n");
        Assert.True(ContentRules.MakeReady(Db, Config, "social_reform").Ok);

        var refused = ContentRules.MakeReady(Db, Config, "e");
        Assert.False(refused.Ok);
        Assert.Contains("`ten_hours_bill` (bill) is Draft", refused.Message);
        Assert.DoesNotContain("`social_reform`", refused.Message);

        Assert.True(ContentRules.MakeReady(Db, Config, "ten_hours_bill").Ok);
        Assert.True(ContentRules.MakeReady(Db, Config, "e").Ok);
    }

    [Fact]
    public void ReadinessProblems_ListEveryMissingOrUnreadyReference()
    {
        Import("issues/social_reform.yaml");
        var ev = new EventTemplate("e", "T", "S",
            [new Effect("open.social_reform", null), new Effect("project.nowhere", null), new Effect("open.social_reform", null)], null);
        var problems = ContentRules.ReadinessProblems(ev, Db.GetTemplate);
        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, p => p.Contains("`social_reform`") && p.Contains("Draft"));
        Assert.Contains(problems, p => p.Contains("`nowhere` does not exist"));
    }

    [Fact]
    public void StartingContent_CanAllBeMadeReady_AndQueuesNoEventByItself()
    {
        ContentRules.Import(Db, ContentRules.ImportContext(Db, Config), TemplateCodecTests.StartingFiles());
        for (var pass = 0; pass < 6 && Db.GetTemplates(status: TemplateStatus.Draft).Count > 0; pass++)
            foreach (var row in Db.GetTemplates(status: TemplateStatus.Draft))
                ContentRules.MakeReady(Db, Config, row.Id);

        foreach (var row in Db.GetTemplates(status: TemplateStatus.Draft))
            Assert.Fail($"{row.Id}: {ContentRules.MakeReady(Db, Config, row.Id).Message}");

        Assert.Empty(Db.GetQueuedEvents());
        Assert.Equal("at /gm start", ContentRules.DescribeFireAt(Db.GetGameState(), 0));
    }

    [Fact]
    public void Schedule_Spring1850BeforeStart_FiresAtStart()
    {
        var state = Db.GetGameState();
        Assert.False(state.Started);
        var (fireAt, problem) = ContentRules.ScheduleFireAt(state, new EventSchedule(Season.Spring, 1850, null));
        Assert.Null(problem);
        Assert.Equal(0, fireAt);
    }

    [Fact]
    public void Schedule_ADate_ConvertsToTheRightResolution()
    {
        var state = Db.GetGameState() with { Started = true, Season = Season.Summer, Year = 1851, ResolutionNumber = 5 };
        var (fireAt, problem) = ContentRules.ScheduleFireAt(state, new EventSchedule(Season.Autumn, 1852, null));
        Assert.Null(problem);
        Assert.Equal(10, fireAt);
        Assert.Equal("at the start of Autumn 1852", ContentRules.DescribeFireAt(state, fireAt!.Value));

        var next = ContentRules.ScheduleFireAt(state, new EventSchedule(Season.Autumn, 1851, null));
        Assert.Equal(6, next.FireAt);
    }

    [Fact]
    public void Schedule_AStartedOrPastTurn_IsRefused()
    {
        var state = Db.GetGameState() with { Started = true, Season = Season.Summer, Year = 1851, ResolutionNumber = 5 };
        var current = ContentRules.ScheduleFireAt(state, new EventSchedule(Season.Summer, 1851, null));
        Assert.Null(current.FireAt);
        Assert.Contains("Summer 1851, has already started", current.Problem);

        var past = ContentRules.ScheduleFireAt(state, new EventSchedule(Season.Spring, 1850, null));
        Assert.Null(past.FireAt);
        Assert.Contains("Spring 1850", past.Problem);
    }

    [Fact]
    public void Schedule_ADelay_CountsFromTheNextTurn()
    {
        var state = Db.GetGameState() with { Started = true, Season = Season.Summer, Year = 1851, ResolutionNumber = 5 };
        Assert.Equal(9, ContentRules.ScheduleFireAt(state, new EventSchedule(null, null, 3)).FireAt);
        Assert.Equal(2, ContentRules.ScheduleFireAt(Db.GetGameState(), new EventSchedule(null, null, 2)).FireAt);
    }

    [Fact]
    public void Ready_RefusesAPastSchedule_AndNamesTheTurn()
    {
        ImportYaml("events/late.yaml", "id: late\ntitle: T\ntext: S\nschedule: { season: Spring, year: 1850 }\n");
        Db.SaveGameState(Db.GetGameState() with { Started = true, Season = Season.Summer, Year = 1850, ResolutionNumber = 1 });

        var refused = ContentRules.MakeReady(Db, Config, "late");

        Assert.False(refused.Ok);
        Assert.Contains("Spring 1850, has already started", refused.Message);
        Assert.Empty(Db.GetQueuedEvents());
    }

    [Fact]
    public void ReplacingAReadyEvent_CancelsItsQueuedCopy_AndReadyRequeuesIt()
    {
        const string yaml = "id: later\ntitle: T\ntext: S\nschedule: { season: Autumn, year: 1850 }\n";
        ImportYaml("events/later.yaml", yaml);
        Assert.True(ContentRules.MakeReady(Db, Config, "later").Ok);
        Assert.Equal(2, Assert.Single(Db.GetQueuedEvents()).FireAtResolution);

        ImportYaml("events/later.yaml", yaml.Replace("Autumn", "Winter"));
        Assert.Empty(Db.GetQueuedEvents());
        Assert.Equal(TemplateStatus.Draft, Db.GetTemplate("later")!.Status);

        Assert.True(ContentRules.MakeReady(Db, Config, "later").Ok);
        Assert.Equal(3, Assert.Single(Db.GetQueuedEvents()).FireAtResolution);
    }

    [Fact]
    public void Remove_DeletesAnUnpostedDraft_AndArchivesTheRest()
    {
        Import("issues/social_reform.yaml", "issues/rearmament.yaml");
        Db.SetTemplateStatus("rearmament", TemplateStatus.Ready);

        Assert.True(ContentRules.Remove(Db, "social_reform").Ok);
        Assert.Null(Db.GetTemplate("social_reform"));

        Assert.True(ContentRules.Remove(Db, "rearmament").Ok);
        Assert.Equal(TemplateStatus.Archived, Db.GetTemplate("rearmament")!.Status);
    }

    [Fact]
    public void Remove_ArchivesAPostedDraft_AndCancelsItsScheduledEvent()
    {
        ImportYaml("events/later.yaml", "id: later\ntitle: T\ntext: S\nschedule: { delay: 2 }\n");
        Assert.True(ContentRules.MakeReady(Db, Config, "later").Ok);
        Db.SetTemplateStatus("later", TemplateStatus.Draft);

        var removed = ContentRules.Remove(Db, "later");

        Assert.Contains("Archived", removed.Message);
        Assert.Equal(TemplateStatus.Archived, Db.GetTemplate("later")!.Status);
        Assert.Empty(Db.GetQueuedEvents());
    }

    [Fact]
    public void Preview_RendersEveryStartingTemplate()
    {
        ContentRules.Import(Db, ContentRules.ImportContext(Db, Config), TemplateCodecTests.StartingFiles());
        foreach (var row in Db.GetTemplates())
        {
            var text = ContentPreview.Render(Db, Config, row.Id);
            Assert.Contains("What players will see", text);
        }
        Assert.Contains("## The Ten Hours Bill becomes law", ContentPreview.Render(Db, Config, "ten_hours_bill"));
        Assert.Contains("The King Summons the Assembly", ContentPreview.Render(Db, Config, "opening_1850"));
    }
}
