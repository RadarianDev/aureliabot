using Xunit;

namespace AureliaBot.Tests;

public class CrisisTests : AssemblyTestBase
{
    private const string Dock = """
        id: dock
        title: The Carden Dock Strike
        ministry: CommerceLabor
        province: north_arpal
        issue: labour
        answers:
          - lean: For
            label: Meet the strikers' demands
            price: 3
            text: The docks reopen within the week.
            effects: { economy: -1, discontent.north_arpal: -2 }
          - lean: Neutral
            label: Commission a royal inquiry
            price: 4
            text: Learned gentlemen take evidence.
            effects: { discontent.north_arpal: -1 }
          - lean: Against
            label: Declare the strike unlawful
            text: The strike collapses.
            effects: { discontent.north_arpal: 2 }
        complication:
          text: The money runs out mid-negotiation.
          effects: { discontent.iracterie: 1, economy: -3 }
        default:
          text: The strike drags on unanswered.
          effects: { economy: -2 }
        """;

    private const string Riot = """
        id: riot
        title: Bread Riots
        ministry: Interior
        province: aurenten
        issue: labour
        answers:
          - lean: For
            label: Open the granaries
            price: 5
            text: Bread is handed out.
            effects: { economy: -1 }
          - lean: Against
            label: Read the Riot Act
            text: The crowds disperse.
        default:
          text: The riots burn out.
        """;

    private static CrisisRules TestRules { get; } = new(
        (c, lean, opinion, salience) => c.Campaign.Factor * (int)lean * opinion * salience / 3,
        (c, salience) => c.Campaign.Factor * salience / 3,
        (c, side, ministerLean, opinion) =>
        {
            if (opinion == 0) return 0;
            var right = (Lean)Math.Sign(opinion);
            if (side == right) return ministerLean == right ? 0 : c.Opposition.CondemnReward;
            return ministerLean == right ? -c.Opposition.CondemnPenalty : 0;
        });

    public CrisisTests()
    {
        foreach (var (id, yaml) in new[] { ("dock", Dock), ("riot", Riot) })
        {
            Db.SaveTemplate(id, TemplateKind.Crisis, yaml);
            Db.SetTemplateStatus(id, TemplateStatus.Ready);
        }
    }

    private Crisis Post(string template)
    {
        var result = Db.ApplyEffects([new Effect($"crisis.{template}", null)], Config, new EffectSource("test", "gm", "test"));
        Assert.Empty(result.Warnings);
        return Db.GetCrises(CrisisStatus.Live).Last();
    }

    private void SetPurse(Ministry ministry, decimal amount) =>
        Db.ApplyEffects([new Effect($"purse.{ministry}", (double)(amount - Db.GetPurseBalance(ministry)))], Config, new EffectSource("test", "gm", "test"));

    private void Respond(Crisis crisis, ulong faction, int answer) =>
        Assert.Null(Db.RespondToCrisis(crisis.Id, faction, answer, "The Crown has spoken.").Refusal);

    private ResolutionReport ResolveCrises() => Db.ResolveDueCrises(Config, TestRules);

    private double Popularity(ulong faction) => Db.GetFaction(faction)!.Popularity;

    private double Opinion(string province = "north_arpal") => Db.LoadOpinionSnapshot(Config).Provincial("labour", province);

    private void SeatCommerceMinister() =>
        SeatCabinet(Chancellor, (Ministry.Treasury, Minister), (Ministry.CommerceLabor, Backbencher));

    [Fact]
    public void ACrisisPostedByAnEvent_IsDueAtTheNextResolution()
    {
        var eventId = Db.QueueEvent(new EventTemplate("strike", "Strike", "The docks fall silent.", [new Effect("crisis.dock", null)], null), 2, false);

        var report = Resolve();

        var posted = Assert.Single(report.NewCrises);
        var crisis = Db.GetCrisis(posted.CrisisId)!;
        Assert.Equal(2, Db.GetGameState().ResolutionNumber);
        Assert.Equal(2, crisis.PostedResolution);
        Assert.Equal(3, crisis.DueResolution);
        Assert.Equal(eventId, crisis.EventId);
        Assert.Equal(CrisisStatus.Live, crisis.Status);
        Assert.Equal(3, crisis.Answers.Count);
        Assert.Equal(1, Db.GetTemplate("dock")!.TimesPosted);
    }

    [Fact]
    public void ACrisisIsNotPosted_WhenItsTemplateIsNotReady_OrItsIssueIsClosed()
    {
        Db.SetTemplateStatus("dock", TemplateStatus.Draft);
        Assert.Single(Db.ApplyEffects([new Effect("crisis.dock", null)], Config, new EffectSource("test", "gm", "test")).Warnings);
        Db.SetTemplateStatus("dock", TemplateStatus.Ready);
        Db.ApplyEffects([new Effect("close.labour", null)], Config, new EffectSource("test", "gm", "test"));
        Assert.Single(Db.ApplyEffects([new Effect("crisis.dock", null)], Config, new EffectSource("test", "gm", "test")).Warnings);
        Assert.Empty(Db.GetCrises());
    }

