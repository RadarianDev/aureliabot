using Xunit;

namespace AureliaBot.Tests;

public class AdvancedEffectTests : AssemblyTestBase
{
    private static readonly GameConfig Game = new(Config.Constants, Config.Provinces, Config.Conditions,
        [new VariableDefinition { Id = "plague", Min = 0, Max = 10 }, new VariableDefinition { Id = "stage", Start = 2 }]);

    private static EffectSource Source() => new("test", "test", "1");

    private EffectContext Context => Db.BuildEffectContext(Game, allowCrisis: true);

    private EffectApplication Run(string text)
    {
        var parsed = EffectParser.ParseText(text, Context);
        Assert.True(parsed.IsValid, string.Join("\n", parsed.Errors));
        return Db.ApplyEffects(parsed.Effects, Game, Source());
    }

    private double Var(string name) => Db.GetVariables(Game).Single(v => v.Definition.Id == name).Value;

    private sealed class Scope(Dictionary<string, double> values) : IFormulaScope
    {
        public double Read(string reference) => values[reference];

        public IRandomSource Random { get; } = new ScriptedRandomSource(4, 2);
    }

    [Theory]
    [InlineData("1 + 2 * 3", 7)]
    [InlineData("(1 + 2) * 3", 9)]
    [InlineData("-2 * -3", 6)]
    [InlineData("10 % 4", 2)]
    [InlineData("a > 2 and b < 1", 1)]
    [InlineData("not (a > 2) or false", 0)]
    [InlineData("min(a, b, 7) + max(1, 2)", 2)]
    [InlineData("clamp(a * 10, 0, 5)", 5)]
    [InlineData("round(2.345, 2)", 2.35)]
    [InlineData("roll(2, 6)", 6)]
    [InlineData("a == 3 && b != 1", 1)]
    public void Formulas_Evaluate(string source, double expected)
    {
        var formula = Formula.TryParse(source, out var error);
        Assert.Null(error);
        Assert.Equal(expected, formula!.Evaluate(new Scope(new() { ["a"] = 3, ["b"] = 0 })), 6);
    }

    [Theory]
    [InlineData("1 +")]
    [InlineData("(1")]
    [InlineData("foo(1)")]
    [InlineData("1 $ 2")]
    [InlineData("")]
    public void BadFormulas_AreRejected(string source) => Assert.Null(Formula.TryParse(source, out _));

    [Fact]
    public void TextWithBlocks_RoundTrips()
    {
        const string text = "var.plague 1, economy {-0.5 * var.plague}, favour ={max(favour, 40)}, "
                            + "if {var.plague >= 3 and threat < 50} [discontent.all 1, if {favour < 30} [threat 2]] else [var.plague -1], "
                            + "random [3: threat 5 | 1: favour -2, var.stage =0], "
                            + "each province where {condition.it.striking} [discontent.it 1], "
                            + "each faction by {popularity.it} top 2 [popularity.it -3]";
        var parsed = EffectParser.ParseText(text, Context);
        Assert.True(parsed.IsValid, string.Join("\n", parsed.Errors));
        var formatted = EffectParser.Format(parsed.Effects);
        Assert.Equal(formatted, EffectParser.Format(EffectParser.Parse(formatted)));
        Assert.Equal(7, parsed.Effects.Count);
    }

    [Fact]
    public void Variables_ChangeAreClampedAndCanBeSet()
    {
        Assert.Equal(2, Var("stage"));
        Run("var.plague 4, var.stage =7");
        Assert.Equal(4, Var("plague"));
        Assert.Equal(7, Var("stage"));
        Run("var.plague 20");
        Assert.Equal(10, Var("plague"));
    }

    [Fact]
    public void Conditions_PickTheirBranch()
    {
        var threat = Db.GetGameState().Threat;
        Run("if {var.plague >= 3} [threat 10] else [var.plague 3]");
        Assert.Equal(3, Var("plague"));
        Assert.Equal(threat, Db.GetGameState().Threat, 6);

        var applied = Run("if {var.plague >= 3} [threat 10] else [var.plague 3]");
        Assert.Equal(threat + 10, Db.GetGameState().Threat, 6);
        Assert.Equal("threat", Assert.Single(applied.Applied).Key);
    }

    [Fact]
    public void FormulaAmounts_AndSetting_UseTheCurrentState()
    {
        Run("var.plague 4");
        var economy = Db.GetGameState().Economy;
        var applied = Run("economy {-0.5 * var.plague}, favour =40");

        Assert.Equal(economy - 2, Db.GetGameState().Economy, 6);
        Assert.Equal(40, Db.GetGameState().Favour, 6);
        Assert.Equal(-2, applied.Applied[0].Amount, 6);
        Assert.Null(applied.Applied[0].Formula);
    }

