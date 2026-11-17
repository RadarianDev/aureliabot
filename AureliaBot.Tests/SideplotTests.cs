using Xunit;

namespace AureliaBot.Tests;

public class SideplotTests : IDisposable
{
    private readonly DatabaseFixture _fixture = new();
    private DatabaseService Db => _fixture.Db;
    private static GameConfig Config => DatabaseFixture.Config;

    public void Dispose() => _fixture.Dispose();

    public SideplotTests()
    {
        var outcomes = ContentRules.Import(Db, ContentRules.ImportContext(Db, Config), TemplateCodecTests.StartingFiles());
        Assert.All(outcomes, o => Assert.True(o.Imported, string.Join("; ", o.Errors)));
        for (var pass = 0; pass < 100 && Db.GetTemplates(status: TemplateStatus.Draft).Count > 0; pass++)
            foreach (var row in Db.GetTemplates(status: TemplateStatus.Draft))
                ContentRules.MakeReady(Db, Config, row.Id);
        Assert.Empty(Db.GetTemplates(status: TemplateStatus.Draft));

        Db.StartGame(Config, DateTimeOffset.UtcNow);
        Run("open.social_reform, open.electoral_reform, open.integration, open.public_works, open.free_trade");
    }

    private void Run(string text)
    {
        var parsed = EffectParser.ParseText(text, Db.BuildEffectContext(Config, allowCrisis: true));
        Assert.True(parsed.IsValid, string.Join("\n", parsed.Errors));
        Db.ApplyEffects(parsed.Effects, Config, new EffectSource("test", "test", "1"));
    }

    private long Fire(string eventId)
    {
        var id = Db.QueueEvent((EventTemplate)Db.GetParsedTemplate(eventId)!,
            EventScheduling.FireAtForDelay(Db.GetGameState(), 0), fromSchedule: false);
        Db.Resolve(Config, new ScriptedRandomSource(), 1, DateTimeOffset.UtcNow);
        return id;
    }

    private bool BillAvailable(string id) => Db.GetBill(id)?.Status == BillStatus.Available;

    private bool CrisisPosted(string id) => Db.GetCrises().Any(c => c.TemplateId == id);

    private bool ProjectStarted(string id) => Db.GetProjects().Any(p => p.TemplateId == id);

    [Theory]
    [InlineData("iracten_match_works", "factory_inspection_act")]
    [InlineData("sorales_burial_club", "friendly_societies_act")]
    [InlineData("cobbler_member", "payment_of_members_act")]
    [InlineData("tovres_navvy_poll", "residence_qualification_act")]
    [InlineData("agrines_poacher_case", "union_excise_act,manorial_courts_act")]
    [InlineData("fidacren_telegraph", "telegraph_act")]
    [InlineData("sagtias_toll_gates", "turnpike_trusts_act")]
    [InlineData("lanten_foreign_hulls", "navigation_acts_repeal")]
    public void A_sideplot_event_brings_in_its_bills(string eventId, string bills)
    {
        Fire(eventId);

        foreach (var bill in bills.Split(','))
            Assert.True(BillAvailable(bill), bill);
    }

    [Theory]
    [InlineData("iracten_match_works", "phossy_jaw_inquiry")]
    [InlineData("sorales_burial_club", "burial_club_losses")]
    [InlineData("tovres_navvy_poll", "tovres_poll_inquiry")]
    [InlineData("sagtias_toll_gates", "toll_gate_riots")]
    [InlineData("escas_cholera_ship", "escas_quarantine")]
    [InlineData("ironclad_trials", "ironclad_contract")]
    public void A_sideplot_event_posts_its_crisis(string eventId, string crisis)
    {
        Fire(eventId);

        Assert.True(CrisisPosted(crisis), crisis);
    }

    [Theory]
    [InlineData("arpal_passes_petition", "arpal_pass_forts")]
    [InlineData("lanten_batteries_alarm", "lanten_shore_batteries")]
    [InlineData("gares_refugee_roads", "gares_relief_road")]
    [InlineData("kolgasi_road_survey", "kolgasi_road")]
    public void An_arc_tied_event_skips_itself_outside_its_situation(string eventId, string project)
    {
        var queued = Fire(eventId);

        Assert.Equal(EventStatus.Cancelled, Db.GetEvent(queued)!.Status);
        Assert.False(ProjectStarted(project));
    }

    [Theory]
    [InlineData("arpal_passes_petition", "var.tariff_war =3", "arpal_pass_forts")]
    [InlineData("lanten_batteries_alarm", "var.tariff_war =4", "lanten_shore_batteries")]
    [InlineData("gares_refugee_roads", "var.garaztia_war =3", "gares_relief_road")]
    [InlineData("gares_refugee_roads", "condition.provencie_gares.refugees 1", "gares_relief_road")]
    public void An_arc_tied_event_starts_its_project_in_its_situation(string eventId, string state, string project)
    {
        Run(state);

        Fire(eventId);

        Assert.True(ProjectStarted(project), project);
    }

    [Fact]
    public void The_relief_road_clears_the_refugee_camps_when_it_finishes()
    {
        Run("condition.provencie_gares.refugees 1");
        Fire("gares_refugee_roads");

        for (var turn = 0; turn < 3; turn++)
        {
            Run("progress.gares_relief_road 14");
            Db.Resolve(Config, new ScriptedRandomSource(), 1, DateTimeOffset.UtcNow);
        }

        Assert.Equal(ProjectStatus.Finished, Db.GetProjects().Single(p => p.TemplateId == "gares_relief_road").Status);
        Assert.DoesNotContain("refugees", Db.GetProvinceConditions()["provencie_gares"]);
    }

    [Fact]
    public void Every_bill_is_made_available_by_some_content()
    {
        var content = TemplateCodecTests.StartingFiles().Select(f => f.Yaml).ToList();
        var releasedByTheGm = new[] { "senate_bill" };
        foreach (var bill in TemplateCodecTests.StartingFiles().Where(f => f.Name.StartsWith("bills/")))
        {
            var id = Path.GetFileNameWithoutExtension(bill.Name);
            Assert.True(releasedByTheGm.Contains(id) || content.Any(yaml => yaml.Contains($"bill.{id}:")), $"{id} is never introduced");
        }
    }
}
