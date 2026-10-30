using System.Runtime.CompilerServices;
using Xunit;

namespace AureliaBot.Tests;

public class EffectRegistryTests : AssemblyTestBase
{
    private static EffectSource Source(ulong? actor = null) => new("test", "test", "1", ActorId: actor);

    private static string RepoFile(string name, [CallerFilePath] string source = "")
    {
        for (var dir = new DirectoryInfo(Path.GetDirectoryName(source)!); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, name))) return Path.Combine(dir.FullName, name);
        throw new FileNotFoundException(name);
    }

    [Fact]
    public void EveryEffectKind_IsDocumentedInTheYamlReference()
    {
        var reference = File.ReadAllText(RepoFile("YAML Reference.md"));
        foreach (var kind in EffectKinds.All)
            Assert.True(reference.Contains($"`{kind.Prefix}"), $"`{kind.Prefix}` is missing from YAML Reference.md");
    }

    [Fact]
    public void RolePopularity_ReachesTheRightFactions()
    {
        var before = Db.GetActiveFactions().ToDictionary(f => f.UserId, f => f.Popularity);

        Db.ApplyEffects(EffectParser.ParseText("popularity.government 2, popularity.opposition -1, popularity.minister.Treasury 3").Effects, Config, Source());

        double Change(ulong id) => Db.GetFaction(id)!.Popularity - before[id];
        Assert.Equal(2, Change(Chancellor));
        Assert.Equal(5, Change(Minister));
        Assert.Equal(2, Change(Backbencher));
        Assert.Equal(-1, Change(Opposer));
        Assert.Equal(0, Change(Independent));
    }

    [Fact]
    public void PopularitySponsor_ReachesTheEffectsActor_AndHomeReachesTheProvince()
    {
        var before = Db.GetActiveFactions().ToDictionary(f => f.UserId, f => f.Popularity);

        Db.ApplyEffects(EffectParser.ParseText("popularity.sponsor 4, popularity.home.aurenten 1").Effects, Config, Source(Opposer));

        Assert.Equal(before[Opposer] + 4, Db.GetFaction(Opposer)!.Popularity, 6);
        Assert.Equal(before[Chancellor] + 1, Db.GetFaction(Chancellor)!.Popularity, 6);
        Assert.Equal(before[SecondOpposer] + 1, Db.GetFaction(SecondOpposer)!.Popularity, 6);
        Assert.Equal(before[Minister], Db.GetFaction(Minister)!.Popularity, 6);
    }

    [Fact]
    public void ASponsorEffect_WithNoActor_IsSkippedWithAWarning()
    {
        var result = Db.ApplyEffects(EffectParser.ParseText("popularity.sponsor 4").Effects, Config, Source());
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void NationwideEffects_ReachEveryProvince()
    {
        Db.ApplyEffects(EffectParser.ParseText("discontent.all 2, condition.all.lawless 1").Effects, Config, Source());

        Assert.All(Db.GetProvinces(), p => Assert.Equal(C.Discontent.Start + 2, p.Discontent, 6));
        Assert.All(Db.GetProvinceConditions().Values, conditions => Assert.Contains("lawless", conditions));
    }

    [Fact]
    public void EventEffects_QueueAndCancel_AndStayOutOfThePublicSummary()
    {
        Db.SaveTemplate("riots", TemplateKind.Event, "id: riots\ntitle: Riots\ntext: The streets burn.\neffects: { discontent.north_arpal: 1 }\n");
        Db.SetTemplateStatus("riots", TemplateStatus.Ready);
        var effects = EffectParser.ParseText("event.riots 2", Db.BuildEffectContext(Config)).Effects;

        Db.ApplyEffects(effects, Config, Source());

        var queued = Assert.Single(Db.GetQueuedEvents());
        Assert.Equal(Db.GetGameState().ResolutionNumber + 3, queued.FireAtResolution);
        Assert.Empty(EffectSummary.Lines(effects, Publisher.Names(Db, Config), C.Display));

        Db.ApplyEffects(EffectParser.ParseText("cancel.riots").Effects, Config, Source());
        Assert.Empty(Db.GetQueuedEvents());
    }

    [Theory]
    [InlineData("event.riots -1")]
    [InlineData("event.riots 1.5")]
    [InlineData("popularity.minister.Navy 1")]
    [InlineData("popularity.party.abc 1")]
    [InlineData("popularity.nobody 1")]
    public void BadNewKeys_AreRejected(string text) => Assert.False(EffectParser.ParseText(text).IsValid);
}
