using Xunit;

namespace AureliaBot.Tests;

public class AlertTests : AssemblyTestBase
{
    private const string Riot = """
        id: riot
        title: Bread Riots
        ministry: Interior
        province: aurenten
        issue: labour
        answers:
          - lean: For
            label: Open the granaries
            text: Bread is handed out.
          - lean: Against
            label: Read the Riot Act
            text: The crowds disperse.
        default:
          text: The riots burn out.
        """;

    private Dictionary<string, double> Discontent() => Db.GetProvinces().ToDictionary(p => p.Id, p => p.Discontent);

    private void SetDiscontent(string province, double value) =>
        Sql("UPDATE provinces SET discontent = $value WHERE id = $id;", ("$id", province), ("$value", value));

    private List<string> AlertsSince(GameState before, Dictionary<string, double>? discontent = null) =>
        Db.ThresholdAlerts(before, discontent ?? Discontent(), Config);

    [Fact]
    public void DiscontentReachingTheThreshold_IsAlertedOnce()
    {
        SetDiscontent("iracterie", C.Discontent.AlertAt - 0.5);
        var before = Discontent();
        SetDiscontent("iracterie", C.Discontent.AlertAt + 0.5);

        var alert = Assert.Single(AlertsSince(Db.GetGameState(), before));
        Assert.Contains("Iracterie", alert);

        Assert.Empty(AlertsSince(Db.GetGameState()));
    }

    [Fact]
    public void FavourFallingBelowTheThreshold_IsAlerted_AndReachingZeroIsAlertedOnItsOwn()
    {
        var before = Db.GetGameState() with { Favour = C.Favour.AlertBelow + 5 };
        Db.SaveGameState(Db.GetGameState() with { Favour = C.Favour.AlertBelow - 5 });
        Assert.Equal(Texts.Get("thresholds.favourLow", ("favour", Display.Number(C.Favour.AlertBelow - 5)), ("threshold", Display.Number(C.Favour.AlertBelow))),
            Assert.Single(AlertsSince(before)));

        Assert.Empty(AlertsSince(Db.GetGameState()));

        Db.SaveGameState(Db.GetGameState() with { Favour = 0 });
        Assert.Equal(Texts.Get("thresholds.favourZero"), Assert.Single(AlertsSince(before)));
    }

    [Fact]
    public void ThreatCrossingSeveralLevels_IsAlertedAtTheHighest()
    {
        var levels = C.Threat.Alerts.OrderBy(l => l).ToList();
        var before = Db.GetGameState() with { Threat = levels[0] - 1 };
        Db.SaveGameState(Db.GetGameState() with { Threat = levels[^1] + 1 });

        var alert = Assert.Single(AlertsSince(before));
        Assert.Contains(Display.Number(levels[^1]), alert);
        Assert.Empty(AlertsSince(Db.GetGameState()));
    }

    [Fact]
    public void AQuestionDueNextAndUnanswered_IsAlerted_UntilAnswered()
    {
        var question = Db.AskQuestion(Opposer, Ministry.Treasury, "Where has the money gone?", C).Question!;
        Assert.Empty(AlertsSince(Db.GetGameState()));

        Db.SaveGameState(Db.GetGameState() with { ResolutionNumber = question.DueResolution - 1 });
        Assert.Contains($"<@{Minister}>", Assert.Single(AlertsSince(Db.GetGameState())));

        Assert.Null(Db.AnswerQuestion(question.Id, Minister, "Into the railways.").Refusal);
        Assert.Empty(AlertsSince(Db.GetGameState()));
    }

    [Fact]
    public void ALiveCrisisDueNextWithoutAResponse_IsAlerted_AndNamesTheVacancy()
    {
        Db.SaveTemplate("riot", TemplateKind.Crisis, Riot);
        Db.SetTemplateStatus("riot", TemplateStatus.Ready);
        Assert.Empty(Db.ApplyEffects([new Effect("crisis.riot", null)], Config, new EffectSource("test", "gm", "test")).Warnings);

        Assert.Contains($"<@{Chancellor}>", Assert.Single(AlertsSince(Db.GetGameState())));

        SeatCabinet(null, (Ministry.Treasury, Minister));
        Assert.Equal(Texts.Get("thresholds.crisisNobody", ("crisis", "Bread Riots"), ("ministry", Ministry.Interior.DisplayName())),
            Assert.Single(AlertsSince(Db.GetGameState())));
    }

    [Fact]
    public void AQuietResolution_RaisesNoThresholdAlert()
    {
        var report = Resolve();
        Assert.DoesNotContain(report.Alerts, a => a.Contains("Discontent in") || a.Contains("Favour") || a.Contains("Threat has"));
    }
}
