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
    private static readonly Issue Corn = MakeIssue("corn_laws", "The Corn Laws", IssueStatus.Closed);

    private static readonly List<Issue> Open = [Labour, FreeTrade, Franchise, PoorRelief];
    private static readonly List<Issue> All = [Labour, FreeTrade, Franchise, PoorRelief, Corn];

    private static ProgrammeParseResult Parse(string text) => ProgrammeParser.Parse(text, Open, All);

    [Fact]
    public void SpecExample_Parses()
    {
        var result = Parse("## Liberal Union\nThe Condition of Labour: *Strongly For*\nFree Trade: *Against*\nThe Franchise: *For*");

        Assert.True(result.IsValid);
        Assert.Equal(3, result.Stances.Count);
        Assert.Equal(Stance.StronglyFor, result.Stances["condition_of_labour"]);
        Assert.Equal(Stance.Against, result.Stances["free_trade"]);
        Assert.Equal(Stance.For, result.Stances["franchise"]);
    }

    [Fact]
    public void HeaderAndBlankLines_AreIgnored()
    {
        var result = Parse("# Party\n\n### Our programme: the future\n   \r\nFree Trade: For\r\n");

        Assert.True(result.IsValid);
        Assert.Single(result.Stances);
        Assert.Equal(Stance.For, result.Stances["free_trade"]);
    }

    [Fact]
    public void Issue_MatchesTitleOrIdIgnoringCase()
    {
        var result = Parse("free trade: For\nFRANCHISE: Against\nCondition_Of_Labour: Strongly Against");

        Assert.True(result.IsValid);
        Assert.Equal(Stance.For, result.Stances["free_trade"]);
        Assert.Equal(Stance.Against, result.Stances["franchise"]);
        Assert.Equal(Stance.StronglyAgainst, result.Stances["condition_of_labour"]);
    }

    [Theory]
    [InlineData("*Strongly For*", Stance.StronglyFor)]
    [InlineData("__against__", Stance.Against)]
    [InlineData("**For**", Stance.For)]
    [InlineData("  STRONGLY   against  ", Stance.StronglyAgainst)]
    [InlineData("_for_", Stance.For)]
    [InlineData("***Strongly For***", Stance.StronglyFor)]
    public void Opinion_IgnoresEmphasisAndCapitalisation(string opinion, Stance expected)
    {
        var result = Parse($"Free Trade: {opinion}");

        Assert.True(result.IsValid);
        Assert.Equal(expected, result.Stances["free_trade"]);
    }

    [Fact]
    public void IssueName_IgnoresEmphasis()
    {
        var result = Parse("**Free Trade**: For\n*The Franchise:* Against\n__poor_relief__: For");

        Assert.True(result.IsValid);
        Assert.Equal(Stance.For, result.Stances["free_trade"]);
        Assert.Equal(Stance.Against, result.Stances["franchise"]);
        Assert.Equal(Stance.For, result.Stances["poor_relief"]);
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
            "The Franchise: Silent",
            "Poor Relief: maybe",
            "The Condition of Labour Strongly For"));

        Assert.False(result.IsValid);
        Assert.Empty(result.Stances);
        Assert.Equal([3, 4, 5, 6, 7, 8], result.Errors.Select(e => e.LineNumber));
        Assert.Contains("unknown", result.Errors[0].Reason);
        Assert.Contains("closed", result.Errors[1].Reason);
        Assert.Contains("line 2", result.Errors[2].Reason);
        Assert.Contains("Silent", result.Errors[3].Reason);
        Assert.Contains("unrecognised", result.Errors[4].Reason);
        Assert.Contains("colon", result.Errors[5].Reason);
        Assert.Equal("Railways: For", result.Errors[0].Text);
    }

    [Fact]
    public void BadIssueAndBadOpinion_OnOneLine_AreBothReported()
    {
        var result = Parse("Railways: Silent");

        var error = Assert.Single(result.Errors);
        Assert.Contains("unknown", error.Reason);
        Assert.Contains("Silent", error.Reason);
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
