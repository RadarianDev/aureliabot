using Xunit;

namespace AureliaBot.Tests;

public class AnnexTests : AssemblyTestBase
{
    private const string SouthArpal = "south_arpal";

    private static EffectSource Source() => new("test", "test", "1");

    private EffectApplication Apply(string text) => Db.ApplyEffects(EffectParser.ParseText(text).Effects, Config, Source());

    private bool InPlay() => Db.GetProvinces().Any(p => p.Id == SouthArpal);

    [Fact]
    public void An_unannexed_province_stays_out_of_the_started_game()
    {
        Assert.False(InPlay());
        Assert.DoesNotContain(Db.ProvincesInPlay(Config), p => p.Id == SouthArpal);
        Assert.Null(ProvinceAutocomplete.Find(Db, Config, "South Arpal"));
        Assert.DoesNotContain(Db.BuildElectionInput(Config).Provinces, p => p.Id == SouthArpal);
    }

    [Fact]
    public void Effects_aimed_at_it_before_annexation_do_nothing()
    {
        var result = Apply("discontent.south_arpal 2, condition.south_arpal.lawless 1");

        Assert.Equal(2, result.Warnings.Count);
        Assert.False(InPlay());
        Assert.Empty(Db.GetProvinceConditions().GetValueOrDefault(SouthArpal, []));
    }

    [Fact]
    public void Annexing_brings_it_in_with_its_starting_discontent_and_conditions()
    {
        var result = Apply("annex.south_arpal");

        Assert.Empty(result.Warnings);
        var province = Db.GetProvince(SouthArpal);
        Assert.NotNull(province);
        Assert.Equal(6, province.Discontent);
        Assert.Equal(0.9, province.Population);
        Assert.Equal(["frontier", "rugged", "farmland", "mugian_quarter"], Db.GetProvinceConditions()[SouthArpal]);
        Assert.Equal(Config.Provinces.Count, Db.ProvincesInPlay(Config).Count);
        Assert.Equal(SouthArpal, ProvinceAutocomplete.Find(Db, Config, "South Arpal")?.Id);
        Assert.Contains(Db.BuildElectionInput(Config).Provinces, p => p.Id == SouthArpal);
        Assert.Equal(Config.Provinces.Count, Standings.ProvinceLeaders(Db, Config).Count);
    }

    [Fact]
    public void Annexing_twice_is_skipped_with_a_warning()
    {
        Apply("annex.south_arpal");
        Apply("discontent.south_arpal -1");

        var result = Apply("annex.south_arpal");

        Assert.Single(result.Warnings);
        Assert.Equal(5, Db.GetProvince(SouthArpal)!.Discontent);
    }

    [Fact]
    public void Annexation_is_announced_in_the_effect_summary()
    {
        var names = new EffectNames(id => Config.Province(id)?.Name ?? id, id => id, id => id, id => id, id => id, id => id, id => id, id => id);

        var lines = EffectSummary.Lines(EffectParser.ParseText("annex.south_arpal").Effects, names, C.Display);

        Assert.Equal(["South Arpal joins the kingdom"], lines);
    }

    [Fact]
    public void The_senate_counts_its_seats_only_once_it_is_annexed()
    {
        var before = ElectionCalculator.SenateSeats(Config, Db.BuildElectionInput(Config));
        Apply("annex.south_arpal");
        var after = ElectionCalculator.SenateSeats(Config, Db.BuildElectionInput(Config));

        Assert.Equal(90, before.Values.Sum());
        Assert.Equal(90 + Config.Province(SouthArpal)!.SenateSeats, after.Values.Sum());
    }

    [Fact]
    public void The_map_switches_to_the_south_arpal_set_once_it_is_annexed()
    {
        Assert.Null(Db.MapVariant(Config));
        Apply("annex.south_arpal");
        Assert.Equal(SouthArpal, Db.MapVariant(Config));

        var colours = new Dictionary<string, string> { [Config.Province(SouthArpal)!.MapKey] = "#C0392B" };
        using var stream = ElectionPublisher.RenderMap(colours, Db.MapVariant(Config));
        using var map = SkiaSharp.SKBitmap.Decode(stream);

        Assert.Equal(561, map.Height);
    }

    [Fact]
    public void The_south_arpal_map_has_every_province_in_its_index_image()
    {
        using var index = SkiaSharp.SKBitmap.Decode(Path.Combine(ProvinceMapGenerator.Folder(SouthArpal), "provinces.png"));
        var keys = index.Pixels.Where(p => p.Alpha == 255).Select(p => $"#{p.Red:X2}{p.Green:X2}{p.Blue:X2}").ToHashSet();

        Assert.All(Config.Provinces, p => Assert.Contains(p.MapKey.ToUpperInvariant(), keys));
    }

    [Fact]
    public void The_arc_variables_and_condition_are_configured()
    {
        Assert.Equal(6, Config.Variable("tariff_war")!.Max);
        Assert.Equal(6, Config.Variable("mugi_pressure")!.Max);
        Assert.Equal(1, Config.Condition("mugian_quarter")!.DiscontentRest);
        Assert.Equal(0.3, Config.Condition("frontier")!.BiasOn("arpal_question"));
    }
}
