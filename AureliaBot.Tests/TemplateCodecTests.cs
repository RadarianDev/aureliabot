using System.Collections;
using Xunit;

namespace AureliaBot.Tests;

public class TemplateCodecTests : IDisposable
{
    private readonly DatabaseFixture _fixture = new();
    private DatabaseService Db => _fixture.Db;
    private static GameConfig Config => DatabaseFixture.Config;

    public void Dispose() => _fixture.Dispose();

    public static string ContentDirectory => Path.Combine(AppContext.BaseDirectory, "content");

    public static IEnumerable<ImportFile> StartingFiles() =>
        Directory.GetFiles(ContentDirectory, "*.yaml", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => new ImportFile(Path.GetRelativePath(ContentDirectory, f).Replace('\\', '/'), File.ReadAllText(f)));

    public static TheoryData<string> StartingFileNames()
    {
        var data = new TheoryData<string>();
        foreach (var file in StartingFiles()) data.Add(file.Name);
        return data;
    }

    private static ImportFile Starting(string name) => StartingFiles().Single(f => f.Name == name);

    private EffectContext ImportContext => ContentRules.ImportContext(Db, Config);

    private const string Crisis = """
        id: dock_strike_carden
        title: The Carden Dock Strike
        ministry: CommerceLabor
        province: north_arpal
        issue: labour
        answers:
          - lean: For
            label: Meet the strikers' demands
            price: 3
            text: The docks reopen within the week, and the owners are furious.
            effects: { discontent.north_arpal: -2, economy: -1, condition.north_arpal.striking: -1 }
          - lean: Neutral
            label: Commission a royal inquiry
            price: 4
            text: Learned gentlemen take evidence, and the dockers wait.
            effects: { discontent.north_arpal: -1 }
          - lean: Against
            label: Declare the strike unlawful
            text: The strike collapses, and so does trust in Carden.
            effects: { discontent.north_arpal: 2, condition.north_arpal.striking: -1 }
        complication:
          text: The money runs out mid-negotiation, and the strike spreads.
          effects: { discontent.north_arpal: 1, discontent.iracterie: 1 }
        default:
          text: The strike drags on unanswered, and ships divert to Mugi.
          effects: { economy: -2, discontent.north_arpal: 2 }
        """;

    [Fact]
    public void StartingContent_IsPresent()
    {
        var names = StartingFiles().Select(f => f.Name).ToList();
        Assert.Equal(6, names.Count(n => n.StartsWith("issues/")));
        Assert.Equal(17, names.Count(n => n.StartsWith("bills/")));
        Assert.Equal(6, names.Count(n => n.StartsWith("projects/")));
        Assert.Contains("events/opening_1850.yaml", names);
    }

    [Fact]
    public void StartingContent_ImportsCleanly_AndValidatesFully()
    {
        var outcomes = ContentRules.Import(Db, ImportContext, StartingFiles());
        Assert.All(outcomes, o => Assert.True(o.Imported, string.Join("\n", o.Errors)));
        Assert.Equal(StartingFiles().Count(), Db.GetTemplates().Count);
        Assert.All(Db.GetTemplates(), t => Assert.Equal(TemplateStatus.Draft, t.Status));

        var full = Db.BuildEffectContext(Config);
        foreach (var row in Db.GetTemplates())
        {
            var parsed = TemplateCodec.Parse(row.Yaml, row.Id, full);
            Assert.True(parsed.IsValid, string.Join("\n", parsed.Errors));
        }
    }

    [Theory]
    [MemberData(nameof(StartingFileNames))]
    public void StartingFile_KindIsRecognisedFromItsFolder(string name)
    {
        var parsed = TemplateCodec.Parse(Starting(name).Yaml, name);
        Assert.True(parsed.IsValid, string.Join("\n", parsed.Errors));
        Assert.Equal(name.Split('/')[0], TemplateCodec.FolderOf(parsed.Template!.Kind));
        Assert.Equal(Path.GetFileNameWithoutExtension(name), parsed.Template.Id);
    }

    [Theory]
    [MemberData(nameof(StartingFileNames))]
    public void StartingFile_RoundTripsThroughSerialize(string name)
    {
        var original = TemplateCodec.Parse(Starting(name).Yaml, name).Template!;
        var yaml = TemplateCodec.Serialize(original);
        var again = TemplateCodec.Parse(yaml, name, ImportContext);
        Assert.True(again.IsValid, yaml + "\n" + string.Join("\n", again.Errors));
        AssertSameContent(original, again.Template!);
    }

