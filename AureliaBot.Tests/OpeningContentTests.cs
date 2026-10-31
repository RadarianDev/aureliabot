using Xunit;

namespace AureliaBot.Tests;

public class OpeningContentTests : IDisposable
{
    private readonly DatabaseFixture _fixture = new();
    private DatabaseService Db => _fixture.Db;

    public void Dispose() => _fixture.Dispose();

    private static GameConfig Config => DatabaseFixture.Config;

    private void ImportAndReadyStartingContent()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "content");
        var files = Directory.EnumerateFiles(root, "*.yaml", SearchOption.AllDirectories)
            .Select(path => new ImportFile(Path.GetFileName(path), File.ReadAllText(path)))
            .ToList();
        var outcomes = ContentRules.Import(Db, ContentRules.ImportContext(Db, Config), files);
        Assert.All(outcomes, o => Assert.True(o.Imported, string.Join("; ", o.Errors)));

        for (var pass = 0; pass < 5 && Db.GetTemplates(status: TemplateStatus.Draft).Count > 0; pass++)
            foreach (var row in Db.GetTemplates(status: TemplateStatus.Draft))
                ContentRules.MakeReady(Db, Config, row.Id);

        foreach (var row in Db.GetTemplates(status: TemplateStatus.Draft))
            Assert.Fail($"{row.Id}: {ContentRules.MakeReady(Db, Config, row.Id).Message}");
    }

    [Fact]
    public void TheOpeningEvent_IsQueuedForTheStart_AndFiresAtGmStart()
    {
        ImportAndReadyStartingContent();
        var queued = Assert.Single(Db.GetQueuedEvents(), e => e.TemplateId == "opening_1850");
        Assert.Equal(0, queued.FireAtResolution);
        var later = Db.GetQueuedEvents().Count - 1;

        var result = Db.StartGame(Config, DateTimeOffset.UtcNow);

        var fired = Assert.Single(result.Report!.FiredEvents);
        Assert.Equal("The King Summons the Assembly", fired.Title);
        Assert.Equal(4, Db.GetOpenIssues().Count);
        Assert.Equal(5, Db.GetBills().Count(b => b.Status == BillStatus.Available));
        Assert.Equal(later, Db.GetQueuedEvents().Count);
        Assert.All(Db.GetQueuedEvents(), e => Assert.True(e.FireAtResolution > 0));
        Assert.DoesNotContain(result.Report.Alerts, a => a.Contains("not"));
    }

    [Fact]
    public void TheOpeningEventSummary_ListsNewIssuesAndBills()
    {
        ImportAndReadyStartingContent();
        var report = Db.StartGame(Config, DateTimeOffset.UtcNow).Report!;
        var lines = EffectSummary.Lines(report.FiredEvents[0].Effects, Publisher.Names(Db, Config), Config.Constants.Display);

        Assert.Contains("New issue: Social Reform", lines);
        Assert.Contains("New bill: The Ten Hours Bill", lines);
        Assert.Equal(9, lines.Count);
    }

    [Fact]
    public void TheScheduledEvents_OpenTheLaterIssues_AndBringInTheirBills()
    {
        ImportAndReadyStartingContent();
        Db.StartGame(Config, DateTimeOffset.UtcNow);

        for (var turn = 0; turn < 12 && Db.GetQueuedEvents().Count > 0; turn++)
        {
            var report = Db.Resolve(Config, new ScriptedRandomSource(), 1, DateTimeOffset.UtcNow);
            Assert.DoesNotContain(report.Alerts, a => a.Contains("not open"));
        }

        Assert.Empty(Db.GetQueuedEvents());
        Assert.Equal(6, Db.GetOpenIssues().Count);
        var introduced = Db.GetBills().Select(b => b.Id).ToHashSet();
        var chained = new[] { "free_ports_act" };
        foreach (var bill in Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "content", "bills"), "*.yaml"))
        {
            var id = Path.GetFileNameWithoutExtension(bill);
            Assert.True(introduced.Contains(id) || chained.Contains(id), $"{id} is never introduced");
        }
    }

    [Fact]
    public void ClosingAnIssue_WithdrawsItsBills()
    {
        ImportAndReadyStartingContent();
        Db.StartGame(Config, DateTimeOffset.UtcNow);

        var result = Db.ApplyEffects([new Effect("close.social_reform", null)], Config, new EffectSource("test", "test", "1"));

        Assert.Empty(result.Warnings);
        Assert.Equal(IssueStatus.Closed, Db.GetIssue("social_reform")!.Status);
        Assert.Equal(BillStatus.Withdrawn, Db.GetBill("ten_hours_bill")!.Status);
    }

    [Fact]
    public void ABillForAClosedIssue_IsRefused()
    {
        ImportAndReadyStartingContent();
        Db.StartGame(Config, DateTimeOffset.UtcNow);
        Db.ApplyEffects([new Effect("close.social_reform", null)], Config, new EffectSource("test", "test", "1"));

        var result = Db.ApplyEffects([new Effect("bill.ten_hours_bill", null)], Config, new EffectSource("test", "test", "2"));

        Assert.Single(result.Warnings);
        Assert.Equal(BillStatus.Withdrawn, Db.GetBill("ten_hours_bill")!.Status);
    }

    [Fact]
    public void EventsFireInQueueOrder_AtTheStartOfTheirTurn_AndNeverOnCampaignDays()
    {
        ImportAndReadyStartingContent();
        Db.StartGame(Config, DateTimeOffset.UtcNow);
        var template = new EventTemplate("strike", "Strike", "Dockers walk out.", [new Effect("discontent.north_arpal", 1)], null);
        var second = new EventTemplate("riot", "Riot", "Bread riots.", [new Effect("discontent.north_arpal", 1)], null);
        var state = Db.GetGameState();
        Db.QueueEvent(template, EventScheduling.FireAtForDelay(state, 0), fromSchedule: false);
        Db.QueueEvent(second, EventScheduling.FireAtForDelay(state, 0), fromSchedule: false);
        var delayed = Db.QueueEvent(template, EventScheduling.FireAtForDelay(state, 1), fromSchedule: false);

        var campaignDay = Db.ResolveCampaignDay(Config, new ScriptedRandomSource(), 1, DateTimeOffset.UtcNow);
        Assert.Empty(campaignDay.FiredEvents);

        var report = Db.Resolve(Config, new ScriptedRandomSource(), 1, DateTimeOffset.UtcNow);
        Assert.Equal(["Strike", "Riot"], report.FiredEvents.Select(e => e.Title).Where(t => t is "Strike" or "Riot"));
        Assert.Equal(EventStatus.Queued, Db.GetEvent(delayed)!.Status);

        var next = Db.Resolve(Config, new ScriptedRandomSource(), 1, DateTimeOffset.UtcNow);
        Assert.Equal(["Strike"], next.FiredEvents.Select(e => e.Title).Where(t => t is "Strike" or "Riot"));
    }

    [Fact]
    public void ConditionEffects_GiveAndRemoveConditions()
    {
        ImportAndReadyStartingContent();
        Db.StartGame(Config, DateTimeOffset.UtcNow);

        Db.ApplyEffects([new Effect("condition.north_arpal.striking", 1)], Config, new EffectSource("t", "t", "1"));
        Assert.Contains("striking", Db.GetProvinceConditions()["north_arpal"]);

        Db.ApplyEffects([new Effect("condition.north_arpal.striking", -1)], Config, new EffectSource("t", "t", "2"));
        Assert.DoesNotContain("striking", Db.GetProvinceConditions()["north_arpal"]);
    }
}
