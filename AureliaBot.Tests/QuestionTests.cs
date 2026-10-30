using Xunit;

namespace AureliaBot.Tests;

public class QuestionTests : IDisposable
{
    private readonly DatabaseFixture _fixture = new();
    private DatabaseService Db => _fixture.Db;

    public void Dispose() => _fixture.Dispose();

    private static GameConfig Config => DatabaseFixture.Config;
    private static GameConstants Constants => Config.Constants;

    private const ulong Chancellor = 1;
    private const ulong Treasurer = 2;
    private const ulong Asker = 3;
    private const ulong Colleague = 4;
    private const ulong Independent = 5;

    private void Start()
    {
        Db.StartGame(Config, DateTimeOffset.UtcNow);
        for (ulong id = 1; id <= 5; id++)
        {
            Db.CreateFaction(id, $"F{id}", "aurenten", 20);
            Db.SetFactionSeats(id, 10);
        }
        Db.CreateParty("Government", "#ffcc00", null, [Chancellor, Treasurer]);
        Db.CreateParty("Opposition", "#336699", null, [Asker, Colleague]);
        Assert.Null(Db.ImposeCabinet(Chancellor, new Dictionary<Ministry, ulong?> { [Ministry.Treasury] = Treasurer }).Refusal);
    }

    private void AtResolution(long number) => Db.SaveGameState(Db.GetGameState() with { ResolutionNumber = number });

    private Question Ask(ulong asker, Ministry ministry)
    {
        var result = Db.AskQuestion(asker, ministry, "Where has the money gone?", Constants);
        Assert.Null(result.Refusal);
        return result.Question!;
    }

    private double Popularity(ulong id) => Db.GetFaction(id)!.Popularity;

    [Fact]
    public void AnUnansweredQuestion_CostsTheMinister_AndRewardsTheAsker()
    {
        Start();
        var question = Ask(Asker, Ministry.Treasury);
        Assert.Equal(Treasurer, question.MinisterAtAsk);
        Assert.Equal(2, question.DueResolution);

        Assert.Empty(Db.ResolveDueQuestions(Config).Questions);
        AtResolution(1);
        var report = Db.ResolveDueQuestions(Config);

        var outcome = Assert.Single(report.Questions);
        Assert.Equal(QuestionOutcomeKind.Unanswered, outcome.Outcome);
        Assert.Equal(20 - Constants.Opposition.UnansweredQuestion, Popularity(Treasurer));
        Assert.Equal(20 + Constants.Opposition.QuestionReward, Popularity(Asker));
        Assert.Equal(-Constants.Opposition.UnansweredQuestion, outcome.MinisterChange);
        Assert.Equal(QuestionStatus.Unanswered, Db.GetQuestion(question.Id)!.Status);
    }

    [Fact]
    public void AnAnsweredQuestion_IsNotPenalised_AndOnlyTheResponsibleMinisterAnswers()
    {
        Start();
        var question = Ask(Asker, Ministry.Treasury);

        Assert.NotNull(Db.AnswerQuestion(question.Id, Chancellor, "Not mine.").Refusal);
        Assert.Contains(Db.QuestionsToAnswer(Treasurer), q => q.Id == question.Id);
        var answer = Db.AnswerQuestion(question.Id, Treasurer, "Into the railways.");
        Assert.Null(answer.Refusal);
        Assert.Equal(QuestionStatus.Answered, answer.Question!.Status);
        Assert.Empty(Db.QuestionsToAnswer(Treasurer));

        AtResolution(1);
        Assert.Empty(Db.ResolveDueQuestions(Config).Questions);
        Assert.Equal(20, Popularity(Treasurer));
    }

    [Fact]
    public void AVacantMinistrysQuestion_GoesToTheChancellor()
    {
        Start();

        var question = Ask(Asker, Ministry.War);

        Assert.Equal(Chancellor, question.MinisterAtAsk);
        Assert.Contains(Db.QuestionsToAnswer(Chancellor), q => q.Id == question.Id);
        AtResolution(1);
        Db.ResolveDueQuestions(Config);
        Assert.Equal(20 - Constants.Opposition.UnansweredQuestion, Popularity(Chancellor));
    }

    [Fact]
    public void ADeadline_MovesOnceWhenTheMinistryChangesHands()
    {
        Start();
        var question = Ask(Asker, Ministry.Treasury);
        Db.Resign(Treasurer);

        AtResolution(1);
        var first = Assert.Single(Db.ResolveDueQuestions(Config).Questions);

        Assert.Equal(QuestionOutcomeKind.Extended, first.Outcome);
        var extended = Db.GetQuestion(question.Id)!;
        Assert.Equal(QuestionStatus.Open, extended.Status);
        Assert.Equal(3, extended.DueResolution);
        Assert.Equal(Chancellor, extended.MinisterAtAsk);

        Db.Resign(Chancellor);
        Db.AppointChancellor(Colleague);
        AtResolution(2);
        var second = Assert.Single(Db.ResolveDueQuestions(Config).Questions);
        Assert.Equal(QuestionOutcomeKind.Unanswered, second.Outcome);
        Assert.Equal(Colleague, second.MinisterId);
    }

    [Fact]
    public void AQuestionWithNobodyToAnswer_IsDropped_AndTheActionRefunded()
    {
        Start();
        var question = Ask(Asker, Ministry.War);
        Assert.False(Db.GetOppositionStanding(Asker, Constants).CanAct);
        Db.Resign(Chancellor);

        AtResolution(1);
        var outcome = Assert.Single(Db.ResolveDueQuestions(Config).Questions);

        Assert.Equal(QuestionOutcomeKind.Dropped, outcome.Outcome);
        Assert.Equal(QuestionStatus.Dropped, Db.GetQuestion(question.Id)!.Status);
        Assert.True(Db.GetOppositionStanding(Asker, Constants).CanAct);
        Assert.Equal(20, Popularity(Asker));
    }

    [Fact]
    public void NoQuestion_CanBeAskedWhenTheMinistryAndTheChancellorshipAreBothVacant()
    {
        Start();
        Db.Resign(Chancellor);

        Assert.NotNull(Db.QuestionRefusal(Asker, Ministry.War, Constants));
        Assert.Null(Db.QuestionRefusal(Asker, Ministry.Treasury, Constants));
    }

    [Fact]
    public void OneOppositionActionASeason_AndOnlyTheOppositionAsks()
    {
        Start();

        Assert.NotNull(Db.QuestionRefusal(Treasurer, Ministry.War, Constants));
        Assert.NotNull(Db.QuestionRefusal(Independent, Ministry.War, Constants));
        Assert.NotNull(Db.QuestionRefusal(Asker, Ministry.Navy, Constants));
        Ask(Asker, Ministry.Treasury);
        Assert.NotNull(Db.AskQuestion(Asker, Ministry.War, "Again?", Constants).Refusal);
        Assert.Null(Db.QuestionRefusal(Colleague, Ministry.War, Constants));
    }
}
