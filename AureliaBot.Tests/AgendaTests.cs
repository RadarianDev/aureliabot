using Xunit;

namespace AureliaBot.Tests;

public class AgendaTests : IDisposable
{
    private readonly DatabaseFixture _fixture = new();
    private DatabaseService Db => _fixture.Db;

    public void Dispose() => _fixture.Dispose();

    private static GameConfig Config => DatabaseFixture.Config;

    private static GameConfig WithAgendas(params AgendaDefinition[] agendas) =>
        new(Config.Constants, Config.Provinces, Config.Conditions, Config.Variables,
            new AgendaConfig(new AgendaRewardFile { PerWish = 3, AllBonus = 2 },
                new Dictionary<AgendaTrack, double> { [AgendaTrack.Economy] = 5 }, agendas));

    private static readonly AgendaDefinition Grower =
        new("grower", "The Grower", "Wants growth.", [new AgendaWish(AgendaTrack.Economy, AgendaDirection.Up)]);

    private ResolutionReport Resolve(GameConfig config) =>
        Db.Resolve(config, new ScriptedRandomSource([], Enumerable.Repeat(0.5, 60)), 7, DateTimeOffset.UtcNow);

    private void Apply(GameConfig config, string text)
    {
        var parsed = EffectParser.ParseText(text, Db.BuildEffectContext(config, allowCrisis: false));
        Assert.True(parsed.IsValid, string.Join("\n", parsed.Errors));
        Db.ApplyEffects(parsed.Effects, config, new EffectSource("test", "test", "1"));
    }

    private void CallElection() =>
        Db.SaveGameState(Db.GetGameState() with { PreElectionSeason = Db.GetGameState().Season, PreElectionYear = Db.GetGameState().Year });

    [Fact]
    public void TheShippedAgendas_AreSixWithTwoWishesEach()
    {
        Assert.Equal(6, Config.Agendas.Agendas.Count);
        Assert.All(Config.Agendas.Agendas, a => Assert.Equal(2, a.Wishes.Count));
        Assert.All(Enum.GetValues<AgendaTrack>(), t => Assert.True(Config.Agendas.MarginOf(t) > 0));
    }

    [Fact]
    public void TheYaml_ReportsUnknownTracksAndDirections()
    {
        var (_, errors) = AgendaConfig.From(new AgendasYaml
        {
            Agendas = new()
            {
                ["odd"] = new AgendaEntryFile { Name = "Odd", Wants = new() { ["weather"] = "up", ["economy"] = "sideways" } },
                ["empty"] = new AgendaEntryFile { Name = "" },
            },
        });

        Assert.Contains(errors, e => e.Contains("unknown track 'weather'"));
        Assert.Contains(errors, e => e.Contains("'sideways'"));
        Assert.Contains(errors, e => e.Contains("'empty' has no name"));
        Assert.Contains(errors, e => e.Contains("'empty' wants nothing"));
    }

    [Fact]
    public void AWish_IsMetOnlyBeyondItsMargin()
    {
        var up = new AgendaWish(AgendaTrack.Economy, AgendaDirection.Up);
        var down = new AgendaWish(AgendaTrack.Economy, AgendaDirection.Down);
        Assert.True(AgendaRules.Met(up, 100, 106, 5));
        Assert.False(AgendaRules.Met(up, 100, 104, 5));
        Assert.True(AgendaRules.Met(down, 100, 94, 5));
        Assert.False(AgendaRules.Met(down, 100, 106, 5));
    }

    [Fact]
    public void TheReward_IsPerWish_PlusABonusForAll()
    {
        var reward = new AgendaRewardFile { PerWish = 3, AllBonus = 2 };
        Assert.Equal(0, AgendaRules.Reward(0, 2, reward));
        Assert.Equal(3, AgendaRules.Reward(1, 2, reward));
        Assert.Equal(8, AgendaRules.Reward(2, 2, reward));
    }

