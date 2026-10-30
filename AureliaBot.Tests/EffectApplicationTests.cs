using Xunit;

namespace AureliaBot.Tests;

public class EffectApplicationTests : IDisposable
{
    private readonly DatabaseFixture _fixture = new();
    private DatabaseService Db => _fixture.Db;

    public void Dispose() => _fixture.Dispose();

    private static GameConfig Config => DatabaseFixture.Config;

    private static EffectSource Source => new("test", "test", "1");

    [Fact]
    public void AClampedEffect_ReportsWhatWasActuallyApplied()
    {
        Db.StartGame(Config, DateTimeOffset.UtcNow);
        var balance = (double)Db.GetPurseBalance(Ministry.War);

        var result = Db.ApplyEffects([new Effect("purse.War", -(balance + 50))], Config, Source);

        Assert.Equal(-balance, result.Applied.Single().Value!.Value, 2);
        Assert.Equal(0, Db.GetPurseBalance(Ministry.War));
    }

    [Fact]
    public void OpinionEffects_OnAClosedIssue_AreRefused()
    {
        Db.StartGame(Config, DateTimeOffset.UtcNow);
        Db.OpenIssue("labour", "labour", "Labour", "For", "Against", 0.2, 3);
        Db.ApplyEffects([new Effect("close.labour", null)], Config, Source);

        var result = Db.ApplyEffects([new Effect("opinion.labour", 0.3)], Config, Source);

        Assert.Single(result.Warnings);
        Assert.Equal(0.2, Db.GetIssue("labour")!.Opinion, 6);
    }

    [Fact]
    public void ClosingAnIssue_ClearsProgrammes_AndOldStancesDontReturnOnReopening()
    {
        Db.StartGame(Config, DateTimeOffset.UtcNow);
        Db.OpenIssue("labour", "labour", "Labour", "For", "Against", 0.2, 3);
        Db.CreateFaction(1, "A", "aurenten", 10);
        Db.CreateFaction(2, "B", "aurenten", 10);
        var party = Db.CreateParty("Liberal Union", "#ffcc00", null, [1, 2]).PartyId!.Value;
        Db.RecordProgramme(party, new Dictionary<string, Stance> { ["labour"] = Stance.For }, null, Config.Constants);

        Db.ApplyEffects([new Effect("close.labour", null)], Config, Source);
        Assert.Empty(Db.GetProgramme(party));

        Db.Resolve(Config, new ScriptedRandomSource(), 1, DateTimeOffset.UtcNow);
        Db.OpenIssue("labour", "labour", "Labour", "For", "Against", 0.2, 3);
        Assert.Null(Db.GetStance(1, "labour"));
    }
}
