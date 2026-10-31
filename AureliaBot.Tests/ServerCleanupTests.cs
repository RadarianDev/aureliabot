using Xunit;

namespace AureliaBot.Tests;

public class ServerCleanupTests : IDisposable
{
    private const ulong PartyCategory = 10;
    private const ulong FactionCategory = 11;
    private const ulong ArchiveCategory = 12;
    private const ulong News = 20;
    private const ulong Gm = 21;
    private const ulong TurnTracker = 22;
    private const ulong GmRole = 30;
    private const ulong CabinetRole = 31;
    private const ulong Everyone = 1;
    private const int BotTop = 50;

    private readonly DatabaseFixture _fixture = new();
    private DatabaseService Db => _fixture.Db;

    public void Dispose() => _fixture.Dispose();

    private static readonly Dictionary<ChannelRole, ulong> Registered = new()
    {
        [ChannelRole.PartyCategory] = PartyCategory,
        [ChannelRole.FactionCategory] = FactionCategory,
        [ChannelRole.ArchiveCategory] = ArchiveCategory,
        [ChannelRole.News] = News,
        [ChannelRole.Gm] = Gm,
        [ChannelRole.TurnTracker] = TurnTracker,
        [ChannelRole.GmRole] = GmRole,
        [ChannelRole.CabinetRole] = CabinetRole,
    };

    private static Party Party(long id, string name, ulong? roleId = null, ulong? channelId = null, ulong? foreignId = null) =>
        new(id, name, "#C0392B", null, null, null, roleId, channelId, null, foreignId);

    private static Faction Faction(ulong userId, ulong? channelId) =>
        new(userId, "Someone", 10, 0, null, "aurenten", channelId, FactionStatus.Active);

    private static GuildRoleInfo Role(ulong id, string name, int position = 5, bool everyone = false, bool managed = false) =>
        new(id, name, position, everyone, managed);

    private static CleanupPlan Plan(
        IReadOnlyCollection<GuildChannelInfo> channels, IReadOnlyCollection<GuildRoleInfo> roles,
        IReadOnlyCollection<Party>? parties = null, IReadOnlyCollection<Faction>? factions = null) =>
        ServerCleanupService.BuildPlan(channels, roles, BotTop, Registered, parties ?? [], factions ?? []);

    [Fact]
    public void Channels_InTheGameCategories_AreSelected_AndGameChannelsAreKept()
    {
        var plan = Plan(
        [
            new(100, "liberal-union", PartyCategory),
            new(101, "old-party", ArchiveCategory),
            new(102, "halvard", FactionCategory),
            new(News, "news", null),
            new(Gm, "gm", null),
            new(TurnTracker, "spring-1840", PartyCategory),
            new(103, "general", null),
        ], []);

        Assert.Equal([100UL, 101UL], plan.Channels.Where(c => c.Kind == CleanupChannelKind.PartyChat).Select(c => c.Id).ToList());
        Assert.Equal([102UL], plan.Channels.Where(c => c.Kind == CleanupChannelKind.FactionChat).Select(c => c.Id).ToList());
    }

    [Fact]
    public void Channels_KnownToTheDatabase_AreSelected_WhereverTheyAre()
    {
        var plan = Plan(
            [new(200, "liberal-union", null), new(201, "halvard", null), new(202, "general", null)],
            [],
            [Party(1, "Liberal Union", channelId: 200)],
            [Faction(1001, 201)]);

        Assert.Equal(CleanupChannelKind.PartyChat, plan.Channels.Single(c => c.Id == 200).Kind);
        Assert.Equal(CleanupChannelKind.FactionChat, plan.Channels.Single(c => c.Id == 201).Kind);
        Assert.DoesNotContain(plan.Channels, c => c.Id == 202);
    }

    [Fact]
    public void Roles_AreFoundByStoredId_PartyName_OrPartyChatName()
    {
        var plan = Plan(
            [new(100, "old-test-party", PartyCategory), new(101, "halvard", FactionCategory)],
            [
                Role(300, "Renamed On Discord"),
                Role(301, "liberal union"),
                Role(302, "Old Test Party"),
                Role(303, "Halvard"),
                Role(304, "Moderators"),
            ],
            [Party(1, "Liberal Union"), Party(2, "Crown League", roleId: 300)]);

        Assert.Equal([300UL, 301UL, 302UL], plan.Roles.Select(r => r.Id).OrderBy(id => id).ToList());
        Assert.Empty(plan.Refused);
    }

    [Fact]
    public void ForeignAffairsChannels_AreSelected_ByStoredId_OrCategory()
    {
        var plan = Plan(
            [new(200, "lu", PartyCategory), new(201, "lu-foreign-affairs", null), new(202, "cl-foreign-affairs", ArchiveCategory)],
            [],
            [Party(1, "Liberal Union", channelId: 200, foreignId: 201)]);

        Assert.Equal([200UL, 201UL, 202UL], plan.Channels.Where(c => c.Kind == CleanupChannelKind.PartyChat).Select(c => c.Id).ToList());
    }