    [Fact]
    public void OnlyTheResponsibleMinister_CanRespond()
    {
        SeatCommerceMinister();
        var crisis = Post("dock");

        Assert.NotNull(Db.RespondToCrisis(crisis.Id, Chancellor, 1, "No.").Refusal);
        Assert.NotNull(Db.RespondToCrisis(crisis.Id, Opposer, 1, "No.").Refusal);
        Assert.NotNull(Db.RespondToCrisis(crisis.Id, Backbencher, 9, "No.").Refusal);
        Assert.Empty(Db.CrisesAwaiting(Chancellor));
        Assert.False(Assert.Single(Db.CrisesAwaiting(Backbencher)).Responded);

        var first = Db.RespondToCrisis(crisis.Id, Backbencher, 1, "Feed them.");
        Assert.Null(first.Refusal);
        Assert.False(first.Replaced);
        var second = Db.RespondToCrisis(crisis.Id, Backbencher, 3, "Arrest them.");
        Assert.True(second.Replaced);
        Assert.Equal(3, Db.GetCrisis(crisis.Id)!.ChosenAnswer);
        Assert.True(Assert.Single(Db.CrisesAwaiting(Backbencher)).Responded);
        Assert.True(Db.IsCrisisLocked(crisis.Id));
    }

    [Fact]
    public void AVacantMinistrysCrisis_GoesToTheChancellor_AndScoresTheChancellorsFaction()
    {
        SetPurse(Ministry.CommerceLabor, 10);
        var crisis = Post("dock");
        Assert.Single(Db.CrisesAwaiting(Chancellor));
        var opinion = Opinion();
        var before = Popularity(Chancellor);
        Respond(crisis, Chancellor, 1);

        var outcome = Assert.Single(ResolveCrises().CrisisOutcomes);

        Assert.Equal(Chancellor, outcome.ResponsibleFactionId);
        var expected = C.Campaign.Factor * opinion * 3 / 3;
        Assert.Equal(expected, outcome.PopularityChange, 6);
        Assert.Equal(before + expected, Popularity(Chancellor), 6);
    }

    [Fact]
    public void ACleanAnswer_PaysFromThePurse_AndAppliesTheAnswer()
    {
        SeatCommerceMinister();
        SetPurse(Ministry.CommerceLabor, 10);
        var economy = Db.GetGameState().Economy;
        var crisis = Post("dock");
        Respond(crisis, Backbencher, 1);

        var outcome = Assert.Single(ResolveCrises().CrisisOutcomes);

        Assert.Equal(CrisisOutcome.Clean, outcome.Outcome);
        Assert.Equal(7m, Db.GetPurseBalance(Ministry.CommerceLabor));
        Assert.Equal(economy - 1, Db.GetGameState().Economy, 6);
        Assert.Equal(["The docks reopen within the week."], outcome.Texts);
        Assert.Equal("Meet the strikers' demands", outcome.AnswerLabel);
        Assert.Equal("The Crown has spoken.", outcome.ResponseText);
        var resolved = Db.GetCrisis(crisis.Id)!;
        Assert.Equal(CrisisStatus.Resolved, resolved.Status);
        Assert.Equal(CrisisOutcome.Clean, resolved.Outcome);
        Assert.Equal(1, resolved.ChosenAnswer);
        Assert.Empty(Db.CrisesAwaiting(Backbencher));
    }

    [Fact]
    public void AnAnswerThePurseCannotCover_EmptiesThePurse_AndAppliesTheAnswerThenTheComplication()
    {
        SeatCommerceMinister();
        SetPurse(Ministry.CommerceLabor, 2);
        var economy = Db.GetGameState().Economy;
        var crisis = Post("dock");
        Respond(crisis, Backbencher, 1);

        var outcome = Assert.Single(ResolveCrises().CrisisOutcomes);

        Assert.Equal(CrisisOutcome.Complicated, outcome.Outcome);
        Assert.Equal(0m, Db.GetPurseBalance(Ministry.CommerceLabor));
        Assert.Equal(economy - 4, Db.GetGameState().Economy, 6);
        Assert.Equal(["The docks reopen within the week.", "The money runs out mid-negotiation."], outcome.Texts);
        Assert.Equal(["economy", "discontent.north_arpal", "discontent.iracterie", "economy"], outcome.Effects.Select(e => e.Key));
    }

