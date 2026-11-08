public sealed record ElectionProvince(string Id, double Population);

public sealed record ElectionFaction(
    ulong Id, long PartyId, double Popularity, string HomeProvinceId, IReadOnlyDictionary<string, double>? Regional = null)
{
    public double PopularityIn(string provinceId) =>
        Regional is not null && Regional.TryGetValue(provinceId, out var regional) ? regional : Popularity;
}

public sealed record ElectionParty(long Id, IReadOnlyDictionary<string, Stance> Programme);

public sealed record ElectionIssue(string Id, double Salience, IReadOnlyDictionary<string, double> OpinionByProvince);

public sealed record ElectionInput(
    int Seats,
    IReadOnlyList<ElectionProvince> Provinces,
    IReadOnlyList<ElectionParty> Parties,
    IReadOnlyList<ElectionFaction> Factions,
    IReadOnlyList<ElectionIssue> Issues,
    ElectionConstants Constants,
    double StrongStancePenalty,
    bool SenateEstablished = false);

public sealed record ProvinceOutcome(
    string ProvinceId,
    int ContestedSeats,
    IReadOnlyDictionary<long, double> Strength,
    IReadOnlyDictionary<long, int> ContestedSeatsByParty,
    IReadOnlyDictionary<long, int> BoroughSeatsByParty,
    long? WinnerPartyId);

public sealed record IssueTerm(string IssueId, Stance Stance, double Opinion, double SalienceShare, double StanceScore, int Crowd, double Contribution);

public sealed record MemberWeight(ulong FactionId, double Popularity, bool AtHome, double Weighted);

public sealed record StrengthBreakdown(
    long PartyId,
    IReadOnlyList<MemberWeight> Members,
    double Weighted,
    double BaseStrength,
    IReadOnlyList<IssueTerm> Issues,
    double ProgrammeScore,
    double Multiplier,
    double Strength);

public sealed record ElectionResult(
    IReadOnlyList<ProvinceOutcome> Provinces,
    IReadOnlyDictionary<long, int> PartySeats,
    IReadOnlyDictionary<ulong, int> FactionSeats,
    IReadOnlyDictionary<long, double> VoteShare,
    IReadOnlySet<long>? BelowThreshold = null);

