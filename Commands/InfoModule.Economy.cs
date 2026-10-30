using System.Globalization;
using System.Text;

public partial class InfoModule
{
    public static int FiscalYearOf(LedgerEntry entry) =>
        entry.SourceType == "fiscal" && int.TryParse(entry.SourceId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
            ? year
            : entry.Year;

    private List<LedgerEntry> PublicLedger(LedgerAccount account, int year, GameState state) =>
        _data.GetLedger(account)
            .Where(e => FiscalYearOf(e) == year && e.PublicFromResolution <= state.ResolutionNumber)
            .ToList();

    private decimal ClosingBalance(LedgerAccount account, int year, GameState state) =>
        _data.GetLedger(account)
            .Where(e => FiscalYearOf(e) <= year && e.PublicFromResolution <= state.ResolutionNumber)
            .Sum(e => e.Amount);

    private string BudgetInfo(int? requestedYear)
    {
        var state = _data.GetGameState();
        var year = requestedYear ?? state.Year;
        if (_data.GetFiscalYear(year) is not { } fiscal) return Texts.Get("info.budget.none", ("year", year));

        var sb = new StringBuilder();
        sb.AppendLine(Texts.Get("info.budget.heading", ("year", year)));
        sb.AppendLine(Texts.Get("info.budget.summary", ("economy", Display.Number(fiscal.Economy)),
            ("rate", (fiscal.TaxRate * 100).ToString("0.#", CultureInfo.InvariantCulture)), ("budget", Display.Crowns(fiscal.Budget))));
        if (year > state.Year)
        {
            sb.AppendLine(Texts.Get("info.budget.notYetOpen"));
            return sb.ToString();
        }

        var current = year == state.Year;
        var allocation = _data.GetAllocation(year);
        var balances = _data.GetPurseBalances();
        sb.AppendLine();
        sb.AppendLine(Texts.Get("info.budget.pursesHeading"));
        foreach (var ministry in GameEnumExtensions.CabinetMinistries)
        {
            var account = (LedgerAccount)(int)ministry;
            var entries = PublicLedger(account, year, state);
            sb.AppendLine(Texts.Get("info.budget.purseLine", ("ministry", ministry.DisplayName()),
                ("allocated", Display.Crowns(allocation.GetValueOrDefault(ministry))),
                ("spent", Display.Crowns(-entries.Where(e => e.Amount < 0 && e.Kind != LedgerKind.CarryOver).Sum(e => e.Amount))),
                ("balance", Display.Crowns(current ? balances.GetValueOrDefault(ministry) : ClosingBalance(account, year, state)))));
        }
        sb.AppendLine(Texts.Get("info.budget.navyLine", ("amount", Display.Crowns(allocation.GetValueOrDefault(Ministry.Navy)))));

        var reserve = PublicLedger(LedgerAccount.Reserve, year, state);
        sb.AppendLine(Texts.Get("info.budget.reserveLine", ("balance", Display.Crowns(current ? state.Reserve : ClosingBalance(LedgerAccount.Reserve, year, state))),
            ("grants", Display.Crowns(-reserve.Where(e => e.Kind == LedgerKind.Grant && e.Amount < 0).Sum(e => e.Amount))),
            ("interest", Display.Crowns(reserve.Where(e => e.Kind == LedgerKind.Interest).Sum(e => e.Amount)))));

        var spending = GameEnumExtensions.CabinetMinistries
            .SelectMany(m => PublicLedger((LedgerAccount)(int)m, year, state))
            .Where(e => e.Amount < 0 && e.Kind is not (LedgerKind.CarryOver or LedgerKind.Grant))
            .GroupBy(e => e.Kind)
            .Select(g => Texts.Get("info.budget.kindLine", ("kind", KindName(g.Key)), ("amount", Display.Crowns(-g.Sum(e => e.Amount)))))
            .ToList();
        if (spending.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(Texts.Get("info.budget.spendingHeading"));
            foreach (var line in spending) sb.AppendLine(line);
        }
        return sb.ToString();
    }

    private static string KindName(LedgerKind kind) => kind switch
    {
        LedgerKind.Upkeep => Texts.Get("info.budget.kind.upkeep"),
        LedgerKind.Purchase => Texts.Get("info.budget.kind.purchase"),
        LedgerKind.Crackdown => Texts.Get("info.budget.kind.crackdown"),
        LedgerKind.Crisis => Texts.Get("info.budget.kind.crisis"),
        LedgerKind.Project => Texts.Get("info.budget.kind.project"),
        LedgerKind.Graft => Texts.Get("info.budget.kind.graft"),
        LedgerKind.Effect => Texts.Get("info.budget.kind.effect"),
        LedgerKind.Adjustment => Texts.Get("info.budget.kind.adjustment"),
        _ => Texts.Get("info.budget.kind.other"),
    };

    private string ProjectInfo(long projectId)
    {
        if (_data.GetProject(projectId) is not { } project) return Texts.Get("info.project.missing");
        var state = _data.GetGameState();
        var config = _constants.Config;
        var sb = new StringBuilder();
        sb.AppendLine(Texts.Get("info.project.heading", ("project", project.Title)));
        sb.AppendLine(Texts.Get("info.project.meta", ("province", Display.Province(config, project.ProvinceId)),
            ("ministry", project.Purse.DisplayName()), ("price", Display.Crowns(project.Price)), ("term", project.Term)));
        sb.AppendLine(project.Status switch
        {
            ProjectStatus.Finished => Texts.Get("info.project.finished", ("price", Display.Crowns(project.Price))),
            ProjectStatus.Active or ProjectStatus.Stalled => ProjectPublisher.SummaryLine(project, state, config),
            _ => Texts.Get("info.project.halted", ("progress", Display.Crowns(project.Progress)), ("price", Display.Crowns(project.Price))),
        });
        return sb.ToString();
    }

    private string MinistryInfo(Ministry ministry)
    {
        var state = _data.GetGameState();
        var cabinet = _data.GetSittingCabinet();
        var sb = new StringBuilder();
        sb.AppendLine(Texts.Get("info.ministry.heading", ("ministry", ministry.DisplayName())));
        sb.AppendLine(cabinet?.HolderOf(ministry) is { } holder
            ? Texts.Get("info.ministry.minister", ("minister", Display.Faction(_data, holder)))
            : cabinet?.ChancellorId is { } chancellor
                ? Texts.Get("info.ministry.vacantChancellor", ("chancellor", Display.Faction(_data, chancellor)))
                : Texts.Get("info.ministry.vacant"));

        var year = state.Year;
        var entries = PublicLedger((LedgerAccount)(int)ministry, year, state);
        sb.AppendLine(Texts.Get("info.ministry.purse", ("year", year),
            ("allocated", Display.Crowns(_data.GetAllocation(year).GetValueOrDefault(ministry))),
            ("granted", Display.Crowns(entries.Where(e => e.Kind == LedgerKind.Grant).Sum(e => e.Amount))),
            ("spent", Display.Crowns(-entries.Where(e => e.Amount < 0 && e.Kind != LedgerKind.CarryOver).Sum(e => e.Amount))),
            ("balance", Display.Crowns(_data.GetPurseBalance(ministry)))));

        if (entries.Count > 0)
        {
            sb.AppendLine();
            foreach (var entry in entries.TakeLast(15))
                sb.AppendLine(Texts.Get("info.ministry.ledgerLine", ("season", GameCalendar.Label(entry.Season, entry.Year)),
                    ("amount", Display.Signed((double)entry.Amount)), ("label", entry.Label)));
        }

        if (ministry is Ministry.War or Ministry.Interior)
        {
            var units = _data.GetUnits(GameEnumExtensions.UnitTypeOf(ministry));
            if (units.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(Texts.Get("info.ministry.units", ("units", string.Join(", ", units.Select(u =>
                    Texts.Get("info.ministry.unit", ("name", u.Name), ("sides", u.Sides), ("province", Display.Province(_constants.Config, u.ProvinceId))))))));
            }
        }

        var projects = _data.GetLiveProjects().Where(p => p.Purse == ministry).ToList();
        if (projects.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(Texts.Get("info.ministry.projects"));
            foreach (var project in projects)
                sb.AppendLine(Texts.Get("info.ministry.projectLine", ("line", ProjectPublisher.SummaryLine(project, state, _constants.Config))));
        }

        var crises = _data.GetCrises(CrisisStatus.Live).Where(c => c.Ministry == ministry).ToList();
        if (crises.Count > 0)
            sb.AppendLine(Texts.Get("info.ministry.crises", ("crises", string.Join(", ", crises.Select(c => c.Title)))));
        var questions = _data.GetOpenQuestions(ministry);
        if (questions.Count > 0)
            sb.AppendLine(Texts.Get("info.ministry.questions", ("count", questions.Count)));
        return sb.ToString();
    }
}
