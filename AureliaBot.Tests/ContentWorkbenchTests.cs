using Discord;
using Xunit;

namespace AureliaBot.Tests;

public class ContentWorkbenchTests : IDisposable
{
    private readonly DatabaseFixture _fixture = new();
    private DatabaseService Db => _fixture.Db;
    private static GameConfig Config => DatabaseFixture.Config;

    public void Dispose() => _fixture.Dispose();

    private static string Starting(string name) => TemplateCodecTests.StartingFiles().Single(f => f.Name == name).Yaml;

    [Theory]
    [MemberData(nameof(TemplateCodecTests.StartingFileNames), MemberType = typeof(TemplateCodecTests))]
    public void YamlParts_RoundTripEveryStartingTemplate(string name)
    {
        var yaml = Starting(name);
        var parts = YamlParts.Split(yaml);

        Assert.Equal(yaml, YamlParts.Join(parts));
        foreach (var part in parts.Where(p => p.Key != YamlParts.PreambleKey))
            Assert.Equal(yaml, YamlParts.Splice(yaml, part.Key, part.Body));
        if (YamlParts.Group(yaml, 300, 10) is { } groups)
            Assert.Equal(yaml, YamlParts.JoinGroups(groups));
    }

    [Fact]
    public void Splice_ReplacesOneKey_AndKeepsCommentsAndOrder()
    {
        const string yaml = "# An issue\nid: a\n\n# The heading\ntitle: Old\nfor: Yes\n";

        var spliced = YamlParts.Splice(yaml, "title", "# The heading\ntitle: New");

        Assert.Equal("# An issue\nid: a\n\n# The heading\ntitle: New\nfor: Yes\n", spliced);
        Assert.Equal(["id", "title", "for"], YamlParts.Split(yaml).Select(p => p.Key));
        Assert.StartsWith("# An issue\nid: a", YamlParts.Split(yaml)[0].Body);
        Assert.Null(YamlParts.Splice(yaml, "against", "against: No"));
    }

    [Fact]
    public void Group_SplitsAtTopLevelKeys_OrGivesUp()
    {
        var yaml = string.Concat(Enumerable.Range(1, 6).Select(i => $"key{i}: {new string('x', 90)}\n"));

        var groups = YamlParts.Group(yaml, 200, 5)!;

        Assert.Equal(3, groups.Count);
        Assert.All(groups, g => Assert.True(g.Length <= 200));
        Assert.Equal(yaml, YamlParts.JoinGroups(groups));
        Assert.Null(YamlParts.Group(yaml, 200, 2));
        Assert.Null(YamlParts.Group(yaml, 50, 5));
    }

    [Theory]
    [MemberData(nameof(TemplateCodecTests.StartingFileNames), MemberType = typeof(TemplateCodecTests))]
    public void YamlPages_NeverBreakALine_AndFitTheBudget(string name)
    {
        var yaml = Starting(name);
        var lines = yaml.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');

        var pages = YamlParts.Pages(yaml, 300);

        Assert.All(pages, p => Assert.True(p.Text.Length <= 300, $"page of {p.Text.Length}"));
        Assert.Equal(lines, pages.SelectMany(p => p.Text.Split('\n')));
        Assert.Equal(1, pages[0].FirstLine);
        for (var i = 1; i < pages.Count; i++)
            Assert.Equal(pages[i - 1].LastLine + 1, pages[i].FirstLine);
        Assert.Equal(lines.Length, pages[^1].LastLine);
    }

    [Fact]
    public void Diff_MarksRemovedAndAddedLines()
    {
        Assert.Equal(["- title: Old", "+ title: New"], YamlParts.Diff("id: a\ntitle: Old\n", "id: a\ntitle: New\n"));
        Assert.Empty(YamlParts.Diff("id: a\n", "id: a\n"));
    }

    [Fact]
    public void StatePacksAndUnpacks_EveryTab()
    {
        foreach (var tab in Enum.GetValues<WorkbenchTab>())
        {
            var state = new WorkbenchState(TemplateKind.Crisis, TemplateStatus.Archived, 3, tab, 2, "blue_death");
            Assert.Equal(state, WorkbenchState.Unpack(state.Pack(), "blue_death"));
        }
        Assert.Equal(WorkbenchState.Start, WorkbenchState.Unpack(WorkbenchState.Start.Pack(), ""));
    }

    [Fact]
    public void ALongTemplateId_FallsBackToASessionToken()
    {
        var sessions = new WorkbenchSessions();
        var id = new string('a', 120);
        var state = WorkbenchState.Start.Open(id);

        var customId = ContentWorkbenchView.CustomId(sessions, ContentWorkbenchView.ModalPrefix, "part", "1700000000-0123abcd.answers", state);

        Assert.True(customId.Length <= ContentWorkbenchView.MaxCustomId);
        var (action, arg, parsed) = ContentWorkbenchView.Parse(sessions, customId[(ContentWorkbenchView.ModalPrefix.Length + 1)..]);
        Assert.Equal("part", action);
        Assert.Equal("1700000000-0123abcd.answers", arg);
        Assert.Equal(state, parsed);
        Assert.Null(ContentWorkbenchView.Parse(new WorkbenchSessions(), customId[(ContentWorkbenchView.ModalPrefix.Length + 1)..]).State);
    }