    [Fact]
    public void ARandomBlock_AppliesOneOutcome()
    {
        var threat = Db.GetGameState().Threat;
        var applied = Run("random [1: threat 5 | 1: threat 7]");
        var change = Db.GetGameState().Threat - threat;
        Assert.True(Math.Abs(change - 5) < 1e-6 || Math.Abs(change - 7) < 1e-6);
        Assert.Single(applied.Applied);
    }

    [Fact]
    public void Each_GoesThroughTheMatchingProvinces()
    {
        Run("condition.aurenten.striking 1");
        var before = Db.GetProvinces().ToDictionary(p => p.Id, p => p.Discontent);

        Run("each province where {condition.it.striking} [discontent.it 1]");

        foreach (var province in Db.GetProvinces())
            Assert.Equal(before[province.Id] + (province.Id == "aurenten" ? 1 : 0), province.Discontent, 6);
    }

    [Fact]
    public void Each_RanksAndPicksTheTop()
    {
        Sql("UPDATE factions SET popularity = 30 WHERE user_id = $id;", ("$id", Opposer.ToString()));
        var before = Db.GetActiveFactions().ToDictionary(f => f.UserId, f => f.Popularity);

        Run("each faction by {popularity.it} top 1 [popularity.it -3]");

        foreach (var faction in Db.GetActiveFactions())
            Assert.Equal(before[faction.UserId] - (faction.UserId == Opposer ? 3 : 0), faction.Popularity, 6);
    }

    [Theory]
    [InlineData("var.nope 1")]
    [InlineData("economy {var.nope}")]
    [InlineData("discontent.it 1")]
    [InlineData("if {mystery > 1} [economy 1]")]
    [InlineData("each province [discontent.nowhere 1]")]
    [InlineData("each county [economy 1]")]
    [InlineData("open.labour =1")]
    [InlineData("if {1} [economy 1")]
    public void Mistakes_AreRejected(string text) => Assert.False(EffectParser.ParseText(text, Context).IsValid);

    [Fact]
    public void YamlBlocks_ParseAndRoundTrip()
    {
        const string yaml = """
            id: plague_spreads
            title: The Plague Spreads
            text: Fever in the marshes.
            effects:
              - { var.plague: 1, economy: "-0.2 * var.plague" }
              - if: "var.plague >= 3"
                then:
                  - { discontent.all: 1 }
                  - random:
                      - weight: 2
                        effects: { threat: 3 }
                      - { favour: -1 }
                else: { favour: "=max(favour, 20)" }
              - each: province
                where: "discontent.it > 5"
                by: "discontent.it"
                top: 2
                do: { discontent.it: -1 }
            """;
        var parsed = TemplateCodec.Parse(yaml, "plague_spreads.yaml", Context);
        Assert.True(parsed.IsValid, string.Join("\n", parsed.Errors.Select(e => e.Message)));
        var ev = Assert.IsType<EventTemplate>(parsed.Template);

        var again = TemplateCodec.Parse(TemplateCodec.Serialize(ev), "plague_spreads.yaml", Context);
        Assert.True(again.IsValid, string.Join("\n", again.Errors.Select(e => e.Message)));
        Assert.Equal(EffectParser.Format(ev.Effects), EffectParser.Format(Assert.IsType<EventTemplate>(again.Template).Effects));
    }

    [Fact]
    public void Previews_ShowEveryBranch_AndPostsOnlyWhatHappened()
    {
        var effects = EffectParser.ParseText("if {var.plague >= 3} [threat 10] else [favour -2]", Context).Effects;
        var names = Publisher.Names(Db, Game);

        var preview = EffectSummary.Lines(effects, names, Game.Constants.Display, preview: true);
        Assert.Equal(4, preview.Count);
        Assert.Contains("var.plague >= 3", preview[0]);

        var applied = Db.ApplyEffects(effects, Game, Source()).Applied;
        Assert.Single(EffectSummary.Lines(applied, names, Game.Constants.Display));
    }

    [Fact]
    public void AnEndlessFormulaError_SkipsTheEffectWithAWarning()
    {
        var economy = Db.GetGameState().Economy;
        var result = Run("economy {1 / (var.plague - var.plague)}");
        Assert.Single(result.Warnings);
        Assert.Equal(economy, Db.GetGameState().Economy, 6);
    }
}
