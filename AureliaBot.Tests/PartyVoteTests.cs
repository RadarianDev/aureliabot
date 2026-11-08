using Xunit;

namespace AureliaBot.Tests;

public class PartyVoteTests : AssemblyTestBase
{
    private Reading OpenTenHours()
    {
        var reading = Propose(Chancellor, "ten_hours");
        Resolve();
        return reading;
    }

    private Reading Counted(long id) => Db.GetReading(id)!;

    [Fact]
    public void The_first_vote_in_a_party_counts_for_its_silent_members()
    {
        var reading = OpenTenHours();
        Vote(VoteSubject.Reading, reading.Id, Backbencher, VoteChoice.Yes);

        Resolve();

        Assert.Equal(90, Counted(reading.Id).YesSeats);
        Assert.Equal(ReadingStatus.Passed, Counted(reading.Id).Status);
    }

    [Fact]
    public void A_member_who_votes_keeps_their_own_vote()
    {
        var reading = OpenTenHours();
        Vote(VoteSubject.Reading, reading.Id, Backbencher, VoteChoice.Yes);
        Vote(VoteSubject.Reading, reading.Id, Minister, VoteChoice.No);

        Resolve();

        Assert.Equal(60, Counted(reading.Id).YesSeats);
        Assert.Equal(30, Counted(reading.Id).NoSeats);
    }

    [Fact]
    public void An_abstention_is_a_members_own_and_does_not_set_the_party_line()
    {
        var reading = OpenTenHours();
        Vote(VoteSubject.Reading, reading.Id, Chancellor, VoteChoice.Abstain);
        Vote(VoteSubject.Reading, reading.Id, Minister, VoteChoice.No);

        Resolve();

        Assert.Equal(40, Counted(reading.Id).AbstainSeats);
        Assert.Equal(50, Counted(reading.Id).NoSeats);
    }

    [Fact]
    public void Changing_the_first_vote_moves_the_party_line_with_it()
    {
        var reading = OpenTenHours();
        Vote(VoteSubject.Reading, reading.Id, Chancellor, VoteChoice.Yes);
        Vote(VoteSubject.Reading, reading.Id, Minister, VoteChoice.Yes);
        Vote(VoteSubject.Reading, reading.Id, Chancellor, VoteChoice.No);

        Resolve();

        Assert.Equal(60, Counted(reading.Id).NoSeats);
        Assert.Equal(30, Counted(reading.Id).YesSeats);
    }

    [Fact]
    public void Independents_follow_nobody()
    {
        var reading = OpenTenHours();
        Vote(VoteSubject.Reading, reading.Id, Chancellor, VoteChoice.Yes);

        Assert.DoesNotContain(Independent, Db.GetPartyFollowers(VoteSubject.Reading, reading.Id).Keys);
    }

    [Fact]
    public void A_retired_members_vote_sets_no_party_line()
    {
        var reading = OpenTenHours();
        Vote(VoteSubject.Reading, reading.Id, Backbencher, VoteChoice.No);
        Sql("UPDATE factions SET status = 'Retired' WHERE user_id = $id;", ("$id", Backbencher.ToString()));

        Assert.Empty(Db.GetPartyFollowers(VoteSubject.Reading, reading.Id));
    }

    [Fact]
    public void Followers_are_not_recorded_as_having_voted_and_log_no_stance()
    {
        var reading = OpenTenHours();
        Vote(VoteSubject.Reading, reading.Id, Backbencher, VoteChoice.Yes);

        Assert.Contains(Db.GetOpenReadingsNotVotedBy(Minister), r => r.Id == reading.Id);
        Resolve();

        Assert.DoesNotContain(Minister, Db.GetVotes(VoteSubject.Reading, reading.Id).Keys);
        Assert.Equal(VoteChoice.Yes, Db.GetPartyFollowers(VoteSubject.Reading, reading.Id)[Minister]);
        Assert.DoesNotContain(Db.GetLatestStances(Minister), s => s.Source == StanceSource.Vote);
        Assert.Contains(Db.GetLatestStances(Backbencher), s => s.Source == StanceSource.Vote);
    }

