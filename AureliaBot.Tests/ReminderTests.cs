using Xunit;

namespace AureliaBot.Tests;

public class ReminderTests : AssemblyTestBase
{
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly ConstantsStore Constants = new(Path.Combine(AppContext.BaseDirectory, "config"));

    private readonly Dictionary<ulong, List<string>> _sent = [];
    private readonly List<string> _alerts = [];

    private DateTimeOffset Next => Db.GetGameState().NextResolutionAt!.Value;

    private ReminderService Service(DateTimeOffset now) =>
        new(Db, Constants, (faction, text) =>
        {
            if (!_sent.TryGetValue(faction, out var list)) _sent[faction] = list = [];
            list.Add(text);
            return Task.FromResult(true);
        }, alert =>
        {
            _alerts.Add(alert);
            return Task.CompletedTask;
        }, new FixedTime(now));

    private Task<int> TickAt(DateTimeOffset now) => Service(now).TickAsync();

    private string SentTo(ulong faction) => Assert.Single(_sent[faction]);

    [Fact]
    public async Task NothingIsSent_BeforeTwelveHoursAhead_OrWhilePaused()
    {
        Assert.Equal(0, await TickAt(Next - ReminderService.Lead - TimeSpan.FromMinutes(1)));

        Db.SaveGameState(Db.GetGameState() with { Paused = true });
        Assert.Equal(0, await TickAt(Next - TimeSpan.FromHours(1)));
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task EachFactionIsRemindedOnce_OfWhatItHasNotDone()
    {
        Assert.Equal(6, await TickAt(Next - TimeSpan.FromHours(11)));

        Assert.Contains(Texts.Get("reminders.actionsUnused", ("count", C.Campaign.ActionsPerTurn)), SentTo(Chancellor));
        Assert.DoesNotContain(Texts.Get("reminders.opposition"), SentTo(Chancellor));
        Assert.Contains(Texts.Get("reminders.opposition"), SentTo(Opposer));

        Assert.Equal(0, await TickAt(Next - TimeSpan.FromHours(10)));
        Assert.All(_sent.Values, list => Assert.Single(list));
    }

    [Fact]
    public async Task AFactionWithNothingOutstanding_IsSentNothing()
    {
        Db.SubmitAction(Independent, ActionKind.Campaign, "labour", C.Campaign.ActionsPerTurn, Stance.For);
        Db.SubmitAction(Independent, ActionKind.Poll, "labour", C.Campaign.ActionsPerTurn);
        Db.SubmitAction(Backbencher, ActionKind.Campaign, "labour", C.Campaign.ActionsPerTurn, Stance.For);

        await TickAt(Next - TimeSpan.FromHours(6));

        Assert.False(_sent.ContainsKey(Independent));
        Assert.Contains(Texts.Get("reminders.actionUnused"), SentTo(Backbencher));
    }

    [Fact]
    public async Task AMinisterIsRemindedOfAQuestionToAnswer()
    {
        Assert.Null(Db.AskQuestion(Opposer, Ministry.Treasury, "Where has the money gone?", C).Refusal);

        await TickAt(Next - TimeSpan.FromHours(6));

        Assert.Contains(Texts.Get("me.questionToAnswer", ("ministry", Ministry.Treasury.DisplayName()), ("asker", Display.Faction(Db, Opposer))), SentTo(Minister));
        Assert.DoesNotContain(Texts.Get("reminders.opposition"), SentTo(Opposer));
    }

    [Fact]
    public async Task ARestartOrADelay_DoesNotResend_ButTheNextResolutionDoes()
    {
        await TickAt(Next - TimeSpan.FromHours(6));
        var first = _sent.Count;

        Db.SaveGameState(Db.GetGameState() with { NextResolutionAt = Next.AddHours(6) });
        Assert.Equal(0, await TickAt(Next - TimeSpan.FromHours(6)));

        Db.SaveGameState(Db.GetGameState() with { ResolutionNumber = Db.GetGameState().ResolutionNumber + 1 });
        Assert.Equal(first, await TickAt(Next - TimeSpan.FromHours(6)));
    }

    [Fact]
    public async Task TheGmIsAlertedOnce_OfACrisisStillUnanswered()
    {
        Db.SaveTemplate("riot", TemplateKind.Crisis, """
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
            """);
        Db.SetTemplateStatus("riot", TemplateStatus.Ready);
        Assert.Empty(Db.ApplyEffects([new Effect("crisis.riot", null)], Config, new EffectSource("test", "gm", "test")).Warnings);

        await TickAt(Next - TimeSpan.FromHours(6));
        await TickAt(Next - TimeSpan.FromHours(5));

        Assert.Contains("Bread Riots", Assert.Single(_alerts));
    }
}