    [Fact]
    public void TheStandardComplication_AppliesWhenACrisisHasNone()
    {
        SeatCabinet(Chancellor, (Ministry.Interior, Backbencher));
        SetPurse(Ministry.Interior, 1);
        var crisis = Post("riot");
        Respond(crisis, Backbencher, 1);

        var outcome = Assert.Single(ResolveCrises().CrisisOutcomes);

        Assert.Equal(CrisisOutcome.Complicated, outcome.Outcome);
        Assert.Equal(Texts.Get("crisis.standardComplication"), outcome.Texts[1]);
        var last = outcome.Effects[^1];
        Assert.Equal("discontent.aurenten", last.Key);
        Assert.True(last.Amount > 0);
    }

    [Fact]
    public void ANeutralAnswer_GainsAndLosesNothing_AndLeavesOpinionAlone()
    {
        SeatCommerceMinister();
        SetPurse(Ministry.CommerceLabor, 10);
        var before = Popularity(Backbencher);
        var opinion = Db.GetIssue("labour")!.Opinion;
        var crisis = Post("dock");
        Respond(crisis, Backbencher, 2);

        var outcome = Assert.Single(ResolveCrises().CrisisOutcomes);

        Assert.Equal(0, outcome.PopularityChange);
        Assert.Equal(before, Popularity(Backbencher));
        Assert.Equal(opinion, Db.GetIssue("labour")!.Opinion, 6);
        Assert.Equal(6m, Db.GetPurseBalance(Ministry.CommerceLabor));
    }

    [Fact]
    public void NoResponse_AppliesTheDefault_AndTheMinisterLosesPopularity()
    {
        SeatCommerceMinister();
        var economy = Db.GetGameState().Economy;
        var before = Popularity(Backbencher);
        var salience = Db.GetIssue("labour")!.Salience;
        Post("dock");

        var outcome = Assert.Single(ResolveCrises().CrisisOutcomes);

        Assert.Equal(CrisisOutcome.Default, outcome.Outcome);
        Assert.Equal(Backbencher, outcome.ResponsibleFactionId);
        Assert.Equal(economy - 2, Db.GetGameState().Economy, 6);
        Assert.Equal(-C.Campaign.Factor * salience / 3, outcome.PopularityChange, 6);
        Assert.Equal(before + outcome.PopularityChange, Popularity(Backbencher), 6);
        Assert.Equal(["The strike drags on unanswered."], outcome.Texts);
        Assert.Equal(salience, Db.GetIssue("labour")!.Salience);
    }

    [Fact]
    public void WithTheMinistryAndTheChancellorshipVacant_TheDefaultAppliesWithNoPenalty()
    {
        SeatCabinet(null, (Ministry.Treasury, Minister));
        var popularity = Db.GetActiveFactions().ToDictionary(f => f.UserId, f => f.Popularity);
        var economy = Db.GetGameState().Economy;
        Post("dock");

        var outcome = Assert.Single(ResolveCrises().CrisisOutcomes);

        Assert.Equal(CrisisOutcome.Default, outcome.Outcome);
        Assert.Null(outcome.ResponsibleFactionId);
        Assert.Equal(0, outcome.PopularityChange);
        Assert.Equal(economy - 2, Db.GetGameState().Economy, 6);
        Assert.All(Db.GetActiveFactions(), f => Assert.Equal(popularity[f.UserId], f.Popularity));
    }

    [Theory]
    [InlineData(1, 10, 1)]
    [InlineData(1, 0, -1)]
    [InlineData(3, 10, -1)]
    public void Opinion_MovesWithTheLeanWhenClean_AndAgainstItWhenComplicated(int answer, int purse, int direction)
    {
        SeatCommerceMinister();
        SetPurse(Ministry.CommerceLabor, purse);
        var opinion = Db.GetIssue("labour")!.Opinion;
        var crisis = Post("dock");
        Respond(crisis, Backbencher, answer);

        ResolveCrises();

        Assert.Equal(opinion + direction * C.Opinion.CrisisShift, Db.GetIssue("labour")!.Opinion, 6);
    }

    [Fact]
    public void Salience_RisesOnce_AndIsMarkedRaisedThisResolution()
    {
        SeatCommerceMinister();
        SetPurse(Ministry.CommerceLabor, 10);
        var salience = Db.GetIssue("labour")!.Salience;
        var crisis = Post("dock");
        Respond(crisis, Backbencher, 2);

        ResolveCrises();
        ResolveCrises();

        var issue = Db.GetIssue("labour")!;
        Assert.Equal(Math.Min(C.Salience.Max, salience + C.Salience.CrisisRaise), issue.Salience);
        Assert.Equal(Db.GetGameState().ResolutionNumber, issue.SalienceRaisedResolution);
    }

