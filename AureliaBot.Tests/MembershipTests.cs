using Xunit;

namespace AureliaBot.Tests;

public class MembershipTests : IDisposable
{
    private const ulong Alice = 1001;
    private const ulong Bob = 1002;
    private const ulong Carol = 1003;
    private const ulong Dave = 1004;

    private readonly DatabaseFixture _fixture = new();
    private DatabaseService Db => _fixture.Db;

    public MembershipTests()
    {
        foreach (var id in new[] { Alice, Bob, Carol, Dave })
            Db.CreateFaction(id, $"Character {id}", "aurenten", 10);
    }

    public void Dispose() => _fixture.Dispose();

    private long Found(string name, params ulong[] founders)
    {
        var result = Db.CreateParty(name, "#C0392B", null, founders);
        Assert.Equal(PartyCreateOutcome.Created, result.Outcome);
        return result.PartyId!.Value;
    }

    [Fact]
    public void Create_WithTwoIndependentFounders_MakesThemMembers()
    {
        var partyId = Found("Liberal Union", Alice, Bob);

        var members = Db.GetPartyMembers(partyId).Select(f => f.UserId).ToHashSet();
        Assert.Equal(new HashSet<ulong> { Alice, Bob }, members);
        Assert.Equal("#C0392B", Db.GetParty(partyId)!.Colour);
    }

    [Fact]
    public void Create_WithOneFounder_IsRefused()
    {
        var result = Db.CreateParty("Lonely Party", "#C0392B", null, [Alice]);

        Assert.Equal(PartyCreateOutcome.TooFewFounders, result.Outcome);
        Assert.Empty(Db.GetAllParties());
    }

    [Fact]
    public void Create_WithTheSameFounderTwice_IsRefused()
    {
        var result = Db.CreateParty("Echo Party", "#C0392B", null, [Alice, Alice]);

        Assert.Equal(PartyCreateOutcome.TooFewFounders, result.Outcome);
    }

    [Fact]
    public void Create_WithAnUnregisteredFounder_IsRefused()
    {
        var result = Db.CreateParty("Ghost Party", "#C0392B", null, [Alice, 9999]);

        Assert.Equal(PartyCreateOutcome.FounderNotRegistered, result.Outcome);
        Assert.Equal(9999UL, result.OffendingFaction);
        Assert.Empty(Db.GetAllParties());
        Assert.True(Db.GetFaction(Alice)!.IsIndependent);
    }

    [Fact]
    public void Create_WithARetiredFounder_IsRefused()
    {
        Db.RetireFaction(Bob);

        var result = Db.CreateParty("Retired Party", "#C0392B", null, [Alice, Bob]);

        Assert.Equal(PartyCreateOutcome.FounderNotRegistered, result.Outcome);
        Assert.Equal(Bob, result.OffendingFaction);
    }

    [Fact]
    public void Create_WithAFounderAlreadyInAParty_IsRefused()
    {
        Found("Liberal Union", Alice, Bob);

        var result = Db.CreateParty("Splinter", "#2E86C1", null, [Bob, Carol]);

        Assert.Equal(PartyCreateOutcome.FounderNotIndependent, result.Outcome);
        Assert.Equal(Bob, result.OffendingFaction);
        Assert.True(Db.GetFaction(Carol)!.IsIndependent);
    }

    [Fact]
    public void Create_WithATakenName_IsRefusedIgnoringCase()
    {
        Found("Liberal Union", Alice, Bob);

        var result = Db.CreateParty("liberal union", "#2E86C1", null, [Carol, Dave]);

        Assert.Equal(PartyCreateOutcome.NameTaken, result.Outcome);
    }

    [Theory]
    [InlineData(Season.Spring)]
    [InlineData(Season.Summer)]
    [InlineData(Season.Autumn)]
    [InlineData(Season.Winter)]
    public void GmMove_WorksInAnySeason(Season season)
    {
        Db.SaveGameState(Db.GetGameState() with { Season = season });
        var partyId = Found("Liberal Union", Alice, Bob);

        var result = Db.MoveFaction(Carol, partyId, "test");

        Assert.NotNull(result);
        Assert.Null(result.DissolvedParty);
        Assert.Equal(partyId, Db.GetFaction(Carol)!.PartyId);
        Assert.Equal(3, Db.GetPartyMembers(partyId).Count);
    }

