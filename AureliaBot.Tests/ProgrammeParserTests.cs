using Xunit;

namespace AureliaBot.Tests;

public class ProgrammeParserTests
{
    private static Issue MakeIssue(string id, string title, IssueStatus status = IssueStatus.Open) =>
        new(id, id, title, "for", "against", 0, 1, null, status);

    private static readonly Issue Labour = MakeIssue("condition_of_labour", "The Condition of Labour");
    private static readonly Issue FreeTrade = MakeIssue("free_trade", "Free Trade");
    private static readonly Issue Franchise = MakeIssue("franchise", "The Franchise");
    private static readonly Issue PoorRelief = MakeIssue("poor_relief", "Poor Relief");
    private static readonly Issue Tariffs = MakeIssue("tariffs", "Trade Tariffs");
    private static readonly Issue Corn = MakeIssue("corn_laws", "The Corn Laws", IssueStatus.Closed);

    private static readonly List<Issue> Open = [Labour, FreeTrade, Franchise, PoorRelief, Tariffs];
    private static readonly List<Issue> All = [Labour, FreeTrade, Franchise, PoorRelief, Tariffs, Corn];

    private static ProgrammeParseResult Parse(string text) => ProgrammeParser.Parse(text, Open, All, ProgrammeVocabulary.Default);

