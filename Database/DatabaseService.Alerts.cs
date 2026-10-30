using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

public partial class DatabaseService
{
    internal List<string> ThresholdAlerts(GameState before, IReadOnlyDictionary<string, double> discontentBefore, GameConfig config)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        var report = new ResolutionReport();
        ThresholdAlertsTx(conn, tx, before, discontentBefore, config, report);
        tx.Commit();
        return report.Alerts;
    }

    private static void ThresholdAlertsTx(
        SqliteConnection conn, SqliteTransaction tx, GameState before, IReadOnlyDictionary<string, double> discontentBefore, GameConfig config,
        ResolutionReport report)
    {
        var after = GetGameStateTx(conn, tx);
        DiscontentAlertsTx(conn, tx, discontentBefore, config.Constants.Discontent, report);
        FavourAlert(before.Favour, after.Favour, config.Constants.Favour, report);
        ThreatAlert(before.Threat, after.Threat, config.Constants.Threat, report);
        DueResponseAlertsTx(conn, tx, after.ResolutionNumber + 1, report);
    }

    private static void DiscontentAlertsTx(
        SqliteConnection conn, SqliteTransaction tx, IReadOnlyDictionary<string, double> discontentBefore, DiscontentConstants discontent,
        ResolutionReport report)
    {
        foreach (var province in GetProvincesTx(conn, tx))
            if (discontentBefore.TryGetValue(province.Id, out var was) && was < discontent.AlertAt && province.Discontent >= discontent.AlertAt)
                report.Alerts.Add(Texts.Get("thresholds.discontent",
                    ("province", province.Name), ("discontent", Display.Number(province.Discontent)), ("threshold", Display.Number(discontent.AlertAt))));
    }

    private static void FavourAlert(double was, double now, FavourConstants favour, ResolutionReport report)
    {
        if (was > 0 && now <= 0)
            report.Alerts.Add(Texts.Get("thresholds.favourZero"));
        else if (was >= favour.AlertBelow && now < favour.AlertBelow)
            report.Alerts.Add(Texts.Get("thresholds.favourLow", ("favour", Display.Number(now)), ("threshold", Display.Number(favour.AlertBelow))));
    }

    private static void ThreatAlert(double was, double now, ThreatConstants threat, ResolutionReport report)
    {
        var crossed = threat.Alerts.Where(level => was < level && now >= level).ToList();
        if (crossed.Count > 0)
            report.Alerts.Add(Texts.Get("thresholds.threat", ("threat", Display.Number(now)), ("threshold", Display.Number(crossed.Max()))));
    }

    private static void DueResponseAlertsTx(SqliteConnection conn, SqliteTransaction tx, long next, ResolutionReport report)
    {
        var posted = report.NewCrises.Select(c => c.CrisisId).ToHashSet();
        foreach (var crisis in QueryCrisesTx(conn, tx, "status = 'Live' AND chosen_answer IS NULL AND due_resolution = $next", ("$next", next)))
        {
            if (posted.Contains(crisis.Id)) continue;
            report.Alerts.Add(ResponsibleMinisterTx(conn, tx, crisis.Ministry) is { } minister
                ? Texts.Get("thresholds.crisisUnanswered", ("crisis", crisis.Title), ("minister", $"<@{minister}>"))
                : Texts.Get("thresholds.crisisNobody",("crisis", crisis.Title), ("ministry", crisis.Ministry.DisplayName())));
        }

        foreach (var question in QueryQuestionsTx(conn, tx, "status = 'Open' AND due_resolution = $next", ("$next", next)))
            if (ResponsibleMinisterTx(conn, tx, question.Ministry) is { } minister)
                report.Alerts.Add(Texts.Get("thresholds.questionUnanswered",
                    ("ministry", question.Ministry.DisplayName()), ("asker", $"<@{question.AskerId}>"), ("minister", $"<@{minister}>")));
    }
}
