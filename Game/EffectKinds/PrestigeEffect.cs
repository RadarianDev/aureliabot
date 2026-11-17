public sealed class PrestigeEffect : EffectKind
{
    public override string Prefix => "prestige";

    public override string? Parse(string[] parts, string? rawValue, EffectParseOptions options, out double? value)
    {
        value = null;
        if (!PopularityEffect.IsTarget(parts, options)) return Texts.Get("effects.errors.prestigeShape");
        if (RequireNumber(rawValue, out value) is { } error) return error;
        return value < 0 ? Texts.Get("effects.errors.prestigeNegative") : null;
    }

    public override string? CheckIds(Effect effect, EffectContext context) => PopularityEffect.CheckTarget(effect, context);

    public override double? CurrentValue(Effect effect, IEffectStore store) =>
        effect.Parts is [_, var id] && ulong.TryParse(id, out var factionId)
            ? store.ActiveFactions.FirstOrDefault(f => f.UserId == factionId)?.Prestige
            : null;

    public override string? Apply(Effect effect, IEffectStore store)
    {
        if (effect.Amount < 0) return Texts.Get("data.effect.prestigeNegative");
        var targets = PopularityEffect.Targets(effect, store);
        if (targets.Count == 0) return Texts.Get("data.effect.noPopularityTarget", ("target", string.Join('.', effect.Parts.Skip(1))));
        foreach (var faction in targets)
            store.AddPrestige(faction, effect.Amount);
        return null;
    }

    public override string? SummaryLine(Effect effect, EffectNames names, DisplayConstants display)
    {
        if (effect.Amount == 0) return null;
        var who = effect.Parts switch
        {
            [_, var id] when ulong.TryParse(id, out _) => Texts.Get("effects.summary.prestige", ("faction", names.Faction(id))),
            [_, PopularityEffect.Chancellor] => Texts.Get("effects.summary.prestigeChancellor"),
            [_, PopularityEffect.Sponsor] => Texts.Get("effects.summary.prestigeSponsor"),
            [_, PopularityEffect.Government] => Texts.Get("effects.summary.prestigeGovernment"),
            [_, PopularityEffect.Opposition] => Texts.Get("effects.summary.prestigeOpposition"),
            [_, PopularityEffect.Minister, var ministry] => Texts.Get("effects.summary.prestigeMinister",
                ("ministry", Enum.TryParse<Ministry>(ministry, out var parsed) ? parsed.DisplayName() : ministry)),
            [_, PopularityEffect.Party, var party] => Texts.Get("effects.summary.prestigeParty", ("party", names.Party(party))),
            [_, PopularityEffect.Home, var province] => Texts.Get("effects.summary.prestigeHome", ("province", names.Province(province))),
            _ => effect.Key,
        };
        return NumberLine(effect.Amount, who, display, EffectSummary.FormatSigned(effect.Amount));
    }
}
