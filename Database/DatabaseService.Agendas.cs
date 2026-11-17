using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;

public enum AgendaStatus { Active, Scored, Void }

public sealed record AgendaHand(
    long Id, ulong FactionId, string AgendaId, long DealtResolution, Season Season, int Year,
    IReadOnlyDictionary<AgendaTrack, double> Start, AgendaStatus Status, int? WishesMet, double? Reward);

public sealed record WishResult(AgendaWish Wish, bool Met);

public sealed record AgendaReveal(ulong FactionId, string AgendaName, IReadOnlyList<WishResult> Results, double Reward);

public sealed record FutureAgenda(AgendaDefinition Agenda, bool HasCurrent);

public enum AssignAgendaRefusal { UnknownAgenda, NotActive, NotStarted }

public sealed record AssignAgendaResult(AssignAgendaRefusal? Refusal, AgendaHand? Dealt = null, bool Repeats = false, string? Future = null);

public partial class DatabaseService
{
    private const string SelectAgendaColumns =
        "id, faction_id, agenda_id, dealt_resolution, dealt_season, dealt_year, start_values, status, wishes_met, reward";

    private static AgendaHand ReadAgenda(SqliteDataReader r) => new(
        r.GetInt64(0),
        ulong.Parse(r.GetString(1)),
        r.GetString(2),
        r.GetInt64(3),
        Enum.Parse<Season>(r.GetString(4)),
        r.GetInt32(5),
        JsonSerializer.Deserialize<Dictionary<AgendaTrack, double>>(r.GetString(6)) ?? [],
        Enum.Parse<AgendaStatus>(r.GetString(7)),
        r.IsDBNull(8) ? null : r.GetInt32(8),
        r.IsDBNull(9) ? null : r.GetDouble(9));

    public AgendaHand? GetActiveAgenda(ulong factionId)
    {
        using var conn = Open();
        return QueryAgendasTx(conn, null, "faction_id = $faction AND status = 'Active'", ("$faction", factionId.ToString())).FirstOrDefault();
    }

    public List<AgendaHand> GetActiveAgendas()
    {
        using var conn = Open();
        return QueryAgendasTx(conn, null, "status = 'Active'");
    }

    public List<AgendaHand> GetAgendaHistory(ulong factionId)
    {
        using var conn = Open();
        return QueryAgendasTx(conn, null, "faction_id = $faction", ("$faction", factionId.ToString()));
    }

    public Dictionary<AgendaTrack, double> GetTrackValues()
    {
        using var conn = Open();
        return TrackValuesTx(conn, null);
    }

