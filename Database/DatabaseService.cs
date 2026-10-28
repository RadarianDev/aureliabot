using System;
using Microsoft.Data.Sqlite;

public partial class DatabaseService
{
    private readonly string _connectionString;

    // Foreign Keys=True: SQLite ignores REFERENCES clauses unless foreign_keys is switched on per
    // connection, and the connection string is the one place every connection is opened from.
    public DatabaseService(BotConfig config)
    {
        _connectionString = $"Data Source={config.DatabasePath};Foreign Keys=True";
        InitializeDatabase();
    }

    // Test-only entry point: lets a test point straight at a throwaway SQLite file without needing a
    // BotConfig or touching the real database.
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

        // whitelisted_channels: per-guild opt-in list of channels the bot will respond to commands
        // in (see Bot.HandleInteractionAsync). A guild with no rows here is unrestricted — the
        // whitelist only takes effect once a server manager adds its first channel, so a fresh
        // install is never accidentally locked out of every channel.
        Exec(connection, @"
            CREATE TABLE IF NOT EXISTS whitelisted_channels (
                guild_id TEXT NOT NULL,
                channel_id TEXT NOT NULL,
                added_by_user_id TEXT NOT NULL,
                added_at INTEGER NOT NULL,
                PRIMARY KEY (guild_id, channel_id)
            );");

        // parties: colour is a "#RRGGBB" hex string (the same format Rendering/PartyData.HexColor
        // takes); emoji_id is a Discord custom-emoji snowflake, NULL until one is set. Membership
        // isn't stored here — it's factions.party_id below, so a faction can only ever be in one
        // party and there's no second list to keep in sync.
        Exec(connection, @"
            CREATE TABLE IF NOT EXISTS parties (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL UNIQUE COLLATE NOCASE,
                colour TEXT NOT NULL,
                emoji_id TEXT
            );");

        // factions: one per player, keyed on their Discord user id (TEXT, like every other Discord
        // id in this database). Deleting a party leaves its factions in place as independents.
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

    // SQLite has no "ADD COLUMN IF NOT EXISTS", so this checks pragma table_info first — needed
    // once the database already exists from before a column was added.
    private static void AddColumnIfMissing(SqliteConnection conn, string table, string column, string definition)
    {
        var check = conn.CreateCommand();
        check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = $col;";
        check.Parameters.AddWithValue("$col", column);
        var exists = Convert.ToInt64(check.ExecuteScalar()) > 0;
        if (!exists) Exec(conn, $"ALTER TABLE {table} ADD COLUMN {column} {definition};");
    }
}
