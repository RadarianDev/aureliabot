using Xunit;

namespace AureliaBot.Tests;

public class ColonialArcTests : IDisposable
{
    private readonly DatabaseFixture _fixture = new();
    private DatabaseService Db => _fixture.Db;
    private static GameConfig Config => DatabaseFixture.Config;

    public ColonialArcTests()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "content");
        var files = Directory.EnumerateFiles(root, "*.yaml", SearchOption.AllDirectories)
            .Select(path => new ImportFile(Path.GetFileName(path), File.ReadAllText(path)))
            .ToList();
        ContentRules.Import(Db, ContentRules.ImportContext(Db, Config), files);
        for (var pass = 0; pass < 6 && Db.GetTemplates(status: TemplateStatus.Draft).Count > 0; pass++)
            foreach (var row in Db.GetTemplates(status: TemplateStatus.Draft))
                ContentRules.MakeReady(Db, Config, row.Id);
        Assert.Empty(Db.GetTemplates(status: TemplateStatus.Draft));
        Db.StartGame(Config, DateTimeOffset.UtcNow);
    }

    public void Dispose() => _fixture.Dispose();

    private EffectContext Context => Db.BuildEffectContext(Config, allowCrisis: true);

    private void Run(string text)
    {
        var parsed = EffectParser.ParseText(text, Context);
        Assert.True(parsed.IsValid, string.Join("\n", parsed.Errors));
        Db.ApplyEffects(parsed.Effects, Config, new EffectSource("test", "test", "1"));
    }

    private void Apply(IReadOnlyList<Effect> effects) => Db.ApplyEffects(effects, Config, new EffectSource("test", "test", "1"));

    private T Template<T>(string id) where T : Template => Assert.IsType<T>(Db.GetParsedTemplate(id));

    private double Var(string name) => Db.GetVariables(Config).Single(v => v.Definition.Id == name).Value;

    private int Queued(string templateId) => Db.GetQueuedEvents().Count(e => e.TemplateId == templateId);

    private void QueueNow(string templateId) =>
        Db.QueueEvent(Template<EventTemplate>(templateId), EventScheduling.FireAtForDelay(Db.GetGameState(), 0), fromSchedule: false);

    private ResolutionReport Resolve() => Db.Resolve(Config, new ScriptedRandomSource(), 1, DateTimeOffset.UtcNow);

    [Fact]
    public void The_charter_founds_a_merchants_post_and_sends_the_Company_to_Bhartunia()
    {
        Run("open.colonies");
        Apply(Template<BillTemplate>("southern_company_charter").PassEffects);

        Assert.Equal(1, Var("colonial_foothold"));
        Assert.Equal(1, Queued("bhartunian_landing"));
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    public void The_terms_at_Seravad_set_the_foothold(int answer, int expected)
    {
        Run("var.colonial_foothold =1");
        Apply(Template<CrisisTemplate>("bhartunian_terms").Answers[answer].Effects);

        Assert.Equal(expected, Var("colonial_foothold"));
    }

    [Theory]
    [InlineData(3, "The Republic Protests")]
    [InlineData(0, "A Sharp Note from Arrosia")]
    public void Arrosia_protests_mildly_only_while_tied_down_in_Garaztia(int garaztiaWar, string expected)
    {
        Run($"var.garaztia_war ={garaztiaWar}");
        QueueNow("arrosian_protest_mild");
        QueueNow("arrosian_protest_sharp");

        var fired = Resolve().FiredEvents.Select(e => e.Title).Where(t => t.Contains("Arrosia") || t.Contains("Republic")).ToList();

        Assert.Equal([expected], fired);
        Assert.Equal(-1, Var("arrosia_goodwill"));
    }

    [Theory]
    [InlineData(1, "mugi_backs_aurelia", "mugi_backs_arrosia")]
    [InlineData(4, "mugi_backs_arrosia", "mugi_backs_aurelia")]
    public void Mugi_leans_the_way_the_South_Arpal_arc_ended(int southArpalEnding, string likely, string unlikely)
    {
        Run($"open.colonies, var.south_arpal_ending ={southArpalEnding}");
        var effects = Template<EventTemplate>("mugi_chooses").Effects;

        for (var i = 0; i < 300; i++) Apply(effects);

        Assert.True(Queued(likely) > 2 * Queued(unlikely), $"{likely} {Queued(likely)}, {unlikely} {Queued(unlikely)}");
    }

    [Fact]
    public void The_prologue_sets_the_tension_from_the_foothold_and_Arrosian_goodwill()
    {
        Run("var.colonial_foothold =3, var.arrosia_goodwill -2");
        Apply(Template<EventTemplate>("iratic_expedition").Effects);

        Assert.Equal(4, Var("meridia_tension"));
        Assert.Equal(IssueStatus.Open, Db.GetIssue("colonies")!.Status);
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(2, false)]
    public void Bon_offers_a_conference_unless_tension_has_already_reached_war(int tension, bool war)
    {
        Run($"open.colonies, var.meridia_tension ={tension}");
        Apply(Template<EventTemplate>("bon_meridia_offer").Effects);

        Assert.Equal(war ? 1 : 0, Queued("meridian_war"));
        Assert.Equal(war ? 0 : 1, Db.GetCrises().Count(c => c.TemplateId == "meridia_conference"));
    }

    [Theory]
    [InlineData(1, 60, 3)]
    [InlineData(0, 60, 2)]
    [InlineData(0, 30, 4)]
    public void The_peace_reads_the_sea_battle_and_the_fleet(int won, int favour, int ending)
    {
        Run($"open.colonies, var.sea_battle_won ={won}, favour ={favour}");
        QueueNow("meridian_peace");
        Resolve();
        Resolve();

        Assert.Equal(ending, Var("meridia_ending"));
        Assert.Equal(IssueStatus.Closed, Db.GetIssue("colonies")!.Status);
    }

    [Fact]
    public void Renouncing_Meridia_ends_the_dispute_and_stops_later_endings()
    {
        Run("open.colonies");
        Apply(Template<BillTemplate>("meridian_renunciation").PassEffects);
        Resolve();
        QueueNow("partition_of_bon");
        Resolve();

        Assert.Equal(5, Var("meridia_ending"));
        Assert.Equal(0, Var("colonial_foothold"));
        Assert.Equal(IssueStatus.Closed, Db.GetIssue("colonies")!.Status);
    }
}
