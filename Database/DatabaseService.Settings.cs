using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

public record GameState(
    bool Started,
    Season Season,
    int Year,
    int TermNumber,
    Season TermStartSeason,
    int TermStartYear,
    Season PreElectionSeason,
    int PreElectionYear,
    long ResolutionNumber,
    int CampaignDaysRun,
    DateTimeOffset? NextResolutionAt,
    DateTimeOffset? LastResolutionAt,
    int? SeasonLengthDays,
    bool Paused,
    int Seats,
    double TaxRate,
    double Economy,
    double Favour,
    double FavourBaseline,
    double LastNavyShare,
    double Threat,
    decimal Reserve,
    int? RngSeed,
    long? NoConfidenceCooldownUntil,
    bool SenateEstablished)
{
    public bool IsPreElection => Season == PreElectionSeason && Year == PreElectionYear;

    public bool IsSenateElectionTurn => SenateEstablished && GameCalendar.IsSenateElectionSeason(Season, Year);

    public bool IsSenateCampaign => IsSenateElectionTurn && !IsPreElection;

    public bool IsJoiningOpen => IsPreElection || IsSenateElectionTurn;

    public string Label => GameCalendar.Label(Season, Year);
}

public partial class DatabaseService
{
    private const string SelectGameStateColumns = @"started, season, year, term_number, term_start_season, term_start_year,
        pre_election_season, pre_election_year, resolution_number, campaign_days_run, next_resolution_at,
        last_resolution_at, season_length_days, paused, seats, tax_rate, economy, favour, favour_baseline,
        last_navy_share, threat, reserve, rng_seed, no_confidence_cooldown_until, senate_established";

    private static GameState ReadGameState(SqliteDataReader r) => new(
        r.GetInt64(0) != 0,
        Enum.Parse<Season>(r.GetString(1)),
        r.GetInt32(2),
        r.GetInt32(3),
        Enum.Parse<Season>(r.GetString(4)),
        r.GetInt32(5),
        Enum.Parse<Season>(r.GetString(6)),
        r.GetInt32(7),
        r.GetInt64(8),
        r.GetInt32(9),
        r.IsDBNull(10) ? null : DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(10)),
        r.IsDBNull(11) ? null : DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(11)),
        r.IsDBNull(12) ? null : r.GetInt32(12),
        r.GetInt64(13) != 0,
        r.GetInt32(14),
        r.GetDouble(15),
        r.GetDouble(16),
        r.GetDouble(17),
        r.GetDouble(18),
        r.GetDouble(19),
        r.GetDouble(20),
        r.GetDecimal(21),
        r.IsDBNull(22) ? null : r.GetInt32(22),
        r.IsDBNull(23) ? null : r.GetInt64(23),
        r.GetInt64(24) != 0);

    public GameState GetGameState()
    {
        using var conn = Open();
        return GetGameStateTx(conn, null);
    }

    private static GameState GetGameStateTx(SqliteConnection conn, SqliteTransaction? tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT {SelectGameStateColumns} FROM game_state WHERE id = 1;";
        using var r = cmd.ExecuteReader();
        r.Read();
        return ReadGameState(r);
    }

    public void SaveGameState(GameState state)
    {
        using var conn = Open();
        SaveGameStateTx(conn, null, state);
    }

    private static void SaveGameStateTx(SqliteConnection conn, SqliteTransaction? tx, GameState s) =>
        Exec(conn, tx, @"
            UPDATE game_state SET
                started = $started, season = $season, year = $year, term_number = $term,
                term_start_season = $termSeason, term_start_year = $termYear,
                pre_election_season = $preSeason, pre_election_year = $preYear,
                resolution_number = $resolution, campaign_days_run = $campaignDays,
                next_resolution_at = $next, last_resolution_at = $last, season_length_days = $length,
                paused = $paused, seats = $seats, tax_rate = $taxRate, economy = $economy, favour = $favour,
                favour_baseline = $favourBaseline, last_navy_share = $navyShare, threat = $threat,
                reserve = $reserve, rng_seed = $seed, no_confidence_cooldown_until = $ncCooldown,
                senate_established = $senateEstablished
            WHERE id = 1;",
            ("$started", s.Started ? 1 : 0),
            ("$season", s.Season.ToString()),
            ("$year", s.Year),
            ("$term", s.TermNumber),
            ("$termSeason", s.TermStartSeason.ToString()),
            ("$termYear", s.TermStartYear),
            ("$preSeason", s.PreElectionSeason.ToString()),
            ("$preYear", s.PreElectionYear),
            ("$resolution", s.ResolutionNumber),
            ("$campaignDays", s.CampaignDaysRun),
            ("$next", s.NextResolutionAt?.ToUnixTimeSeconds()),
            ("$last", s.LastResolutionAt?.ToUnixTimeSeconds()),
            ("$length", s.SeasonLengthDays),
            ("$paused", s.Paused ? 1 : 0),
            ("$seats", s.Seats),
            ("$taxRate", s.TaxRate),
            ("$economy", s.Economy),
            ("$favour", s.Favour),
            ("$favourBaseline", s.FavourBaseline),
            ("$navyShare", s.LastNavyShare),
            ("$threat", s.Threat),
            ("$reserve", Money(s.Reserve)),
            ("$seed", s.RngSeed),
            ("$ncCooldown", s.NoConfidenceCooldownUntil),
            ("$senateEstablished", s.SenateEstablished ? 1 : 0));

    public ulong? GetChannel(ChannelRole role)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT discord_id FROM channels WHERE role = $role;";
        cmd.Parameters.AddWithValue("$role", role.ToString());
        return cmd.ExecuteScalar() is string id ? ulong.Parse(id) : null;
    }

    public ulong? GetPostingChannel(ChannelRole role) =>
        GetChannel(role) ?? (role == ChannelRole.Notices ? GetChannel(ChannelRole.News) : null);

    public Dictionary<ChannelRole, ulong> GetChannels()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT role, discord_id FROM channels;";
        var map = new Dictionary<ChannelRole, ulong>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            if (Enum.TryParse<ChannelRole>(r.GetString(0), out var role))
                map[role] = ulong.Parse(r.GetString(1));
        return map;
    }

    public void SetChannel(ChannelRole role, ulong discordId)
    {
        using var conn = Open();
        Exec(conn, null, @"
            INSERT INTO channels (role, discord_id) VALUES ($role, $id)
            ON CONFLICT(role) DO UPDATE SET discord_id = excluded.discord_id;",
            ("$role", role.ToString()),
            ("$id", discordId.ToString()));
    }

    public string? GetSetting(string key)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key = $key;";
        cmd.Parameters.AddWithValue("$key", key);
        return cmd.ExecuteScalar() as string;
    }

    public void SetSetting(string key, string? value)
    {
        using var conn = Open();
        if (value is null)
            Exec(conn, null, "DELETE FROM settings WHERE key = $key;", ("$key", key));
        else
            Exec(conn, null, @"
                INSERT INTO settings (key, value) VALUES ($key, $value)
                ON CONFLICT(key) DO UPDATE SET value = excluded.value;",
                ("$key", key),
                ("$value", value));
    }

    private static string Money(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
}
