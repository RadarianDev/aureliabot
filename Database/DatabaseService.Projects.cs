using Microsoft.Data.Sqlite;

public partial class DatabaseService
{
    private static string? StartProjectTx(
        SqliteConnection conn, SqliteTransaction tx, GameConfig config, string templateId, string? provinceId, EffectSource source, ResolutionReport? report) =>
        Texts.Get("data.effect.projectNotBuilt");

    private static string? ProgressProjectTx(SqliteConnection conn, SqliteTransaction tx, string templateId, decimal amount) =>
        Texts.Get("data.effect.projectNotBuilt");

    private static string? HaltProjectTx(SqliteConnection conn, SqliteTransaction tx, string templateId) =>
        Texts.Get("data.effect.projectNotBuilt");

    private static void PayProjectsTx(SqliteConnection conn, SqliteTransaction tx, GameState state, GameConfig config, ResolutionReport report)
    {
    }
}
