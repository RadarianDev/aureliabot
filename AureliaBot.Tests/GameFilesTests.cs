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
        Assert.Single(Db.GetQueuedEvents(), e => e.TemplateId == "opening_1850");
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
}
