using System;
using Microsoft.Data.Sqlite;

public partial class DatabaseService
{
    private readonly string _connectionString;

    public DatabaseService(BotConfig config)
    {
        _connectionString = $"Data Source={config.DatabasePath};Foreign Keys=True";
        InitializeDatabase();
    }

    internal DatabaseService(string connectionString)
    {
        _connectionString = connectionString;
        InitializeDatabase();
    }

    private static void LogError(string operation, Exception ex) =>
        Console.Error.WriteLine($"[DatabaseService] {operation} failed: {ex.Message}");

    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        Exec(connection, @"
            CREATE TABLE IF NOT EXISTS whitelisted_channels (
                guild_id TEXT NOT NULL,
                channel_id TEXT NOT NULL,
                added_by_user_id TEXT NOT NULL,
                added_at INTEGER NOT NULL,
                PRIMARY KEY (guild_id, channel_id)
            );");

        Exec(connection, @"
            CREATE TABLE IF NOT EXISTS parties (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL UNIQUE COLLATE NOCASE,
                colour TEXT NOT NULL,
                emoji_id TEXT
            );");

        Exec(connection, @"
            CREATE TABLE IF NOT EXISTS factions (
                user_id TEXT PRIMARY KEY,
                popularity REAL NOT NULL DEFAULT 0,
                seats INTEGER NOT NULL DEFAULT 0,
                party_id INTEGER REFERENCES parties(id) ON DELETE SET NULL
            );");
        Exec(connection, "CREATE INDEX IF NOT EXISTS ix_factions_party ON factions (party_id);");
    }

    private static void Exec(SqliteConnection conn, string sql)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static void AddColumnIfMissing(SqliteConnection conn, string table, string column, string definition)
    {
        var check = conn.CreateCommand();
        check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = $col;";
        check.Parameters.AddWithValue("$col", column);
        var exists = Convert.ToInt64(check.ExecuteScalar()) > 0;
        if (!exists) Exec(conn, $"ALTER TABLE {table} ADD COLUMN {column} {definition};");
    }
}
