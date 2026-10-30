using Xunit;

namespace AureliaBot.Tests;

public class EffectParserTests
{
    private static EffectContext Context(bool allowHere = false, bool allowCrisis = false) => new(
        new HashSet<string> { "north_arpal", "iracterie" },
        new HashSet<string> { "striking", "lawless" },
        new HashSet<string> { "labour" },
        new HashSet<string> { "labour", "public_health" },
        new HashSet<string> { "ten_hours_bill" },
        new HashSet<string> { "royal_railway" },
        new HashSet<string> { "dock_strike_carden" },
        new HashSet<string> { "123" })
    {
        AllowHere = allowHere,
        AllowCrisis = allowCrisis,
    };

    [Fact]
    public void ParsesTheSpecExample()
    {
        var result = EffectParser.ParseText(
            "discontent.north_arpal -2, economy -1, opinion.labour +0.05, crisis.dock_strike_carden", Context(allowCrisis: true));

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        Assert.Equal(["discontent.north_arpal", "economy", "opinion.labour", "crisis.dock_strike_carden"], result.Effects.Select(e => e.Key));
        Assert.Equal(-2, result.Effects[0].Value);
        Assert.Equal(0.05, result.Effects[2].Value);
        Assert.Null(result.Effects[3].Value);
    }

    [Theory]
    [InlineData("baseline.labour 0.1")]
    [InlineData("purse.Navy 2")]
    [InlineData("condition.north_arpal.striking 2")]
    [InlineData("discontent.here 1")]
    [InlineData("crisis.dock_strike_carden")]
    [InlineData("economy")]
    [InlineData("open.labour 3")]
    [InlineData("discontent.atlantis 1")]
    [InlineData("bill.unknown_bill")]
    [InlineData("popularity.999 2")]
    [InlineData("wealth 2")]
    public void RejectsInvalidEffects(string text)
    {
        var result = EffectParser.ParseText(text, Context());
        Assert.False(result.IsValid);
        Assert.Empty(result.Effects);
    }

    [Fact]
    public void HereIsAllowedInProjects()
    {
        var result = EffectParser.ParseText("discontent.here -2, condition.here.lawless -1", Context(allowHere: true));
        Assert.True(result.IsValid, string.Join("; ", result.Errors));

        var resolved = EffectParser.ResolveHere(result.Effects, "iracterie");
        Assert.Equal(["discontent.iracterie", "condition.iracterie.lawless"], resolved.Select(e => e.Key));
    }

    [Fact]
    public void ReportsEveryErrorTogether()
    {
        var result = EffectParser.ParseText("economy x, wealth 2, purse.Navy 1", Context());
        Assert.Equal(3, result.Errors.Count);
    }

    [Fact]
    public void FormatRoundTrips()
    {
        var parsed = EffectParser.ParseText("economy -5, purse.CommerceLabor -2, open.public_health, taxRate 0.05", Context()).Effects;
        var again = EffectParser.Parse(EffectParser.Format(parsed));
        Assert.Equal(parsed, again);
    }

    [Fact]
    public void AcceptsUnicodeMinusAndYamlTrue()
    {
        var text = EffectParser.ParseText("economy −3", Context());
        Assert.Equal(-3, text.Effects.Single().Value);

        var pairs = EffectParser.FromPairs([("open.labour", "true")], Context());
        Assert.True(pairs.IsValid);
    }
}