    [Fact]
    public void Crisis_IsRecognised_AndRoundTrips()
    {
        var parsed = TemplateCodec.Parse(Crisis, "crisis.yaml", ImportContext);
        Assert.True(parsed.IsValid, string.Join("\n", parsed.Errors));
        var crisis = Assert.IsType<CrisisTemplate>(parsed.Template);
        Assert.Equal(3, crisis.Answers.Count);
        Assert.Equal(0m, crisis.Answers[2].Price);
        Assert.Equal(Lean.Neutral, crisis.Answers[1].Lean);
        Assert.Equal(Ministry.CommerceLabor, crisis.Ministry);
        Assert.NotNull(crisis.Complication);

        var again = TemplateCodec.Parse(TemplateCodec.Serialize(crisis), "crisis.yaml");
        Assert.True(again.IsValid, string.Join("\n", again.Errors));
        AssertSameContent(crisis, again.Template!);
    }

    [Theory]
    [InlineData("id: a\ntitle: T\nfor: F\nagainst: A\nopinion: 0\nsalience: 1\n", TemplateKind.Issue)]
    [InlineData("id: a\ntitle: T\ntext: Story.\n", TemplateKind.Event)]
    [InlineData("id: a\ntitle: T\nprice: 1\nterm: 1\nprovince: aurenten\n", TemplateKind.Project)]
    [InlineData("id: a\ntitle: T\nissue: labour\nlean: Against\nmotion: M\npass: { text: P }\n", TemplateKind.Bill)]
    public void Kind_IsRecognisedFromFields(string yaml, TemplateKind kind)
    {
        var parsed = TemplateCodec.Parse(yaml, "x.yaml");
        Assert.True(parsed.IsValid, string.Join("\n", parsed.Errors));
        Assert.Equal(kind, parsed.Template!.Kind);
    }

    [Fact]
    public void UnknownKind_IsAnError()
    {
        var parsed = TemplateCodec.Parse("id: a\ntitle: T\n", "x.yaml");
        Assert.False(parsed.IsValid);
        Assert.Contains("kind of template", parsed.Errors.Single().Message);
    }

    [Fact]
    public void Lines_AreOneBased()
    {
        var parsed = TemplateCodec.Parse("id: a\ntitle: T\nfor: F\nagainst: A\nopinion: 2\nsalience: 1\n", "x.yaml");
        var error = Assert.Single(parsed.Errors);
        Assert.Equal(5, error.Line);
        Assert.Equal("x.yaml, line 5: `opinion` must be from -1 to 1, not 2.", error.ToString());
    }

    [Fact]
    public void SyntaxError_ReportsItsLine()
    {
        var parsed = TemplateCodec.Parse("id: a\ntitle: T\ntext: \"unclosed\neffects: {}\n", "x.yaml");
        Assert.False(parsed.IsValid);
        Assert.NotNull(parsed.Errors[0].Line);
        Assert.StartsWith("YAML syntax", parsed.Errors[0].Message);
    }

    [Fact]
    public void BadFile_ReportsItsLine_AndBlocksOnlyItself()
    {
        var bad = new ImportFile("issues/bad.yaml", "id: bad\ntitle: Bad\nfor: F\nagainst: A\nopinion: 0\nsalience: 9\n");
        var outcomes = ContentRules.Import(Db, ImportContext, [Starting("issues/social_reform.yaml"), bad, Starting("bills/ten_hours_bill.yaml")]);

        Assert.True(outcomes[0].Imported);
        Assert.False(outcomes[1].Imported);
        Assert.True(outcomes[2].Imported);
        Assert.Equal(6, Assert.Single(outcomes[1].Errors).Line);
        Assert.Null(Db.GetTemplate("bad"));
        Assert.NotNull(Db.GetTemplate("social_reform"));
        Assert.NotNull(Db.GetTemplate("ten_hours_bill"));
        Assert.Contains("issues/bad.yaml, line 6", ContentRules.FormatImport(outcomes));
    }

