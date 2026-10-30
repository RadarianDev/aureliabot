public sealed record AttackResult(bool Succeeded, double TargetChange, double AttackerChange);

public static class Formulas
{
    public static double ProvincialOpinion(double baseOpinion, IEnumerable<ConditionDefinition> conditions, string issueId) =>
        throw new NotImplementedException();

    public static double NationalOpinion(IEnumerable<(double Population, double Opinion)> provinces) =>
        throw new NotImplementedException();

    public static double StanceScore(Stance stance, double opinion, double strongStancePenalty) =>
        throw new NotImplementedException();

    public static double SalienceFactor(double salience) => throw new NotImplementedException();

    public static double CampaignGain(GameConstants constants, Stance stance, double nationalOpinion, double salience, int sameSideCampaigners) =>
        throw new NotImplementedException();

    public static AttackResult Attack(GameConstants constants, Stance targetStance, double nationalOpinion, double salience) =>
        throw new NotImplementedException();

    public static double DriftToward(double value, double target, double share) => throw new NotImplementedException();

    public static double StepToward(double value, double target, double step) => throw new NotImplementedException();

    public static double RestingDiscontent(DiscontentConstants discontent, IEnumerable<ConditionDefinition> conditions) =>
        throw new NotImplementedException();
}
