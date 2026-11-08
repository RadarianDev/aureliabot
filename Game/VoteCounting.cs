public sealed record VoteTally(int YesSeats, int NoSeats, int AbstainSeats);

public static class VoteCounting
{
    public static VoteTally Tally(IReadOnlyDictionary<ulong, VoteChoice> votes, IReadOnlyDictionary<ulong, int> seats)
    {
        int SeatsFor(VoteChoice choice) => votes
            .Where(vote => vote.Value == choice)
            .Sum(vote => seats.TryGetValue(vote.Key, out var held) ? Math.Max(0, held) : 0);

        return new VoteTally(SeatsFor(VoteChoice.Yes), SeatsFor(VoteChoice.No), SeatsFor(VoteChoice.Abstain));
    }

    public static bool MajorityPasses(VoteTally tally) => tally.YesSeats > tally.NoSeats;

    public static bool OverridePasses(VoteTally tally, double share)
    {
        var cast = tally.YesSeats + tally.NoSeats;
        return cast > 0 && tally.YesSeats >= share * cast - 1e-9;
    }

    public static bool NoConfidencePasses(VoteTally tally, int filledSeats) => 2L * tally.YesSeats > filledSeats;

    public static int TablingThreshold(int assemblySeats, double share) =>
        Math.Max(1, (int)Math.Round(assemblySeats * share, MidpointRounding.AwayFromZero));

    public static Stance? LoggedStance(Lean billLean, VoteChoice choice) => (billLean, choice) switch
    {
        (Lean.For, VoteChoice.Yes) or (Lean.Against, VoteChoice.No) => Stance.For,
        (Lean.Against, VoteChoice.Yes) or (Lean.For, VoteChoice.No) => Stance.Against,
        _ => null,
    };

    public static double SponsorReward(GameConstants constants, Lean billLean, double nationalOpinion, double salience) =>
        constants.Law.SponsorFactor * (int)billLean * nationalOpinion * Formulas.SalienceFactor(salience);
}
