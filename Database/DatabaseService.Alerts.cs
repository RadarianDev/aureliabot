using Microsoft.Data.Sqlite;

public partial class DatabaseService
{
    private static void ThresholdAlertsTx(
        SqliteConnection conn, SqliteTransaction tx, GameState before, IReadOnlyDictionary<string, double> discontentBefore, GameConfig config,
        ResolutionReport report)
    {
    }
}