    private static Stance StanceOf(string line, string issueId)
    {
        var result = Parse(line);
        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.Reason)));
        Assert.Empty(result.Ignored);
        return result.Stances.GetValueOrDefault(issueId, Stance.Silent);
    }

    [Fact]
    public void SpecExample_Parses()
    {
        var result = Parse("## Liberal Union\nThe Condition of Labour: *Strongly For*\nFree Trade: *Against*\nThe Franchise: *For*");

        Assert.True(result.IsValid);
        Assert.Equal(3, result.Stances.Count);
        Assert.Equal(Stance.StronglyFor, result.Stances["condition_of_labour"]);
        Assert.Equal(Stance.Against, result.Stances["free_trade"]);
        Assert.Equal(Stance.For, result.Stances["franchise"]);
        Assert.Empty(result.Ignored);
    }

    [Fact]
    public void HeaderAndBlankLines_AreIgnoredSilently()
    {
        var result = Parse("# Party\n\n### Our programme: the future\n   \r\n> ## Quoted header\n---\n<:flag:123456>\nFree Trade: For\r\n");

        Assert.True(result.IsValid);
        Assert.Single(result.Stances);
        Assert.Equal(Stance.For, result.Stances["free_trade"]);
        Assert.Empty(result.Ignored);
    }

    [Theory]
    [InlineData("free trade: For")]
    [InlineData("FREE TRADE: For")]
    [InlineData("free_trade: For")]
    [InlineData("Free-Trade: For")]
    [InlineData("The Free Trade: For")]
    [InlineData("Free Trade!: For")]
    public void Issue_MatchesTitleOrIdIgnoringCaseAndPunctuation(string line)
    {
        Assert.Equal(Stance.For, StanceOf(line, "free_trade"));
    }

    [Theory]
    [InlineData("Condition of Labour: For")]
    [InlineData("condition_of_labour: For")]
    [InlineData("Labour: For")]
    [InlineData("Condition: For")]
    [InlineData("Franchise: For", "franchise")]
    [InlineData("Relief: For", "poor_relief")]
    public void Issue_MatchesWithoutTheOrByAUniquePart(string line, string issueId = "condition_of_labour")
    {
        Assert.Equal(Stance.For, StanceOf(line, issueId));
    }

    [Fact]
    public void Issue_AmbiguousPart_IsAnErrorNamingTheCandidates()
    {
        var result = Parse("Trade: For");

        var error = Assert.Single(result.Errors);
        Assert.Contains("Free Trade", error.Reason);
        Assert.Contains("Trade Tariffs", error.Reason);
    }

    [Fact]
    public void Issue_PartialMatchesWholeWordsOnly()
    {
        var result = Parse("Lab: For");

        var error = Assert.Single(result.Errors);
        Assert.Contains("unknown", error.Reason);
    }

    [Theory]
    [InlineData("*Strongly For*", Stance.StronglyFor)]
    [InlineData("__against__", Stance.Against)]
    [InlineData("**For**", Stance.For)]
    [InlineData("  STRONGLY   against  ", Stance.StronglyAgainst)]
    [InlineData("_for_", Stance.For)]
    [InlineData("***Strongly For***", Stance.StronglyFor)]
    [InlineData("~~For~~", Stance.For)]
    [InlineData("`Against`", Stance.Against)]
    [InlineData("||Strongly Against||", Stance.StronglyAgainst)]
    [InlineData("\"For\"", Stance.For)]
    [InlineData("“Against”", Stance.Against)]
    [InlineData("For.", Stance.For)]
    [InlineData("Against!", Stance.Against)]
    [InlineData("(Strongly) For", Stance.StronglyFor)]
    [InlineData("For ✅", Stance.For)]
    [InlineData("❌ Against", Stance.Against)]
    [InlineData("👍🏽 For", Stance.For)]
    [InlineData("For <:yes:123456789>", Stance.For)]
    [InlineData("<a:dance:123> Strongly For", Stance.StronglyFor)]
    public void Opinion_IgnoresFormatting(string opinion, Stance expected)
    {
        Assert.Equal(expected, StanceOf($"Free Trade: {opinion}", "free_trade"));
    }

    [Theory]
    [InlineData("for", Stance.For)]
    [InlineData("support", Stance.For)]
    [InlineData("Supports", Stance.For)]
    [InlineData("supportive", Stance.For)]
    [InlineData("in favour", Stance.For)]
    [InlineData("In Favor Of", Stance.For)]
    [InlineData("in favour of it", Stance.For)]
    [InlineData("pro", Stance.For)]
    [InlineData("Yes", Stance.For)]
    [InlineData("aye", Stance.For)]
    [InlineData("approve", Stance.For)]
    [InlineData("+", Stance.For)]
    [InlineData("+1", Stance.For)]
    [InlineData("against", Stance.Against)]
    [InlineData("oppose", Stance.Against)]
    [InlineData("opposes", Stance.Against)]
    [InlineData("opposed", Stance.Against)]
    [InlineData("opposed to", Stance.Against)]
    [InlineData("anti", Stance.Against)]
    [InlineData("No", Stance.Against)]
    [InlineData("nay", Stance.Against)]
    [InlineData("reject", Stance.Against)]
    [InlineData("-", Stance.Against)]
    [InlineData("-1", Stance.Against)]
    [InlineData("strongly for", Stance.StronglyFor)]
    [InlineData("strongly support", Stance.StronglyFor)]
    [InlineData("very much in favour of", Stance.StronglyFor)]
    [InlineData("firmly against", Stance.StronglyAgainst)]
    [InlineData("fiercely opposed", Stance.StronglyAgainst)]
    [InlineData("staunchly anti", Stance.StronglyAgainst)]
    [InlineData("wholly for", Stance.StronglyFor)]
    [InlineData("fully support", Stance.StronglyFor)]
    [InlineData("strong support", Stance.StronglyFor)]
    [InlineData("For, strongly", Stance.StronglyFor)]
    [InlineData("++", Stance.StronglyFor)]
    [InlineData("+2", Stance.StronglyFor)]
    [InlineData("--", Stance.StronglyAgainst)]
    [InlineData("-2", Stance.StronglyAgainst)]
    [InlineData("we are firmly against", Stance.StronglyAgainst)]
    [InlineData("We're for it", Stance.For)]
    [InlineData("the party stands against this", Stance.Against)]
    public void Opinion_AcceptsAliases(string opinion, Stance expected)
    {
        Assert.Equal(expected, StanceOf($"Free Trade: {opinion}", "free_trade"));
    }

    [Theory]
    [InlineData("Silent")]
    [InlineData("non-aligned")]
    [InlineData("Nonaligned")]
    [InlineData("non aligned")]
    [InlineData("Neutral")]
    [InlineData("no stance")]
    [InlineData("abstain")]
    [InlineData("undecided")]
    [InlineData("none")]
    [InlineData("N/A")]
    [InlineData("0")]
    [InlineData("*Silent*")]
    public void Silent_IsAcceptedAndRecordsNoStance(string opinion)
    {
        var result = Parse($"Free Trade: {opinion}\nThe Franchise: For");

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.Reason)));
        Assert.False(result.Stances.ContainsKey("free_trade"));
        Assert.Equal(Stance.For, result.Stances["franchise"]);
    }

    [Fact]
    public void Silent_StillCountsForDuplicates()
    {
        var result = Parse("Free Trade: Silent\nFree Trade: For");

        var error = Assert.Single(result.Errors);
        Assert.Equal(2, error.LineNumber);
        Assert.Contains("line 1", error.Reason);
    }

    [Fact]
    public void StrongSilent_IsNotAnOpinion()
    {
        var result = Parse("Free Trade: strongly neutral");

        var error = Assert.Single(result.Errors);
        Assert.Contains("unrecognised", error.Reason);
    }

    [Theory]
    [InlineData("- Free Trade: For")]
    [InlineData("* Free Trade: For")]
    [InlineData("• Free Trade: For")]
    [InlineData("•Free Trade: For")]
    [InlineData("1. Free Trade: For")]
    [InlineData("12) Free Trade: For")]
    [InlineData("> Free Trade: For")]
    [InlineData("> - **Free Trade**: For")]
    [InlineData("-# Free Trade: For")]
    [InlineData("**1.** Free Trade: For")]
    [InlineData("\t  Free   Trade  :   For  ")]
    [InlineData("🇦🇺 Free Trade: For")]
    [InlineData("\\*Free Trade\\*: For")]
    public void Line_IgnoresBulletsQuotesAndMarkers(string line)
    {
        Assert.Equal(Stance.For, StanceOf(line, "free_trade"));
    }

    [Theory]
    [InlineData("Free Trade: Against")]
    [InlineData("Free Trade - Against")]
    [InlineData("Free Trade – Against")]
    [InlineData("Free Trade — Against")]
    [InlineData("Free Trade—Against")]
    [InlineData("Free Trade -- Against")]
    [InlineData("Free Trade = Against")]
    [InlineData("Free Trade -> Against")]
    [InlineData("Free Trade --> Against")]
    [InlineData("Free Trade => Against")]
    [InlineData("Free Trade → Against")]
    [InlineData("Free Trade | Against")]
    [InlineData("Free Trade\tAgainst")]
    [InlineData("Free Trade:Against")]
    [InlineData("Free Trade :: Against")]
    public void Separators_AreAllAccepted(string line)
    {
        Assert.Equal(Stance.Against, StanceOf(line, "free_trade"));
    }

    [Theory]
    [InlineData("Against: Free Trade", Stance.Against)]
    [InlineData("Strongly for: Free Trade", Stance.StronglyFor)]
    [InlineData("Against — The Franchise", Stance.Against, "franchise")]
    [InlineData("**Strongly Against** - Poor Relief", Stance.StronglyAgainst, "poor_relief")]
    public void ReversedOrder_IsAccepted(string line, Stance expected, string issueId = "free_trade")
    {
        Assert.Equal(expected, StanceOf(line, issueId));
    }

    [Theory]
    [InlineData("Strongly support free trade", Stance.StronglyFor)]
    [InlineData("We oppose Free Trade", Stance.Against)]
    [InlineData("In favour of Free Trade", Stance.For)]
    [InlineData("Opposed to free trade.", Stance.Against)]
    [InlineData("Free Trade strongly for", Stance.StronglyFor)]
    [InlineData("Free Trade: we strongly support", Stance.StronglyFor)]
    [InlineData("For Free Trade!", Stance.For)]
    [InlineData("No stance on Free Trade", Stance.Silent)]
    [InlineData("Neutral on free trade", Stance.Silent)]
    public void NoSeparator_OpinionStartingOrEndingTheLine(string line, Stance expected)
    {
        Assert.Equal(expected, StanceOf(line, "free_trade"));
    }

    [Fact]
    public void TrailingCommentary_AfterAnotherSeparator_IsAccepted()
    {
        Assert.Equal(Stance.For, StanceOf("Free Trade: For - it brings prosperity to all", "free_trade"));
        Assert.Equal(Stance.StronglyFor, StanceOf("Strongly support free trade - markets bring wealth", "free_trade"));
    }

    [Fact]
    public void ProseLines_AreIgnoredAndReported()
    {
        var result = Parse(string.Join("\n",
            "## Liberal Union",
            "Our principles are as follows:",
            "Free Trade: For",
            "We believe in liberty and progress.",
            "For the glory of Aurelia!",
            "Railways: maybe"));

        Assert.True(result.IsValid);
        Assert.Single(result.Stances);
        Assert.Equal([2, 4, 5, 6], result.Ignored.Select(i => i.LineNumber));
        Assert.Equal("Our principles are as follows:", result.Ignored[0].Text);
    }

    [Fact]
    public void MissingIssues_HaveNoStance()
    {
        var result = Parse("Free Trade: For");

        Assert.True(result.IsValid);
        Assert.False(result.Stances.ContainsKey("franchise"));
        Assert.False(result.Stances.ContainsKey("condition_of_labour"));
        Assert.False(result.Stances.ContainsKey("poor_relief"));
    }

    [Fact]
    public void EmptyMessage_IsAValidProgrammeWithNoStances()
    {
        var result = Parse("## Silent Party");

        Assert.True(result.IsValid);
        Assert.Empty(result.Stances);
    }

    [Fact]
    public void EveryError_IsReportedTogetherWithLineNumbers()
    {
        var result = Parse(string.Join("\n",
            "## Party",
            "Free Trade: For",
            "Railways: For",
            "The Corn Laws: Against",
            "free_trade: Against",
            "Poor Relief: maybe",
            "The Franchise",
            "Trade: Against",
            "Strongly For",
            "Some prose here."));

        Assert.False(result.IsValid);
        Assert.Empty(result.Stances);
        Assert.Equal([3, 4, 5, 6, 7, 8, 9], result.Errors.Select(e => e.LineNumber));
        Assert.Contains("unknown", result.Errors[0].Reason);
        Assert.Contains("Railways", result.Errors[0].Reason);
        Assert.Contains("closed", result.Errors[1].Reason);
        Assert.Contains("line 2", result.Errors[2].Reason);
        Assert.Contains("unrecognised", result.Errors[3].Reason);
        Assert.Contains("maybe", result.Errors[3].Reason);
        Assert.Contains("no opinion", result.Errors[4].Reason);
        Assert.Contains("could be any of", result.Errors[5].Reason);
        Assert.Contains("names no issue", result.Errors[6].Reason);
        Assert.Equal("Railways: For", result.Errors[0].Text);
        Assert.Equal([10], result.Ignored.Select(i => i.LineNumber));
    }

    [Fact]
    public void ClosedIssue_ByPartOfItsTitle_IsReportedAsClosed()
    {
        var error = Assert.Single(Parse("Corn Laws: For").Errors);
        Assert.Contains("closed", error.Reason);
    }

    [Fact]
    public void MixedProgramme_ParsesEveryForm()
    {
        var result = Parse(string.Join("\n",
            "# **The Liberal Union** 🕊️",
            "> Our programme for the coming Assembly:",
            "1. **The Condition of Labour** — *strongly support*",
            "2. Free Trade -> opposed",
            "3. ~~Franchise~~ | non-aligned",
            "• Against: Poor Relief"));

        Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.Reason)));
        Assert.Equal(Stance.StronglyFor, result.Stances["condition_of_labour"]);
        Assert.Equal(Stance.Against, result.Stances["free_trade"]);
        Assert.False(result.Stances.ContainsKey("franchise"));
        Assert.Equal(Stance.Against, result.Stances["poor_relief"]);
        Assert.Equal([2], result.Ignored.Select(i => i.LineNumber));
    }

    [Fact]
    public void CustomVocabulary_ReplacesAList()
    {
        var vocabulary = ProgrammeVocabulary.Default with { For = ["for", "hurrah"] };

        Assert.Equal(Stance.For, ProgrammeParser.Parse("Free Trade: hurrah", Open, All, vocabulary).Stances["free_trade"]);
        Assert.Single(ProgrammeParser.Parse("The Franchise: support", Open, All, vocabulary).Errors);
    }

    [Fact]
    public void Vocabulary_RejectsAPhraseInTwoLists()
    {
        var vocabulary = ProgrammeVocabulary.Default with { Silent = ["silent", "No"] };

        Assert.Contains(vocabulary.Validate(), e => e.Contains("'No'"));
    }

    [Fact]
    public void ShippedVocabularyFile_LoadsAndIsValid()
    {
        var vocabulary = ProgrammeVocabulary.Load(Path.Combine(AppContext.BaseDirectory, "config"));

        Assert.Empty(vocabulary.Validate());
        Assert.Equal(Stance.Silent, ProgrammeParser.ParseOpinion("non-aligned", vocabulary));
        Assert.Equal(Stance.StronglyFor, ProgrammeParser.ParseOpinion("strongly support", vocabulary));
        Assert.Equal(Stance.Against, ProgrammeParser.ParseOpinion("no", vocabulary));
        Assert.Equal(Stance.Silent, ProgrammeParser.ParseOpinion("0", vocabulary));
    }

    [Fact]
    public void VocabularyFile_ListsLeftOutKeepTheirDefaults()
    {
        var folder = Path.Combine(Path.GetTempPath(), "programme-vocabulary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            File.WriteAllText(Path.Combine(folder, ProgrammeVocabulary.File), "for: [hurrah]\n");
            var vocabulary = ProgrammeVocabulary.Load(folder);

            Assert.Equal(["hurrah"], vocabulary.For);
            Assert.Equal(ProgrammeVocabulary.Default.Against, vocabulary.Against);
            Assert.Same(ProgrammeVocabulary.Default, ProgrammeVocabulary.Load(Path.Combine(folder, "missing")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData("https://discord.com/channels/1/2/3")]
    [InlineData("https://ptb.discord.com/channels/1/2/3")]
    [InlineData("https://canary.discord.com/channels/1/2/3")]
    [InlineData("<https://discordapp.com/channels/1/2/3>")]
    public void MessageLink_Parses(string text)
    {
        Assert.True(ProgrammeParser.TryParseMessageLink(text, out var link));
        Assert.Equal(new MessageLink(1, 2, 3), link);
    }

    [Theory]
    [InlineData("https://discord.com/channels/1/2")]
    [InlineData("https://example.com/channels/1/2/3")]
    [InlineData("https://discord.com/channels/@me/2/3")]
    public void MessageLink_RejectsOtherText(string text)
    {
        Assert.False(ProgrammeParser.TryParseMessageLink(text, out _));
    }
}


public class ProgrammeFlipTests : IDisposable
{
    private readonly DatabaseFixture _fixture = new();
    private DatabaseService Db => _fixture.Db;

    public void Dispose() => _fixture.Dispose();

    private long SetUpParty()
    {
        Db.CreateFaction(1, "One", "aurenten", 10);
        Db.CreateFaction(2, "Two", "aurenten", 10);
        Db.OpenIssue("free_trade", "free_trade", "Free Trade", "for", "against", 0, 1);
        Db.OpenIssue("franchise", "franchise", "The Franchise", "for", "against", 0, 1);
        Db.OpenIssue("poor_relief", "poor_relief", "Poor Relief", "for", "against", 0, 1);
        return Db.CreateParty("Liberal Union", "#ffcc00", null, [1UL, 2UL]).PartyId!.Value;
    }

    private void LeavePreElection() => Db.SaveGameState(Db.GetGameState() with { Season = Season.Summer });

    [Fact]
    public void Flips_OnlySignChangesCount()
    {
        var before = new Dictionary<string, Stance>
        {
            ["a"] = Stance.For, ["b"] = Stance.StronglyAgainst, ["c"] = Stance.For, ["d"] = Stance.Against,
        };
        var after = new Dictionary<string, Stance>
        {
            ["a"] = Stance.StronglyAgainst, ["b"] = Stance.For, ["c"] = Stance.StronglyFor, ["e"] = Stance.For,
        };

        Assert.Equal(["a", "b"], DatabaseService.ProgrammeFlips(before, after));
    }

    [Fact]
    public void RecordProgramme_OutsidePreElection_ChargesEachMemberPerFlip()
    {
        var party = SetUpParty();
        Db.RecordProgramme(party, new Dictionary<string, Stance> { ["free_trade"] = Stance.For, ["franchise"] = Stance.Against }, null, DatabaseFixture.Constants);
        LeavePreElection();

        var result = Db.RecordProgramme(party,
            new Dictionary<string, Stance> { ["free_trade"] = Stance.StronglyAgainst, ["franchise"] = Stance.StronglyFor },
            "https://discord.com/channels/1/2/3", DatabaseFixture.Constants);

        var penalty = DatabaseFixture.Constants.Programme.FlipPenalty;
        Assert.Equal(["franchise", "free_trade"], result.FlippedIssueIds);
        Assert.Equal(2 * penalty, result.PenaltyPerMember);
        Assert.Equal(10 - 2 * penalty, Db.GetFaction(1)!.Popularity, 6);
        Assert.Equal(10 - 2 * penalty, Db.GetFaction(2)!.Popularity, 6);
        Assert.Equal(Stance.StronglyAgainst, Db.GetProgramme(party)["free_trade"]);
    }

    [Fact]
    public void RecordProgramme_AddingDroppingOrChangingStrength_IsFree()
    {
        var party = SetUpParty();
        Db.RecordProgramme(party, new Dictionary<string, Stance> { ["free_trade"] = Stance.For, ["franchise"] = Stance.Against }, null, DatabaseFixture.Constants);
        LeavePreElection();

        var result = Db.RecordProgramme(party,
            new Dictionary<string, Stance> { ["free_trade"] = Stance.StronglyFor, ["poor_relief"] = Stance.Against },
            null, DatabaseFixture.Constants);

        Assert.Empty(result.FlippedIssueIds);
        Assert.Equal(0, result.PenaltyPerMember);
        Assert.Equal(10, Db.GetFaction(1)!.Popularity, 6);
        var programme = Db.GetProgramme(party);
        Assert.False(programme.ContainsKey("franchise"));
        Assert.Equal(Stance.Against, programme["poor_relief"]);
    }

    [Fact]
    public void RecordProgramme_DuringPreElection_IsFree()
    {
        var party = SetUpParty();
        Assert.True(Db.GetGameState().IsPreElection);
        Db.RecordProgramme(party, new Dictionary<string, Stance> { ["free_trade"] = Stance.For }, null, DatabaseFixture.Constants);

        var result = Db.RecordProgramme(party, new Dictionary<string, Stance> { ["free_trade"] = Stance.StronglyAgainst }, null, DatabaseFixture.Constants);

        Assert.Equal(["free_trade"], result.FlippedIssueIds);
        Assert.Equal(0, result.PenaltyPerMember);
        Assert.Empty(result.PenalisedFactions);
        Assert.Equal(10, Db.GetFaction(1)!.Popularity, 6);
    }
}