    [Fact]
    public void A_follower_with_seats_is_told_privately_and_a_voter_is_not()
    {
        var reading = OpenTenHours();
        Vote(VoteSubject.Reading, reading.Id, Backbencher, VoteChoice.Yes);

        var report = Resolve();

        Assert.Contains(report.PrivateMessages[Minister], m => m.Contains("ten_hours") && m.Contains(AssemblyPublisher.ChoiceName(VoteChoice.Yes)));
        Assert.False(report.PrivateMessages.TryGetValue(Backbencher, out var own) && own.Any(m => m.Contains("ten_hours")));
    }

    [Fact]
    public void A_follower_without_seats_is_not_told()
    {
        Db.SetFactionSeats(Minister, 0);
        var reading = OpenTenHours();
        Vote(VoteSubject.Reading, reading.Id, Backbencher, VoteChoice.Yes);

        var report = Resolve();

        Assert.False(report.PrivateMessages.TryGetValue(Minister, out var own) && own.Any(m => m.Contains("ten_hours")));
    }

    [Fact]
    public void The_party_line_shows_only_while_a_member_has_not_voted()
    {
        var reading = OpenTenHours();
        Assert.Empty(Db.GetPartyFollowers(VoteSubject.Reading, reading.Id));

        Vote(VoteSubject.Reading, reading.Id, Backbencher, VoteChoice.No);
        Assert.Equal(VoteChoice.No, Db.GetPartyFollowers(VoteSubject.Reading, reading.Id)[Minister]);

        Vote(VoteSubject.Reading, reading.Id, Minister, VoteChoice.Yes);
        Assert.DoesNotContain(Minister, Db.GetPartyFollowers(VoteSubject.Reading, reading.Id).Keys);
    }

    [Fact]
    public void A_cabinet_vote_is_followed_and_the_follower_is_told()
    {
        var nomination = Db.Nominate(Chancellor, new Dictionary<Ministry, ulong>
        {
            [Ministry.Treasury] = Minister, [Ministry.War] = Backbencher, [Ministry.Interior] = Opposer, [Ministry.CommerceLabor] = SecondOpposer,
        }).Nomination!;
        Assert.Null(Db.CastCabinetVote(nomination.Id, Backbencher, VoteChoice.Yes).Refusal);

        var reports = new[] { Resolve(), Resolve() };

        Assert.Equal(90, reports.SelectMany(r => r.CabinetVotes).Single().YesSeats);
        Assert.Contains(reports.SelectMany(r => r.PrivateMessages.GetValueOrDefault(Minister, [])), m => m.Contains(AssemblyPublisher.ChoiceName(VoteChoice.Yes)));
    }

    [Fact]
    public void No_confidence_is_never_followed()
    {
        Db.CastVote(VoteSubject.NoConfidence, 1, Backbencher, VoteChoice.Yes);

        Assert.Empty(Db.GetPartyFollowers(VoteSubject.NoConfidence, 1));
    }

    [Fact]
    public void Following_also_counts_in_the_senate()
    {
        Db.SaveGameState(Db.GetGameState() with { SenateEstablished = true });
        Db.SetFactionSenateSeats(Minister, 12);
        Db.SetFactionSenateSeats(Opposer, 5);
        var reading = OpenTenHours();
        Vote(VoteSubject.Reading, reading.Id, Backbencher, VoteChoice.Yes);
        Vote(VoteSubject.Reading, reading.Id, Opposer, VoteChoice.No);

        Resolve();

        Assert.Equal(12, Counted(reading.Id).SenateYesSeats);
        Assert.Equal(5, Counted(reading.Id).SenateNoSeats);
    }
}
