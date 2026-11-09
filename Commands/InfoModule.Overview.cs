using System.Globalization;
using System.Text;

public partial class InfoModule
{
    private string KingdomInfo()
    {
        var state = _data.GetGameState();
        var config = _constants.Config;
        var sb = new StringBuilder();
        sb.AppendLine(Texts.Get("info.kingdom.heading", ("season", state.Label)));
        sb.AppendLine(Texts.Get("dashboard.economy", ("economy", Display.Number(state.Economy)),
            ("rate", (state.TaxRate * 100).ToString("0.#", CultureInfo.InvariantCulture)), ("reserve", Display.Crowns(state.Reserve))));
        if (_data.GetFiscalYear(state.Year) is { } fiscal)
            sb.AppendLine(Texts.Get("dashboard.budget", ("year", state.Year), ("budget", Display.Crowns(fiscal.Budget))));
        sb.AppendLine(Texts.Get("dashboard.favour", ("favour", Wording.Favour(state.Favour))));

        sb.AppendLine();
        sb.AppendLine(Texts.Get("info.kingdom.provinces"));
        var conditions = _data.GetProvinceConditions();
        foreach (var province in _data.GetProvinces())
            sb.AppendLine(Texts.Get("dashboard.province", ("province", province.Name), ("discontent", Wording.Discontent(province.Discontent)),
                ("conditions", string.Join(", ", conditions.GetValueOrDefault(province.Id, []).Select(c => config.Condition(c)?.Name ?? c)))));
        return sb.ToString();
    }

    private string GovernmentInfo()
    {
        var cabinet = _data.GetSittingCabinet();
        var governing = _data.GetGoverningPartyIds();
        var parties = _data.GetAllParties();
        var sb = new StringBuilder();
        sb.AppendLine(Texts.Get("info.government.heading"));
        if (cabinet?.Status == CabinetStatus.Caretaker) sb.AppendLine(Texts.Get("info.government.caretaker"));
        sb.AppendLine(Texts.Get("info.government.chancellor",
            ("faction", cabinet?.ChancellorId is { } chancellor ? Display.Faction(_data, chancellor) : Texts.Get("cabinet.vacant"))));
        foreach (var ministry in GameEnumExtensions.CabinetMinistries)
            sb.AppendLine(Texts.Get("info.government.minister", ("ministry", ministry.DisplayName()),
                ("faction", cabinet?.HolderOf(ministry) is { } holder ? Display.Faction(_data, holder) : Texts.Get("cabinet.vacant"))));

        sb.AppendLine();
        sb.AppendLine(Texts.Get("info.government.inGovernment", ("parties", PartyList(parties.Where(p => governing.Contains(p.Id))))));
        sb.AppendLine(Texts.Get("info.government.inOpposition", ("parties", PartyList(parties.Where(p => !governing.Contains(p.Id))))));
        return sb.ToString();
    }

    private static string PartyList(IEnumerable<Party> parties)
    {
        var names = parties.Select(Display.Party).ToList();
        return names.Count == 0 ? Texts.Get("info.live.noneYet") : string.Join(", ", names);
    }

    private string ParliamentInfo()
    {
        var state = _data.GetGameState();
        var parties = _data.GetAllParties();
        var factions = _data.GetActiveFactions();
        var sb = new StringBuilder();
        sb.AppendLine(Texts.Get("info.parliament.assembly", ("seats", state.Seats)));
        var assembly = Publisher.SeatLines(parties, factions, f => f.Seats);
        sb.AppendLine(assembly.Count == 0 ? Texts.Get("dashboard.noParties") : string.Join("\n", assembly));
        sb.AppendLine(Texts.Get("info.parliament.nextElection", ("season", GameCalendar.Label(state.PreElectionSeason, state.PreElectionYear))));

        sb.AppendLine();
        if (!state.SenateEstablished)
        {
            sb.AppendLine(Texts.Get("info.parliament.noSenate"));
            return sb.ToString();
        }
        sb.AppendLine(Texts.Get("info.parliament.senate", ("seats", factions.Sum(f => f.SenateSeats))));
        var senate = Publisher.SeatLines(parties, factions, f => f.SenateSeats, skipEmpty: true);
        sb.AppendLine(senate.Count == 0 ? Texts.Get("dashboard.senateEmpty") : string.Join("\n", senate));
        return sb.ToString();
    }

    private string LawsInfo()
    {
        var laws = _data.GetBills().Where(b => b.Status == BillStatus.Passed).ToList();
        var sb = new StringBuilder();
        sb.AppendLine(Texts.Get("info.laws.heading"));
        if (laws.Count == 0) sb.AppendLine(Texts.Get("info.laws.none"));
        foreach (var law in laws)
            sb.AppendLine(Texts.Get("info.laws.line", ("law", law.Title), ("issue", CrisisPublisher.IssueTitle(_data, law.IssueId)), ("lean", AssemblyPublisher.LeanName(law.Lean))));
        return sb.ToString();
    }

    private string ConditionInfo(string conditionId)
    {
        var config = _constants.Config;
        if (config.Condition(conditionId) is not { } condition) return Texts.Get("info.condition.missing");
        var affected = _data.GetProvinceConditions()
            .Where(kv => kv.Value.Contains(conditionId))
            .Select(kv => Display.Province(config, kv.Key))
            .ToList();
        var sb = new StringBuilder();
        sb.AppendLine($"## {condition.Name}");
        sb.AppendLine(condition.Description);
        sb.AppendLine(affected.Count == 0
            ? Texts.Get("info.condition.nowhere")
            : Texts.Get("info.condition.provinces", ("provinces", string.Join(", ", affected))));
        return sb.ToString();
    }

    private string EventInfo(long eventId) =>
        _data.GetFiredEvent(eventId) is { } fired
            ? Publisher.FormatEvent(fired.Title, fired.Text, Expected(fired.ParsedEffects))
            : Texts.Get("info.event.missing");
}
