using Microsoft.Data.Sqlite;
using Xunit;

namespace AureliaBot.Tests;

public abstract class AssemblyTestBase : IDisposable
{
    protected const ulong Chancellor = 1;
    protected const ulong Minister = 2;
    protected const ulong Backbencher = 3;
    protected const ulong Opposer = 4;
    protected const ulong SecondOpposer = 5;
    protected const ulong Independent = 6;

    private readonly DatabaseFixture _fixture = new();
    protected DatabaseService Db => _fixture.Db;
    protected static GameConfig Config => DatabaseFixture.Config;
    protected static GameConstants C => DatabaseFixture.Constants;

    protected static AssemblyRules Rules { get; } = new(
        (votes, seats) => new VoteTally(
            votes.Where(v => v.Value == VoteChoice.Yes).Sum(v => seats.GetValueOrDefault(v.Key)),
            votes.Where(v => v.Value == VoteChoice.No).Sum(v => seats.GetValueOrDefault(v.Key)),
            votes.Where(v => v.Value == VoteChoice.Abstain).Sum(v => seats.GetValueOrDefault(v.Key))),
        tally => tally.YesSeats > tally.NoSeats,
        (tally, filled) => tally.YesSeats * 2 > filled,
        (seats, share) => (int)Math.Round(seats * share, MidpointRounding.AwayFromZero),
        (lean, choice) => lean == Lean.Neutral
            ? null
            : choice switch
            {
                VoteChoice.Yes => (Stance)(int)lean,
                VoteChoice.No => (Stance)(-(int)lean),
                _ => null,
            },
        (c, lean, opinion, salience) => c.Law.SponsorFactor * (int)lean * opinion * salience / 3);

    protected AssemblyTestBase()
    {
        var now = DateTimeOffset.UtcNow;
        Db.StartGame(Config, now);
        Db.SaveGameState(Db.GetGameState() with
        {
            Season = Season.Summer,
            Year = 1850,
            TermNumber = 1,
            TermStartSeason = Season.Summer,
            TermStartYear = 1850,
            PreElectionSeason = Season.Spring,
            PreElectionYear = 1854,
            ResolutionNumber = 1,
            NextResolutionAt = now.AddDays(1),
        });

        Db.OpenIssue("labour", "labour", "The Condition of Labour", "For", "Against", 0.4, 3);
        Db.OpenIssue("franchise", "franchise", "The Franchise", "For", "Against", -0.2, 2);
        Db.CreateFaction(Chancellor, "Halvard", "aurenten", 20);
        Db.CreateFaction(Minister, "Mira", "iracterie", 20);
        Db.CreateFaction(Backbencher, "Bram", "north_arpal", 20);
        Db.CreateFaction(Opposer, "Osric", "tovrerie", 20);
        Db.CreateFaction(SecondOpposer, "Selda", "aurenten", 20);
        Db.CreateFaction(Independent, "Ivo", "iracterie", 20);
        var liberals = Db.CreateParty("Liberal Union", "#ffcc00", null, [Chancellor, Minister, Backbencher]).PartyId!.Value;
        Db.CreateParty("Country Party", "#336699", null, [Opposer, SecondOpposer]);
        Db.RecordProgramme(liberals, new Dictionary<string, Stance> { ["labour"] = Stance.For }, null, C);
        foreach (var (id, seats) in new[] { (Chancellor, 40L), (Minister, 30L), (Backbencher, 20L), (Opposer, 50L), (SecondOpposer, 30L), (Independent, 10L) })
            Db.SetFactionSeats(id, seats);

        AddBill("ten_hours", "labour", Lean.For, "economy -1");
        AddBill("labour_repeal", "labour", Lean.Against, "");
        AddBill("reform", "franchise", Lean.For, "");
        SeatCabinet(Chancellor, (Ministry.Treasury, Minister));
    }

    public void Dispose() => _fixture.Dispose();