public static class ElectionCalculator
{
    public static ElectionResult Run(ElectionInput input)
    {
        if (input.Parties.Count == 0) return NoResult();

        var (parties, membersByParty, totalPopularity, strengthByProvince, voteShare) = Prepare(input);
        var factionCount = membersByParty.Values.Sum(members => members.Count);
        var boroughSeatsPerFaction = input.SenateEstablished ? 0 : 1;
        var contestedByProvince = Apportion(input.Provinces, Math.Max(0, input.Seats - factionCount * boroughSeatsPerFaction));
        var contestedByParty = parties.ToDictionary(p => p.Id, _ => 0);
        var belowThreshold = PartiesBelowThreshold(input, parties, voteShare);
        var outcomes = new List<ProvinceOutcome>();

        foreach (var province in input.Provinces)
        {
            var strength = strengthByProvince[province.Id];
            var contesting = strength.Where(kv => !belowThreshold.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
            var won = DHondt(contesting, totalPopularity, contestedByProvince[province.Id]);
            var seats = parties.ToDictionary(p => p.Id, p => won.GetValueOrDefault(p.Id));
            var borough = parties.ToDictionary(p => p.Id, p => boroughSeatsPerFaction * membersByParty[p.Id].Count(f => f.HomeProvinceId == province.Id));

            foreach (var party in parties)
                contestedByParty[party.Id] += seats[party.Id];

            outcomes.Add(new ProvinceOutcome(province.Id, contestedByProvince[province.Id], strength, seats, borough,
                Winner(strength, seats, borough, totalPopularity)));
        }

        var factionSeats = new Dictionary<ulong, int>();
        foreach (var party in parties)
            foreach (var (factionId, seats) in SplitAmongMembers(membersByParty[party.Id], contestedByParty[party.Id]))
                factionSeats[factionId] = seats + boroughSeatsPerFaction;

        var partySeats = parties.ToDictionary(p => p.Id, p => contestedByParty[p.Id] + membersByParty[p.Id].Count * boroughSeatsPerFaction);

        return new ElectionResult(outcomes, partySeats, factionSeats, voteShare, belowThreshold);
    }

    public static ElectionResult RunSenate(ElectionInput input, IReadOnlyDictionary<string, int> seatsByProvince)
    {
        if (input.Parties.Count == 0) return NoResult();

        var (parties, membersByParty, totalPopularity, strengthByProvince, voteShare) = Prepare(input);
        var contestedByParty = parties.ToDictionary(p => p.Id, _ => 0);
        var outcomes = new List<ProvinceOutcome>();

        foreach (var province in input.Provinces)
        {
            var strength = strengthByProvince[province.Id];
            var borough = parties.ToDictionary(p => p.Id, p => membersByParty[p.Id].Count(f => f.HomeProvinceId == province.Id));
            var contested = Math.Max(0, seatsByProvince.GetValueOrDefault(province.Id) - borough.Values.Sum());
            var seats = DHondt(strength, totalPopularity, contested);

            foreach (var party in parties)
                contestedByParty[party.Id] += seats[party.Id];

            outcomes.Add(new ProvinceOutcome(province.Id, contested, strength, seats, borough, Winner(strength, seats, borough, totalPopularity)));
        }

        var factionSeats = new Dictionary<ulong, int>();
        foreach (var party in parties)
            foreach (var (factionId, seats) in SplitAmongMembers(membersByParty[party.Id], contestedByParty[party.Id]))
                factionSeats[factionId] = seats + 1;

        var partySeats = parties.ToDictionary(p => p.Id, p => contestedByParty[p.Id] + membersByParty[p.Id].Count);
        return new ElectionResult(outcomes, partySeats, factionSeats, voteShare);
    }

    public static IReadOnlyDictionary<string, int> SenateSeats(GameConfig config) =>
        config.Provinces.ToDictionary(p => p.Id, p => p.SenateSeats);

    private sealed record Contest(
        List<ElectionParty> Parties,
        Dictionary<long, List<ElectionFaction>> Members,
        Dictionary<long, double> TotalPopularity,
        Dictionary<string, Dictionary<long, double>> StrengthByProvince,
        Dictionary<long, double> VoteShare);

    private static ElectionResult NoResult() =>
        new([], new Dictionary<long, int>(), new Dictionary<ulong, int>(), new Dictionary<long, double>());

    private static Contest Prepare(ElectionInput input)
    {
        var parties = input.Parties.OrderBy(p => p.Id).ToList();
        var partyIds = parties.Select(p => p.Id).ToHashSet();
        var factions = input.Factions.Where(f => partyIds.Contains(f.PartyId)).ToList();
        var members = parties.ToDictionary(p => p.Id, p => factions.Where(f => f.PartyId == p.Id).ToList());
        var popularity = parties.ToDictionary(p => p.Id, p => members[p.Id].Sum(f => f.Popularity));
        var strength = input.Provinces.ToDictionary(
            province => province.Id,
            province => parties.ToDictionary(p => p.Id, p => Strength(input, parties, p, members[p.Id], province.Id)));
        return new Contest(parties, members, popularity, strength, NationalVote(input.Provinces, strength, parties));
    }

    private static Dictionary<long, double> NationalVote(
        IReadOnlyList<ElectionProvince> provinces,
        IReadOnlyDictionary<string, Dictionary<long, double>> strengthByProvince,
        IReadOnlyList<ElectionParty> parties)
    {
        var counted = provinces.Where(p => strengthByProvince[p.Id].Values.Sum() > 0).ToList();
        var population = counted.Sum(p => Math.Max(0, p.Population));
        return parties.ToDictionary(party => party.Id, party => population > 0
            ? counted.Sum(p => Math.Max(0, p.Population) * strengthByProvince[p.Id][party.Id] / strengthByProvince[p.Id].Values.Sum()) / population
            : 0);
    }

    private static HashSet<long> PartiesBelowThreshold(ElectionInput input, IReadOnlyList<ElectionParty> parties, IReadOnlyDictionary<long, double> voteShare)
    {
        if (!input.SenateEstablished) return [];
        var below = parties.Where(p => voteShare[p.Id] < input.Constants.Threshold).Select(p => p.Id).ToHashSet();
        return below.Count == parties.Count ? [] : below;
    }

    internal static Dictionary<string, int> Apportion(IReadOnlyList<ElectionProvince> provinces, int seats)
    {
        var result = provinces.ToDictionary(p => p.Id, _ => 0);
        if (provinces.Count == 0 || seats == 0) return result;

        var totalPopulation = provinces.Sum(p => Math.Max(0, p.Population));
        double Quota(ElectionProvince p) => totalPopulation > 0
            ? seats * Math.Max(0, p.Population) / totalPopulation
            : (double)seats / provinces.Count;

        var quotas = provinces.Select((p, index) => (Province: p, Index: index, Quota: Quota(p))).ToList();
        foreach (var q in quotas) result[q.Province.Id] = (int)Math.Floor(q.Quota);

        var left = seats - result.Values.Sum();
        foreach (var q in quotas
            .OrderByDescending(q => q.Quota - Math.Floor(q.Quota))
            .ThenByDescending(q => q.Province.Population)
            .ThenBy(q => q.Index)
            .Take(left))
            result[q.Province.Id]++;

        return result;
    }

    public static List<StrengthBreakdown> Explain(ElectionInput input, string provinceId)
    {
        var parties = input.Parties.OrderBy(p => p.Id).ToList();
        return parties
            .Select(p => Breakdown(input, parties, p, input.Factions.Where(f => f.PartyId == p.Id).ToList(), provinceId))
            .ToList();
    }

    private static double Strength(ElectionInput input, IReadOnlyList<ElectionParty> parties, ElectionParty party,
        IReadOnlyList<ElectionFaction> members, string provinceId) =>
        Breakdown(input, parties, party, members, provinceId).Strength;

    private static StrengthBreakdown Breakdown(ElectionInput input, IReadOnlyList<ElectionParty> parties, ElectionParty party,
        IReadOnlyList<ElectionFaction> members, string provinceId)
    {
        var c = input.Constants;
        var weights = members
            .Select(f => (f, AtHome: f.HomeProvinceId == provinceId))
            .Select(x => (x.f, x.AtHome, Popularity: x.f.PopularityIn(provinceId)))
            .Select(x => new MemberWeight(x.f.Id, x.Popularity, x.AtHome, x.Popularity * (x.AtHome ? c.HomeMultiplier : 1)))
            .ToList();
        var weighted = weights.Sum(w => w.Weighted);
        var baseStrength = Math.Pow(Math.Max(0, weighted), c.BaseExponent);
        var issues = IssueTerms(input, parties, party, provinceId);
        var score = issues.Sum(t => t.Contribution);
        var multiplier = Math.Clamp(1 + c.ProgrammeWeight * score, c.MultiplierMin, c.MultiplierMax);
        return new StrengthBreakdown(party.Id, weights, weighted, baseStrength, issues, score, multiplier, baseStrength * multiplier);
    }

    internal static double ProgrammeScore(ElectionInput input, IReadOnlyList<ElectionParty> parties, ElectionParty party, string provinceId) =>
        IssueTerms(input, parties, party, provinceId).Sum(t => t.Contribution);

    private static List<IssueTerm> IssueTerms(ElectionInput input, IReadOnlyList<ElectionParty> parties, ElectionParty party, string provinceId)
    {
        var terms = new List<IssueTerm>();
        var totalSalience = input.Issues.Sum(i => i.Salience);
        if (totalSalience == 0) return terms;

        foreach (var issue in input.Issues)
        {
            var stance = party.Programme.GetValueOrDefault(issue.Id, Stance.Silent);
            if (stance == Stance.Silent) continue;

            var sign = stance.Sign();
            var crowd = input.Constants.Crowding
                ? parties.Count(p => p.Programme.GetValueOrDefault(issue.Id, Stance.Silent).Sign() == sign)
                : 1;
            var opinion = issue.OpinionByProvince.GetValueOrDefault(provinceId, 0);
            var share = issue.Salience / totalSalience;
            var stanceScore = Formulas.StanceScore(stance, opinion, input.StrongStancePenalty);
            terms.Add(new IssueTerm(issue.Id, stance, opinion, share, stanceScore, crowd, share * stanceScore / Math.Sqrt(crowd)));
        }
        return terms;
    }

    private static Dictionary<long, int> DHondt(IReadOnlyDictionary<long, double> strength, IReadOnlyDictionary<long, double> popularity, int seats)
    {
        var won = strength.Keys.ToDictionary(id => id, _ => 0);
        for (var seat = 0; seat < seats; seat++)
        {
            var next = Best(strength.Select(kv => (kv.Key, kv.Value / (won[kv.Key] + 1))), popularity);
            if (next is not { } id) break;
            won[id]++;
        }
        return won;
    }

    private static long? Winner(IReadOnlyDictionary<long, double> strength, IReadOnlyDictionary<long, int> contested,
        IReadOnlyDictionary<long, int> borough, IReadOnlyDictionary<long, double> popularity) =>
        strength.Values.Any(s => s > 0)
            ? strength.Keys
                .OrderByDescending(id => contested[id] + borough[id])
                .ThenByDescending(id => strength[id])
                .ThenByDescending(id => popularity[id])
                .ThenBy(id => id)
                .First()
            : null;

    private static long? Best(IEnumerable<(long Id, double Value)> candidates, IReadOnlyDictionary<long, double> popularity) =>
        candidates
            .OrderByDescending(c => c.Value)
            .ThenByDescending(c => popularity[c.Id])
            .ThenBy(c => c.Id)
            .Select(c => (long?)c.Id)
            .FirstOrDefault();

    internal static Dictionary<ulong, int> SplitAmongMembers(IReadOnlyList<ElectionFaction> members, int seats)
    {
        var result = members.ToDictionary(f => f.Id, _ => 0);
        if (members.Count == 0 || seats == 0) return result;

        var total = members.Sum(f => Math.Max(0, f.Popularity));
        double Quota(ElectionFaction f) => total > 0 ? seats * Math.Max(0, f.Popularity) / total : (double)seats / members.Count;

        foreach (var f in members) result[f.Id] = (int)Math.Floor(Quota(f));
        var left = seats - result.Values.Sum();
        foreach (var f in members
            .OrderByDescending(f => Quota(f) - Math.Floor(Quota(f)))
            .ThenByDescending(f => f.Popularity)
            .ThenBy(f => f.Id)
            .Take(left))
            result[f.Id]++;

        return result;
    }
}