    [Fact]
    public void GmMove_ToIndependent_LeavesALargerPartyStanding()
    {
        var partyId = Found("Liberal Union", Alice, Bob, Carol);

        var result = Db.MoveFaction(Carol, null, "test");

        Assert.NotNull(result);
        Assert.Equal(partyId, result.FromPartyId);
        Assert.Null(result.DissolvedParty);
        Assert.True(Db.GetFaction(Carol)!.IsIndependent);
        Assert.Equal(2, Db.GetPartyMembers(partyId).Count);
    }

    [Fact]
    public void GmMove_OfAnUnregisteredFaction_IsRefused()
    {
        var partyId = Found("Liberal Union", Alice, Bob);

        Assert.Null(Db.MoveFaction(9999, partyId, "test"));
    }

    [Fact]
    public void PartyLeftWithOneMember_Dissolves_AndItsLastMemberKeepsItsSeats()
    {
        var partyId = Found("Liberal Union", Alice, Bob);
        Db.SetFactionSeats(Alice, 12);
        Db.SetFactionSeats(Bob, 7);

        var result = Db.MoveFaction(Bob, null, "test");

        Assert.NotNull(result);
        Assert.NotNull(result.DissolvedParty);
        Assert.Equal(partyId, result.DissolvedParty.Id);
        Assert.Null(Db.GetParty(partyId));
        var alice = Db.GetFaction(Alice)!;
        Assert.True(alice.IsIndependent);
        Assert.Equal(12, alice.Seats);
        Assert.Equal(7, Db.GetFaction(Bob)!.Seats);
    }

    [Fact]
    public void MovingTheSecondLastMemberToAnotherParty_DissolvesTheOldParty()
    {
        var oldParty = Found("Liberal Union", Alice, Bob);
        var newParty = Found("Radicals", Carol, Dave);

        var result = Db.MoveFaction(Bob, newParty, "test");

        Assert.NotNull(result);
        Assert.Equal(oldParty, result.DissolvedParty?.Id);
        Assert.Equal(newParty, Db.GetFaction(Bob)!.PartyId);
        Assert.True(Db.GetFaction(Alice)!.IsIndependent);
    }

    [Fact]
    public void DeletingAParty_MakesItsMembersIndependent()
    {
        var partyId = Found("Liberal Union", Alice, Bob, Carol);
        Db.SetPartyDiscordIds(partyId, 555, 666);
        Db.SetFactionSeats(Carol, 4);

        var deleted = Db.DeleteParty(partyId, "test");

        Assert.NotNull(deleted);
        Assert.Equal(555UL, deleted.RoleId);
        Assert.Equal(666UL, deleted.ChannelId);
        Assert.Null(Db.GetParty(partyId));
        Assert.All(new[] { Alice, Bob, Carol }, id => Assert.True(Db.GetFaction(id)!.IsIndependent));
        Assert.Equal(4, Db.GetFaction(Carol)!.Seats);
    }

    [Fact]
    public void UpdateParty_KeepsMembersAndRejectsATakenName()
    {
        var liberal = Found("Liberal Union", Alice, Bob);
        Found("Radicals", Carol, Dave);

        Assert.True(Db.UpdateParty(liberal, "Liberal Alliance", "#2E86C1", 42));
        Assert.False(Db.UpdateParty(liberal, "Radicals", "#2E86C1", 42));

        var party = Db.GetParty(liberal)!;
        Assert.Equal("Liberal Alliance", party.Name);
        Assert.Equal("#2E86C1", party.Colour);
        Assert.Equal(42UL, party.EmojiId);
        Assert.Equal(2, Db.GetPartyMembers(liberal).Count);
    }
}