    [Theory]
    [InlineData("id: a\ntitle: T\nfor: F\nopinion: 0\nsalience: 1\n", "`against` is required.")]
    [InlineData("id: a\ntitle: T\nfor: F\nagainst: A\nopinion: -1.5\nsalience: 1\n", "`opinion` must be from -1 to 1")]
    [InlineData("id: a\ntitle: T\nfor: F\nagainst: A\nopinion: 0\nsalience: -1\n", "`salience` must be from 0 to 5")]
    [InlineData("id: a\ntitle: T\nfor: F\nagainst: A\nopinion: lots\nsalience: 1\n", "`opinion` must be a number")]
    [InlineData("id: Bad-Id\ntitle: T\nfor: F\nagainst: A\nopinion: 0\nsalience: 1\n", "`id` must be a slug")]
    [InlineData("id: a\ntitle: T\nfor: F\nagainst: A\nopinion: 0\nsalience: 1\nbaseline: 0\n", "unknown field `baseline`")]
    [InlineData("id: a\ntitle:\nfor: F\nagainst: A\nopinion: 0\nsalience: 1\n", "`title` is empty.")]
    [InlineData("id: a\ntitle: T\nissue: labour\nlean: Neutral\nmotion: M\npass: { text: P }\n", "`lean` must be `For` or `Against`")]
    [InlineData("id: a\ntitle: T\nissue: labour\nlean: For\nmotion: M\n", "`pass` is required.")]
    [InlineData("id: a\ntitle: T\nissue: labour\nlean: For\nmotion: M\npass: { effects: { economy: 1 } }\n", "pass.`text` is required.")]
    [InlineData("id: a\ntitle: T\nprice: -1\nterm: 1\nprovince: aurenten\n", "`price` can't be negative.")]
    [InlineData("id: a\ntitle: T\nprice: 1\nterm: 0\nprovince: aurenten\n", "`term` must be at least 1.")]
    [InlineData("id: a\ntitle: T\nprice: 1\nterm: 1\npurse: Navy\n", "the Navy has no minister")]
    [InlineData("id: a\ntitle: T\nprice: 1\nterm: 1\npurse: Admiralty\n", "`purse` must be `War`, `Interior`, `CommerceLabor` or `Treasury`")]
    [InlineData("id: a\ntitle: T\nprice: 1\nterm: 1\nbonus: { text: Hi }\n", "exactly one deadline")]
    [InlineData("id: a\ntitle: T\nprice: 1\nterm: 1\nbonus: { within: 2, by: { season: Spring, year: 1851 } }\n", "exactly one deadline")]
    [InlineData("id: a\ntitle: T\nprice: 1\nterm: 1\neffects: { discontent.here: -1 }\n", "`here` needs a `province`")]
    [InlineData("id: a\ntitle: T\ntext: S\nschedule: { delay: 1, season: Spring, year: 1850 }\n", "`schedule` takes either")]
    [InlineData("id: a\ntitle: T\ntext: S\nschedule: { season: Spring }\n", "schedule.`year` is required.")]
    [InlineData("id: a\ntitle: T\ntext: S\nschedule: { season: Midsummer, year: 1850 }\n", "schedule.`season` must be")]
    [InlineData("id: a\ntitle: T\ntext: S\nschedule: { delay: -1 }\n", "schedule.`delay` must be at least 0.")]
    public void RequiredFieldAndRangeErrors(string yaml, string expected)
    {
        var parsed = TemplateCodec.Parse(yaml, "x.yaml");
        Assert.False(parsed.IsValid);
        Assert.Contains(parsed.Errors, e => e.Message.Contains(expected));
        Assert.All(parsed.Errors, e => Assert.NotNull(e.Line));
    }

    [Fact]
    public void Crisis_NeedsTwoAnswersAndADefault()
    {
        var yaml = """
            id: c
            title: C
            ministry: War
            province: aurenten
            issue: army
            answers:
              - lean: Sideways
                label: Only one
                text: T
            """;
        var parsed = TemplateCodec.Parse(yaml, "c.yaml");
        Assert.Contains(parsed.Errors, e => e.Message.Contains("at least two answers"));
        Assert.Contains(parsed.Errors, e => e.Message == "`default` is required.");
        Assert.Contains(parsed.Errors, e => e.Line == 7 && e.Message.Contains("answer 1: `lean` must be `For`, `Against` or `Neutral`"));
    }

