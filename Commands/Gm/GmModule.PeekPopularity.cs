public partial class GmModule
{
    private string PopularityByProvince(ProvinceDefinition? only)
    {
        var config = _constants.Config;
        var c = config.Constants.Popularity;
        var regional = _data.GetAllRegionalPopularity(c);
        var marks = _data.GetPopularityMarks();
        var parties = _data.GetAllParties().ToDictionary(p => p.Id);
        var provinces = only is null ? _data.ProvincesInPlay(config) : [only];
        var lines = new List<string>
        {
            only is null ? Texts.Get("gm.peek.popularity.header") : Texts.Get("gm.peek.popularity.headerProvince", ("province", only.Name)),
            Texts.Get("gm.peek.popularity.constants",
                ("start", Fixed(c.Start)), ("drift", Fixed(c.Drift)), ("spread", Fixed(c.Spread)), ("min", Fixed(c.Min)), ("max", Fixed(c.Max))),
        };

        var factions = _data.GetActiveFactions();
        if (only is not null)
            factions = [.. factions.OrderByDescending(f => regional.GetValueOrDefault(f.UserId)?.GetValueOrDefault(only.Id) ?? 0)];
        foreach (var faction in factions)
        {
            var mark = marks.TryGetValue(faction.UserId, out var m) ? m : (double?)null;
            var cells = provinces.Select(p =>
            {
                var value = Fixed(regional.GetValueOrDefault(faction.UserId)?.GetValueOrDefault(p.Id) ?? faction.Popularity);
                return p.Id == faction.HomeProvinceId
                    ? Texts.Get("gm.peek.popularity.homeCell", ("province", p.Name), ("popularity", value))
                    : Texts.Get("gm.peek.popularity.cell", ("province", p.Name), ("popularity", value));
            });
            lines.Add("");
            lines.Add(Texts.Get("gm.peek.popularity.faction",
                ("faction", Display.Faction(faction)),
                ("party", faction.PartyId is { } id && parties.TryGetValue(id, out var party) ? Display.Party(party) : Display.Independent),
                ("popularity", Fixed(faction.Popularity)),
                ("before", mark is { } before ? Fixed(before) : Texts.Get("gm.peek.popularity.noMark")),
                ("change", Standings.Change(faction.Popularity, mark))));
            lines.Add(string.Join(" · ", cells));
        }
        if (factions.Count == 0) lines.Add(Texts.Get("standings.none"));
        return string.Join("\n", lines);
    }

    private string PopularityGlobal()
    {
        var config = _constants.Config;
        var regional = _data.GetAllRegionalPopularity(config.Constants.Popularity);
        var parties = _data.GetAllParties().ToDictionary(p => p.Id);
        var lines = new List<string> { Texts.Get("gm.peek.popularity.globalHeader"), Texts.Get("gm.peek.popularity.globalNote") };

        var rank = 0;
        foreach (var faction in _data.GetActiveFactions().OrderByDescending(f => f.Popularity))
        {
            var byProvince = _data.ProvincesInPlay(config)
                .Select(p => (Province: p, Popularity: regional.GetValueOrDefault(faction.UserId)?.GetValueOrDefault(p.Id) ?? faction.Popularity))
                .OrderBy(x => x.Popularity)
                .ToList();
            var lowest = byProvince[0];
            var highest = byProvince[^1];
            lines.Add(Texts.Get("gm.peek.popularity.globalLine",
                ("rank", ++rank),
                ("faction", Display.Faction(faction)),
                ("party", faction.PartyId is { } id && parties.TryGetValue(id, out var party) ? Display.Party(party) : Display.Independent),
                ("popularity", Fixed(faction.Popularity)),
                ("median", Fixed(Median(byProvince.Select(x => x.Popularity).ToList()))),
                ("highest", Fixed(highest.Popularity)), ("highestProvince", highest.Province.Name),
                ("lowest", Fixed(lowest.Popularity)), ("lowestProvince", lowest.Province.Name)));
        }
        if (rank == 0) lines.Add(Texts.Get("standings.none"));
        return string.Join("\n", lines);
    }

    private static double Median(IReadOnlyList<double> sorted) =>
        sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]) / 2;
}
