public sealed record AttackResult(bool Succeeded, double TargetChange, double AttackerChange);

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

    public static double CampaignGain(GameConstants constants, Stance stance, double nationalOpinion, double salience, int sameSideCampaigners)
    {
        var crowd = constants.Campaign.Crowding ? Math.Max(1, sameSideCampaigners) : 1;
        return constants.Campaign.Factor
            * StanceScore(stance, nationalOpinion, constants.StrongStancePenalty)
            * SalienceFactor(salience)
            / Math.Sqrt(crowd);
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
        throw new NotImplementedException();

    public static double CrisisDefaultLoss(GameConstants constants, double salience) => throw new NotImplementedException();

    public static double CondemnationChange(GameConstants constants, Lean side, Lean? ministerLean, double provincialOpinion) =>
        throw new NotImplementedException();
}
