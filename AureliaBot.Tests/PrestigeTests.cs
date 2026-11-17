using Xunit;

namespace AureliaBot.Tests;

public class PrestigeTests : AssemblyTestBase
{
    private static EffectSource Source(ulong? actor = null) => new("test", "test", "1", ActorId: actor);

    private EffectApplication Apply(string text, ulong? actor = null) =>
        Db.ApplyEffects(EffectParser.ParseText(text).Effects, Config, Source(actor));

    [Fact]
    public void Factions_start_with_no_prestige()
    {
        Assert.All(Db.GetActiveFactions(), f => Assert.Equal(0, f.Prestige));
    }

    [Fact]
    public void Prestige_effects_reach_the_same_targets_as_popularity_and_leave_popularity_alone()
    {
        var popularity = Db.GetActiveFactions().ToDictionary(f => f.UserId, f => f.Popularity);

        var result = Apply($"prestige.{Opposer} 3, prestige.government 1, prestige.sponsor 2", Independent);

        Assert.Empty(result.Warnings);
        Assert.Equal(3, Db.GetFaction(Opposer)!.Prestige);
        Assert.Equal(1, Db.GetFaction(Chancellor)!.Prestige);
        Assert.Equal(1, Db.GetFaction(Backbencher)!.Prestige);
        Assert.Equal(2, Db.GetFaction(Independent)!.Prestige);
        Assert.Equal(popularity, Db.GetActiveFactions().ToDictionary(f => f.UserId, f => f.Popularity));
    }

    [Fact]
    public void Prestige_never_fades_across_resolutions()
    {
        Apply($"prestige.{Opposer} 4");

        Resolve();
        Resolve();

        Assert.Equal(4, Db.GetFaction(Opposer)!.Prestige);
    }

    [Fact]
    public void Formulas_read_prestige()
    {
        Apply($"prestige.{Opposer} 4");

        Apply($"if {{prestige.{Opposer} >= 4}} [prestige.{Minister} 1]");

        Assert.Equal(1, Db.GetFaction(Minister)!.Prestige);
    }

    [Fact]
    public void The_leaderboard_ranks_by_prestige_then_popularity()
    {
        Apply($"prestige.{Opposer} 5, prestige.{Minister} 2");

        var ranked = Standings.ByPrestige(Db);
        var lines = Standings.PrestigeLines(Db);

        Assert.Equal([Opposer, Minister], ranked.Take(2).Select(f => f.UserId));
        Assert.Equal(1, Standings.PrestigeRank(Db, Opposer));
        Assert.Equal(2, Standings.PrestigeRank(Db, Minister));
        Assert.Equal(Db.GetActiveFactions().Count, lines.Count);
        Assert.StartsWith($"1. <@{Opposer}>", lines[0]);
        Assert.Contains("**5**", lines[0]);
    }

    [Fact]
    public void Prestige_is_never_lost()
    {
        Apply($"prestige.{Opposer} 4");

        Assert.False(EffectParser.ParseText($"prestige.{Opposer} -2").IsValid);
        var formula = Apply($"prestige.{Opposer} {{-2}}");
        var set = Apply($"prestige.{Opposer} =1");
        Db.AddPrestige(Opposer, -3);

        Assert.Single(formula.Warnings);
        Assert.Single(set.Warnings);
        Assert.Equal(4, Db.GetFaction(Opposer)!.Prestige);
    }

    [Fact]
    public void A_carried_motion_rewards_the_tabler_and_the_cosigners()
    {
        Db.SetFactionSeats(Opposer, 20);
        var motion = Db.TableNoConfidence(Opposer, C, Rules).Motion!;
        Assert.Equal(NoConfidenceOutcome.WentToVote, Db.CosignNoConfidence(motion.Id, SecondOpposer, C, Rules).Outcome);
        Db.SetFactionSeats(Opposer, 50);
        Resolve();
        foreach (var faction in new[] { Opposer, SecondOpposer, Backbencher })
            Vote(VoteSubject.NoConfidence, motion.Id, faction, VoteChoice.Yes);

        var outcome = Assert.Single(Count().NoConfidenceMotions);

        Assert.Equal(NoConfidenceStatus.Passed, outcome.Outcome);
        Assert.Equal(Opposer, outcome.PrestigeLeader);
        Assert.Equal([SecondOpposer], outcome.PrestigeSupporters);
        Assert.Equal(C.Prestige.NoConfidenceLeader, Db.GetFaction(Opposer)!.Prestige);
        Assert.Equal(C.Prestige.NoConfidenceSupporter, Db.GetFaction(SecondOpposer)!.Prestige);
        Assert.Equal(0, Db.GetFaction(Chancellor)!.Prestige);
    }

    [Fact]
    public void A_failed_motion_rewards_the_chancellor_and_the_government()
    {
        var motion = Db.TableNoConfidence(Opposer, C, Rules).Motion!;
        Resolve();
        Vote(VoteSubject.NoConfidence, motion.Id, Opposer, VoteChoice.Yes);

        var outcome = Assert.Single(Count().NoConfidenceMotions);

        Assert.Equal(NoConfidenceStatus.Failed, outcome.Outcome);
        Assert.Equal(Chancellor, outcome.PrestigeLeader);
        Assert.Equal([Minister, Backbencher], outcome.PrestigeSupporters);
        Assert.Equal(C.Prestige.NoConfidenceLeader, Db.GetFaction(Chancellor)!.Prestige);
        Assert.Equal(C.Prestige.NoConfidenceSupporter, Db.GetFaction(Minister)!.Prestige);
        Assert.Equal(C.Prestige.NoConfidenceSupporter, Db.GetFaction(Backbencher)!.Prestige);
        Assert.Equal(0, Db.GetFaction(Opposer)!.Prestige);
    }

    [Fact]
    public void Places_are_shared_by_equal_prestige()
    {
        double[] everyone = [5, 2, 2, 1, 0];

        Assert.Equal([0, 1, 1, 2, 3], everyone.Select(p => Formulas.PrestigePlace(p, everyone)));
    }

    [Fact]
    public void The_first_places_gain_popularity_every_season()
    {
        Apply($"prestige.{Opposer} 3");

        Resolve();

        var lead = Db.GetFaction(Opposer)!.Popularity - Db.GetFaction(SecondOpposer)!.Popularity;
        Assert.Equal(C.Prestige.Renown[0], lead, 6);
    }

    [Fact]
    public void Agendas_are_worth_more_than_a_no_confidence_win()
    {
        var agenda = Config.Agendas.Reward;

        Assert.True(AgendaRules.Reward(2, 2, agenda) > C.Prestige.NoConfidenceLeader * 2);
    }

    [Fact]
    public void The_summary_names_prestige()
    {
        var names = new EffectNames(id => id, id => id, id => id, id => $"<@{id}>", id => id, id => id, id => id, id => id);

        var lines = EffectSummary.Lines(EffectParser.ParseText($"prestige.{Opposer} 2, prestige.government 1").Effects, names, C.Display);

        Assert.Contains($"<@{Opposer}> prestige", lines[0]);
        Assert.Contains("Prestige of every government faction", lines[1]);
    }
}
