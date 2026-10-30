using Xunit;

namespace AureliaBot.Tests;

public class EffectSummaryTests
{
    private const string Up = "<:up:1>";
    private const string Down = "<:down:2>";

    private static readonly DisplayConstants Display = new() { UpEmoji = Up, DownEmoji = Down };

    private static readonly EffectNames Names = new(
        Province: id => id switch { "north_arpal" => "North Arpal", "iracterie" => "Iracterie", _ => id },
        Issue: id => id switch { "labour" => "The Condition of Labour", "free_trade" => "Free Trade", _ => id },
        Condition: id => id switch { "strike" => "Strike", "lawless" => "Lawless", _ => id },
        Faction: id => $"<@{id}>",
        IssueTemplate: id => id == "public_health" ? "Public Health" : id,
        BillTemplate: id => id == "ten_hours" ? "The Ten Hours Bill" : id,
        ProjectTemplate: id => id == "royal_railway" ? "The Royal Railway" : id,
        CrisisTemplate: id => id == "dock_strike_carden" ? "The Carden Dock Strike" : id);

    private static IReadOnlyList<string> Lines(params (string Key, double? Value)[] effects) =>
        EffectSummary.Lines(effects.Select(e => new Effect(e.Key, e.Value)).ToList(), Names, Display);

    private static string Single(string key, double? value) => Assert.Single(Lines((key, value)));

    [Theory]
    [InlineData("economy", -5, Down + " Economy −5")]
    [InlineData("economy", 2.5, Up + " Economy +2.5")]
    [InlineData("purse.CommerceLabor", -2, Down + " Commerce and Labor purse −2")]
    [InlineData("purse.War", 1.25, Up + " War purse +1.25")]
    [InlineData("reserve", 3, Up + " Reserve +3")]
    [InlineData("popularity.123456789", 2, Up + " <@123456789> popularity +2")]
    [InlineData("popularity.123456789", -1.333, Down + " <@123456789> popularity −1.33")]
    [InlineData("seats", 30, Up + " Assembly seats +30")]
    [InlineData("seats", -10, Down + " Assembly seats −10")]
    [InlineData("taxRate", 0.05, Up + " Tax rate +5%")]
    [InlineData("taxRate", -0.025, Down + " Tax rate −2.5%")]
    public void Public_numbers_show_their_amount(string key, double value, string expected) =>
        Assert.Equal(expected, Single(key, value));

    [Theory]
    [InlineData("discontent.iracterie", -3, Down + " Discontent in Iracterie")]
    [InlineData("discontent.north_arpal", 1, Up + " Discontent in North Arpal")]
    [InlineData("opinion.labour", 0.05, Up + " Opinion on The Condition of Labour")]
    [InlineData("opinion.labour", -0.2, Down + " Opinion on The Condition of Labour")]
    [InlineData("salience.labour", 1, Up + " Salience: The Condition of Labour")]
    [InlineData("favour", -10, Down + " Anzarian Favour")]
    [InlineData("threat", 2, Up + " Foreign Threat")]
    public void Mood_and_threat_show_direction_only(string key, double value, string expected) =>
        Assert.Equal(expected, Single(key, value));

    [Fact]
    public void Favour_and_its_baseline_read_the_same()
    {
        Assert.Equal(Single("favour", 5), Single("favourBaseline", 5));
        Assert.Equal(Single("favour", -7), Single("favourBaseline", -7));
    }

    [Theory]
    [InlineData("open.public_health", "New issue: Public Health")]
    [InlineData("close.free_trade", "Issue closed: Free Trade")]
    [InlineData("bill.ten_hours", "New bill: The Ten Hours Bill")]
    [InlineData("project.royal_railway", "Project begun: The Royal Railway")]
    [InlineData("crisis.dock_strike_carden", "Crisis: The Carden Dock Strike")]
    public void Actions_are_plain_lines(string key, string expected) =>
        Assert.Equal(expected, Single(key, null));

    [Fact]
    public void Conditions_given_and_removed()
    {
        Assert.Equal("North Arpal is now *Strike*", Single("condition.north_arpal.strike", 1));
        Assert.Equal("North Arpal is no longer *Strike*", Single("condition.north_arpal.strike", -1));
    }

    [Fact]
    public void Zero_effects_are_dropped()
    {
        Assert.Empty(Lines(("economy", 0), ("discontent.iracterie", 0), ("favour", 0), ("taxRate", 0)));
        Assert.Empty(EffectSummary.Lines([], Names, Display));
    }

    [Fact]
    public void Order_is_preserved_as_written()
    {
        var lines = Lines(
            ("discontent.north_arpal", 1),
            ("salience.labour", 1),
            ("economy", 0),
            ("condition.north_arpal.strike", 1),
            ("crisis.dock_strike_carden", null));
        Assert.Equal(
        [
            Up + " Discontent in North Arpal",
            Up + " Salience: The Condition of Labour",
            "North Arpal is now *Strike*",
            "Crisis: The Carden Dock Strike",
        ], lines);
    }

    [Fact]
    public void Passed_law_example_from_the_spec()
    {
        var effects = EffectParser.ParseText(
            "economy -5, purse.CommerceLabor -2, discontent.iracterie -3, discontent.north_arpal -1").Effects;
        Assert.Equal(
        [
            Down + " Economy −5",
            Down + " Commerce and Labor purse −2",
            Down + " Discontent in Iracterie",
            Down + " Discontent in North Arpal",
        ], EffectSummary.Lines(effects, Names, Display));
    }
}
