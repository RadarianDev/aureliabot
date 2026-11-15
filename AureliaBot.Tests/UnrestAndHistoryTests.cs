using Xunit;

namespace AureliaBot.Tests;

public class UnrestAndHistoryTests : IDisposable
{
    private const ulong Chancellor = 1;
    private const ulong Backbencher = 2;
    private const ulong Opposition = 3;
    private const ulong OppositionPartner = 4;

    private readonly DatabaseFixture _fixture = new();
    private DatabaseService Db => _fixture.Db;

    public void Dispose() => _fixture.Dispose();

    private static GameConfig Config => DatabaseFixture.Config;

    private ResolutionReport Resolve() =>
        Db.Resolve(Config, new ScriptedRandomSource([], Enumerable.Repeat(0.5, 40)), 1, DateTimeOffset.UtcNow);

    private void Apply(string text)
    {
        var parsed = EffectParser.ParseText(text, Db.BuildEffectContext(Config, allowCrisis: false));
        Assert.True(parsed.IsValid, string.Join("\n", parsed.Errors));
        Db.ApplyEffects(parsed.Effects, Config, new EffectSource("test", "test", "1"));
    }

    private void SetUpGovernment()
    {
        Db.StartGame(Config, DateTimeOffset.UtcNow);
        foreach (var id in new[] { Chancellor, Backbencher, Opposition, OppositionPartner })
            Db.CreateFaction(id, $"F{id}", "aurenten", 10);
        Db.CreateParty("Government", "#ffcc00", null, [Chancellor, Backbencher]);
        Db.CreateParty("Opposition", "#cc0000", null, [Opposition, OppositionPartner]);
        Assert.Null(Db.ImposeCabinet(Chancellor, new Dictionary<Ministry, ulong?>()).Refusal);
    }

    [Fact]
    public void UnrestCost_GrowsWithDiscontentAboveTheThreshold()
    {
        Assert.Equal(0, Formulas.UnrestCost(3, 4, 1));
        Assert.Equal(0, Formulas.UnrestCost(4, 4, 1));
        Assert.Equal(3, Formulas.UnrestCost(7, 4, 1));
        Assert.Equal(1.5, Formulas.UnrestCost(7, 4, 0.5));
    }

    [Fact]
    public void AQuietRealm_CostsTheGovernmentNothing()
    {
        SetUpGovernment();
        var report = Resolve();

        Assert.DoesNotContain(report.News, n => n.Contains("Unrest turns"));
        Assert.Equal(Db.GetFaction(Opposition)!.Popularity, Db.GetFaction(Chancellor)!.Popularity, 6);
    }

    [Fact]
    public void ARestlessProvince_CostsTheCabinetMost_ThenItsParty_AndNotTheOpposition()
    {
        SetUpGovernment();
        Apply("discontent.aurenten 5");

        var report = Resolve();

        Assert.Contains(report.News, n => n.Contains("Unrest turns") && n.Contains("Aurenten"));
        var chancellor = Db.GetRegionalPopularity(Chancellor, Config.Constants.Popularity)["aurenten"];
        var backbencher = Db.GetRegionalPopularity(Backbencher, Config.Constants.Popularity)["aurenten"];
        var opposition = Db.GetRegionalPopularity(Opposition, Config.Constants.Popularity)["aurenten"];
        Assert.True(chancellor < backbencher, $"{chancellor} < {backbencher}");
        Assert.True(backbencher < opposition, $"{backbencher} < {opposition}");
    }

    [Fact]
    public void HistoryIsRecorded_AtTheStart_AndAfterEveryResolution()
    {
        SetUpGovernment();
        Resolve();
        Resolve();

        var history = Db.GetHistory();
        Assert.Equal(3, history.Turns.Count);
        Assert.Equal(3, history.Of(HistorySeries.Economy).Count);
        Assert.Equal(2, history.Of(HistorySeries.Popularity(Chancellor)).Count);
        Assert.Equal(3, history.Of(HistorySeries.Discontent("aurenten")).Count);
    }

    [Fact]
    public void RecordHistoryIfMissing_FillsInTheCurrentTurnOnce()
    {
        SetUpGovernment();
        Db.RecordHistoryIfMissing();
        Db.RecordHistoryIfMissing();

        Assert.Single(Db.GetHistory().Turns);
    }

    [Fact]
    public void APublicChart_RendersAPng_WithALegendLinePerSeries()
    {
        SetUpGovernment();
        Resolve();

        using var chart = ChartRequests.Public(Db, PublicChart.Money, null);

        Assert.NotNull(chart);
        var png = chart!.Image.ToArray();
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4]);
        Assert.Equal(2 + GameEnumExtensions.CabinetMinistries.Count, chart.Embed.Description.Split('\n').Length);
    }

    [Fact]
    public void ChartsSharingAMessage_EachPointAtTheirOwnFile()
    {
        SetUpGovernment();
        Resolve();

        using var economy = ChartRequests.Public(Db, PublicChart.Economy, null, "economy.png")!;
        using var money = ChartRequests.Public(Db, PublicChart.Money, null, "money.png")!;

        Assert.Equal("attachment://economy.png", economy.Embed.Image!.Value.Url);
        Assert.Equal("attachment://money.png", money.Embed.Image!.Value.Url);
        using var attachment = economy.Attachment();
        Assert.Equal("economy.png", attachment.FileName);
    }

    [Fact]
    public void BeforeTheGameStarts_ThereIsNothingToChart() =>
        Assert.Null(ChartRequests.Public(Db, PublicChart.Economy, null));

    [Fact]
    public void NiceScale_CoversTheDataWithRoundSteps()
    {
        var scale = ChartScale.Nice(987, 1043);
        Assert.True(scale.Low <= 987 && scale.High >= 1043);
        Assert.Equal(20, scale.Step);

        var flat = ChartScale.Nice(10, 10);
        Assert.True(flat.Low < 10 && flat.High > 10);
    }
}
