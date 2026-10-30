using Xunit;

namespace AureliaBot.Tests;

public class ElectionFlowTests : IDisposable
{
    private readonly DatabaseFixture _fixture = new();
    private DatabaseService Db => _fixture.Db;

    public void Dispose() => _fixture.Dispose();

    private static GameConfig Config => DatabaseFixture.Config;

    private void StartWithTwoParties()
    {
        Db.StartGame(Config, DateTimeOffset.UtcNow);
        Db.OpenIssue("labour", "labour", "Labour", "For", "Against", 0.3, 3);
        Db.CreateFaction(1, "A", "aurenten", 20);
        Db.CreateFaction(2, "B", "iracterie", 15);
        Db.CreateFaction(3, "C", "north_arpal", 10);
        Db.CreateFaction(4, "D", "tovrerie", 12);
        Db.CreateFaction(5, "Independent", "aurenten", 30);
        var liberals = Db.CreateParty("Liberal Union", "#ffcc00", null, [1, 2]).PartyId!.Value;
        var country = Db.CreateParty("Country Party", "#336699", null, [3, 4]).PartyId!.Value;
        Db.RecordProgramme(liberals, new Dictionary<string, Stance> { ["labour"] = Stance.For }, null, Config.Constants);
        Db.RecordProgramme(country, new Dictionary<string, Stance> { ["labour"] = Stance.Against }, null, Config.Constants);
    }

    [Fact]
    public void TheFoundingElection_FillsTheAssembly_AndStoresProvinceResults()
    {
        StartWithTwoParties();

        var report = Db.Resolve(Config, new ScriptedRandomSource(), 1, DateTimeOffset.UtcNow);

        Assert.NotNull(report.Election);
        Assert.Equal(1, report.Election!.TermNumber);
        var factions = Db.GetAllFactions();
        Assert.Equal(180, factions.Sum(f => f.Seats));
        Assert.Equal(0, factions.Single(f => f.UserId == 5).Seats);
        Assert.All(factions.Where(f => f.UserId != 5), f => Assert.True(f.Seats >= 1));
        var rows = Db.GetProvinceResults();
        Assert.Equal(10, rows.Select(r => r.ProvinceId).Distinct().Count());
        Assert.Equal(180, rows.Sum(r => r.Seats));
        Assert.All(rows, r => Assert.Equal(1, r.TermNumber));
    }

    [Fact]
    public void TheResults_RenderASeatChartAndAProvinceMap()
    {
        StartWithTwoParties();
        var election = Db.Resolve(Config, new ScriptedRandomSource(), 1, DateTimeOffset.UtcNow).Election!;

        using var images = ElectionPublisher.Render(election.Result, election.Parties,
            ElectionPublisher.WinnersFromResult(election.Result), Config, out var failure);

        Assert.Null(failure);
        Assert.True(new FileInfo(images.SeatChartPath!).Length > 1000);
        Assert.True(new FileInfo(images.MapPath!).Length > 1000);
        Assert.Contains("Liberal Union", ElectionPublisher.ResultsText(election, Config, id => id.ToString()));
    }

    [Fact]
    public void StoredWinners_MatchTheElection()
    {
        StartWithTwoParties();
        var election = Db.Resolve(Config, new ScriptedRandomSource(), 1, DateTimeOffset.UtcNow).Election!;

        var stored = ElectionPublisher.WinnersFromRows(Db.GetProvinceResults());

        Assert.Equal(ElectionPublisher.WinnersFromResult(election.Result).OrderBy(kv => kv.Key), stored.OrderBy(kv => kv.Key));
    }

    [Fact]
    public void AProvinceWhereEveryPartyHasNoStrength_StillFillsItsSeats()
    {
        var input = new ElectionInput(
            10,
            [new ElectionProvince("p", 1)],
            [new ElectionParty(1, new Dictionary<string, Stance>()), new ElectionParty(2, new Dictionary<string, Stance>())],
            [new ElectionFaction(11, 1, 0, "p"), new ElectionFaction(12, 2, 0, "p")],
            [],
            Config.Constants.Election,
            Config.Constants.StrongStancePenalty);

        var result = ElectionCalculator.Run(input);

        Assert.Equal(10, result.PartySeats.Values.Sum());
        Assert.Null(result.Provinces[0].WinnerPartyId);
    }

    [Fact]
    public void NoElection_RunsOutsideAPreElection()
    {
        StartWithTwoParties();
        Db.Resolve(Config, new ScriptedRandomSource(), 1, DateTimeOffset.UtcNow);
        var seats = Db.GetAllFactions().ToDictionary(f => f.UserId, f => f.Seats);

        var report = Db.Resolve(Config, new ScriptedRandomSource(), 1, DateTimeOffset.UtcNow);

        Assert.Null(report.Election);
        Assert.Equal(seats, Db.GetAllFactions().ToDictionary(f => f.UserId, f => f.Seats));
    }
}
