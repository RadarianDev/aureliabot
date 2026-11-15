using Xunit;

namespace AureliaBot.Tests;

public class ElectionNightTests
{
    private static GameConfig Config => DatabaseFixture.Config;

    private static readonly IReadOnlyList<ElectionPartySnapshot> Parties =
    [
        new(1, "Liberal Union", "#ffcc00", null),
        new(2, "Radicals", "#cc0000", null),
    ];

    private static ProvinceOutcome Outcome(int index, int liberals, int radicals, int independents = 0) =>
        new(Config.Provinces[index].Id, liberals + radicals,
            new Dictionary<long, double> { [1] = liberals, [2] = radicals },
            new Dictionary<long, int> { [1] = liberals, [2] = radicals },
            new Dictionary<long, int>(),
            liberals == radicals ? null : liberals > radicals ? 1 : 2,
            independents);

    private static ElectionResult Result(params ProvinceOutcome[] provinces) =>
        new(provinces,
            new Dictionary<long, int>
            {
                [1] = provinces.Sum(p => p.ContestedSeatsByParty.GetValueOrDefault(1)),
                [2] = provinces.Sum(p => p.ContestedSeatsByParty.GetValueOrDefault(2)),
            },
            new Dictionary<ulong, int>(),
            new Dictionary<long, double> { [1] = 0.6, [2] = 0.4 },
            IndependentSeats: provinces.Sum(p => p.IndependentBoroughSeats));

    [Fact]
    public void EveryProvinceDeclares_SmallestFirst()
    {
        var returns = ElectionPublisher.NightReturns(Result(Outcome(0, 30, 10), Outcome(1, 2, 3), Outcome(2, 5, 5)), Parties, Config);

        Assert.Equal(3, returns.Count);
        Assert.Contains(Config.Provinces[1].Name, returns[0]);
        Assert.Contains(Config.Provinces[2].Name, returns[1]);
        Assert.Contains(Config.Provinces[0].Name, returns[2]);
    }

    [Fact]
    public void TheTallyRunsUp_ToTheFinalResult()
    {
        var returns = ElectionPublisher.NightReturns(Result(Outcome(0, 30, 10), Outcome(1, 2, 3, independents: 1)), Parties, Config);

        Assert.Contains("🟥 3 · 🟨 2 · Independent 1", returns[0]);
        Assert.Contains("🟨 32 · 🟥 13 · Independent 1", returns[1]);
        Assert.Contains("2 of 2 provinces declared", returns[1]);
    }

    [Fact]
    public void AMajorityIsCalled_OnceAndWhereItIsCrossed()
    {
        var returns = ElectionPublisher.NightReturns(Result(Outcome(0, 10, 0), Outcome(1, 50, 0), Outcome(2, 0, 55)), Parties, Config);

        Assert.DoesNotContain("majority!", returns[0]);
        Assert.Contains("Liberal Union has won a majority!", returns[1]);
        Assert.DoesNotContain("majority!", returns[2]);
    }

    [Fact]
    public void ATie_CarriesNoProvince() =>
        Assert.Contains("No party carries", ElectionPublisher.NightReturns(Result(Outcome(0, 5, 5)), Parties, Config)[0]);
}
