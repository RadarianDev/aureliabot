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

    public string ConnectionString => _connectionString;

    private static void LogError(string operation, Exception ex) =>
        Console.Error.WriteLine($"[DatabaseService] {operation} failed: {ex.Message}");

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

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
        AddColumnIfMissing(connection, "parties", "created_season", "TEXT");
        AddColumnIfMissing(connection, "parties", "created_year", "INTEGER");
        AddColumnIfMissing(connection, "parties", "created_at", "INTEGER NOT NULL DEFAULT 0");
        AddColumnIfMissing(connection, "parties", "role_id", "TEXT");
        AddColumnIfMissing(connection, "parties", "channel_id", "TEXT");
        AddColumnIfMissing(connection, "parties", "programme_message_url", "TEXT");
        AddColumnIfMissing(connection, "parties", "foreign_channel_id", "TEXT");

        Exec(connection, @"
            CREATE TABLE IF NOT EXISTS factions (
                user_id TEXT PRIMARY KEY,
                popularity REAL NOT NULL DEFAULT 0,
                seats INTEGER NOT NULL DEFAULT 0,
                party_id INTEGER REFERENCES parties(id) ON DELETE SET NULL
            );");
        Exec(connection, "CREATE INDEX IF NOT EXISTS ix_factions_party ON factions (party_id);");
        AddColumnIfMissing(connection, "factions", "character_name", "TEXT NOT NULL DEFAULT ''");
        AddColumnIfMissing(connection, "factions", "home_province_id", "TEXT");
        AddColumnIfMissing(connection, "factions", "private_channel_id", "TEXT");
        AddColumnIfMissing(connection, "factions", "status", "TEXT NOT NULL DEFAULT 'Active'");
        AddColumnIfMissing(connection, "factions", "created_at", "INTEGER NOT NULL DEFAULT 0");

        Exec(connection, @"
            CREATE TABLE IF NOT EXISTS game_state (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                started INTEGER NOT NULL DEFAULT 0,
                season TEXT NOT NULL DEFAULT 'Spring',
                year INTEGER NOT NULL DEFAULT 1850,
                term_number INTEGER NOT NULL DEFAULT 0,
                term_start_season TEXT NOT NULL DEFAULT 'Spring',
                term_start_year INTEGER NOT NULL DEFAULT 1850,
                pre_election_season TEXT NOT NULL DEFAULT 'Spring',
                pre_election_year INTEGER NOT NULL DEFAULT 1850,
                resolution_number INTEGER NOT NULL DEFAULT 0,
                campaign_days_run INTEGER NOT NULL DEFAULT 0,
                next_resolution_at INTEGER,
                last_resolution_at INTEGER,
                season_length_days INTEGER,
                paused INTEGER NOT NULL DEFAULT 0,
                seats INTEGER NOT NULL DEFAULT 180,
                tax_rate REAL NOT NULL DEFAULT 0.2,
                economy REAL NOT NULL DEFAULT 100,
                favour REAL NOT NULL DEFAULT 60,
                favour_baseline REAL NOT NULL DEFAULT 60,
                last_navy_share REAL NOT NULL DEFAULT 0,
                threat REAL NOT NULL DEFAULT 25,
                reserve TEXT NOT NULL DEFAULT '0',
                rng_seed INTEGER,
                no_confidence_cooldown_until INTEGER
            );");
        Exec(connection, "INSERT OR IGNORE INTO game_state (id) VALUES (1);");

        Exec(connection, @"
            CREATE TABLE IF NOT EXISTS channels (
                role TEXT PRIMARY KEY,
                discord_id TEXT NOT NULL
            );");

        Exec(connection, @"
            CREATE TABLE IF NOT EXISTS settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );");

        Exec(connection, @"
            CREATE TABLE IF NOT EXISTS alerted_users (
                user_id TEXT PRIMARY KEY,
                alerted_at INTEGER NOT NULL
            );");

        Exec(connection, @"
            CREATE TABLE IF NOT EXISTS provinces (
                id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                population REAL NOT NULL,
                discontent REAL NOT NULL
            );");

        Exec(connection, @"
            CREATE TABLE IF NOT EXISTS unrest_samples (
                resolution_number INTEGER PRIMARY KEY,
                unrest REAL NOT NULL
            );");

        Exec(connection, @"
            CREATE TABLE IF NOT EXISTS province_conditions (
                province_id TEXT NOT NULL REFERENCES provinces(id) ON DELETE CASCADE,
                condition_id TEXT NOT NULL,
                since_season TEXT NOT NULL,
                since_year INTEGER NOT NULL,
                PRIMARY KEY (province_id, condition_id)
            );");

        Exec(connection, @"
            CREATE TABLE IF NOT EXISTS templates (
                id TEXT PRIMARY KEY,
                kind TEXT NOT NULL,
                status TEXT NOT NULL DEFAULT 'Draft',
                yaml TEXT NOT NULL,
                created_at INTEGER NOT NULL,
                updated_at INTEGER NOT NULL,
                times_posted INTEGER NOT NULL DEFAULT 0
            );");
        Exec(connection, "CREATE INDEX IF NOT EXISTS ix_templates_kind ON templates (kind, status);");

        Exec(connection, @"
            CREATE TABLE IF NOT EXISTS issues (
                id TEXT PRIMARY KEY,
                template_id TEXT NOT NULL,
                title TEXT NOT NULL,
                for_text TEXT NOT NULL,
                against_text TEXT NOT NULL,
                opinion REAL NOT NULL,
                salience REAL NOT NULL,
                salience_raised_resolution INTEGER,
                status TEXT NOT NULL DEFAULT 'Open',
                opened_resolution INTEGER NOT NULL,
                closed_resolution INTEGER
            );");

        Exec(connection, @"
            CREATE TABLE IF NOT EXISTS programme_stances (
                party_id INTEGER NOT NULL REFERENCES parties(id) ON DELETE CASCADE,
                issue_id TEXT NOT NULL REFERENCES issues(id),
                stance INTEGER NOT NULL,
                PRIMARY KEY (party_id, issue_id)
            );");

        Exec(connection, @"
            CREATE TABLE IF NOT EXISTS party_join_requests (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                faction_id TEXT NOT NULL REFERENCES factions(user_id) ON DELETE CASCADE,
                party_id INTEGER NOT NULL REFERENCES parties(id) ON DELETE CASCADE,
                requested_at INTEGER NOT NULL,
                status TEXT NOT NULL DEFAULT 'Pending',
                message_id TEXT
            );");

        Exec(connection, @"
            CREATE TABLE IF NOT EXISTS party_join_answers (
                request_id INTEGER NOT NULL REFERENCES party_join_requests(id) ON DELETE CASCADE,
                faction_id TEXT NOT NULL REFERENCES factions(user_id) ON DELETE CASCADE,
                accept INTEGER NOT NULL,
                answered_at INTEGER NOT NULL,
                PRIMARY KEY (request_id, faction_id)
            );");

        Exec(connection, @"
            CREATE TABLE IF NOT EXISTS events (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                template_id TEXT NOT NULL,
                title TEXT NOT NULL,
                text TEXT NOT NULL,
                effects TEXT NOT NULL DEFAULT '',
                status TEXT NOT NULL DEFAULT 'Queued',
                from_schedule INTEGER NOT NULL DEFAULT 0,
                queued_at INTEGER NOT NULL,
                fire_at_resolution INTEGER NOT NULL,
                fired_resolution INTEGER,
                message_id TEXT
            );");
        Exec(connection, "CREATE INDEX IF NOT EXISTS ix_events_queue ON events (status, fire_at_resolution, id);");

        Exec(connection, @"
            CREATE TABLE IF NOT EXISTS crises (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                template_id TEXT NOT NULL,
                event_id INTEGER REFERENCES events(id),
                title TEXT NOT NULL,
                ministry TEXT NOT NULL,
                province_id TEXT NOT NULL REFERENCES provinces(id),
                issue_id TEXT NOT NULL REFERENCES issues(id),
                answers TEXT NOT NULL,
                complication_text TEXT,
                complication_effects TEXT,
                default_text TEXT NOT NULL,
                default_effects TEXT NOT NULL DEFAULT '',
                posted_at INTEGER NOT NULL,
                posted_resolution INTEGER NOT NULL,
                due_resolution INTEGER NOT NULL,
                status TEXT NOT NULL DEFAULT 'Live',
                chosen_answer INTEGER,
                responder_faction_id TEXT,
                response_text TEXT,
                responded_at INTEGER,
                outcome TEXT,
                message_id TEXT
            );");

        Exec(connection, @"
            CREATE TABLE IF NOT EXISTS condemnations (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                faction_id TEXT NOT NULL REFERENCES factions(user_id),
                crisis_id INTEGER NOT NULL REFERENCES crises(id) ON DELETE CASCADE,
                side INTEGER NOT NULL,
                made_at INTEGER NOT NULL,
                UNIQUE (faction_id, crisis_id)
            );");

        Exec(connection, @"
            CREATE TABLE IF NOT EXISTS bills (
                id TEXT PRIMARY KEY,
                template_id TEXT NOT NULL,
                issue_id TEXT NOT NULL REFERENCES issues(id),
                title TEXT NOT NULL,
                motion TEXT NOT NULL,
                lean INTEGER NOT NULL,
                pass_text TEXT NOT NULL,
                pass_effects TEXT NOT NULL DEFAULT '',
                status TEXT NOT NULL DEFAULT 'Available',
                cooldown_until INTEGER,
                available_resolution INTEGER NOT NULL
            );");

        Exec(connection, @"
            CREATE TABLE IF NOT EXISTS readings (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                bill_id TEXT NOT NULL REFERENCES bills(id),
                sponsor_faction_id TEXT NOT NULL REFERENCES factions(user_id),
                kind TEXT NOT NULL,
                proposed_at INTEGER NOT NULL,
                proposed_resolution INTEGER NOT NULL,
                opens_resolution INTEGER NOT NULL,
                due_resolution INTEGER NOT NULL,
                status TEXT NOT NULL DEFAULT 'Scheduled',
                yes_seats INTEGER,
                no_seats INTEGER,
                abstain_seats INTEGER,
                announce_message_id TEXT,
                message_id TEXT
            );");

        Exec(connection, @"
            CREATE TABLE IF NOT EXISTS no_confidence (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                tabler_faction_id TEXT NOT NULL REFERENCES factions(user_id),
                tabled_at INTEGER NOT NULL,
                tabled_resolution INTEGER NOT NULL,
                gather_until_resolution INTEGER NOT NULL,
                due_resolution INTEGER,
                status TEXT NOT NULL DEFAULT 'Gathering',
                yes_seats INTEGER,
                no_seats INTEGER,
                message_id TEXT
            );");

        Exec(connection, @"
            CREATE TABLE IF NOT EXISTS no_confidence_cosigners (
                no_confidence_id INTEGER NOT NULL REFERENCES no_confidence(id) ON DELETE CASCADE,
                faction_id TEXT NOT NULL REFERENCES factions(user_id),
                signed_at INTEGER NOT NULL,
                PRIMARY KEY (no_confidence_id, faction_id)
            );");

        Exec(connection, @"
            CREATE TABLE IF NOT EXISTS votes (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                subject_type TEXT NOT NULL,
                subject_id INTEGER NOT NULL,
                faction_id TEXT NOT NULL REFERENCES factions(user_id),
                choice TEXT NOT NULL,
                cast_at INTEGER NOT NULL,
                UNIQUE (subject_type, subject_id, faction_id)
            );");

        Exec(connection, @"
            CREATE TABLE IF NOT EXISTS cabinets (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                kind TEXT NOT NULL,
                chancellor_faction_id TEXT REFERENCES factions(user_id),
                status TEXT NOT NULL,
                created_at INTEGER NOT NULL,
                created_resolution INTEGER NOT NULL,
                due_resolution INTEGER,
                message_id TEXT
            );");

        Exec(connection, @"
            CREATE TABLE IF NOT EXISTS cabinet_posts (
                cabinet_id INTEGER NOT NULL REFERENCES cabinets(id) ON DELETE CASCADE,
                ministry TEXT NOT NULL,
                faction_id TEXT REFERENCES factions(user_id),
                PRIMARY KEY (cabinet_id, ministry)
            );");

        Exec(connection, @"
            CREATE TABLE IF NOT EXISTS purses (
                ministry TEXT PRIMARY KEY,
                balance TEXT NOT NULL DEFAULT '0',
                allocated_this_year TEXT NOT NULL DEFAULT '0'
            );");
        foreach (var ministry in Enum.GetNames<Ministry>())
            Exec(connection, $"INSERT OR IGNORE INTO purses (ministry) VALUES ('{ministry}');");

        Exec(connection, @"
            CREATE TABLE IF NOT EXISTS fiscal_years (
                year INTEGER PRIMARY KEY,
                economy REAL NOT NULL,
                tax_rate REAL NOT NULL,
                budget TEXT NOT NULL,
                computed_resolution INTEGER NOT NULL
            );");

        Exec(connection, @"
            CREATE TABLE IF NOT EXISTS allocations (
                year INTEGER NOT NULL,
                ministry TEXT NOT NULL,
                amount TEXT NOT NULL,
                submitted_by TEXT,
                submitted_at INTEGER NOT NULL,
                PRIMARY KEY (year, ministry)
            );");

        Exec(connection, @"
            CREATE TABLE IF NOT EXISTS ledger (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                account TEXT NOT NULL,
                amount TEXT NOT NULL,
                kind TEXT NOT NULL,
                label TEXT NOT NULL,
                source_type TEXT,
                source_id TEXT,
                made_at INTEGER NOT NULL,
                resolution_number INTEGER NOT NULL,
                season TEXT NOT NULL,
                year INTEGER NOT NULL,
                public_from_resolution INTEGER NOT NULL
            );");
        Exec(connection, "CREATE INDEX IF NOT EXISTS ix_ledger_account ON ledger (account, year);");

        Exec(connection, @"
            CREATE TABLE IF NOT EXISTS units (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                type TEXT NOT NULL,
                name TEXT NOT NULL,
                sides INTEGER NOT NULL,
                province_id TEXT NOT NULL REFERENCES provinces(id),
                raised_at INTEGER NOT NULL
            );");

        Exec(connection, @"
            CREATE TABLE IF NOT EXISTS pending_orders (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                unit_type TEXT,
                unit_id INTEGER REFERENCES units(id) ON DELETE CASCADE,
                kind TEXT NOT NULL,
                province_id TEXT REFERENCES provinces(id),
                name TEXT,
                cost TEXT NOT NULL DEFAULT '0',
                ordered_by TEXT,
                ordered_at INTEGER NOT NULL,
                resolution_number INTEGER NOT NULL
            );");

        Exec(connection, @"
            CREATE TABLE IF NOT EXISTS projects (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                template_id TEXT NOT NULL,
                title TEXT NOT NULL,
                purse TEXT NOT NULL,
                province_id TEXT NOT NULL REFERENCES provinces(id),
                price TEXT NOT NULL,
                term INTEGER NOT NULL,
                progress TEXT NOT NULL DEFAULT '0',
                spending TEXT NOT NULL,
                started_resolution INTEGER NOT NULL,
                first_payment_resolution INTEGER NOT NULL,
                bonus_due_resolution INTEGER,
                effects TEXT NOT NULL DEFAULT '',
                bonus_text TEXT,
                bonus_effects TEXT,
                status TEXT NOT NULL DEFAULT 'Active',
                finished_resolution INTEGER
            );");

        Exec(connection, @"
            CREATE TABLE IF NOT EXISTS actions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                faction_id TEXT NOT NULL REFERENCES factions(user_id),
                kind TEXT NOT NULL,
                issue_id TEXT NOT NULL REFERENCES issues(id),
                province_id TEXT REFERENCES provinces(id),
                stance INTEGER,
                off_programme INTEGER NOT NULL DEFAULT 0,
                target_faction_id TEXT REFERENCES factions(user_id),
                submitted_at INTEGER NOT NULL,
                resolved INTEGER NOT NULL DEFAULT 0,
                resolution_number INTEGER,
                result TEXT
            );");
        Exec(connection, "CREATE INDEX IF NOT EXISTS ix_actions_pending ON actions (resolved, faction_id);");

        Exec(connection, @"
            CREATE TABLE IF NOT EXISTS opposition_actions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                faction_id TEXT NOT NULL REFERENCES factions(user_id),
                kind TEXT NOT NULL,
                season TEXT NOT NULL,
                year INTEGER NOT NULL,
                subject_id INTEGER,
                made_at INTEGER NOT NULL
            );");
        AddColumnIfMissing(connection, "opposition_actions", "cancelled", "INTEGER NOT NULL DEFAULT 0");

        Exec(connection, @"
            CREATE TABLE IF NOT EXISTS questions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                asker_faction_id TEXT NOT NULL REFERENCES factions(user_id),
                ministry TEXT NOT NULL,
                text TEXT NOT NULL,
                asked_at INTEGER NOT NULL,
                asked_resolution INTEGER NOT NULL,
                due_resolution INTEGER NOT NULL,
                minister_at_ask TEXT,
                status TEXT NOT NULL DEFAULT 'Open',
                answer_text TEXT,
                answered_at INTEGER,
                answerer_faction_id TEXT,
                message_id TEXT
            );");

        Exec(connection, @"
            CREATE TABLE IF NOT EXISTS stance_log (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                faction_id TEXT NOT NULL REFERENCES factions(user_id),
                issue_id TEXT NOT NULL REFERENCES issues(id),
                stance INTEGER NOT NULL,
                season TEXT NOT NULL,
                year INTEGER NOT NULL,
                resolution_number INTEGER NOT NULL,
                source TEXT NOT NULL,
                off_programme INTEGER NOT NULL DEFAULT 0,
                logged_at INTEGER NOT NULL
            );");
        Exec(connection, "CREATE INDEX IF NOT EXISTS ix_stance_log_faction ON stance_log (faction_id, issue_id, id);");

        Exec(connection, @"
            CREATE TABLE IF NOT EXISTS province_results (
                term_number INTEGER NOT NULL,
                province_id TEXT NOT NULL,
                party_id INTEGER NOT NULL,
                party_name TEXT NOT NULL,
                strength REAL NOT NULL,
                seats INTEGER NOT NULL,
                PRIMARY KEY (term_number, province_id, party_id)
            );");
        AddColumnIfMissing(connection, "province_results", "winner", "INTEGER NOT NULL DEFAULT 0");

        Exec(connection, @"
            CREATE TABLE IF NOT EXISTS log_entries (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                at INTEGER NOT NULL,
                resolution_number INTEGER NOT NULL,
                kind TEXT NOT NULL,
                source TEXT NOT NULL,
                faction_id TEXT,
                payload TEXT NOT NULL,
                public_flag INTEGER NOT NULL DEFAULT 0
            );");
        Exec(connection, "CREATE INDEX IF NOT EXISTS ix_log_entries_kind ON log_entries (kind, resolution_number);");

        Exec(connection, @"
            CREATE TABLE IF NOT EXISTS reminders_sent (
                faction_id TEXT NOT NULL,
                resolution_number INTEGER NOT NULL,
                PRIMARY KEY (faction_id, resolution_number)
            );");

        Exec(connection, @"
            CREATE TABLE IF NOT EXISTS variables (
                name TEXT PRIMARY KEY,
                value REAL NOT NULL
            );");
    }

    private static void Exec(SqliteConnection conn, string sql)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static void Exec(SqliteConnection conn, SqliteTransaction? tx, string sql, params (string Name, object? Value)[] parameters) =>
        ExecCount(conn, tx, sql, parameters);

    private static int ExecCount(SqliteConnection conn, SqliteTransaction? tx, string sql, params (string Name, object? Value)[] parameters)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd.ExecuteNonQuery();
    }

    private static void AddColumnIfMissing(SqliteConnection conn, string table, string column, string definition)
    {
        var check = conn.CreateCommand();
        check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = $col;";
        check.Parameters.AddWithValue("$col", column);
        var exists = Convert.ToInt64(check.ExecuteScalar()) > 0;
        if (!exists) Exec(conn, $"ALTER TABLE {table} ADD COLUMN {column} {definition};");
    }

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}
