using Microsoft.Data.Sqlite;
using Xunit;

namespace AureliaBot.Tests;

public class GaraztianWarArcTests : IDisposable
{
    private readonly DatabaseFixture _fixture = new();
    private DatabaseService Db => _fixture.Db;
    private static GameConfig Config => DatabaseFixture.Config;

    public GaraztianWarArcTests()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "content");
        var files = Directory.EnumerateFiles(root, "*.yaml", SearchOption.AllDirectories)
            .Select(path => new ImportFile(Path.GetFileName(path), File.ReadAllText(path)))
            .ToList();
        ContentRules.Import(Db, ContentRules.ImportContext(Db, Config), files);
        for (var pass = 0; pass < 100 && Db.GetTemplates(status: TemplateStatus.Draft).Count > 0; pass++)
            foreach (var row in Db.GetTemplates(status: TemplateStatus.Draft))
                ContentRules.MakeReady(Db, Config, row.Id);
        Assert.Empty(Db.GetTemplates(status: TemplateStatus.Draft));
    }

    public void Dispose() => _fixture.Dispose();

    private void Start() => Db.StartGame(Config, DateTimeOffset.UtcNow);

    private void Run(string text)
    {
        var parsed = EffectParser.ParseText(text, Db.BuildEffectContext(Config, allowCrisis: true));
        Assert.True(parsed.IsValid, string.Join("\n", parsed.Errors));
        Apply(parsed.Effects);
    }

    private void Apply(IReadOnlyList<Effect> effects) =>
        Db.ApplyEffects(effects, Config, new EffectSource("test", "test", Guid.NewGuid().ToString()));

    private ResolutionReport Resolve() => Db.Resolve(Config, new ScriptedRandomSource(), 1, DateTimeOffset.UtcNow);

    private double Var(string name) => Db.GetVariables(Config).Single(v => v.Definition.Id == name).Value;

    private List<GameEvent> QueuedByHand(string templateId) =>
        Db.GetQueuedEvents().Where(e => e.TemplateId == templateId && !e.FromSchedule).ToList();

    private void PassBill(string id)
    {
        Run($"bill.{id}");
        var bill = (BillTemplate)Db.GetParsedTemplate(id)!;
        Sql("UPDATE bills SET status = 'Passed' WHERE id = $id;", ("$id", id));
        Apply(bill.PassEffects);
    }

    private void Sql(string sql, params (string Name, object? Value)[] parameters)
    {
        using var conn = new SqliteConnection(_fixture.ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void The_kings_visit_and_the_default_are_scheduled_for_1857_and_1858()
    {
        var state = Db.GetGameState();
        var scheduled = Db.GetQueuedEvents().Where(e => e.FromSchedule).ToDictionary(e => e.TemplateId, e => e.FireAtResolution);

        Assert.Equal("at the start of Summer 1857", ContentRules.DescribeFireAt(state, scheduled["garaztian_king_visit"]));
        Assert.Equal("at the start of Summer 1858", ContentRules.DescribeFireAt(state, scheduled["garaztian_default"]));
    }

    [Fact]
    public void Lending_the_King_his_interest_puts_the_default_off_two_seasons()
    {
        Start();
        Run("open.garaztian_question");
        var crisis = (CrisisTemplate)Db.GetParsedTemplate("garaztian_kings_request")!;

        Apply(crisis.Answers[0].Effects);

        Assert.Equal(1, Var("garaztia_loan"));
        Assert.DoesNotContain(Db.GetQueuedEvents(), e => e.TemplateId == "garaztian_default" && e.FromSchedule);
        var delayed = Assert.Single(QueuedByHand("garaztian_default"));
        Assert.Equal(Db.GetGameState().ResolutionNumber + 6, delayed.FireAtResolution);
    }

    [Fact]
    public void The_guarantee_before_the_collection_ends_in_the_grateful_neighbour()
    {
        Start();
        Run("event.garaztian_default");
        Resolve();
        Assert.Equal(1, Var("garaztia_war"));
        Assert.Single(QueuedByHand("arrosian_ultimatum"));

        PassBill("garaztian_loan");
        Resolve();

        Assert.Equal(1, Var("garaztia_ending"));
        Assert.Equal(0, Var("garaztia_war"));
        Assert.Equal(3, Var("garaztia_goodwill"));
        Assert.Empty(QueuedByHand("arrosian_ultimatum"));
        Assert.Empty(QueuedByHand("the_collection"));
        Assert.Equal(IssueStatus.Closed, Db.GetIssue("garaztian_question")!.Status);
    }

    [Fact]
    public void The_guarantee_after_the_collection_pays_off_the_bailiffs_and_stops_the_war()
    {
        Start();
        Run("open.garaztian_question, var.garaztia_war =3, event.valleys_choose 3");

        PassBill("garaztian_loan");
        Resolve();

        Assert.Equal(2, Var("garaztia_ending"));
        Assert.Equal(0, Var("garaztia_war"));
        Assert.Empty(QueuedByHand("valleys_choose"));
    }

    [Theory]
    [InlineData(2, 0, 5)]
    [InlineData(1, 2, 6)]
    [InlineData(1, 1, 3)]
    [InlineData(0, 0, 3)]
    public void The_peace_at_Bon_follows_the_valleys_and_Garaztias_goodwill(int valleys, int goodwill, int ending)
    {
        Start();
        Run($"open.garaztian_question, var.garaztia_war =4, var.valleys ={valleys}, var.garaztia_goodwill ={goodwill}, event.garaztian_war_ends");

        Resolve();
        Resolve();

        Assert.Equal(ending, Var("garaztia_ending"));
        Assert.Equal(0, Var("garaztia_war"));
    }

    [Fact]
    public void The_Escas_Convention_makes_Aurelia_the_creditors_partner_whatever_the_valleys_did()
    {
        Start();
        Run("open.garaztian_question, var.garaztia_war =5, var.valleys =2");
        PassBill("arrosian_convention");
        Run("event.garaztian_war_ends");

        Resolve();
        Resolve();

        Assert.Equal(4, Var("garaztia_ending"));
        Assert.Equal(3, Var("arrosia_goodwill"));
    }

    [Theory]
    [InlineData(3, 1)]
    [InlineData(-2, 2)]
    public void The_valleys_are_certain_only_at_the_extremes_of_goodwill(int goodwill, int valleys)
    {
        Start();
        Run($"open.garaztian_question, var.garaztia_war =3, var.garaztia_goodwill ={goodwill}, event.valleys_choose");

        Resolve();
        Resolve();

        Assert.Equal(valleys, Var("valleys"));
        Assert.Single(QueuedByHand("garaztian_war_ends"));
    }
}
