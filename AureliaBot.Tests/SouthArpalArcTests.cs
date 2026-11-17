using Microsoft.Data.Sqlite;
using Xunit;

namespace AureliaBot.Tests;

public class SouthArpalArcTests : IDisposable
{
    private readonly DatabaseFixture _fixture = new();
    private DatabaseService Db => _fixture.Db;
    private static GameConfig Config => DatabaseFixture.Config;

    public SouthArpalArcTests()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "content");
        var files = Directory.EnumerateFiles(root, "*.yaml", SearchOption.AllDirectories)
            .Select(path => new ImportFile(Path.GetFileName(path), File.ReadAllText(path)))
            .ToList();
        ContentRules.Import(Db, ContentRules.ImportContext(Db, Config), files);
        for (var pass = 0; pass < 100 && Db.GetTemplates(status: TemplateStatus.Draft).Count > 0; pass++)
            foreach (var row in Db.GetTemplates(status: TemplateStatus.Draft))
                ContentRules.MakeReady(Db, Config, row.Id);
        Db.StartGame(Config, DateTimeOffset.UtcNow);
        Run("open.free_trade, open.rearmament, open.arpal_question");
    }

    public void Dispose() => _fixture.Dispose();

    private static EffectSource Source() => new("test", "test", "1");

    private void Run(string text)
    {
        var parsed = EffectParser.ParseText(text, Db.BuildEffectContext(Config, allowCrisis: true));
        Assert.True(parsed.IsValid, string.Join("\n", parsed.Errors));
        Db.ApplyEffects(parsed.Effects, Config, Source());
    }

    private void Apply(IReadOnlyList<Effect> effects) => Db.ApplyEffects(effects, Config, Source());

    private double Var(string name) => Db.GetVariables(Config).Single(v => v.Definition.Id == name).Value;

    private ResolutionReport Resolve() => Db.Resolve(Config, new ScriptedRandomSource(), 1, DateTimeOffset.UtcNow);

    private void QueueNext(string templateId) =>
        Db.QueueEvent((EventTemplate)Db.GetParsedTemplate(templateId)!, Db.GetGameState().ResolutionNumber + 1, fromSchedule: false);

    private CrisisTemplate Crisis(string id) => (CrisisTemplate)Db.GetParsedTemplate(id)!;

    private BillTemplate Bill(string id) => (BillTemplate)Db.GetParsedTemplate(id)!;

    private int Queued(string templateId) => Db.GetQueuedEvents().Count(e => e.TemplateId == templateId);

    private static List<string> Fired(ResolutionReport report) => report.FiredEvents.Select(e => e.Title).ToList();

    private void Sql(string sql)
    {
        using var conn = new SqliteConnection(_fixture.ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private void MarkPosted(string templateId) => Sql($"UPDATE templates SET times_posted = 1 WHERE id = '{templateId}';");

    [Fact]
    public void Without_the_Tariff_Act_the_Diet_brings_the_plain_decrees_and_skips_the_grievance()
    {
        QueueNext("mugi_electoral_diet");
        Resolve();
        var report = Resolve();

        Assert.Contains("The Tsar's Decrees", Fired(report));
        Assert.DoesNotContain("The Tsar Answers the Tariff Act", Fired(report));
        Assert.Equal(1, Var("tariff_war"));
        Assert.Equal(1, Queued("kolgasi_market_closed"));
        Assert.Equal(1, Queued("mugi_remonstrance"));
    }

    [Fact]
    public void With_the_Tariff_Act_in_force_the_war_starts_at_one_and_the_grievance_decrees_fire()
    {
        Sql(@"INSERT INTO bills (id, template_id, issue_id, title, motion, lean, pass_text, pass_effects, status, available_resolution)
              VALUES ('tariff_act', 'tariff_act', 'free_trade', 'The Tariff Act', 'x', -1, 'x', '', 'Passed', 0);");
        QueueNext("mugi_electoral_diet");
        Resolve();
        Assert.Equal(1, Var("tariff_war"));

        var report = Resolve();

        Assert.Contains("The Tsar Answers the Tariff Act", Fired(report));
        Assert.DoesNotContain("The Tsar's Decrees", Fired(report));
        Assert.Equal(2, Var("tariff_war"));
    }

    [Fact]
    public void Passing_the_Kolgasi_Settlement_after_the_ultimatum_averts_the_war()
    {
        QueueNext("mugi_ultimatum");
        Resolve();
        Assert.Equal(1, Queued("arpal_war"));

        Apply(Bill("kolgasi_settlement").PassEffects);
        var report = Resolve();

        Assert.DoesNotContain("War with Mugi", Fired(report));
        Assert.Contains("South Arpal Is Given Up", Fired(report));
        Assert.Equal(2, Var("south_arpal_ending"));
        Assert.Equal(1, Queued("exiles_from_kolgasi"));
    }

    [Theory]
    [InlineData(1, 1, 4, "South Arpal Is Aurelian")]
    [InlineData(1, 0, 6, "A White Peace")]
    [InlineData(0, 0, 5, "The Peace of Defeat")]
    public void The_peace_talks_choose_the_ending_from_the_battle_records(int passesHeld, int kolgasiTaken, int ending, string title)
    {
        MarkPosted("arpal_war");
        Run($"var.passes_held ={passesHeld}, var.kolgasi_taken ={kolgasiTaken}, condition.ocidanten.blockaded 1");
        QueueNext("arpal_peace_talks");
        Resolve();
        Assert.False(Db.GetProvinceConditions()["ocidanten"].Contains("blockaded"));

        var report = Resolve();

        Assert.Contains(title, Fired(report));
        Assert.Equal(ending, Var("south_arpal_ending"));
        Assert.Equal(ending == 4, Db.GetProvinces().Any(p => p.Id == "south_arpal"));
    }

    [Theory]
    [InlineData(1, "The Tsar Returns the Treaty Unsealed", 0)]
    [InlineData(3, "The Treaty of Bon Is Sealed", 1)]
    public void The_commercial_treaty_is_sealed_only_once_the_Diet_presses_the_Tsar(int pressure, string title, int ending)
    {
        Run($"var.mugi_pressure ={pressure}, var.tariff_war =3");

        Apply(Bill("mugian_commercial_treaty").PassEffects);
        var report = Resolve();

        Assert.Contains(title, Fired(report));
        Assert.Equal(ending, Var("south_arpal_ending"));
        Assert.Equal(ending == 1 ? 0 : 3, Var("tariff_war"));
    }

    [Fact]
    public void The_remonstrance_is_skipped_once_the_arc_has_ended()
    {
        Run("var.south_arpal_ending =1");
        QueueNext("mugi_remonstrance");

        var report = Resolve();

        Assert.Empty(report.FiredEvents);
        Assert.Equal(0, Queued("senarstei_mission") + Queued("squadron_off_lanten"));
    }

    [Theory]
    [InlineData(60, 1)]
    [InlineData(40, 0)]
    public void The_Anzarian_fleet_sails_against_the_squadron_only_with_enough_favour(int favour, int demand)
    {
        Run($"favour ={favour}");

        Apply(Crisis("squadron_crisis").Answers[0].Effects);

        Assert.Equal(demand, Var("admirals_demand"));
    }

    [Fact]
    public void Answering_duty_with_duty_queues_the_cotton_petition_only_once()
    {
        var dutyForDuty = Crisis("mugian_duties").Answers[2].Effects;

        Apply(dutyForDuty);
        Apply(dutyForDuty);

        Assert.Equal(1, Queued("cotton_famine_petition"));
        Assert.Equal(2, Var("tariff_war"));
    }
}
