using Xunit;

namespace AureliaBot.Tests;

public class SenateTests : AssemblyTestBase
{
    private void SetCalendar(Season season, int year, bool established = true) =>
        Db.SaveGameState(Db.GetGameState() with { Season = season, Year = year, SenateEstablished = established });

    private void SeatSenators(params (ulong Faction, long Seats)[] seats)
    {
        foreach (var (faction, count) in seats)
            Db.SetFactionSenateSeats(faction, count);
    }

    private Reading OpenTenHours()
    {
        var reading = Propose(Chancellor, "ten_hours");
        Resolve();
        return reading;
    }

    private void GovernmentYes(long readingId)
    {
        Vote(VoteSubject.Reading, readingId, Chancellor, VoteChoice.Yes);
        Vote(VoteSubject.Reading, readingId, Minister, VoteChoice.Yes);
        Vote(VoteSubject.Reading, readingId, Backbencher, VoteChoice.Yes);
    }

    [Theory]
    [InlineData(Season.Summer, 1851, true, true)]
    [InlineData(Season.Summer, 1852, true, false)]
    [InlineData(Season.Autumn, 1851, true, false)]
    [InlineData(Season.Summer, 1851, false, false)]
    public void Senate_campaigns_fall_in_odd_summers_once_it_exists(Season season, int year, bool established, bool campaign)
    {
        SetCalendar(season, year, established);
        var state = Db.GetGameState();

        Assert.Equal(campaign, state.IsSenateCampaign);
        Assert.Equal(campaign, state.IsJoiningOpen);
        Assert.Equal(campaign ? 2 : 1, GameCalendar.SeasonLengthDays(state, C));
        Assert.Equal(campaign ? C.Senate.ActionsPerCampaignDay : C.Campaign.ActionsPerTurn, C.ActionsAllowed(state));
    }

    [Fact]
    public void A_pre_election_turn_keeps_its_own_length_and_actions_when_the_senate_votes_too()
    {
        Db.SaveGameState(Db.GetGameState() with { Season = Season.Summer, Year = 1851, SenateEstablished = true, PreElectionSeason = Season.Summer, PreElectionYear = 1851 });
        var state = Db.GetGameState();

        Assert.True(state.IsSenateElectionTurn);
        Assert.False(state.IsSenateCampaign);
        Assert.Equal(C.PreElectionDays, GameCalendar.SeasonLengthDays(state, C));
        Assert.Equal(C.Campaign.ActionsPerPreElectionDay, C.ActionsAllowed(state));
    }

    [Fact]
    public void A_senate_election_fills_all_89_seats_and_leaves_the_assembly_alone()
    {
        SetCalendar(Season.Summer, 1851);
        var assembly = Db.GetAllFactions().ToDictionary(f => f.UserId, f => f.Seats);

        var report = Resolve();

        Assert.NotNull(report.SenateElection);
        Assert.Null(report.Election);
        var factions = Db.GetAllFactions();
        Assert.Equal(89, factions.Sum(f => f.SenateSeats));
        Assert.Equal(0, factions.Single(f => f.UserId == Independent).SenateSeats);
        Assert.All(factions.Where(f => f.UserId != Independent), f => Assert.True(f.SenateSeats >= 1));
        Assert.Equal(assembly, factions.ToDictionary(f => f.UserId, f => f.Seats));
    }

    [Fact]
    public void Without_the_senate_no_senate_election_runs()
    {
        SetCalendar(Season.Summer, 1851, established: false);

        Assert.Null(Resolve().SenateElection);
        Assert.All(Db.GetAllFactions(), f => Assert.Equal(0, f.SenateSeats));
    }

    [Fact]
    public void A_bill_establishing_the_senate_opens_the_campaign_the_next_turn_when_that_is_an_odd_summer()
    {
        SetCalendar(Season.Winter, 1850, established: false);
        AddBill("senate_act", "franchise", Lean.For, "senate 1");
        var reading = Propose(Chancellor, "senate_act");
        Resolve();
        GovernmentYes(reading.Id);

        var report = Resolve();

        Assert.True(report.SenateCampaignOpened);
        Assert.Null(report.SenateElection);
        Assert.True(Db.GetGameState().IsSenateCampaign);
    }

    [Fact]
    public void A_bill_passed_at_the_end_of_an_odd_summer_waits_for_the_next_campaign()
    {
        SetCalendar(Season.Spring, 1851, established: false);
        AddBill("senate_act", "franchise", Lean.For, "senate 1");
        var reading = Propose(Chancellor, "senate_act");
        Resolve();
        GovernmentYes(reading.Id);

        var report = Resolve();

        Assert.True(Db.GetGameState().SenateEstablished);
        Assert.Null(report.SenateElection);
    }

    [Fact]
    public void A_bill_needs_both_chambers()
    {
        SetCalendar(Season.Summer, 1852);
        SeatSenators((Chancellor, 20), (Opposer, 10));
        var reading = OpenTenHours();
        GovernmentYes(reading.Id);
        Vote(VoteSubject.Reading, reading.Id, Opposer, VoteChoice.No);

        Resolve();

        var counted = Db.GetReading(reading.Id)!;
        Assert.Equal(ReadingStatus.Passed, counted.Status);
        Assert.Equal(20, counted.SenateYesSeats);
        Assert.Equal(10, counted.SenateNoSeats);
    }

