public static class FormulaReferences
{
    private static readonly string[] Seasons = ["spring", "summer", "autumn", "winter"];
    private static readonly HashSet<string> Calendar = ["year", "season", "resolution", "term", "preElection"];
    private static readonly HashSet<string> FactionFields = ["popularity", "seats", "independent", "government", "opposition", "office"];
    private static readonly HashSet<string> PartyFields = ["seats", "members", "popularity", "government"];

    public static string? Check(string reference, EffectContext context)
    {
        var parts = reference.Split('.');
        if (parts.Length == 1 && (Calendar.Contains(reference) || Seasons.Contains(reference))) return null;
        switch (parts)
        {
            case ["var", var name]:
                return context.Variables is null || context.Variables.Contains(name) ? null : Texts.Get("formula.errors.unknownVariable", ("name", name));
            case ["condition", var province, var condition]:
                if (province != EffectParser.Here && !context.Provinces.Contains(province)) return Texts.Get("effects.errors.unknownProvince", ("id", province));
                return context.Conditions.Contains(condition) ? null : Texts.Get("effects.errors.unknownCondition", ("id", condition));
            case ["vacant", var office]:
                return office == "chancellor" || ParseMinistry(office) is not null ? null : Texts.Get("formula.errors.unknownOffice", ("office", office));
            case ["faction", var id, var field]:
                if (!FactionFields.Contains(field)) return Texts.Get("formula.errors.unknownField", ("field", field), ("fields", string.Join(", ", FactionFields)));
                return ulong.TryParse(id, out _) && context.Factions.Contains(id) ? null : Texts.Get("effects.errors.noFaction", ("id", id));
            case ["party", var id, var field]:
                if (!PartyFields.Contains(field)) return Texts.Get("formula.errors.unknownField", ("field", field), ("fields", string.Join(", ", PartyFields)));
                return long.TryParse(id, out _) ? null : Texts.Get("formula.errors.unknownParty", ("id", id));
            case ["issue", var id, "open"]:
                return context.Issues.Contains(id) ? null : Texts.Get("effects.errors.unknownIssue", ("id", id));
        }

        if (EffectKinds.Find(parts[0]) is not { IsAction: false } kind)
            return Texts.Get("formula.errors.unknownReference", ("reference", reference));
        var options = new EffectParseOptions(AllowHere: true, AllowCrisis: true);
        if (kind.Parse(parts, "1", options, out var value) is not null)
            return Texts.Get("formula.errors.unknownReference", ("reference", reference));
        return kind.CheckIds(new Effect(reference, value), context);
    }

    public static double Read(string reference, IEffectStore store)
    {
        var parts = reference.Split('.');
        var state = store.State;
        switch (reference)
        {
            case "year": return state.Year;
            case "season": return (int)state.Season + 1;
            case "resolution": return state.ResolutionNumber;
            case "term": return state.TermNumber;
            case "preElection": return Formula.Truth(state.IsPreElection);
        }
        if (parts.Length == 1 && Array.IndexOf(Seasons, reference) is var index and >= 0) return index + 1;

        switch (parts)
        {
            case ["var", var name]:
                return store.Variable(name);
            case ["condition", var province, var condition]:
                return Formula.Truth(store.HasCondition(province, condition));
            case ["vacant", "chancellor"]:
                return Formula.Truth(store.SittingCabinet?.ChancellorId is null);
            case ["vacant", var office] when ParseMinistry(office) is { } ministry:
                return Formula.Truth(store.SittingCabinet?.HolderOf(ministry) is null);
            case ["faction", var id, var field] when ulong.TryParse(id, out var factionId):
                return FactionField(store, factionId, field);
            case ["party", var id, var field] when long.TryParse(id, out var partyId):
                return PartyField(store, partyId, field);
            case ["issue", var id, "open"]:
                return Formula.Truth(store.Issue(id) is { Status: IssueStatus.Open });
            case ["popularity", ..]:
                var targets = PopularityEffect.Targets(new Effect(reference, null), store);
                var popular = store.ActiveFactions.Where(f => targets.Contains(f.UserId)).ToList();
                return popular.Count == 0 ? 0 : popular.Average(f => f.Popularity);
        }

        if (EffectKinds.Find(parts[0]) is { IsAction: false } kind && kind.CurrentValue(new Effect(reference, null), store) is { } current)
            return current;
        throw new FormulaException(Texts.Get("formula.errors.unreadable", ("reference", reference)));
    }

    private static double FactionField(IEffectStore store, ulong factionId, string field)
    {
        if (store.ActiveFactions.FirstOrDefault(f => f.UserId == factionId) is not { } faction) return 0;
        return field switch
        {
            "popularity" => faction.Popularity,
            "seats" => faction.Seats,
            "independent" => Formula.Truth(faction.IsIndependent),
            "government" => Formula.Truth(store.RoleOf(factionId) == FactionRole.Government),
            "opposition" => Formula.Truth(store.RoleOf(factionId) == FactionRole.Opposition),
            _ => Formula.Truth(store.SittingCabinet?.Holders.Contains(factionId) ?? false),
        };
    }

    private static double PartyField(IEffectStore store, long partyId, string field)
    {
        var members = store.ActiveFactions.Where(f => f.PartyId == partyId).ToList();
        return field switch
        {
            "seats" => members.Sum(f => f.Seats),
            "members" => members.Count,
            "popularity" => members.Count == 0 ? 0 : members.Average(f => f.Popularity),
            _ => Formula.Truth(members.Any(f => store.RoleOf(f.UserId) == FactionRole.Government)),
        };
    }

    private static Ministry? ParseMinistry(string office) =>
        Enum.TryParse<Ministry>(office, ignoreCase: true, out var ministry) && ministry.HoldsCrises() ? ministry : null;
}

public sealed class StoreFormulaScope(IEffectStore store) : IFormulaScope
{
    public double Read(string reference) => FormulaReferences.Read(reference, store);

    public IRandomSource Random => store.Random;
}
