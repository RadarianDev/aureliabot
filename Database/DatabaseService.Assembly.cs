using Microsoft.Data.Sqlite;

public partial class DatabaseService
{
    private static void CountDueReadingsTx(SqliteConnection conn, SqliteTransaction tx, GameState state, GameConfig config, ResolutionReport report)
    {
    }

    private static void CountDueNoConfidenceTx(SqliteConnection conn, SqliteTransaction tx, GameState state, GameConfig config, ResolutionReport report)
    {
    }

    private static void OpenScheduledReadingsTx(SqliteConnection conn, SqliteTransaction tx, GameConfig config, ResolutionReport report)
    {
    }
}
