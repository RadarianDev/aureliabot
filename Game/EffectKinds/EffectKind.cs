public sealed record EffectParseOptions(bool AllowHere, bool AllowCrisis);

public interface IEffectStore
{
    GameState State { get; }
    GameConfig Config { get; }
    EffectSource Source { get; }

    void SetState(string column, double value);

    decimal Balance(LedgerAccount account);
    void PostLedger(LedgerAccount account, decimal amount);

    IReadOnlyList<Province> Provinces { get; }
    void SetDiscontent(string provinceId, double value);
    bool SetCondition(string provinceId, string conditionId, bool present);

    Issue? Issue(string issueId);
    void SetIssueOpinion(string issueId, double value);
    void SetIssueSalience(string issueId, double value, bool raised);

    IReadOnlyList<Faction> ActiveFactions { get; }
    Cabinet? SittingCabinet { get; }
    FactionRole RoleOf(ulong factionId);
    void AddPopularity(ulong factionId, double delta);

    string? OpenIssue(string templateId);
    string? CloseIssue(string issueId);
    string? MakeBillAvailable(string templateId);
    string? StartProject(string templateId);
    string? PostCrisis(string templateId);
    string? QueueEvent(string templateId, int delay);
    string? CancelEvents(string templateId);
}

public abstract class EffectKind
{
    public abstract string Prefix { get; }

    public virtual bool IsAction => false;

    public virtual bool Hidden => false;

    public abstract string? Parse(string[] parts, string? rawValue, EffectParseOptions options, out double? value);

    public virtual string? CheckIds(Effect effect, EffectContext context) => null;

    public abstract string? Apply(Effect effect, IEffectStore store);

    public virtual double? CurrentValue(Effect effect, IEffectStore store) => null;

    public abstract string? SummaryLine(Effect effect, EffectNames names, DisplayConstants display);

    protected static string? RequireNumber(string? rawValue, out double? value)
    {
        value = null;
        if (rawValue is null) return Texts.Get("effects.errors.needsNumber");
        if (!EffectParser.TryParseNumber(rawValue, out var number)) return Texts.Get("effects.errors.notANumber", ("value", rawValue));
        value = number;
        return null;
    }

    protected static string? RequireNoValue(string? rawValue, out double? value)
    {
        value = null;
        return rawValue is null || rawValue.Equals("true", StringComparison.OrdinalIgnoreCase)
            ? null
            : Texts.Get("effects.errors.actionValue");
    }

    protected static string? HereCheck(string province, EffectParseOptions options) =>
        province == EffectParser.Here && !options.AllowHere ? Texts.Get("effects.errors.hereOnlyInProjects") : null;

    protected static string NumberLine(double amount, string name, DisplayConstants display, string? shown) =>
        shown is null
            ? Texts.Get("effects.summary.line", ("arrow", amount > 0 ? display.UpEmoji : display.DownEmoji), ("name", name))
            : Texts.Get("effects.summary.lineWithAmount", ("arrow", amount > 0 ? display.UpEmoji : display.DownEmoji), ("name", name), ("amount", shown));
}

public static class EffectKinds
{
    public static IReadOnlyList<EffectKind> All { get; } =
    [
        new StateEffect("economy", "economy", 0, double.MaxValue, publicAmount: true, () => Texts.Get("effects.summary.economy")),
        new TaxRateEffect(),
        new StateEffect("favour", "favour", 0, 100, publicAmount: false, () => Texts.Get("effects.summary.favour")),
        new StateEffect("favourBaseline", "favour_baseline", 0, 100, publicAmount: false, () => Texts.Get("effects.summary.favour")),
        new StateEffect("threat", "threat", 0, 100, publicAmount: false, () => Texts.Get("effects.summary.threat")),
        new SeatsEffect(),
        new ReserveEffect(),
        new PurseEffect(),
        new DiscontentEffect(),
        new ConditionEffect(),
        new IssueValueEffect("opinion", issue => Texts.Get("effects.summary.opinion", ("issue", issue))),
        new IssueValueEffect("salience", issue => Texts.Get("effects.summary.salience", ("issue", issue))),
        new PopularityEffect(),
        new OpenIssueEffect(),
        new CloseIssueEffect(),
        new BillEffect(),
        new ProjectEffect(),
        new CrisisEffect(),
        new QueueEventEffect(),
        new CancelEventEffect(),
    ];

    private static readonly Dictionary<string, EffectKind> ByPrefix = All.ToDictionary(k => k.Prefix, StringComparer.Ordinal);

    public static EffectKind? Find(string prefix) => ByPrefix.GetValueOrDefault(prefix);
}
