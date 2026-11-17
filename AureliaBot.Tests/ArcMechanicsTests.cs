using Xunit;

namespace AureliaBot.Tests;

public class ArcMechanicsTests : AssemblyTestBase
{
    private static EffectSource Source() => new("test", "test", "1");

    private EffectContext Context => Db.BuildEffectContext(Config, allowCrisis: true);

    private EffectApplication Run(string text)
    {
        var parsed = EffectParser.ParseText(text, Context);
        Assert.True(parsed.IsValid, string.Join("\n", parsed.Errors));
        return Db.ApplyEffects(parsed.Effects, Config, Source());
    }

    private double Var(string name) => Db.GetVariables(Config).Single(v => v.Definition.Id == name).Value;

    private EventTemplate ParseEvent(string yaml)
    {
        var parsed = TemplateCodec.Parse(yaml, "test.yaml", Context);
        Assert.True(parsed.IsValid, string.Join("\n", parsed.Errors.Select(e => e.Message)));
        return Assert.IsType<EventTemplate>(parsed.Template);
    }

    [Fact]
    public void Random_weights_can_be_formulas_read_when_the_block_runs()
    {
        Run("var.tariff_war =3");

        Run("random [{var.tariff_war}: var.mugi_pressure 1 | {var.tariff_war - 3}: var.mugi_pressure 5]");
        Assert.Equal(1, Var("mugi_pressure"));

        Run("var.tariff_war =0, random [{var.tariff_war}: var.mugi_pressure 1 | 2: var.mugi_pressure 4]");
        Assert.Equal(5, Var("mugi_pressure"));
    }

    [Fact]
    public void A_random_block_whose_weights_are_all_zero_does_nothing_and_warns()
    {
        var result = Run("random [{var.tariff_war}: var.mugi_pressure 1]");

        Assert.Single(result.Warnings);
        Assert.Equal(0, Var("mugi_pressure"));
    }

    [Fact]
    public void Formula_weights_survive_text_and_yaml_round_trips_and_preview_as_weights()
    {
        const string text = "random [{max(1, 1 + var.mugi_pressure)}: var.tariff_war 1 | 2: var.tariff_war 2]";
        var effects = EffectParser.ParseText(text, Context).Effects;
        Assert.Equal(text, EffectParser.Format(effects));

        var ev = ParseEvent("""
            id: remonstrance
            title: The Diet Remonstrates
            text: The Tsar answers it in person.
            effects:
              - random:
                  - weight: "max(1, 1 + var.mugi_pressure)"
                    effects: { var.tariff_war: -1 }
                  - weight: "max(1, var.tariff_war)"
                    effects: { var.tariff_war: 1 }
            """);
        var again = ParseEvent(TemplateCodec.Serialize(ev));
        Assert.Equal(EffectParser.Format(ev.Effects), EffectParser.Format(again.Effects));

        var names = new EffectNames(id => id, id => id, id => id, id => id, id => id, id => id, id => id, id => id);
        var lines = EffectSummary.Lines(effects, names, C.Display, preview: true);
        Assert.Contains(lines, l => l.Contains("weight `max(1, 1 + var.mugi_pressure)`"));
    }

    [Fact]
    public void Law_reads_whether_a_bill_has_passed()
    {
        Sql("INSERT INTO templates (id, kind, status, yaml, created_at, updated_at, times_posted) VALUES ('ten_hours', 'Bill', 'Ready', '', 0, 0, 1);");
        Run("if {law.ten_hours} [var.tariff_war 1] else [var.mugi_pressure 1]");
        Assert.Equal((0, 1), (Var("tariff_war"), Var("mugi_pressure")));

        Sql("UPDATE bills SET status = 'Passed' WHERE id = 'ten_hours';");
        Run("if {law.ten_hours} [var.tariff_war 1] else [var.mugi_pressure 1]");
        Assert.Equal((1, 1), (Var("tariff_war"), Var("mugi_pressure")));

        Assert.False(EffectParser.ParseText("if {law.no_such_bill} [threat 1]", Context).IsValid);
    }

    [Fact]
    public void Posted_reads_how_often_a_template_has_been_posted()
    {
        Sql("INSERT INTO templates (id, kind, status, yaml, created_at, updated_at, times_posted) VALUES ('musters', 'Crisis', 'Ready', '', 0, 0, 2);");

        Run("var.tariff_war {posted.musters}");

        Assert.Equal(2, Var("tariff_war"));
        Assert.False(EffectParser.ParseText("var.tariff_war {posted.no_such_template}", Context).IsValid);
    }

    [Fact]
    public void An_event_whose_when_is_false_when_due_is_skipped_with_an_alert()
    {
        var ev = ParseEvent("""
            id: empty_berths
            title: Empty Berths at Lanten
            text: Mugian hulls carry the western trade past Lanten.
            when: "var.tariff_war >= 2"
            effects: { var.mugi_pressure: 1 }
            """);
        var eventId = Db.QueueEvent(ev, Db.GetGameState().ResolutionNumber + 1, false);

        var report = Resolve();

        Assert.Empty(report.FiredEvents);
        Assert.Contains(report.Alerts, a => a.Contains("Empty Berths at Lanten") && a.Contains("skipped"));
        Assert.Equal(EventStatus.Cancelled, Db.GetEvent(eventId)!.Status);
        Assert.Equal(0, Var("mugi_pressure"));
    }

    [Fact]
    public void An_event_whose_when_holds_fires_and_its_when_survives_the_round_trip()
    {
        var ev = ParseEvent("""
            id: empty_berths
            title: Empty Berths at Lanten
            text: Mugian hulls carry the western trade past Lanten.
            when: "var.tariff_war >= 2"
            effects: { var.mugi_pressure: 1 }
            """);
        Assert.Equal("var.tariff_war >= 2", ParseEvent(TemplateCodec.Serialize(ev)).When?.Source);
        Run("var.tariff_war =2");
        Db.QueueEvent(ev, Db.GetGameState().ResolutionNumber + 1, false);

        var report = Resolve();

        Assert.Single(report.FiredEvents);
        Assert.Equal(1, Var("mugi_pressure"));
    }

    [Fact]
    public void A_when_with_an_unknown_reference_is_refused_on_import()
    {
        var parsed = TemplateCodec.Parse("""
            id: broken
            title: Broken
            text: Nothing.
            when: "var.no_such_variable > 1"
            """, "broken.yaml", Context);

        Assert.False(parsed.IsValid);
    }
}