    [Fact]
    public void Pick_NeverRepeatsThePreviousAgenda_UnlessItIsTheOnlyOne()
    {
        var random = new Random(1);
        for (var i = 0; i < 100; i++)
            Assert.NotEqual("general", AgendaRules.Pick(Config.Agendas.Agendas, "general", new Dictionary<string, int>(), random)!.Id);
        Assert.Equal("grower", AgendaRules.Pick([Grower], "grower", new Dictionary<string, int>(), random)!.Id);
    }

    [Fact]
    public void Pick_ChoosesAmongTheLeastHeld()
    {
        var held = Config.Agendas.Agendas.ToDictionary(a => a.Id, _ => 2);
        held["patriot"] = 1;
        var random = new Random(1);
        for (var i = 0; i < 50; i++)
            Assert.Equal("patriot", AgendaRules.Pick(Config.Agendas.Agendas, null, held, random)!.Id);
    }

    [Fact]
    public void DealingTwelveFactions_GivesEverySixAgendasTwice_AroundTheGmsChoices()
    {
        Db.StartGame(Config, DateTimeOffset.UtcNow);
        for (ulong id = 1; id <= 12; id++)
            Db.CreateFaction(id, $"F{id}", Config.Provinces[(int)(id % 10)].Id, 10);
        Assert.Null(Db.AssignAgenda(1, "general", Config, now: false).Refusal);
        Assert.Null(Db.AssignAgenda(2, "general", Config, now: false).Refusal);
        Assert.Null(Db.AssignAgenda(3, "magnate", Config, now: false).Refusal);

        Db.DealMissingAgendas(Config, new Random(5));

        var hands = Db.GetActiveAgendas();
        Assert.Equal(12, hands.Count);
        Assert.Equal("general", hands.Single(h => h.FactionId == 1).AgendaId);
        Assert.Equal("general", hands.Single(h => h.FactionId == 2).AgendaId);
        Assert.Equal("magnate", hands.Single(h => h.FactionId == 3).AgendaId);
        Assert.All(hands.GroupBy(h => h.AgendaId), g => Assert.Equal(2, g.Count()));
        Assert.Empty(Db.GetAgendaAssignments());
    }

    [Fact]
    public void AnAssignmentForTheNextDeal_IsUsedAtTheElection_AndCountsAsARepeatWhenItIs()
    {
        var config = WithAgendas(Grower, Grower with { Id = "other", Name = "The Other" });
        Db.StartGame(config, DateTimeOffset.UtcNow);
        Db.CreateFaction(1, "A", "aurenten", 10);
        Resolve(config);
        var held = Db.GetActiveAgenda(1)!.AgendaId;

        var result = Db.AssignAgenda(1, held, config, now: false);
        Assert.True(result.Repeats);
        Assert.Null(result.Dealt);
        Assert.Equal(held, Db.GetActiveAgenda(1)!.AgendaId);

        CallElection();
        Resolve(config);

        Assert.Equal(held, Db.GetActiveAgenda(1)!.AgendaId);
        Assert.Empty(Db.GetAgendaAssignments());
    }

    [Fact]
    public void AssigningNow_ReplacesTheAgendaAtOnce()
    {
        Db.StartGame(Config, DateTimeOffset.UtcNow);
        Db.CreateFaction(1, "A", "aurenten", 10);
        Db.DealAgenda(1, Config, new Random(1));

        var result = Db.AssignAgenda(1, "treasurer", Config, now: true);

        Assert.Equal("treasurer", result.Dealt!.AgendaId);
        Assert.Equal("treasurer", Db.GetActiveAgenda(1)!.AgendaId);
        Assert.Single(Db.GetAgendaHistory(1), h => h.Status == AgendaStatus.Void);
    }

    [Fact]
    public void Assigning_RefusesUnknownAgendasAndRetiredFactions()
    {
        Db.StartGame(Config, DateTimeOffset.UtcNow);
        Db.CreateFaction(1, "A", "aurenten", 10);
        Assert.Equal(AssignAgendaRefusal.UnknownAgenda, Db.AssignAgenda(1, "nope", Config, now: false).Refusal);
        Db.RetireFaction(1);
        Assert.Equal(AssignAgendaRefusal.NotActive, Db.AssignAgenda(1, "general", Config, now: false).Refusal);
    }

