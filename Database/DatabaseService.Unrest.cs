using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

public partial class DatabaseService
{
    private static void UnrestCostsGovernmentTx(SqliteConnection conn, SqliteTransaction tx, GameConfig config, ResolutionReport report)
    {
        if (GetSittingCabinetTx(conn, tx) is not { } cabinet) return;
        var c = config.Constants;
        var holders = cabinet.Holders.ToHashSet();
        var factions = QueryFactionsTx(conn, tx, "status = 'Active'");
        var governingParties = factions.Where(f => holders.Contains(f.UserId) && f.PartyId is not null).Select(f => f.PartyId!.Value).ToHashSet();
        var government = factions.Where(f => holders.Contains(f.UserId) || f.PartyId is { } party && governingParties.Contains(party)).ToList();
        if (government.Count == 0) return;

        var hit = new List<string>();
        foreach (var province in GetProvincesTx(conn, tx))
        {
            var lost = false;
            foreach (var faction in government)
            {
                var perPoint = holders.Contains(faction.UserId) ? c.Unrest.Cabinet : c.Unrest.Backbench;
                var cost = Formulas.UnrestCost(province.Discontent, c.Unrest.From, perPoint);
                if (cost > 0 && AddRegionalPopularityTx(conn, tx, faction.UserId, province.Id, -cost, c.Popularity) != 0)
                    lost = true;
            }
            if (lost) hit.Add(province.Name);
        }
        if (hit.Count > 0) report.News.Add(Texts.Get("unrest.news", ("provinces", string.Join(", ", hit))));
    }
}
