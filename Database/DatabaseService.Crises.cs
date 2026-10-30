using Microsoft.Data.Sqlite;

public partial class DatabaseService
{
    private static string? PostCrisisTx(
        SqliteConnection conn, SqliteTransaction tx, GameState state, GameConfig config, string templateId, EffectSource source, ResolutionReport? report) =>
        null;

    private static void ResolveDueCrisesTx(SqliteConnection conn, SqliteTransaction tx, GameState state, GameConfig config, ResolutionReport report)
    {
    }
}