    protected void Sql(string sql, params (string Name, object? Value)[] parameters)
    {
        using var conn = new SqliteConnection(_fixture.ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    protected void AddBill(string id, string issue, Lean lean, string effects) =>
        Sql(@"INSERT INTO bills (id, template_id, issue_id, title, motion, lean, pass_text, pass_effects, status, available_resolution)
              VALUES ($id, $id, $issue, $id, 'That it be enacted.', $lean, 'And so it was.', $effects, 'Available', 0);",
            ("$id", id), ("$issue", issue), ("$lean", (int)lean), ("$effects", effects));

    protected void SeatCabinet(ulong? chancellor, params (Ministry Ministry, ulong Holder)[] posts)
    {
        Sql("UPDATE cabinets SET status = 'Replaced' WHERE kind = 'Sitting';");
        Sql("INSERT INTO cabinets (kind, chancellor_faction_id, status, created_at, created_resolution) VALUES ('Sitting', $chancellor, 'Approved', 0, 1);",
            ("$chancellor", chancellor?.ToString()));
        foreach (var (ministry, holder) in posts)
            Sql("INSERT INTO cabinet_posts (cabinet_id, ministry, faction_id) VALUES ((SELECT MAX(id) FROM cabinets), $ministry, $holder);",
                ("$ministry", ministry.ToString()), ("$holder", holder.ToString()));
    }

    protected ResolutionReport Resolve() => Db.Resolve(Config, new ScriptedRandomSource(), 1, DateTimeOffset.UtcNow);

    protected ResolutionReport Count() => Db.CountDueAssemblyVotes(Config, Rules);

    protected Reading Propose(ulong faction, string bill)
    {
        var result = Db.ProposeBill(faction, bill, C);
        Assert.Equal(ProposeBillOutcome.Proposed, result.Outcome);
        return result.Reading!;
    }

    protected void Vote(VoteSubject subject, long id, ulong faction, VoteChoice choice) =>
        Assert.Equal(AssemblyVoteOutcome.Recorded, Db.CastAssemblyVote(subject, id, faction, choice, Rules).Outcome);
}

public class BillTests : AssemblyTestBase
{
    [Fact]
    public void AYesVoteOnAnAgainstBill_LogsAgainst_AndANoVoteLogsFor_OnlyOnceCounted()
    {
        var reading = Propose(Opposer, "labour_repeal");
        Resolve();

        var yes = Db.CastAssemblyVote(VoteSubject.Reading, reading.Id, Chancellor, VoteChoice.Yes, Rules);
        var no = Db.CastAssemblyVote(VoteSubject.Reading, reading.Id, Opposer, VoteChoice.No, Rules);
        var abstain = Db.CastAssemblyVote(VoteSubject.Reading, reading.Id, Independent, VoteChoice.Abstain, Rules);

        Assert.Equal(Stance.Against, yes.Logged);
        Assert.True(yes.OffProgramme);
        Assert.Equal(Stance.For, yes.ProgrammeStance);
        Assert.Equal(Stance.For, no.Logged);
        Assert.False(no.OffProgramme);
        Assert.Null(abstain.Logged);
        Assert.NotEqual(Stance.Against, Db.GetLatestLoggedStance(Chancellor, "labour"));

        Count();

        Assert.Equal(Stance.Against, Db.GetLatestLoggedStance(Chancellor, "labour"));
        Assert.Equal(Stance.For, Db.GetLatestLoggedStance(Opposer, "labour"));
        Assert.Null(Db.GetLatestLoggedStance(Independent, "labour"));
    }

    [Fact]
    public void AProposedBill_OpensAtTheNextResolution_AndIsCountedAtTheOneAfter()
    {
        var reading = Propose(Chancellor, "ten_hours");
        Assert.Equal(ReadingStatus.Scheduled, reading.Status);
        Assert.Equal(BillStatus.Voting, Db.GetBill("ten_hours")!.Status);
        Assert.Equal(AssemblyVoteOutcome.NotOpen, Db.CastAssemblyVote(VoteSubject.Reading, reading.Id, Chancellor, VoteChoice.Yes, Rules).Outcome);
        Assert.Empty(Count().VoteResults);

        var opened = Resolve();

        Assert.Equal(reading.Id, Assert.Single(opened.ReadingsOpened).ReadingId);
        var open = Db.GetReading(reading.Id)!;
        Assert.Equal(ReadingStatus.Open, open.Status);
        Assert.Equal(Db.GetGameState().ResolutionNumber + 1, open.DueResolution);

        Vote(VoteSubject.Reading, reading.Id, Chancellor, VoteChoice.Yes);
        Vote(VoteSubject.Reading, reading.Id, Minister, VoteChoice.Yes);
        Vote(VoteSubject.Reading, reading.Id, Opposer, VoteChoice.No);
        Vote(VoteSubject.Reading, reading.Id, Independent, VoteChoice.Abstain);
        var economy = Db.GetGameState().Economy;
        var popularity = Db.GetFaction(Chancellor)!.Popularity;

        var counted = Count();

        var result = Assert.Single(counted.VoteResults);
        Assert.True(result.Passed);
        Assert.Equal((90, 80, 10), (result.YesSeats, result.NoSeats, result.AbstainSeats));
        var law = Assert.Single(counted.Laws);
        Assert.Equal(Chancellor, law.SponsorFactionId);
        Assert.True(law.SponsorPopularityChange > 0);
        Assert.Equal(popularity + law.SponsorPopularityChange, Db.GetFaction(Chancellor)!.Popularity, 6);
        Assert.Equal(economy - 1, Db.GetGameState().Economy, 6);
        Assert.Equal(BillStatus.Passed, Db.GetBill("ten_hours")!.Status);
        var passed = Db.GetReading(reading.Id)!;
        Assert.Equal(ReadingStatus.Passed, passed.Status);
        Assert.Equal((90, 80, 10), (passed.YesSeats, passed.NoSeats, passed.AbstainSeats));
    }

    [Fact]
    public void ARejectedBill_Rests_WhileItsRivalsStayProposable_AndComesBack()
    {
        var reading = Propose(Chancellor, "ten_hours");
        Assert.DoesNotContain(Db.GetProposableBills(), b => b.IssueId == "labour");
        Resolve();
        Vote(VoteSubject.Reading, reading.Id, Opposer, VoteChoice.No);
        var salience = Db.GetIssue("labour")!.Salience;

        var counted = Count();

        Assert.False(Assert.Single(counted.VoteResults).Passed);
        Assert.Empty(counted.Laws);
        Assert.Equal(ReadingStatus.Rejected, Db.GetReading(reading.Id)!.Status);
        var bill = Db.GetBill("ten_hours")!;
        Assert.Equal(BillStatus.Cooldown, bill.Status);
        Assert.Equal(Db.GetGameState().ResolutionNumber + 1 + C.RejectedCooldownSeasons, bill.CooldownUntil);
        Assert.Equal(Math.Max(C.Salience.Min, salience - C.Salience.RejectDrop), Db.GetIssue("labour")!.Salience, 6);
        var proposable = Db.GetProposableBills().Select(b => b.Id).ToList();
        Assert.Contains("labour_repeal", proposable);
        Assert.DoesNotContain("ten_hours", proposable);
        Assert.Equal(ProposeBillOutcome.NotAvailable, Db.ProposeBill(Opposer, "ten_hours", C).Outcome);

        Resolve();
        for (var i = 0; i < C.RejectedCooldownSeasons; i++)
        {
            Assert.Equal(BillStatus.Cooldown, Db.GetBill("ten_hours")!.Status);
            Resolve();
        }

        Assert.Equal(BillStatus.Available, Db.GetBill("ten_hours")!.Status);
        Assert.Contains("ten_hours", Db.GetProposableBills().Select(b => b.Id));
    }

    [Fact]
    public void MinistersBackbenchersAndIndependents_CannotProposeGovernmentBills()
    {
        Assert.Equal(ProposeBillOutcome.MinisterCannot, Db.ProposeBill(Minister, "ten_hours", C).Outcome);
        Assert.Equal(ProposeBillOutcome.GovernmentBackbencher, Db.ProposeBill(Backbencher, "ten_hours", C).Outcome);
        Assert.Equal(ProposeBillOutcome.Independent, Db.ProposeBill(Independent, "ten_hours", C).Outcome);
        Assert.Equal(ReadingKind.Government, Propose(Chancellor, "ten_hours").Kind);
    }

    [Fact]
    public void AVacantChancellorship_ProposesNoGovernmentBill()
    {
        SeatCabinet(null, (Ministry.Treasury, Minister));

        Assert.Equal(ProposeBillOutcome.ChancellorshipVacant, Db.ProposeBill(Minister, "ten_hours", C).Outcome);
        Assert.Equal(ProposeBillOutcome.ChancellorshipVacant, Db.ProposeBill(Chancellor, "ten_hours", C).Outcome);
        Assert.Equal(BillStatus.Available, Db.GetBill("ten_hours")!.Status);
    }

    [Fact]
    public void OnlyOneBillPerIssue_IsBeforeTheAssembly()
    {
        Propose(Chancellor, "ten_hours");

        Assert.Equal(ProposeBillOutcome.IssueBusy, Db.ProposeBill(Opposer, "labour_repeal", C).Outcome);
        Assert.Equal(ReadingKind.Private, Propose(Opposer, "reform").Kind);
    }

    [Fact]
    public void TheChancellor_ProposesOneBillATurn_AndAWithdrawalGivesTheSlotBack()
    {
        var reading = Propose(Chancellor, "ten_hours");
        Assert.Equal(ProposeBillOutcome.GovernmentLimit, Db.ProposeBill(Chancellor, "reform", C).Outcome);
        Assert.Equal(WithdrawOutcome.NotYours, Db.WithdrawReading(reading.Id, Opposer).Outcome);

        Assert.Equal(WithdrawOutcome.Withdrawn, Db.WithdrawReading(reading.Id, Chancellor).Outcome);

        Assert.Equal(ReadingStatus.Cancelled, Db.GetReading(reading.Id)!.Status);
        Assert.Equal(BillStatus.Available, Db.GetBill("ten_hours")!.Status);
        Propose(Chancellor, "reform");
        Assert.DoesNotContain(Db.GetScheduledReadingsBy(Chancellor), r => r.BillId == "ten_hours");
    }

    [Fact]
    public void AWithdrawnPrivateBill_RefundsTheOppositionAction_ButNotOnceTheVoteOpens()
    {
        var reading = Propose(Opposer, "reform");
        Assert.False(Db.GetOppositionStanding(Opposer, C).CanAct);
        Assert.Equal(reading.Id, Assert.Single(Db.GetScheduledReadingsBy(Opposer)).Id);

        Assert.Equal(WithdrawOutcome.Withdrawn, Db.WithdrawReading(reading.Id, Opposer).Outcome);

        var standing = Db.GetOppositionStanding(Opposer, C);
        Assert.True(standing.CanProposeBill);
        Assert.Equal(0, standing.BillsThisYear);

        var again = Propose(Opposer, "reform");
        Resolve();
        Assert.Equal(WithdrawOutcome.NotScheduled, Db.WithdrawReading(again.Id, Opposer).Outcome);
    }

    [Fact]
    public void ABillScheduledIntoASnapPreElection_IsCancelledWithoutCooldown_AndNoLongerCounts()
    {
        var reading = Propose(Opposer, "reform");
        Assert.Equal(1, Db.GetOppositionStanding(Opposer, C).BillsThisYear);
        Db.SaveGameState(Db.GetGameState() with { PreElectionSeason = Season.Autumn, PreElectionYear = 1850 });

        var report = Resolve();

        Assert.True(Db.GetGameState().IsPreElection);
        Assert.Empty(report.ReadingsOpened);
        Assert.Equal(reading.Id, Assert.Single(report.ReadingsCancelled).ReadingId);
        Assert.Equal(ReadingStatus.Cancelled, Db.GetReading(reading.Id)!.Status);
        var bill = Db.GetBill("reform")!;
        Assert.Equal(BillStatus.Available, bill.Status);
        Assert.Null(bill.CooldownUntil);
        Assert.Equal(0, Db.GetOppositionStanding(Opposer, C).BillsThisYear);
    }

    [Fact]
    public void NoBill_IsProposedDuringOrJustBeforeAPreElection()
    {
        Db.SaveGameState(Db.GetGameState() with { PreElectionSeason = Season.Autumn, PreElectionYear = 1850 });
        Assert.Equal(ProposeBillOutcome.BeforePreElection, Db.ProposeBill(Chancellor, "ten_hours", C).Outcome);

        Db.SaveGameState(Db.GetGameState() with { PreElectionSeason = Season.Summer, PreElectionYear = 1850 });
        Assert.Equal(ProposeBillOutcome.PreElection, Db.ProposeBill(Opposer, "ten_hours", C).Outcome);
        Assert.Empty(Db.GetReadings(ReadingStatus.Scheduled));
    }

    [Fact]
    public void APrivateBill_SpendsTheSeasonsOppositionAction_AndOnlyOneBillAYear()
    {
        Propose(Opposer, "reform");
        Assert.True(Db.GetOppositionStanding(Opposer, C).UsedThisSeason);
        Assert.Equal(ProposeBillOutcome.OppositionActionUsed, Db.ProposeBill(Opposer, "labour_repeal", C).Outcome);

        Resolve();

        var standing = Db.GetOppositionStanding(Opposer, C);
        Assert.False(standing.UsedThisSeason);
        Assert.False(standing.CanProposeBill);
        Assert.Equal(ProposeBillOutcome.PrivateBillCapReached, Db.ProposeBill(Opposer, "labour_repeal", C).Outcome);
        Assert.Equal(ReadingKind.Private, Propose(SecondOpposer, "labour_repeal").Kind);
    }

    [Fact]
    public void OnlyActiveFactionsWithSeats_Vote_AndOnlyOnOpenReadings()
    {
        var reading = Propose(Chancellor, "ten_hours");
        Resolve();
        Db.SetFactionSeats(Independent, 0);

        Assert.Equal(AssemblyVoteOutcome.NoSeats, Db.CastAssemblyVote(VoteSubject.Reading, reading.Id, Independent, VoteChoice.Yes, Rules).Outcome);
        Vote(VoteSubject.Reading, reading.Id, Opposer, VoteChoice.Yes);
        var changed = Db.CastAssemblyVote(VoteSubject.Reading, reading.Id, Opposer, VoteChoice.No, Rules);
        Assert.Equal(VoteChoice.Yes, changed.Previous);
        Assert.Equal(VoteChoice.No, Db.GetVotes(VoteSubject.Reading, reading.Id)[Opposer]);
        Assert.Contains(Db.GetOpenReadingsNotVotedBy(Chancellor), r => r.Id == reading.Id);
        Assert.DoesNotContain(Db.GetOpenReadingsNotVotedBy(Opposer), r => r.Id == reading.Id);
    }
}
