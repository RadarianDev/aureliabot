public sealed class UnitsEffect(string prefix, UnitType type) : EffectKind
{
    public override string Prefix => prefix;

    public UnitType Type => type;

    public override string? Parse(string[] parts, string? rawValue, EffectParseOptions options, out double? value)
    {
        value = null;
        if (parts.Length != 2) return Texts.Get("effects.errors.unitsShape", ("prefix", prefix));
        if (RequireNumber(rawValue, out value) is { } error) return error;
        if (value != Math.Floor(value!.Value)) return Texts.Get("effects.errors.unitsWhole");
        return HereCheck(parts[1], options);
    }

    public override string? CheckIds(Effect effect, EffectContext context) =>
        effect.Target == EffectParser.Here || context.Provinces.Contains(effect.Target!)
            ? null
            : Texts.Get("effects.errors.unknownProvince", ("id", effect.Target));

    public override string? Apply(Effect effect, IEffectStore store) =>
        effect.Amount >= 0
            ? store.RaiseUnits(type, effect.Target!, (int)effect.Amount)
            : store.RemoveUnits(type, effect.Target!, (int)-effect.Amount);

    public override double? CurrentValue(Effect effect, IEffectStore store) => store.UnitCount(type, effect.Target!);

    public override string? SummaryLine(Effect effect, EffectNames names, DisplayConstants display) =>
        effect.Amount == 0 ? null
        : NumberLine(effect.Amount, type == UnitType.Division
                ? Texts.Get("effects.summary.divisions", ("province", names.Province(effect.Target!)))
                : Texts.Get("effects.summary.gendarmerie", ("province", names.Province(effect.Target!))),
            display, EffectSummary.FormatSigned(effect.Amount));
}