    [Fact]
    public void Dealing_GivesEachFactionOneAgenda_AndARedealIsADifferentOne()
    {
        Db.StartGame(Config, DateTimeOffset.UtcNow);
        Db.CreateFaction(1, "A", "aurenten", 10);

        var first = Db.DealAgenda(1, Config, new Random(3));
        Assert.NotNull(first);
        Assert.Null(Db.DealAgenda(1, Config, new Random(3)));

        for (var i = 0; i < 20; i++)
        {
            var before = Db.GetActiveAgenda(1)!;
            var next = Db.RedealAgenda(1, Config, new Random(i))!;
            Assert.NotEqual(before.AgendaId, next.AgendaId);
        }
        Assert.Single(Db.GetActiveAgendas());
        Assert.Equal(20, Db.GetAgendaHistory(1).Count(h => h.Status == AgendaStatus.Void));
    }

    [Fact]
    public void BeforeTheGameStarts_NothingIsDealt()
    {
        Db.CreateFaction(1, "A", "aurenten", 10);
        Assert.Null(Db.DealAgenda(1, Config, new Random(1)));
        Assert.Empty(Db.DealMissingAgendas(Config, new Random(1)));
    }

    [Fact]
    public void AnAgendaDealtInThePreElection_CarriesIntoTheTerm_AndIsScoredAtTheNextElection()
    {
        var config = WithAgendas(Grower);
        Db.StartGame(config, DateTimeOffset.UtcNow);
        Db.CreateFaction(1, "A", "aurenten", 10);
        Db.DealAgenda(1, config, new Random(1));

        var founding = Resolve(config);
        Assert.Empty(founding.AgendaReveals);
        var carried = Db.GetActiveAgenda(1)!;

        Apply(config, "economy 50");
        CallElection();
        var popularityBefore = Db.GetFaction(1)!.Popularity;
        var report = Resolve(config);

        var reveal = Assert.Single(report.AgendaReveals);
        Assert.Equal("The Grower", reveal.AgendaName);
        Assert.True(Assert.Single(reveal.Results).Met);
        Assert.Equal(5, reveal.Reward);
        Assert.Equal(AgendaStatus.Scored, Db.GetAgendaHistory(1).Single(h => h.Id == carried.Id).Status);
        Assert.NotEqual(carried.Id, Db.GetActiveAgenda(1)!.Id);
        Assert.Contains(report.PrivateMessages[1], m => m.Contains("Your secret agenda"));
        Assert.True(Db.GetFaction(1)!.Popularity > popularityBefore);
    }

    [Fact]
    public void AHandWhoseIdLeftTheFile_IsVoidedWithAnAlert_AndReplaced()
    {
        var before = WithAgendas(Grower);
        var after = WithAgendas(Grower with { Id = "renamed" });
        Db.StartGame(before, DateTimeOffset.UtcNow);
        Db.CreateFaction(1, "A", "aurenten", 10);
        Db.DealAgenda(1, before, new Random(1));

        var report = Resolve(after);

        Assert.Contains(report.Alerts, a => a.Contains("`grower`"));
        Assert.Equal(AgendaStatus.Void, Db.GetAgendaHistory(1)[0].Status);
        Assert.Equal("renamed", Db.GetActiveAgenda(1)!.AgendaId);
    }

    [Fact]
    public void AnElection_DealsEveryFactionWithoutAnAgenda_AndVoidsRetiredOnes()
    {
        var config = WithAgendas(Grower);
        Db.StartGame(config, DateTimeOffset.UtcNow);
        Db.CreateFaction(1, "A", "aurenten", 10);
        Db.CreateFaction(2, "B", "aurenten", 10);
        Resolve(config);
        Assert.Equal(2, Db.GetActiveAgendas().Count);

        Db.RetireFaction(2);
        CallElection();
        var report = Resolve(config);

        Assert.Equal(AgendaStatus.Void, Assert.Single(Db.GetAgendaHistory(2)).Status);
        Assert.Equal(1UL, Assert.Single(report.AgendaReveals).FactionId);
    }
}
