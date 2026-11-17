using Discord.WebSocket;
using Xunit;

namespace AureliaBot.Tests;

public class GameFilesTests : IDisposable
{
    private readonly DatabaseFixture _fixture = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"aureliabot-game-{Guid.NewGuid():N}");
    private readonly GameFilesService _files;

    public GameFilesTests()
    {
        CopyFolder(Path.Combine(AppContext.BaseDirectory, "content"), Path.Combine(_root, "content"));
        CopyFolder(Path.Combine(AppContext.BaseDirectory, "config"), Path.Combine(_root, "config"));
        var config = new BotConfig { GameRoot = _root };
        var constants = new ConstantsStore(config);
        _files = new GameFilesService(config, Db, constants, new AlertService(new DiscordSocketClient(), Db));
    }

    private DatabaseService Db => _fixture.Db;

    public void Dispose()
    {
        _files.Dispose();
        _fixture.Dispose();
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private static void CopyFolder(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    [Fact]
    public async Task TheFirstLoad_ImportsAndReadiesEveryStartingFile()
    {
        var result = await _files.LoadContentAsync();

        var fileCount = Directory.EnumerateFiles(Path.Combine(_root, "content"), "*.yaml", SearchOption.AllDirectories).Count();
        Assert.Equal(fileCount, result.Imported.Count(o => o.Imported));
        Assert.Empty(result.NotReady);
        Assert.Empty(Db.GetTemplates(status: TemplateStatus.Draft));
        Assert.All(Db.GetQueuedEvents(), e => Assert.True(e.FromSchedule, e.TemplateId));
    }

    [Fact]
    public async Task ASecondLoad_ChangesNothing()
    {
        await _files.LoadContentAsync();
        var again = await _files.LoadContentAsync();

        Assert.False(again.Changed);
        Assert.Equal(Db.GetTemplates().Count, again.Unchanged);
    }

    [Fact]
    public async Task AnEditedFile_IsReimportedAndReadiedAgain_OthersAreLeftAlone()
    {
        await _files.LoadContentAsync();
        var path = Path.Combine(_root, "content", "issues", "social_reform.yaml");
        File.WriteAllText(path, File.ReadAllText(path).Replace("salience: 3", "salience: 4"));

        var result = await _files.LoadContentAsync();

        var outcome = Assert.Single(result.Imported);
        Assert.Equal("social_reform", outcome.Id);
        Assert.Contains("social_reform", result.MadeReady);
        Assert.Equal(TemplateStatus.Ready, Db.GetTemplate("social_reform")!.Status);
        Assert.Contains("salience: 4", Db.GetTemplate("social_reform")!.Yaml);
    }

    [Fact]
    public async Task ABrokenFile_IsReported_AndTheRestStillLoad()
    {
        File.WriteAllText(Path.Combine(_root, "content", "issues", "broken.yaml"), "id: broken\ntitle: Broken\nfor: x\nopinion: 9\n");

        var result = await _files.LoadContentAsync();

        Assert.Single(result.Imported, o => !o.Imported);
        Assert.Equal(TemplateStatus.Ready, Db.GetTemplate("social_reform")!.Status);
    }

    private void Delete(string relative) => File.Delete(Path.Combine(_root, "content", relative));

    private void Apply(string text)
    {
        var parsed = EffectParser.ParseText(text, Db.BuildEffectContext(DatabaseFixture.Config, allowCrisis: true));
        Assert.True(parsed.IsValid, string.Join("\n", parsed.Errors));
        Db.ApplyEffects(parsed.Effects, DatabaseFixture.Config, new EffectSource("test", "test", "1"));
    }

    [Fact]
    public async Task Prune_ArchivesATemplateWhoseFileIsGone_AndForgetsTheFile()
    {
        await _files.LoadContentAsync();
        Delete("projects/drill_hall.yaml");

        Assert.Equal(["drill_hall"], _files.Orphans().Select(t => t.Id));
        var result = await _files.PruneAsync();

        Assert.Equal(["drill_hall"], result.Archived);
        Assert.Equal(TemplateStatus.Archived, Db.GetTemplate("drill_hall")!.Status);
        Assert.Empty(_files.Orphans());
        Assert.False((await _files.LoadContentAsync()).Changed);
    }

    [Fact]
    public async Task Prune_NeverTouchesATemplateThatHadNoFile()
    {
        await _files.LoadContentAsync();
        var outcome = ContentRules.ImportOne(Db, ContentRules.ImportContext(Db, DatabaseFixture.Config), new ImportFile("custom.yaml",
            "id: custom_issue\ntitle: Custom\nfor: Yes.\nagainst: No.\nopinion: 0\nsalience: 1\n"));
        Assert.True(outcome.Imported);

        Assert.Empty(_files.Orphans());
        Assert.Equal(0, (await _files.PruneAsync()).Count);
        Assert.NotNull(Db.GetTemplate("custom_issue"));
    }

    [Fact]
    public async Task Prune_WithdrawsAnAvailableBill_AndCancelsQueuedCopiesOfAnEvent()
    {
        await _files.LoadContentAsync();
        Db.StartGame(DatabaseFixture.Config, DateTimeOffset.UtcNow);
        Apply("open.social_reform, bill.vagrancy_act, event.cobbler_member 3");
        Delete("bills/vagrancy_act.yaml");
        Delete("events/cobbler_member.yaml");

        var result = await _files.PruneAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result.BillsWithdrawn);
        Assert.Equal(1, result.EventsCancelled);
        Assert.Empty(result.InPlay);
        Assert.Equal(BillStatus.Withdrawn, Db.GetBills().Single(b => b.TemplateId == "vagrancy_act").Status);
        Assert.DoesNotContain(Db.GetQueuedEvents(), e => e.TemplateId == "cobbler_member");
    }
}