    [Fact]
    public void Condemning_IsForTheOpposition_OncePerCrisis_AndSpendsTheOppositionAction()
    {
        SeatCommerceMinister();
        var dock = Post("dock");
        var riot = Post("riot");

        Assert.NotNull(Db.Condemn(Independent, dock.Id, Lean.For, C).Refusal);
        Assert.NotNull(Db.Condemn(Backbencher, dock.Id, Lean.For, C).Refusal);
        Assert.NotNull(Db.Condemn(Opposer, dock.Id, Lean.Neutral, C).Refusal);

        var result = Db.Condemn(Opposer, dock.Id, Lean.Against, C);
        Assert.Null(result.Refusal);
        Assert.Equal(Lean.Against, result.Condemnation!.Side);
        Assert.False(Db.GetOppositionStanding(Opposer, C).CanAct);
        Assert.NotNull(Db.Condemn(Opposer, dock.Id, Lean.For, C).Refusal);
        Assert.NotNull(Db.Condemn(Opposer, riot.Id, Lean.For, C).Refusal);
        Assert.Single(Db.GetCondemnations(dock.Id));

        Db.SaveGameState(Db.GetGameState() with { Season = Season.Autumn });
        Assert.NotNull(Db.Condemn(Opposer, dock.Id, Lean.For, C).Refusal);
        Assert.Null(Db.Condemn(Opposer, riot.Id, Lean.For, C).Refusal);
        Assert.Equal([dock.Id, riot.Id], Db.CondemnableCrises(SecondOpposer).Select(c => c.Id));
        Assert.Empty(Db.CondemnableCrises(Opposer));
    }

    [Fact]
    public void Condemnations_StaySealedWhileTheCrisisIsLive_AndAreNotPublicInTheLog()
    {
        SeatCommerceMinister();
        var crisis = Post("dock");
        Assert.Null(Db.Condemn(Opposer, crisis.Id, Lean.For, C).Refusal);

        Assert.False(Db.GetCrisis(crisis.Id)!.CondemnationsRevealed);
        Assert.DoesNotContain(Db.GetLogEntries(LogKinds.StateChange, DateTimeOffset.UnixEpoch), entry => entry.Source == "opposition condemn" && entry.Public);
    }

    [Fact]
    public void ANeutralAnswer_CountsAsNotOnTheRightSide_ForCondemnations()
    {
        SeatCommerceMinister();
        SetPurse(Ministry.CommerceLabor, 10);
        var right = (Lean)Math.Sign(Opinion());
        Assert.NotEqual(Lean.Neutral, right);
        var wrong = (Lean)(-(int)right);
        var crisis = Post("dock");
        Assert.Null(Db.Condemn(Opposer, crisis.Id, right, C).Refusal);
        Assert.Null(Db.Condemn(SecondOpposer, crisis.Id, wrong, C).Refusal);
        var opposer = Popularity(Opposer);
        var second = Popularity(SecondOpposer);
        Respond(crisis, Backbencher, 2);

        var outcome = Assert.Single(ResolveCrises().CrisisOutcomes);

        Assert.Equal(opposer + C.Opposition.CondemnReward, Popularity(Opposer), 6);
        Assert.Equal(second, Popularity(SecondOpposer), 6);
        Assert.Equal(C.Opposition.CondemnReward, outcome.Condemnations.Single(s => s.FactionId == Opposer).PopularityChange, 6);
    }

    [Fact]
    public void AMinisterOnTheRightSide_CostsTheWrongCondemner_AndGivesTheRightOneNothing()
    {
        SeatCommerceMinister();
        SetPurse(Ministry.CommerceLabor, 10);
        var right = (Lean)Math.Sign(Opinion());
        var wrong = (Lean)(-(int)right);
        var crisis = Post("dock");
        Assert.Null(Db.Condemn(Opposer, crisis.Id, right, C).Refusal);
        Assert.Null(Db.Condemn(SecondOpposer, crisis.Id, wrong, C).Refusal);
        var opposer = Popularity(Opposer);
        var second = Popularity(SecondOpposer);
        Respond(crisis, Backbencher, right == Lean.For ? 1 : 3);

        ResolveCrises();

        Assert.Equal(opposer, Popularity(Opposer), 6);
        Assert.Equal(second - C.Opposition.CondemnPenalty, Popularity(SecondOpposer), 6);
    }

    [Fact]
    public void WithNoResponse_ARightSideCondemnerGains()
    {
        SeatCommerceMinister();
        var right = (Lean)Math.Sign(Opinion());
        var crisis = Post("dock");
        Assert.Null(Db.Condemn(Opposer, crisis.Id, right, C).Refusal);
        var opposer = Popularity(Opposer);

        ResolveCrises();

        Assert.Equal(opposer + C.Opposition.CondemnReward, Popularity(Opposer), 6);
    }
}
