using Discord;
using Xunit;

namespace AureliaBot.Tests;

public class PartyChannelTests
{
    private static Party Party(long id, string name) => new(id, name, "#C0392B", null, null, null, null, null, null, null);

    private static Faction Faction(ulong userId, long? partyId, FactionStatus status = FactionStatus.Active) =>
        new(userId, "Someone", 10, 0, partyId, "aurenten", null, status);

    [Fact]
    public void RolePositionsBySeats_PutsTheBiggestPartyOnTop_WithinTheSlotsTheRolesHold()
    {
        var positions = PartySyncService.RolePositionsBySeats([(100, 9, 5, 1), (200, 7, 20, 2), (300, 4, 12, 3), (400, 2, 12, 4)]);

        Assert.Equal(9, positions[200]);
        Assert.Equal(7, positions[300]);
        Assert.Equal(4, positions[400]);
        Assert.Equal(2, positions[100]);
    }

    [Theory]
    [InlineData("Liberal Union", "lu")]
    [InlineData("Party of the People's Will", "potpw")]
    [InlineData("  The People's Party! ", "tpp")]
    [InlineData("Anti-Royalist Front", "arf")]
    [InlineData("Ústřední Strana", "ús")]
    [InlineData("Party 1848", "p1")]
    [InlineData("Whigs", "whigs")]
    [InlineData("Whigs!", "whigs")]
    [InlineData("Free Traders' League & Co.", "ftlc")]
    [InlineData("!!!", "party")]
    [InlineData("", "party")]
    public void ChannelName_IsTheInitials_OrTheSlugOfASingleWord(string name, string expected)
    {
        Assert.Equal(expected, PartySyncService.ChannelName(name));
    }

    [Fact]
    public void ChannelName_OfALongSingleWord_LeavesRoomForTheForeignSuffix()
    {
        var name = PartySyncService.ChannelName(new string('a', 150));

        Assert.True(PartySyncService.ForeignChannelName(name).Length <= 100);
    }

    [Fact]
    public void CollidingInitials_AreNumbered_InPartyIdOrder()
    {
        var names = PartySyncService.ChatNames(
        [
            Party(7, "Liberal Unity"),
            Party(2, "Liberal Union"),
            Party(4, "Crown League"),
            Party(9, "Labour Unionists"),
        ]);

        Assert.Equal("lu", names[2]);
        Assert.Equal("cl", names[4]);
        Assert.Equal("lu-2", names[7]);
        Assert.Equal("lu-3", names[9]);
    }

    [Fact]
    public void ANumberedName_IsNotTakenTwice()
    {
        var names = PartySyncService.ChatNames([Party(1, "Lu-2"), Party(2, "Liberal Union"), Party(3, "Long Unity"), Party(4, "L 2")]);

        Assert.Equal(["l2", "lu", "lu-2", "l2-2"], names.OrderBy(n => n.Key).Select(n => n.Value).ToList());
    }

    [Fact]
    public void ForeignChannel_IsNamedAfterTheChat()
    {
        Assert.Equal("lu-foreign-affairs", PartySyncService.ForeignChannelName("lu"));
        Assert.Equal("lu-2-foreign-affairs", PartySyncService.ForeignChannelName("lu-2"));
    }

    [Theory]
    [InlineData("lu", "lu")]
    [InlineData("lu-2", "lu")]
    [InlineData("lu-foreign-affairs", "lu")]
    [InlineData("lu-2-foreign-affairs", "lu")]
    [InlineData("liberal-union", "liberal-union")]
    [InlineData("party-", "party-")]
    public void ChannelStem_DropsTheForeignSuffixAndTheNumber(string channel, string stem)
    {
        Assert.Equal(stem, PartySyncService.ChannelStem(channel));
    }

    [Fact]
    public void AnyActiveMember_MayInviteAnOutsider()
    {
        Assert.Null(PartySyncService.GuestRefusal(Faction(1, 5), false, Faction(2, 6)));
        Assert.Null(PartySyncService.GuestRefusal(Faction(1, 5), false, Faction(2, null)));
        Assert.Null(PartySyncService.GuestRefusal(Faction(1, 5), false, null));
        Assert.Null(PartySyncService.GuestRefusal(Faction(1, 5), false, Faction(2, 5, FactionStatus.Retired)));
    }

    [Fact]
    public void NonMembers_Bots_AndOwnMembers_AreRefused()
    {
        Assert.Equal(ForeignGuestRefusal.NotInParty, PartySyncService.GuestRefusal(null, false, null));
        Assert.Equal(ForeignGuestRefusal.NotInParty, PartySyncService.GuestRefusal(Faction(1, null), false, null));
        Assert.Equal(ForeignGuestRefusal.NotInParty, PartySyncService.GuestRefusal(Faction(1, 5, FactionStatus.Retired), false, null));
        Assert.Equal(ForeignGuestRefusal.Bot, PartySyncService.GuestRefusal(Faction(1, 5), true, null));
        Assert.Equal(ForeignGuestRefusal.OwnMember, PartySyncService.GuestRefusal(Faction(1, 5), false, Faction(2, 5)));
        Assert.Equal(ForeignGuestRefusal.OwnMember, PartySyncService.GuestRefusal(Faction(1, 5), false, Faction(1, 5)));
    }

    [Fact]
    public void Guests_AreTheUserOverwrites_OtherThanTheBot()
    {
        var allow = new OverwritePermissions((ulong)PartySyncService.GuestPermissions, 0);
        var guests = PartySyncService.Guests(
        [
            new Overwrite(1, PermissionTarget.Role, allow),
            new Overwrite(99, PermissionTarget.User, allow),
            new Overwrite(1001, PermissionTarget.User, allow),
            new Overwrite(1002, PermissionTarget.User, allow),
        ], 99);

        Assert.Equal([1001UL, 1002UL], guests);
    }

    [Fact]
    public void GuestPermissions_GrantNothingManagerial()
    {
        var managerial = ChannelPermission.ManageChannels | ChannelPermission.ManageMessages | ChannelPermission.ManageRoles
            | ChannelPermission.PinMessages | ChannelPermission.MentionEveryone | ChannelPermission.ManageWebhooks;

        Assert.Equal(0UL, (ulong)(PartySyncService.GuestPermissions & managerial));
    }
}