    [Theory]
    [InlineData("events/x.yaml", "id: x\ntitle: T\ntext: S\neffects: { economy: 1,\n  wealth: 2 }\n", 5, "`wealth`: is not an effect key.")]
    [InlineData("events/x.yaml", "id: x\ntitle: T\ntext: S\neffects:\n  economy: 1\n  discontent.here: 1\n", 6, "`here` only works in a project's effects.")]
    [InlineData("bills/x.yaml", "id: x\ntitle: T\nissue: labour\nlean: For\nmotion: M\npass:\n  text: P\n  effects: { crisis.dock_strike_carden: true }\n", 8, "only events can post a crisis.")]
    [InlineData("events/x.yaml", "id: x\ntitle: T\ntext: S\neffects: { condition.aurenten.lawless: 2 }\n", 4, "a condition takes 1 (gives it) or -1 (removes it).")]
    [InlineData("events/x.yaml", "id: x\ntitle: T\ntext: S\neffects: { purse.Navy: 2 }\n", 4, "the Navy purse is a contribution")]
    [InlineData("events/x.yaml", "id: x\ntitle: T\ntext: S\neffects: { discontent.atlantis: 2 }\n", 4, "unknown province `atlantis`.")]
    [InlineData("events/x.yaml", "id: x\ntitle: T\ntext: S\neffects: { condition.aurenten.haunted: 1 }\n", 4, "unknown condition `haunted`.")]
    public void EffectErrors_CarryTheEffectsLine(string file, string yaml, int line, string expected)
    {
        var parsed = TemplateCodec.Parse(yaml, file, ImportContext);
        Assert.False(parsed.IsValid);
        var error = Assert.Single(parsed.Errors);
        Assert.Equal(line, error.Line);
        Assert.Contains(expected, error.Message);
    }

    [Fact]
    public void ProjectEffects_AllowHere_AndEventsAllowCrisis()
    {
        Assert.True(TemplateCodec.Parse(Starting("projects/fen_drainage.yaml").Yaml, "p.yaml", ImportContext).IsValid);
        Assert.True(TemplateCodec.Parse("id: e\ntitle: T\ntext: S\neffects: { crisis.anything: true }\n", "e.yaml", ImportContext).IsValid);
    }

    [Fact]
    public void ImportContext_SkipsTemplateReferences_ButFullContextChecksThem()
    {
        var yaml = "id: e\ntitle: T\ntext: S\neffects: { open.nowhere: true, bill.nothing: true, opinion.nothing: 0.1 }\n";
        Assert.True(TemplateCodec.Parse(yaml, "e.yaml", ImportContext).IsValid);
        var full = TemplateCodec.Parse(yaml, "e.yaml", Db.BuildEffectContext(Config));
        Assert.Equal(3, full.Errors.Count);
    }

    [Fact]
    public void ReimportingAnId_ReplacesIt_AndReturnsReadyToDraft()
    {
        var first = ContentRules.ImportOne(Db, ImportContext, Starting("issues/social_reform.yaml"));
        Assert.False(first.Replaced);
        Db.SetTemplateStatus("social_reform", TemplateStatus.Ready);

        var changed = Starting("issues/social_reform.yaml") with { Yaml = Starting("issues/social_reform.yaml").Yaml.Replace("salience: 3", "salience: 4") };
        var second = ContentRules.ImportOne(Db, ImportContext, changed);

        Assert.True(second.Replaced);
        Assert.Equal(TemplateStatus.Ready, second.PreviousStatus);
        var row = Db.GetTemplate("social_reform")!;
        Assert.Equal(TemplateStatus.Draft, row.Status);
        Assert.Contains("salience: 4", row.Yaml);
        Assert.Contains("returned to Draft", ContentRules.Describe(second));
    }

    [Fact]
    public void Edit_WithANewId_SavesACopy()
    {
        ContentRules.ImportOne(Db, ImportContext, Starting("issues/social_reform.yaml"));
        Db.SetTemplateStatus("social_reform", TemplateStatus.Ready);
        var yaml = Db.GetTemplate("social_reform")!.Yaml.Replace("id: social_reform", "id: social_reform_two");

        var edit = ContentRules.SaveEdit(Db, ImportContext, "social_reform", yaml);

        Assert.True(edit.Copy);
        Assert.Equal(TemplateStatus.Ready, Db.GetTemplate("social_reform")!.Status);
        Assert.Equal(TemplateStatus.Draft, Db.GetTemplate("social_reform_two")!.Status);
    }

