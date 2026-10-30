public sealed record VoteTally(int YesSeats, int NoSeats, int AbstainSeats);

public static class VoteCounting
{
    public static VoteTally Tally(IReadOnlyDictionary<ulong, VoteChoice> votes, IReadOnlyDictionary<ulong, int> seats) =>
        throw new NotImplementedException();

    public static bool MajorityPasses(VoteTally tally) => throw new NotImplementedException();

    public static bool NoConfidencePasses(VoteTally tally, int filledSeats) => throw new NotImplementedException();

    public static int TablingThreshold(int assemblySeats, double share) => throw new NotImplementedException();

    public static Stance? LoggedStance(Lean billLean, VoteChoice choice) => throw new NotImplementedException();

    public static double SponsorReward(GameConstants constants, Lean billLean, double nationalOpinion, double salience) =>
        throw new NotImplementedException();
}
