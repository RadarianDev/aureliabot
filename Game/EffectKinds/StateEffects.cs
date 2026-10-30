public class StateEffect(string prefix, string column, double min, double max, bool publicAmount, Func<string> name) : EffectKind
{
    public override string Prefix => prefix;

    public override string? Parse(string[] parts, string? rawValue, EffectParseOptions options, out double? value)
    {
        value = null;
        if (parts.Length != 1) return Texts.Get("effects.errors.scalarNoId", ("prefix", prefix));
        return RequireNumber(rawValue, out value);
    }

    public override double? CurrentValue(Effect effect, IEffectStore store) => Read(store.State);

    protected virtual double Read(GameState state) => prefix switch
    {
        "economy" => state.Economy,
        "taxRate" => state.TaxRate,
        "favour" => state.Favour,
        "favourBaseline" => state.FavourBaseline,
        "threat" => state.Threat,
        "seats" => state.Seats,
        _ => 0,
    };

    public override string? Apply(Effect effect, IEffectStore store)
    {
        store.SetState(column, Math.Clamp(Read(store.State) + effect.Amount, min, max));
        return null;
    }

    public override string? SummaryLine(Effect effect, EffectNames names, DisplayConstants display) =>
        effect.Amount == 0 ? null : NumberLine(effect.Amount, name(), display, publicAmount ? Shown(effect.Amount) : null);

    protected virtual string Shown(double amount) => EffectSummary.FormatSigned(amount);
}

public sealed class TaxRateEffect() : StateEffect("taxRate", "tax_rate", 0, 1, publicAmount: true, () => Texts.Get("effects.summary.taxRate"))
{
    protected override string Shown(double amount) =>
        Texts.Get("effects.summary.percent", ("amount", EffectSummary.FormatSigned(amount * 100)));
}

public sealed class SeatsEffect() : StateEffect("seats", "seats", 1, int.MaxValue, publicAmount: true, () => Texts.Get("effects.summary.seats"))
{
    public override string? Apply(Effect effect, IEffectStore store)
    {
        store.SetState("seats", Math.Max(1, store.State.Seats + (int)Math.Round(effect.Amount)));
        return null;
    }
}

public sealed class ReserveEffect : EffectKind
{
    public override string Prefix => "reserve";

    public override string? Parse(string[] parts, string? rawValue, EffectParseOptions options, out double? value)
    {
        value = null;
        if (parts.Length != 1) return Texts.Get("effects.errors.scalarNoId", ("prefix", Prefix));
        return RequireNumber(rawValue, out value);
    }

    public override double? CurrentValue(Effect effect, IEffectStore store) => (double)store.Balance(LedgerAccount.Reserve);

    public override string? Apply(Effect effect, IEffectStore store)
    {
        store.PostLedger(LedgerAccount.Reserve, (decimal)effect.Amount);
        return null;
    }

    public override string? SummaryLine(Effect effect, EffectNames names, DisplayConstants display) =>
        effect.Amount == 0 ? null : NumberLine(effect.Amount, Texts.Get("effects.summary.reserve"), display, EffectSummary.FormatSigned(effect.Amount));
}

public sealed class PurseEffect : EffectKind
{
    private static readonly HashSet<string> Ministries =
        [nameof(Ministry.War), nameof(Ministry.Interior), nameof(Ministry.CommerceLabor), nameof(Ministry.Treasury)];

    public override string Prefix => "purse";

    public override string? Parse(string[] parts, string? rawValue, EffectParseOptions options, out double? value)
    {
        value = null;
        if (parts.Length != 2) return Texts.Get("effects.errors.purseShape");
        if (parts[1] == nameof(Ministry.Navy)) return Texts.Get("effects.errors.navyPurse");
        if (!Ministries.Contains(parts[1])) return Texts.Get("effects.errors.purseMinistry");
        return RequireNumber(rawValue, out value);
    }

    public override double? CurrentValue(Effect effect, IEffectStore store) =>
        (double)store.Balance(Enum.Parse<LedgerAccount>(effect.Target!));

    public override string? Apply(Effect effect, IEffectStore store)
    {
        store.PostLedger(Enum.Parse<LedgerAccount>(effect.Target!), (decimal)effect.Amount);
        return null;
    }

    public override string? SummaryLine(Effect effect, EffectNames names, DisplayConstants display)
    {
        if (effect.Amount == 0) return null;
        var ministry = Enum.TryParse<Ministry>(effect.Target, out var parsed) ? parsed.DisplayName() : effect.Target!;
        return NumberLine(effect.Amount, Texts.Get("effects.summary.purse", ("ministry", ministry)), display, EffectSummary.FormatSigned(effect.Amount));
    }
}