    [Fact]
    public void An_assembly_rejection_ends_the_bill_whatever_the_senate_voted()
    {
        SetCalendar(Season.Summer, 1852);
        SeatSenators((Chancellor, 20), (Opposer, 10));
        var reading = OpenTenHours();
        Vote(VoteSubject.Reading, reading.Id, Chancellor, VoteChoice.Yes);
        Vote(VoteSubject.Reading, reading.Id, Minister, VoteChoice.Abstain);
        Vote(VoteSubject.Reading, reading.Id, Backbencher, VoteChoice.Abstain);
        Vote(VoteSubject.Reading, reading.Id, Opposer, VoteChoice.No);

        Resolve();

        Assert.Equal(ReadingStatus.Rejected, Db.GetReading(reading.Id)!.Status);
        Assert.Equal(BillStatus.Cooldown, Db.GetBill("ten_hours")!.Status);
        Assert.Single(Db.GetReadingsOfBill("ten_hours"));
    }

    private Reading ReturnedOverride()
    {
        SetCalendar(Season.Summer, 1852);
        SeatSenators((Chancellor, 10), (Opposer, 20));
        var reading = OpenTenHours();
        GovernmentYes(reading.Id);
        Vote(VoteSubject.Reading, reading.Id, Opposer, VoteChoice.No);

        var report = Resolve();

        Assert.True(Assert.Single(report.VoteResults, r => r.Subject == VoteSubject.Reading).ReturnedBySenate);
        Assert.Empty(report.Laws);
        Assert.Equal(ReadingStatus.ReturnedBySenate, Db.GetReading(reading.Id)!.Status);
        Assert.Equal(BillStatus.Voting, Db.GetBill("ten_hours")!.Status);
        var over = Db.GetReadingsOfBill("ten_hours").Single(r => r.Kind == ReadingKind.Override);
        Assert.Equal(ReadingStatus.Open, over.Status);
        return over;
    }

    [Fact]
    public void A_returned_bill_becomes_law_with_three_fifths_of_the_assembly()
    {
        var over = ReturnedOverride();
        GovernmentYes(over.Id);
        Vote(VoteSubject.Reading, over.Id, Opposer, VoteChoice.No);
        Vote(VoteSubject.Reading, over.Id, SecondOpposer, VoteChoice.Abstain);

        var report = Resolve();

        Assert.Equal(ReadingStatus.Passed, Db.GetReading(over.Id)!.Status);
        Assert.Equal(BillStatus.Passed, Db.GetBill("ten_hours")!.Status);
        Assert.Single(report.Laws);
    }

    [Fact]
    public void A_returned_bill_falls_short_of_three_fifths_and_is_rejected()
    {
        var over = ReturnedOverride();
        GovernmentYes(over.Id);
        Vote(VoteSubject.Reading, over.Id, Opposer, VoteChoice.No);
        Vote(VoteSubject.Reading, over.Id, SecondOpposer, VoteChoice.No);

        var report = Resolve();

        Assert.Equal(ReadingStatus.Rejected, Db.GetReading(over.Id)!.Status);
        Assert.Equal(BillStatus.Cooldown, Db.GetBill("ten_hours")!.Status);
        Assert.Empty(report.Laws);
    }

    [Fact]
    public void A_senator_without_assembly_seats_votes_on_bills_but_not_on_overrides()
    {
        SetCalendar(Season.Summer, 1852);
        Db.SetFactionSeats(Independent, 0);
        SeatSenators((Independent, 5), (Chancellor, 1));
        var reading = OpenTenHours();

        Vote(VoteSubject.Reading, reading.Id, Independent, VoteChoice.No);
        GovernmentYes(reading.Id);
        Resolve();

        var over = Db.GetReadingsOfBill("ten_hours").Single(r => r.Kind == ReadingKind.Override);
        Assert.Equal(AssemblyVoteOutcome.NoSeats, Db.CastAssemblyVote(VoteSubject.Reading, over.Id, Independent, VoteChoice.No, Rules).Outcome);
    }

    [Fact]
    public void Senate_seats_are_ignored_while_the_senate_is_switched_off()
    {
        SetCalendar(Season.Summer, 1852, established: false);
        SeatSenators((Opposer, 50));
        var reading = OpenTenHours();
        GovernmentYes(reading.Id);
        Vote(VoteSubject.Reading, reading.Id, Opposer, VoteChoice.No);

        Resolve();

        var counted = Db.GetReading(reading.Id)!;
        Assert.Equal(ReadingStatus.Passed, counted.Status);
        Assert.Null(counted.SenateYesSeats);
    }

    [Fact]
    public void A_home_province_is_full_at_the_cap()
    {
        var two = C with { Senate = C.Senate with { MaxHomeFactions = 2 } };

        Assert.True(Db.IsHomeProvinceFull("aurenten", two));
        Assert.False(Db.IsHomeProvinceFull("tovrerie", two));
    }
}