    public AgendaHand? DealAgenda(ulong factionId, GameConfig config, Random random)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        if (!GetGameStateTx(conn, tx).Started || GetFactionTx(conn, tx, factionId) is not { IsActive: true }) return null;
        if (ActiveAgendaTx(conn, tx, factionId) is not null) return null;
        var hand = DealTx(conn, tx, config, factionId, random);
        tx.Commit();
        return hand;
    }

    public List<AgendaHand> DealMissingAgendas(GameConfig config, Random random)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        if (!GetGameStateTx(conn, tx).Started) return [];
        var dealt = QueryFactionsTx(conn, tx, "status = 'Active'")
            .Where(f => ActiveAgendaTx(conn, tx, f.UserId) is null)
            .Select(f => DealTx(conn, tx, config, f.UserId, random))
            .OfType<AgendaHand>()
            .ToList();
        tx.Commit();
        return dealt;
    }

    public AgendaHand? RedealAgenda(ulong factionId, GameConfig config, Random random)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        if (!GetGameStateTx(conn, tx).Started || GetFactionTx(conn, tx, factionId) is not { IsActive: true }) return null;
        if (ActiveAgendaTx(conn, tx, factionId) is { } current)
            Exec(conn, tx, "UPDATE agendas SET status = 'Void' WHERE id = $id;", ("$id", current.Id));
        var hand = DealTx(conn, tx, config, factionId, random);
        tx.Commit();
        return hand;
    }

    private static void ScoreAndDealAgendasTx(SqliteConnection conn, SqliteTransaction tx, GameConfig config, int seed, ResolutionReport report)
    {
        var agendas = config.Agendas;
        if (agendas.Agendas.Count == 0) return;
        var state = GetGameStateTx(conn, tx);
        var now = TrackValuesTx(conn, tx);
        var random = new Random(seed);
        var futures = FuturesTx(conn, tx);

        foreach (var hand in QueryAgendasTx(conn, tx, "status = 'Active'"))
        {
            if (GetFactionTx(conn, tx, hand.FactionId) is not { IsActive: true })
            {
                Exec(conn, tx, "UPDATE agendas SET status = 'Void' WHERE id = $id;", ("$id", hand.Id));
                continue;
            }
            if (agendas.Find(hand.AgendaId) is not { } definition)
            {
                Exec(conn, tx, "UPDATE agendas SET status = 'Void' WHERE id = $id;", ("$id", hand.Id));
                report.Alerts.Add(Texts.Get("secretAgenda.gm.unknownId", ("faction", $"<@{hand.FactionId}>"), ("agenda", hand.AgendaId)));
                continue;
            }
            if (hand.DealtResolution == state.ResolutionNumber && futures.ContainsKey(hand.FactionId))
            {
                Exec(conn, tx, "UPDATE agendas SET status = 'Void' WHERE id = $id;", ("$id", hand.Id));
                continue;
            }
            if (hand.DealtResolution == state.ResolutionNumber)
            {
                Exec(conn, tx, "UPDATE agendas SET start_values = $start WHERE id = $id;", ("$id", hand.Id), ("$start", JsonSerializer.Serialize(now)));
                continue;
            }

            var results = definition.Wishes
                .Select(w => new WishResult(w, AgendaRules.Met(w, hand.Start.GetValueOrDefault(w.Track, now[w.Track]), now[w.Track], agendas.MarginOf(w.Track))))
                .ToList();
            var met = results.Count(r => r.Met);
            var reward = AgendaRules.Reward(met, results.Count, agendas.Reward);
            if (reward != 0) AddPopularityTx(conn, tx, hand.FactionId, reward, config.Constants.Popularity);
            Exec(conn, tx, @"
                UPDATE agendas SET status = 'Scored', wishes_met = $met, reward = $reward, scored_resolution = $resolution WHERE id = $id;",
                ("$id", hand.Id), ("$met", met), ("$reward", reward), ("$resolution", state.ResolutionNumber));
            report.AgendaReveals.Add(new AgendaReveal(hand.FactionId, definition.Name, results, reward));
        }

        foreach (var faction in QueryFactionsTx(conn, tx, "status = 'Active'"))
        {
            if (ActiveAgendaTx(conn, tx, faction.UserId) is not null) continue;
            var future = futures.GetValueOrDefault(faction.UserId);
            Exec(conn, tx, "DELETE FROM agenda_futures WHERE faction_id = $faction;", ("$faction", faction.UserId.ToString()));
            if (DealTx(conn, tx, config, faction.UserId, random, future) is { } hand && agendas.Find(hand.AgendaId) is { } definition)
                report.AddPrivate(faction.UserId, future == hand.AgendaId
                    ? AgendaWording.FutureBegins(definition)
                    : AgendaWording.Dealt(definition, agendas.Reward));
        }
        Exec(conn, tx, "DELETE FROM agenda_futures;");
    }

    private static void DrawFutureAgendasTx(SqliteConnection conn, SqliteTransaction tx, GameConfig config, int seed, ResolutionReport report)
    {
        var agendas = config.Agendas;
        if (agendas.Agendas.Count == 0) return;
        var random = new Random(seed + 1);
        foreach (var faction in QueryFactionsTx(conn, tx, "status = 'Active'"))
            if (DrawFutureTx(conn, tx, config, faction.UserId, random) is { } future)
                report.AddPrivate(faction.UserId, AgendaWording.Future(future.Agenda, agendas.Reward, future.HasCurrent));
    }

    public FutureAgenda? DealFutureAgenda(ulong factionId, GameConfig config, Random random)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        if (GetGameStateTx(conn, tx) is not { Started: true, IsPreElection: true }) return null;
        if (GetFactionTx(conn, tx, factionId) is not { IsActive: true }) return null;
        var future = DrawFutureTx(conn, tx, config, factionId, random);
        tx.Commit();
        return future;
    }

    public Dictionary<ulong, FutureAgenda> DealMissingFutureAgendas(GameConfig config, Random random)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        if (GetGameStateTx(conn, tx) is not { Started: true, IsPreElection: true }) return [];
        var dealt = new Dictionary<ulong, FutureAgenda>();
        foreach (var faction in QueryFactionsTx(conn, tx, "status = 'Active'"))
            if (ActiveAgendaTx(conn, tx, faction.UserId) is null && DrawFutureTx(conn, tx, config, faction.UserId, random) is { } future)
                dealt[faction.UserId] = future;
        tx.Commit();
        return dealt;
    }

    private static FutureAgenda? DrawFutureTx(SqliteConnection conn, SqliteTransaction tx, GameConfig config, ulong factionId, Random random)
    {
        var agendas = config.Agendas;
        if (FuturesTx(conn, tx).ContainsKey(factionId)) return null;
        var assigned = AssignmentsTx(conn, tx).GetValueOrDefault(factionId);
        Exec(conn, tx, "DELETE FROM agenda_assignments WHERE faction_id = $faction;", ("$faction", factionId.ToString()));
        var current = ActiveAgendaTx(conn, tx, factionId)?.AgendaId;
        var agenda = assigned is not null && agendas.Find(assigned) is { } chosen
            ? chosen
            : AgendaRules.Pick(agendas.Agendas, current ?? LastAgendaIdTx(conn, tx, factionId), FutureHeldTx(conn, tx), random);
        if (agenda is null) return null;
        SetFutureTx(conn, tx, factionId, agenda.Id);
        return new FutureAgenda(agenda, current is not null);
    }

    public Dictionary<ulong, string> GetFutureAgendas()
    {
        using var conn = Open();
        return FuturesTx(conn, null);
    }

    private static Dictionary<ulong, string> FuturesTx(SqliteConnection conn, SqliteTransaction? tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT faction_id, agenda_id FROM agenda_futures;";
        var result = new Dictionary<ulong, string>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            result[ulong.Parse(r.GetString(0))] = r.GetString(1);
        return result;
    }

    private static void SetFutureTx(SqliteConnection conn, SqliteTransaction tx, ulong factionId, string agendaId) =>
        Exec(conn, tx, @"
            INSERT INTO agenda_futures (faction_id, agenda_id) VALUES ($faction, $agenda)
            ON CONFLICT (faction_id) DO UPDATE SET agenda_id = excluded.agenda_id;",
            ("$faction", factionId.ToString()), ("$agenda", agendaId));

    private static Dictionary<string, int> FutureHeldTx(SqliteConnection conn, SqliteTransaction tx) =>
        FuturesTx(conn, tx).Values.Concat(AssignmentsTx(conn, tx).Values)
            .GroupBy(id => id)
            .ToDictionary(g => g.Key, g => g.Count());

    public AssignAgendaResult AssignAgenda(ulong factionId, string agendaId, GameConfig config, bool now)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        if (config.Agendas.Find(agendaId) is null) return new AssignAgendaResult(AssignAgendaRefusal.UnknownAgenda);
        if (GetFactionTx(conn, tx, factionId) is not { IsActive: true }) return new AssignAgendaResult(AssignAgendaRefusal.NotActive);
        var started = GetGameStateTx(conn, tx).Started;
        if (now && !started) return new AssignAgendaResult(AssignAgendaRefusal.NotStarted);

        var current = ActiveAgendaTx(conn, tx, factionId);
        var previous = current?.AgendaId ?? LastAgendaIdTx(conn, tx, factionId);
        AgendaHand? dealt = null;
        if (now)
        {
            if (current is not null) Exec(conn, tx, "UPDATE agendas SET status = 'Void' WHERE id = $id;", ("$id", current.Id));
            dealt = DealTx(conn, tx, config, factionId, new Random(), agendaId);
        }
        else if (FuturesTx(conn, tx).ContainsKey(factionId))
        {
            SetFutureTx(conn, tx, factionId, agendaId);
            tx.Commit();
            return new AssignAgendaResult(null, Repeats: previous == agendaId, Future: agendaId);
        }
        else
        {
            Exec(conn, tx, @"
                INSERT INTO agenda_assignments (faction_id, agenda_id) VALUES ($faction, $agenda)
                ON CONFLICT (faction_id) DO UPDATE SET agenda_id = excluded.agenda_id;",
                ("$faction", factionId.ToString()), ("$agenda", agendaId));
        }
        tx.Commit();
        return new AssignAgendaResult(null, dealt, previous == agendaId);
    }

    public bool CancelAgendaAssignment(ulong factionId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM agenda_assignments WHERE faction_id = $faction;";
        cmd.Parameters.AddWithValue("$faction", factionId.ToString());
        return cmd.ExecuteNonQuery() > 0;
    }

    public Dictionary<ulong, string> GetAgendaAssignments()
    {
        using var conn = Open();
        return AssignmentsTx(conn, null);
    }

    private static Dictionary<ulong, string> AssignmentsTx(SqliteConnection conn, SqliteTransaction? tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT faction_id, agenda_id FROM agenda_assignments;";
        var result = new Dictionary<ulong, string>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            result[ulong.Parse(r.GetString(0))] = r.GetString(1);
        return result;
    }

    private static string? LastAgendaIdTx(SqliteConnection conn, SqliteTransaction tx, ulong factionId) =>
        QueryAgendasTx(conn, tx, "faction_id = $faction", ("$faction", factionId.ToString())).LastOrDefault()?.AgendaId;

    private static Dictionary<string, int> HeldTx(SqliteConnection conn, SqliteTransaction tx, ulong exceptFactionId) =>
        QueryAgendasTx(conn, tx, "status = 'Active'").Select(h => h.AgendaId)
            .Concat(AssignmentsTx(conn, tx).Where(kv => kv.Key != exceptFactionId).Select(kv => kv.Value))
            .Concat(FuturesTx(conn, tx).Values)
            .GroupBy(id => id)
            .ToDictionary(g => g.Key, g => g.Count());

    private static AgendaHand? DealTx(SqliteConnection conn, SqliteTransaction tx, GameConfig config, ulong factionId, Random random, string? forced = null)
    {
        var agendas = config.Agendas;
        var assigned = forced ?? (AssignmentsTx(conn, tx).TryGetValue(factionId, out var id) ? id : null);
        Exec(conn, tx, "DELETE FROM agenda_assignments WHERE faction_id = $faction;", ("$faction", factionId.ToString()));
        var agenda = assigned is not null && agendas.Find(assigned) is { } chosen
            ? chosen
            : AgendaRules.Pick(agendas.Agendas, LastAgendaIdTx(conn, tx, factionId), HeldTx(conn, tx, factionId), random);
        if (agenda is null) return null;
        var state = GetGameStateTx(conn, tx);
        Exec(conn, tx, @"
            INSERT INTO agendas (faction_id, agenda_id, dealt_resolution, dealt_season, dealt_year, start_values, status)
            VALUES ($faction, $agenda, $resolution, $season, $year, $start, 'Active');",
            ("$faction", factionId.ToString()), ("$agenda", agenda.Id), ("$resolution", state.ResolutionNumber),
            ("$season", state.Season.ToString()), ("$year", state.Year), ("$start", JsonSerializer.Serialize(TrackValuesTx(conn, tx))));
        return ActiveAgendaTx(conn, tx, factionId);
    }

    private static AgendaHand? ActiveAgendaTx(SqliteConnection conn, SqliteTransaction tx, ulong factionId) =>
        QueryAgendasTx(conn, tx, "faction_id = $faction AND status = 'Active'", ("$faction", factionId.ToString())).FirstOrDefault();

    private static List<AgendaHand> QueryAgendasTx(SqliteConnection conn, SqliteTransaction? tx, string where, params (string Name, object? Value)[] parameters)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT {SelectAgendaColumns} FROM agendas WHERE {where} ORDER BY id;";
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        var list = new List<AgendaHand>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(ReadAgenda(r));
        return list;
    }

    private static Dictionary<AgendaTrack, double> TrackValuesTx(SqliteConnection conn, SqliteTransaction? tx)
    {
        var state = GetGameStateTx(conn, tx);
        var provinces = GetProvincesTx(conn, tx);
        var population = provinces.Sum(p => p.Population);
        return new Dictionary<AgendaTrack, double>
        {
            [AgendaTrack.Economy] = state.Economy,
            [AgendaTrack.Reserve] = (double)GetBalanceTx(conn, tx, LedgerAccount.Reserve),
            [AgendaTrack.Army] = GetUnitsTx(conn, tx, UnitType.Division).Sum(u => u.Sides),
            [AgendaTrack.Discontent] = population > 0 ? provinces.Sum(p => p.Discontent * p.Population) / population : 0,
            [AgendaTrack.Favour] = state.Favour,
            [AgendaTrack.Threat] = state.Threat,
        };
    }
}
