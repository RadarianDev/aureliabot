public sealed class DiscontentEffect : EffectKind
{
    public const string All = "all";

    public override string Prefix => "discontent";

    public override string? Parse(string[] parts, string? rawValue, EffectParseOptions options, out double? value)
    {
        value = null;
        if (parts.Length != 2) return Texts.Get("effects.errors.discontentShape");
        return HereCheck(parts[1], options) ?? RequireNumber(rawValue, out value);
    }

    public override string? CheckIds(Effect effect, EffectContext context) =>
        effect.Target is EffectParser.Here or All || context.Provinces.Contains(effect.Target!)
            ? null
            : Texts.Get("effects.errors.unknownProvince", ("id", effect.Target));

    public override double? CurrentValue(Effect effect, IEffectStore store) =>
        store.Provinces.FirstOrDefault(p => p.Id == effect.Target)?.Discontent;

    public override string? Apply(Effect effect, IEffectStore store)
    {
        var range = store.Config.Constants.Discontent;
        var targets = effect.Target == All ? store.Provinces : store.Provinces.Where(p => p.Id == effect.Target).ToList();
        if (targets.Count == 0) return Texts.Get("data.effect.noProvince", ("id", effect.Target));
        foreach (var province in targets)
            store.SetDiscontent(province.Id, Math.Clamp(province.Discontent + effect.Amount, range.Min, range.Max));
        return null;
    }

    public override string? SummaryLine(Effect effect, EffectNames names, DisplayConstants display) =>
        effect.Amount == 0 ? null
        : effect.Target == All ? NumberLine(effect.Amount, Texts.Get("effects.summary.discontentAll"), display, null)
        : NumberLine(effect.Amount, Texts.Get("effects.summary.discontent", ("province", names.Province(effect.Target!))), display, null);
}

public sealed class ConditionEffect : EffectKind
{
    public override string Prefix => "condition";

    public override string? Parse(string[] parts, string? rawValue, EffectParseOptions options, out double? value)
    {
        value = null;
        if (parts.Length != 3) return Texts.Get("effects.errors.conditionShape");
        if (RequireNumber(rawValue, out value) is { } error) return error;
        if (value is not (1 or -1)) return Texts.Get("effects.errors.conditionValue");
        return HereCheck(parts[1], options);
    }

    public override string? CheckIds(Effect effect, EffectContext context)
    {
        if (effect.Target is not (EffectParser.Here or DiscontentEffect.All) && !context.Provinces.Contains(effect.Target!))
            return Texts.Get("effects.errors.unknownProvince", ("id", effect.Target));
        return context.Conditions.Contains(effect.SubTarget!) ? null : Texts.Get("effects.errors.unknownCondition", ("id", effect.SubTarget));
    }

    public override string? Apply(Effect effect, IEffectStore store)
    {
        if (store.Config.Condition(effect.SubTarget!) is null) return Texts.Get("data.effect.noCondition", ("id", effect.SubTarget));
        var targets = effect.Target == DiscontentEffect.All
            ? store.Provinces.Select(p => p.Id).ToList()
            : store.Provinces.Where(p => p.Id == effect.Target).Select(p => p.Id).ToList();
        if (targets.Count == 0) return Texts.Get("data.effect.noProvince", ("id", effect.Target));
        foreach (var province in targets)
            store.SetCondition(province, effect.SubTarget!, effect.Amount > 0);
        return null;
    }

    public override string? SummaryLine(Effect effect, EffectNames names, DisplayConstants display)
    {
        if (effect.Amount == 0) return null;
        var condition = names.Condition(effect.SubTarget!);
        if (effect.Target == DiscontentEffect.All)
            return effect.Amount > 0
                ? Texts.Get("effects.summary.conditionGainedAll", ("condition", condition))
                : Texts.Get("effects.summary.conditionLostAll", ("condition", condition));
        var province = names.Province(effect.Target!);
        return effect.Amount > 0
            ? Texts.Get("effects.summary.conditionGained", ("province", province), ("condition", condition))
            : Texts.Get("effects.summary.conditionLost", ("province", province), ("condition", condition));
    }
}