    [Fact]
    public void Export_ImportsBackUnchanged()
    {
        ContentRules.Import(Db, ImportContext, StartingFiles());
        var before = Db.GetTemplates().ToDictionary(t => t.Id, t => t.Yaml);

        var files = ContentRules.ReadArchive(ContentRules.Export(Db.GetTemplates()));
        Assert.Equal(before.Count, files.Count);
        Assert.Contains(files, f => f.Name == "issues/social_reform.yaml");
        Assert.Contains(files, f => f.Name == "events/opening_1850.yaml");

        var outcomes = ContentRules.Import(Db, ImportContext, files);
        Assert.All(outcomes, o => Assert.True(o.Imported && o.Replaced));
        Assert.Equal(before, Db.GetTemplates().ToDictionary(t => t.Id, t => t.Yaml));
    }

    [Theory]
    [InlineData("Plain words, with commas")]
    [InlineData("Colon: then space")]
    [InlineData("- starts like a list")]
    [InlineData("\"Quoted\" and 'single' and back\\slash")]
    [InlineData("Ends with a colon:")]
    [InlineData("Has a # comment marker")]
    [InlineData("Two lines\nof text")]
    [InlineData(" leading space")]
    [InlineData("trailing space ")]
    [InlineData("tab\tinside")]
    [InlineData("true")]
    [InlineData("1850")]
    [InlineData("{ looks: like a map }")]
    [InlineData("Ends with newline\n")]
    [InlineData("Line one\n  indented line\n\nafter a blank")]
    public void Serialize_QuotesAnyText(string text)
    {
        var issue = new IssueTemplate("tricky", text, text, "Against.", -0.35, 2.5);
        var parsed = TemplateCodec.Parse(TemplateCodec.Serialize(issue), "tricky.yaml");
        Assert.True(parsed.IsValid, TemplateCodec.Serialize(issue) + string.Join("\n", parsed.Errors));
        AssertSameContent(issue, parsed.Template!);
    }

    [Fact]
    public void A_summary_survives_a_round_trip()
    {
        var issue = new IssueTemplate("tricky", "Tricky", "For.", "Against.", 0, 1) { Summary = "What it's about: this, and that." };

        var parsed = TemplateCodec.Parse(TemplateCodec.Serialize(issue), "tricky.yaml");

        Assert.True(parsed.IsValid, string.Join("\n", parsed.Errors));
        Assert.Equal(issue.Summary, parsed.Template!.Summary);
    }

    [Fact]
    public void Every_starting_issue_bill_crisis_and_project_has_a_summary()
    {
        var missing = StartingFiles()
            .Where(f => !f.Name.StartsWith("events/"))
            .Where(f => TemplateCodec.Parse(f.Yaml, f.Name).Template?.Summary is null)
            .Select(f => f.Name);

        Assert.Empty(missing);
    }

    [Fact]
    public void Serialize_WrapsLongEffectMaps()
    {
        var opening = TemplateCodec.Parse(Starting("events/opening_1850.yaml").Yaml, "o.yaml").Template!;
        var yaml = TemplateCodec.Serialize(opening);
        Assert.All(yaml.Split('\n'), line => Assert.True(line.Length <= 100 || !line.Contains("effects"), line));
        Assert.DoesNotContain("schedule", yaml);
    }

    internal static void AssertSameContent(object? expected, object? actual)
    {
        if (expected is null || actual is null)
        {
            Assert.Equal(expected, actual);
            return;
        }
        if (expected is string || expected.GetType().IsPrimitive || expected is decimal || expected is Enum)
        {
            Assert.Equal(expected, actual);
            return;
        }
        if (expected is IEnumerable expectedItems)
        {
            var left = expectedItems.Cast<object?>().ToList();
            var right = ((IEnumerable)actual).Cast<object?>().ToList();
            Assert.Equal(left.Count, right.Count);
            for (var i = 0; i < left.Count; i++) AssertSameContent(left[i], right[i]);
            return;
        }
        Assert.Equal(expected.GetType(), actual.GetType());
        foreach (var property in expected.GetType().GetProperties().Where(p => p.GetIndexParameters().Length == 0))
        {
            if (property.Name == "EqualityContract") continue;
            AssertSameContent(property.GetValue(expected), property.GetValue(actual));
        }
    }
}