    [Fact]
    public void EveryCustomIdThePanelBuilds_FitsAndIsUnique()
    {
        ContentRules.Import(Db, ContentRules.ImportContext(Db, Config), TemplateCodecTests.StartingFiles());
        var sessions = new WorkbenchSessions();
        var panels = new List<MessageComponent> { ContentWorkbenchView.Browser(Db, Config, sessions, WorkbenchState.Start) };
        foreach (var row in Db.GetTemplates())
            foreach (var tab in Enum.GetValues<WorkbenchTab>())
                panels.Add(ContentWorkbenchView.Template(Db, Config, sessions, WorkbenchState.Start.Open(row.Id) with { Tab = tab },
                    new WorkbenchNotice("Saved.", ReopenToken: "0123456789ab")));

        foreach (var panel in panels)
        {
            var ids = CustomIds(panel.Components).ToList();
            Assert.All(ids, id => Assert.True(id.Length <= ContentWorkbenchView.MaxCustomId, id));
            Assert.Equal(ids.Count, ids.Distinct().Count());
            Assert.True(Count(panel.Components) <= 40);
        }
    }

    [Fact]
    public void EditStamp_ChangesWithEverySave_EvenWithinASecond()
    {
        var context = ContentRules.ImportContext(Db, Config);
        ContentRules.ImportOne(Db, context, new ImportFile("a.yaml", Starting("issues/electoral_reform.yaml")));
        var before = ContentRules.EditStamp(Db.GetTemplate("electoral_reform")!);

        ContentRules.SaveEdit(Db, context, "electoral_reform", Starting("issues/electoral_reform.yaml").Replace("salience: 3", "salience: 4"));

        Assert.NotEqual(before, ContentRules.EditStamp(Db.GetTemplate("electoral_reform")!));
    }

    [Fact]
    public void SaveNew_RefusesAnIdThatExists()
    {
        var context = ContentRules.ImportContext(Db, Config);
        var yaml = Starting("issues/electoral_reform.yaml");
        Assert.True(ContentRules.SaveNew(Db, context, yaml).Imported);

        var again = ContentRules.SaveNew(Db, context, yaml.Replace("Electoral Reform", "Changed"));

        Assert.False(again.Imported);
        Assert.Contains("already exists", again.Errors.Single().Message);
        Assert.Contains("Electoral Reform", Db.GetTemplate("electoral_reform")!.Yaml);
        Assert.True(ContentRules.SaveNew(Db, context, YamlParts.SetId(yaml, "electoral_reform_copy")).Imported);
    }

    [Fact]
    public void Problems_PointAtTheLineOfAMissingReference()
    {
        var context = ContentRules.ImportContext(Db, Config);
        ContentRules.ImportOne(Db, context, new ImportFile("b.yaml", Starting("bills/act_of_union.yaml")));
        var row = Db.GetTemplate("act_of_union")!;

        var problems = ContentRules.Problems(Db, Db.BuildEffectContext(Config), Db.GetGameState(), row);

        var issue = problems.First(p => p.Message.Contains("`integration`"));
        Assert.Equal(3, issue.Line);
        Assert.Equal("issue", YamlParts.KeyAtLine(row.Yaml, issue.Line!.Value));
    }

    [Theory]
    [InlineData(TemplateKind.Issue)]
    [InlineData(TemplateKind.Bill)]
    [InlineData(TemplateKind.Project)]
    [InlineData(TemplateKind.Event)]
    [InlineData(TemplateKind.Crisis)]
    public void EverySkeleton_ParsesAsItsKind(TemplateKind kind)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "config", "skeletons", $"{kind.ToString().ToLowerInvariant()}.yaml");

        var parsed = TemplateCodec.Parse(File.ReadAllText(path), path, ContentRules.ImportContext(Db, Config));

        Assert.True(parsed.IsValid, string.Join("\n", parsed.Errors));
        Assert.Equal(kind, parsed.Template!.Kind);
    }

    private static IEnumerable<string> CustomIds(IEnumerable<IMessageComponent> components)
    {
        foreach (var component in components)
        {
            switch (component)
            {
                case ButtonComponent { CustomId: { } id }:
                    yield return id;
                    break;
                case SelectMenuComponent menu:
                    yield return menu.CustomId;
                    break;
                case ContainerComponent container:
                    foreach (var id in CustomIds(container.Components)) yield return id;
                    break;
                case ActionRowComponent row:
                    foreach (var id in CustomIds(row.Components)) yield return id;
                    break;
            }
        }
    }

    private static int Count(IEnumerable<IMessageComponent> components) =>
        components.Sum(c => 1 + c switch
        {
            ContainerComponent container => Count(container.Components),
            ActionRowComponent row => Count(row.Components),
            _ => 0,
        });
}