    [Fact]
    public void OrphanRoles_AreFoundByAbbreviatedChatNames_OldOrNew()
    {
        var plan = Plan(
            [
                new(100, "lu", PartyCategory),
                new(101, "cl-2", ArchiveCategory),
                new(102, "potpw-foreign-affairs", PartyCategory),
                new(103, "old-test-party", ArchiveCategory),
                new(104, "whigs", PartyCategory),
            ],
            [
                Role(300, "Liberal Union"),
                Role(301, "Crown League"),
                Role(302, "Party of the People's Will"),
                Role(303, "Old Test Party"),
                Role(304, "Whigs"),
                Role(305, "Landed Gentry"),
                Role(306, "Moderators"),
            ]);

        Assert.Equal([300UL, 301UL, 302UL, 303UL, 304UL], plan.Roles.Select(r => r.Id).OrderBy(id => id).ToList());
    }

    [Fact]
    public void ProtectedRoles_AreRefused_WithTheirReason()
    {
        var plan = Plan([],
        [
            Role(Everyone, "Liberal Union", everyone: true),
            Role(GmRole, "Liberal Union"),
            Role(CabinetRole, "Liberal Union"),
            Role(400, "Liberal Union", managed: true),
            Role(401, "Liberal Union", position: BotTop),
            Role(402, "Liberal Union", position: BotTop + 3),
            Role(403, "Liberal Union", position: BotTop - 1),
        ],
        [Party(1, "Liberal Union")]);

        Assert.Equal([403UL], plan.Roles.Select(r => r.Id).ToList());
        var reasons = plan.Refused.ToDictionary(r => r.Id, r => r.Reason);
        Assert.Equal(CleanupRefusal.Everyone, reasons[Everyone]);
        Assert.Equal(CleanupRefusal.GmRole, reasons[GmRole]);
        Assert.Equal(CleanupRefusal.CabinetRole, reasons[CabinetRole]);
        Assert.Equal(CleanupRefusal.Managed, reasons[400]);
        Assert.Equal(CleanupRefusal.AboveBot, reasons[401]);
        Assert.Equal(CleanupRefusal.AboveBot, reasons[402]);
    }

    [Fact]
    public void RolesWithoutALetter_DoNotMatchByChatName()
    {
        var plan = Plan([new(100, "party", PartyCategory)], [Role(500, "!!!")]);

        Assert.Empty(plan.Roles);
    }

    [Fact]
    public void NothingFound_IsAnEmptyPlan()
    {
        var plan = Plan([new(News, "news", null), new(103, "general", null)], [Role(GmRole, "GM"), Role(304, "Moderators")]);

        Assert.True(plan.IsEmpty);
    }

    [Fact]
    public void ClearDiscordIds_ClearsOnlyTheDeletedIds_AndKeepsPartiesAndFactions()
    {
        Db.CreateFaction(1001, "Alice", "aurenten", 10);
        Db.CreateFaction(1002, "Bob", "aurenten", 10);
        Db.CreateFaction(1003, "Carol", "aurenten", 10);
        var first = Db.CreateParty("Liberal Union", "#C0392B", null, [1001, 1002]).PartyId!.Value;
        Db.SetPartyDiscordIds(first, 300, 100, null);
        Db.SetFactionPrivateChannel(1001, 201);
        Db.SetFactionPrivateChannel(1002, 202);

        var cleared = Db.ClearDiscordIds([300], [100, 201]);

        Assert.Equal(new ClearedDiscordIds(1, 1), cleared);
        var party = Db.GetParty(first)!;
        Assert.Null(party.RoleId);
        Assert.Null(party.ChannelId);
        Assert.Null(Db.GetFaction(1001)!.PrivateChannelId);
        Assert.Equal(202UL, Db.GetFaction(1002)!.PrivateChannelId);
        Assert.Single(Db.GetAllParties());
        Assert.Equal(3, Db.GetAllFactions().Count);
    }

    [Fact]
    public void ClearDiscordIds_KeepsTheOtherIdOfAParty()
    {
        Db.CreateFaction(1001, "Alice", "aurenten", 10);
        Db.CreateFaction(1002, "Bob", "aurenten", 10);
        var party = Db.CreateParty("Liberal Union", "#C0392B", null, [1001, 1002]).PartyId!.Value;
        Db.SetPartyDiscordIds(party, 300, 100, 101);

        Db.ClearDiscordIds([], [100]);

        Assert.Equal(300UL, Db.GetParty(party)!.RoleId);
        Assert.Null(Db.GetParty(party)!.ChannelId);
        Assert.Equal(101UL, Db.GetParty(party)!.ForeignChannelId);
    }

    [Fact]
    public void ClearDiscordIds_ClearsTheForeignAffairsChannel()
    {
        Db.CreateFaction(1001, "Alice", "aurenten", 10);
        Db.CreateFaction(1002, "Bob", "aurenten", 10);
        var party = Db.CreateParty("Liberal Union", "#C0392B", null, [1001, 1002]).PartyId!.Value;
        Db.SetPartyDiscordIds(party, 300, 100, 101);

        var cleared = Db.ClearDiscordIds([], [101]);

        Assert.Equal(new ClearedDiscordIds(1, 0), cleared);
        Assert.Equal(100UL, Db.GetParty(party)!.ChannelId);
        Assert.Null(Db.GetParty(party)!.ForeignChannelId);
    }
}
