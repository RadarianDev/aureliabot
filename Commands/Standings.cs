public static class Standings
{
    public static string Change(double popularity, double? mark)
    {
        if (mark is not { } before) return "";
        var change = popularity - before;
        if (Math.Abs(change) < 0.05) return Texts.Get("standings.steady");
        return change > 0
            ? Texts.Get("standings.up", ("change", Display.Number(change)))
            : Texts.Get("standings.down", ("change", Display.Number(-change)));
    }

    public static List<string> Lines(DatabaseService data, GameConfig config, bool compact = false)
    {
        var parties = data.GetAllParties().ToDictionary(p => p.Id);
        var marks = data.GetPopularityMarks();
        return data.GetActiveFactions().Select((f, index) =>
        {
            (string, object?)[] values =
            [
                ("rank", index + 1),
                ("faction", Display.Faction(f)),
                ("popularity", Display.Number(f.Popularity)),
                ("change", Change(f.Popularity, marks.TryGetValue(f.UserId, out var mark) ? mark : null)),
                ("party", f.PartyId is { } id && parties.TryGetValue(id, out var party) ? Display.Party(party) : Display.Independent),
                ("home", Display.Province(config, f.HomeProvinceId)),
            ];
            return compact ? Texts.Get("standings.compactLine", values) : Texts.Get("standings.line", values);
        }).ToList();
    }

    public static List<string> ProvinceLeaders(DatabaseService data, GameConfig config)
    {
        var regional = data.GetAllRegionalPopularity(config.Constants.Popularity);
        if (regional.Count == 0) return [];
        return config.Provinces.Select(p =>
        {
            var (leader, popularity) = regional
                .Select(kv => (Id: kv.Key, Popularity: kv.Value.GetValueOrDefault(p.Id)))
                .OrderByDescending(x => x.Popularity)
                .ThenBy(x => x.Id)
                .First();
            return Texts.Get("standings.leader",
                ("province", p.Name), ("faction", Display.Faction(data, leader)), ("popularity", Display.Number(popularity)));
        }).ToList();
    }

    public static List<string> InProvince(DatabaseService data, GameConfig config, string provinceId) =>
        data.GetAllRegionalPopularity(config.Constants.Popularity)
            .Select(kv => (Id: kv.Key, Popularity: kv.Value.GetValueOrDefault(provinceId)))
            .OrderByDescending(x => x.Popularity)
            .ThenBy(x => x.Id)
            .Select((x, index) => Texts.Get("standings.provinceLine",
                ("rank", index + 1), ("faction", Display.Faction(data, x.Id)), ("popularity", Display.Number(x.Popularity))))
            .ToList();
}
