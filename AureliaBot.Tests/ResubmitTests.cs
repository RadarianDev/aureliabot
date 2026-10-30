using Xunit;

namespace AureliaBot.Tests;

public class ResubmitTests : AssemblyTestBase
{
    private ResubmitService Service => new(Db, null);

    private static CommandPayload Payload(string command, params (string Name, string Value)[] options) =>
        new(command, options.ToDictionary(o => o.Name, o => o.Value));

    [Fact]
    public void APayload_SurvivesTheLog_WithSpacesInItsValues()
    {
        var payload = Payload("war raise", ("name", "The Royal Guard: 1st"), ("province", "aurenten"));

        var parsed = CommandPayload.TryParse(payload.ToJson())!;

        Assert.Equal("war raise", parsed.Command);
        Assert.Equal(payload.Options, parsed.Options);
        Assert.Equal("/war raise name:The Royal Guard: 1st province:aurenten", parsed.Display);
    }

    [Fact]
    public void AnOldTextPayload_IsShownAsItIs()
    {
        Assert.Null(CommandPayload.TryParse("/campaign issue:labour"));
        Assert.Equal("Submitted Campaign 4 on labour.", CommandPayload.Describe("Submitted Campaign 4 on labour."));
    }

    [Fact]
    public void EveryCoveredCommand_FindsItsSlashCommand_WithOptionsThatCanBeRebuilt()
    {
        foreach (var command in ResubmitService.Covered)
        {
            var target = ResubmitService.Find(command);
            Assert.True(target is not null, $"/{command} has no slash command.");
            Assert.All(target!.Method.GetParameters(), p => Assert.True(ResubmitService.CanConvert(p.ParameterType), $"/{command} {p.Name}"));
        }
    }

    [Fact]
    public void GmAndReadOnlyCommands_AreNotPlayerActions()
    {
        Assert.False(ResubmitService.IsPlayerAction("gm undo"));
        Assert.False(ResubmitService.IsPlayerAction("content import"));
        Assert.False(ResubmitService.IsPlayerAction("info"));
        Assert.False(ResubmitService.IsPlayerAction("ministry status"));
        Assert.True(ResubmitService.IsPlayerAction("campaign"));
        Assert.Null(ResubmitService.Find("info"));
    }

    [Fact]
    public void Arguments_AreRebuiltFromTheOptions_WithDefaultsForMissingOnes()
    {
        var campaign = ResubmitService.Find("campaign")!.Method;
        Assert.Equal(new object?[] { "labour", StanceChoice.StronglyFor }, ResubmitService.Arguments(campaign, Payload("campaign", ("issue", "labour"), ("stance", "StronglyFor")).Options)!);
        Assert.Equal(new object?[] { "labour", null }, ResubmitService.Arguments(campaign, Payload("campaign", ("issue", "labour")).Options)!);
        Assert.Null(ResubmitService.Arguments(campaign, Payload("campaign", ("issue", "labour"), ("stance", "Sideways")).Options));

        var fund = ResubmitService.Find("ministry fund")!.Method;
        Assert.Equal(new object?[] { "school", 2.5 }, ResubmitService.Arguments(fund, Payload("ministry fund", ("project", "school"), ("amount", "2.5")).Options)!);

        var raise = ResubmitService.Find("war raise")!.Method;
        Assert.Null(ResubmitService.Arguments(raise, Payload("war raise", ("name", "Guards")).Options));
    }

    [Fact]
    public void OnlyTheSubmitter_CanResubmit_AndOnlyOnce()
    {
        var payload = Payload("poll", ("issue", "labour"));
        var lost = Db.LogAndRead(LogKinds.LostCommand, "poll", payload.ToJson(), Opposer);
        var at = lost.At.ToUnixTimeSeconds();

        var stranger = Service.Peek(lost.Id, at, Independent);
        Assert.Equal(ResubmitClaimOutcome.NotYours, stranger.Outcome);
        Assert.Equal(Opposer, stranger.Owner);

        Assert.Equal(ResubmitClaimOutcome.Claimed, Service.Peek(lost.Id, at, Opposer).Outcome);
        var claim = Service.Claim(lost.Id, at, Opposer);
        Assert.Equal(ResubmitClaimOutcome.Claimed, claim.Outcome);
        Assert.Equal(payload.Display, claim.Payload!.Display);
        Assert.Equal(ResubmitClaimOutcome.Used, Service.Claim(lost.Id, at, Opposer).Outcome);
    }

    [Fact]
    public void AButton_FromAnEarlierRestore_IsGone()
    {
        var lost = Db.LogAndRead(LogKinds.LostCommand, "poll", Payload("poll", ("issue", "labour")).ToJson(), Opposer);
        var command = Db.LogAndRead(LogKinds.Command, "poll", Payload("poll", ("issue", "labour")).ToJson(), Opposer);

        Assert.Equal(ResubmitClaimOutcome.Gone, Service.Claim(lost.Id, lost.At.ToUnixTimeSeconds() - 60, Opposer).Outcome);
        Assert.Equal(ResubmitClaimOutcome.Gone, Service.Claim(command.Id, command.At.ToUnixTimeSeconds(), Opposer).Outcome);
        Assert.Equal(ResubmitClaimOutcome.Gone, Service.Claim(lost.Id + 100, lost.At.ToUnixTimeSeconds(), Opposer).Outcome);
        Assert.Equal($"resubmit:{lost.Id}:{lost.At.ToUnixTimeSeconds()}", ResubmitService.ButtonId(lost));
    }
}
