public sealed class VariableEffect : EffectKind
{
    public override string Prefix => "var";

    public override bool Hidden => true;

    public override string? Parse(string[] parts, string? rawValue, EffectParseOptions options, out double? value)
    {
        value = null;
        if (parts.Length != 2) return Texts.Get("effects.errors.variableShape");
        return RequireNumber(rawValue, out value);
    }

    public override string? CheckIds(Effect effect, EffectContext context) =>
        context.Variables is null || context.Variables.Contains(effect.Target!)
            ? null
            : Texts.Get("formula.errors.unknownVariable", ("name", effect.Target));

    public override string? Apply(Effect effect, IEffectStore store)
    {
        if (store.Config.Variable(effect.Target!) is not { } definition) return Texts.Get("formula.errors.unknownVariable", ("name", effect.Target));
        store.SetVariable(definition.Id, definition.Clamp(store.Variable(definition.Id) + effect.Amount));
        return null;
    }

    public override double? CurrentValue(Effect effect, IEffectStore store) =>
        store.Config.Variable(effect.Target!) is null ? null : store.Variable(effect.Target!);

    public override string? SummaryLine(Effect effect, EffectNames names, DisplayConstants display) => null;
}
