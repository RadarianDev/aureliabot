using Xunit;

namespace AureliaBot.Tests;

public class ElectionPostTests
{
    private static readonly List<ElectionPartySnapshot> Parties =
    [
        new(1, "Liberal Union", "#C0392B", null),
        new(2, "Tory Club", "#2E86C1", 555),
        new(3, "Radicals", "#27AE60", null),
        new(4, "Agrarians", "#F1C40F", null),
    ];

    private static ElectionResult Result()
    {
        var provinces = new List<ProvinceOutcome>
        {
            new("ocidanten", 10,
                new Dictionary<long, double> { [1] = 30, [2] = 20 },
                new Dictionary<long, int> { [1] = 6, [2] = 4 },
                new Dictionary<long, int> { [1] = 1 },
                1),
            new("north_arpal", 5,
                new Dictionary<long, double> { [2] = 12, [3] = 4 },
                new Dictionary<long, int> { [2] = 4, [3] = 1 },
                new Dictionary<long, int> { [3] = 1 },
                2),
        };
        return new ElectionResult(
            provinces,
            new Dictionary<long, int> { [1] = 7, [2] = 8, [3] = 2, [4] = 0 },
            new Dictionary<ulong, int> { [101] = 7, [202] = 8, [303] = 2 },
            new Dictionary<long, double> { [1] = 0.45, [2] = 0.4, [3] = 0.15 });
    }

    [Fact]
    public void SeatChartParties_AreOrderedBySeats_AndSkipEmptyParties()
    {
        var result = Result() with { PartySeats = new Dictionary<long, int> { [1] = 7, [2] = 8, [3] = 7, [4] = 0 } };

        var chart = ElectionPublisher.SeatChartParties(result, Parties);

        Assert.Equal(["Tory Club", "Liberal Union", "Radicals"], chart.Select(p => p.Name));
        Assert.Equal([8, 7, 7], chart.Select(p => p.SeatCount));
        Assert.Equal("#2E86C1", chart[0].HexColor);
    }

    [Fact]
    public void MapColours_LeaveOutNullWinnersAndDeletedParties()
    {
        var config = DatabaseFixture.Config;
        var winners = new Dictionary<string, long?>
        {
            ["ocidanten"] = 1,
            ["north_arpal"] = 99,
            ["aurenten"] = null,
            ["tovrerie"] = 2,
        };

        var colours = ElectionPublisher.MapColours(winners, Parties, config);

        Assert.Equal(2, colours.Count);
        Assert.Equal("#C0392B", colours[config.Province("ocidanten")!.MapKey]);
        Assert.Equal("#2E86C1", colours[config.Province("tovrerie")!.MapKey]);
    }

    [Fact]
    public void WinnersFromRows_GoToMostSeats_ThenStrength_ThenLowerId()
    {
        var rows = new List<ProvinceResultRow>
        {
            new(2, "ocidanten", 3, "C", 20, 4),
            new(2, "ocidanten", 1, "A", 20, 5),
            new(2, "ocidanten", 2, "B", 10, 9),
            new(2, "aurenten", 4, "D", 15, 3),
            new(2, "aurenten", 2, "B", 15, 3),
            new(2, "tovrerie", 1, "A", 5, 1),
            new(2, "tovrerie", 3, "C", 8, 0),
        };

        var winners = ElectionPublisher.WinnersFromRows(rows);

        Assert.Equal(2, winners["ocidanten"]);
        Assert.Equal(2, winners["aurenten"]);
        Assert.Equal(1, winners["tovrerie"]);
    }

    [Fact]
    public void ResultsText_ListsEveryPartysSeats_AndProclamationSummonsTheAssembly()
    {
        var report = new ElectionReport(2, Result(), Parties);

        var text = ElectionPublisher.ResultsText(report, DatabaseFixture.Config);
        var proclamation = ElectionPublisher.Proclamation(report);

        string Named(long id) => ElectionPublisher.PartyName(Parties.Single(p => p.Id == id));
        string Emoji(long id) => ElectionPublisher.PartyEmoji(Parties.Single(p => p.Id == id));
        Assert.Contains(Texts.Get("election.partyLine", ("party", Named(1)), ("seats", 7), ("share", "45.0")), text);
        Assert.Contains(Texts.Get("election.partyLine", ("party", Named(2)), ("seats", 8), ("share", "40.0")), text);
        Assert.Contains(Texts.Get("election.partyLine", ("party", Named(3)), ("seats", 2), ("share", "15.0")), text);
        Assert.Equal("<:p:555>", Emoji(2));
        Assert.Contains($"{Emoji(1)} 7 · {Emoji(2)} 4", text);
        Assert.Contains($"{Emoji(2)} 4 · {Emoji(3)} 2", text);
        Assert.Contains("By His Majesty's Command", proclamation);
        Assert.Contains("17", proclamation);
        Assert.DoesNotContain("[election.", text + proclamation);
    }
}
