public sealed class PopularityEffect : EffectKind
{
    public const string Chancellor = "chancellor";
    public const string Government = "government";
    public const string Opposition = "opposition";
    public const string Sponsor = "sponsor";
    public const string Minister = "minister";
    public const string Party = "party";
    public const string Home = "home";

    public override string Prefix => "popularity";

    public override string? Parse(string[] parts, string? rawValue, EffectParseOptions options, out double? value)
    {
        value = null;
        var valid = parts switch
        {
            [_, var id] when ulong.TryParse(id, out _) => true,
            [_, Chancellor or Government or Opposition or Sponsor] => true,
            [_, Minister, var ministry] => Enum.TryParse<Ministry>(ministry, out var parsed) && parsed.HoldsCrises(),
            [_, Party, var party] => long.TryParse(party, out _),
            [_, Home, _] => true,
            _ => false,
        };
        return valid ? RequireNumber(rawValue, out value) : Texts.Get("effects.errors.popularityShape");
    }

    public override string? CheckIds(Effect effect, EffectContext context) => effect.Parts switch
    {
        [_, var id] when ulong.TryParse(id, out _) && !context.Factions.Contains(id) => Texts.Get("effects.errors.noFaction", ("id", id)),
        [_, Home, var province] when !context.Provinces.Contains(province) => Texts.Get("effects.errors.unknownProvince", ("id", province)),
        _ => null,
    };

    public static IReadOnlyList<ulong> Targets(Effect effect, IEffectStore store)
    {
        var cabinet = store.SittingCabinet;
        var active = store.ActiveFactions;
        return effect.Parts switch
        {
            [_, var id] when ulong.TryParse(id, out var factionId) => active.Any(f => f.UserId == factionId) ? [factionId] : [],
            [_, Chancellor] => cabinet?.ChancellorId is { } chancellor ? [chancellor] : [],
            [_, Sponsor] => store.Source.ActorId is { } actor ? [actor] : [],
            [_, Government] => active.Where(f => store.RoleOf(f.UserId) == FactionRole.Government).Select(f => f.UserId).ToList(),
            [_, Opposition] => active.Where(f => store.RoleOf(f.UserId) == FactionRole.Opposition).Select(f => f.UserId).ToList(),
            [_, Minister, var ministry] => cabinet?.HolderOf(Enum.Parse<Ministry>(ministry)) is { } holder ? [holder] : [],
            [_, Party, var party] => active.Where(f => f.PartyId == long.Parse(party)).Select(f => f.UserId).ToList(),
            [_, Home, var province] => active.Where(f => f.HomeProvinceId == province).Select(f => f.UserId).ToList(),
            _ => [],
        };
    }

    public override double? CurrentValue(Effect effect, IEffectStore store) =>
        effect.Parts is [_, var id] && ulong.TryParse(id, out var factionId)
            ? store.ActiveFactions.FirstOrDefault(f => f.UserId == factionId)?.Popularity
            : null;

    public override string? Apply(Effect effect, IEffectStore store)
    {
        var targets = Targets(effect, store);
        if (targets.Count == 0) return Texts.Get("data.effect.noPopularityTarget", ("target", string.Join('.', effect.Parts.Skip(1))));
        foreach (var faction in targets)
            store.AddPopularity(faction, effect.Amount);
        return null;
    }

    public override string? SummaryLine(Effect effect, EffectNames names, DisplayConstants display)
    {
        if (effect.Amount == 0) return null;
        var who = effect.Parts switch
        {
            [_, var id] when ulong.TryParse(id, out _) => Texts.Get("effects.summary.popularity", ("faction", names.Faction(id))),
            [_, Chancellor] => Texts.Get("effects.summary.popularityChancellor"),
            [_, Sponsor] => Texts.Get("effects.summary.popularitySponsor"),
            [_, Government] => Texts.Get("effects.summary.popularityGovernment"),
            [_, Opposition] => Texts.Get("effects.summary.popularityOpposition"),
            [_, Minister, var ministry] => Texts.Get("effects.summary.popularityMinister",
                ("ministry", Enum.TryParse<Ministry>(ministry, out var parsed) ? parsed.DisplayName() : ministry)),
            [_, Party, var party] => Texts.Get("effects.summary.popularityParty", ("party", names.Party(party))),
            [_, Home, var province] => Texts.Get("effects.summary.popularityHome", ("province", names.Province(province))),
            _ => effect.Key,
        };
        return NumberLine(effect.Amount, who, display, EffectSummary.FormatSigned(effect.Amount));
    }
}
