public sealed record ElectionProvince(string Id, double Population);

public sealed record ElectionFaction(ulong Id, long PartyId, double Popularity, string HomeProvinceId);

public sealed record ElectionParty(long Id, IReadOnlyDictionary<string, Stance> Programme);

public sealed record ElectionIssue(string Id, double Salience, IReadOnlyDictionary<string, double> OpinionByProvince);

public sealed record ElectionInput(
    int Seats,
    IReadOnlyList<ElectionProvince> Provinces,
    IReadOnlyList<ElectionParty> Parties,
    IReadOnlyList<ElectionFaction> Factions,
    IReadOnlyList<ElectionIssue> Issues,
    ElectionConstants Constants,
    double StrongStancePenalty);

public sealed record ProvinceOutcome(
    string ProvinceId,
    int ContestedSeats,
    IReadOnlyDictionary<long, double> Strength,
    IReadOnlyDictionary<long, int> ContestedSeatsByParty,
    IReadOnlyDictionary<long, int> BoroughSeatsByParty,
    long? WinnerPartyId);

public sealed record ElectionResult(
    IReadOnlyList<ProvinceOutcome> Provinces,
    IReadOnlyDictionary<long, int> PartySeats,
    IReadOnlyDictionary<ulong, int> FactionSeats,
    IReadOnlyDictionary<long, double> VoteShare);

public static class ElectionCalculator
{
    public static ElectionResult Run(ElectionInput input) => throw new NotImplementedException();
}
