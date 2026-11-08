public sealed record AttackResult(bool Succeeded, double TargetChange, double AttackerChange);

public sealed record RegionalShift(double NationalChange, double RegionalChange, IReadOnlyDictionary<string, double> Deviations);

public static class Formulas
{
    public static double ProvincialOpinion(double baseOpinion, IEnumerable<ConditionDefinition> conditions, string issueId) =>
        Math.Clamp(baseOpinion + conditions.Sum(c => c.BiasOn(issueId)), -1, 1);

    public static double NationalOpinion(IEnumerable<(double Population, double Opinion)> provinces)
    {
        var totalPopulation = 0.0;
        var weighted = 0.0;
        foreach (var (population, opinion) in provinces)
        {
            totalPopulation += population;
            weighted += population * opinion;
        }
        return totalPopulation == 0 ? 0 : weighted / totalPopulation;
    }

    public static double StanceScore(Stance stance, double opinion, double strongStancePenalty) => stance switch
    {
        Stance.For or Stance.Against => (int)stance * opinion,
        Stance.StronglyFor or Stance.StronglyAgainst => 2 * stance.Sign() * opinion - strongStancePenalty,
        _ => 0,
    };

    public static double SalienceFactor(double salience) => salience / 3;

    public static double CampaignGain(GameConstants constants, Stance stance, double provincialOpinion, double salience, int sameSideCampaigners)
    {
        var crowd = constants.Campaign.Crowding ? Math.Max(1, sameSideCampaigners) : 1;
        var heard = constants.Campaign.Baseline * salience;
        var agreed = constants.Campaign.Factor
            * StanceScore(stance, provincialOpinion, constants.StrongStancePenalty)
            * SalienceFactor(salience);
        return (heard + agreed) / Math.Sqrt(crowd);
    }

    public static double RegionalPopularity(double national, double deviation, PopularityConstants range) =>
        Math.Clamp(national + deviation, range.Min, range.Max);

    public static RegionalShift ShiftInProvince(
        double national, IReadOnlyDictionary<string, double> deviations, IReadOnlyDictionary<string, double> populations,
        string provinceId, double delta, PopularityConstants range)
    {
        var totalPopulation = populations.Values.Sum();
        var weight = totalPopulation > 0 ? populations.GetValueOrDefault(provinceId) / totalPopulation : 0;
        if (weight <= 0 || delta == 0) return new RegionalShift(0, 0, deviations);

        var regional = national + deviations.GetValueOrDefault(provinceId);
        var allowed = Math.Clamp(delta, Math.Min(0, range.Min - regional), Math.Max(0, range.Max - regional));
        var nationalChange = Math.Clamp(national + weight * allowed, range.Min, range.Max) - national;
        var regionalChange = nationalChange / weight;

        var shifted = populations.Keys.ToDictionary(
            id => id,
            id => deviations.GetValueOrDefault(id) - nationalChange + (id == provinceId ? regionalChange : 0));
        return new RegionalShift(nationalChange, regionalChange, shifted);
    }

    public static AttackResult Attack(GameConstants constants, Stance targetStance, double nationalOpinion, double salience)
    {
        var score = StanceScore(targetStance, nationalOpinion, constants.StrongStancePenalty);
        return score < 0
            ? new AttackResult(true, -constants.Attack.Factor * Math.Abs(score) * SalienceFactor(salience), 0)
            : new AttackResult(false, 0, -constants.Attack.Backfire);
    }

    public static double DriftToward(double value, double target, double share) => value + share * (target - value);

    public static double StepToward(double value, double target, double step)
    {
        var distance = target - value;
        return Math.Abs(distance) <= step ? target : value + Math.Sign(distance) * step;
    }

    public static double RestingDiscontent(DiscontentConstants discontent, IEnumerable<ConditionDefinition> conditions) =>
        discontent.Rest + conditions.Sum(c => c.DiscontentRest);

    public static double CrisisGain(GameConstants constants, Lean answerLean, double provincialOpinion, double salience) =>
        constants.Campaign.Factor * (int)answerLean * provincialOpinion * SalienceFactor(salience);

    public static double CrisisDefaultLoss(GameConstants constants, double salience) =>
        constants.Campaign.Factor * SalienceFactor(salience);

    public static double WhipCountChange(GameConstants constants, VoteChoice predicted, VoteChoice? actual)
    {
        if (actual is null or VoteChoice.Abstain) return 0;
        return actual == predicted ? constants.Opposition.WhipCountReward : -constants.Opposition.WhipCountPenalty;
    }

    public static double CondemnationChange(GameConstants constants, Lean side, Lean? ministerLean, double provincialOpinion)
    {
        var rightSign = Math.Sign(provincialOpinion);
        if (rightSign == 0 || side == Lean.Neutral) return 0;
        var ministerRight = ministerLean is { } lean && (int)lean == rightSign;
        if ((int)side == rightSign) return ministerRight ? 0 : constants.Opposition.CondemnReward;
        return ministerRight ? -constants.Opposition.CondemnPenalty : 0;
    }
}
