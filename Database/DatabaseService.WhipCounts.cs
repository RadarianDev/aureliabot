using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

public record WhipCount(long Id, ulong FactionId, long ReadingId, ulong MinisterId, VoteChoice Side);

public sealed record WhipCountResult(string? Refusal, WhipCount? Count = null, string? BillTitle = null);

public partial class DatabaseService
{
    private const string SelectWhipCountColumns = "id, faction_id, reading_id, minister_faction_id, side";

    private static WhipCount ReadWhipCount(SqliteDataReader r) => new(
        r.GetInt64(0),
        ulong.Parse(r.GetString(1)),
        r.GetInt64(2),
        ulong.Parse(r.GetString(3)),
        Enum.Parse<VoteChoice>(r.GetString(4)));

    private static List<WhipCount> QueryWhipCountsTx(SqliteConnection conn, SqliteTransaction? tx, string where, params (string Name, object? Value)[] parameters)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT {SelectWhipCountColumns} FROM whip_counts WHERE {where} ORDER BY id;";
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        var list = new List<WhipCount>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(ReadWhipCount(r));
        return list;
    }

    private static VoteChoice ExpectedVoteTx(SqliteConnection conn, SqliteTransaction? tx, Reading reading) =>
        RoleOfTx(conn, tx, reading.SponsorId) == FactionRole.Government ? VoteChoice.Yes : VoteChoice.No;

    private static string? WhipCountRefusalTx(
        SqliteConnection conn, SqliteTransaction? tx, ulong factionId, long readingId, ulong ministerId, VoteChoice side, GameConstants constants)
    {
        if (RoleOfTx(conn, tx, factionId) != FactionRole.Opposition) return Texts.Get("whipCount.refusal.notOpposition");
        if (!GetOppositionStandingTx(conn, tx, factionId, constants).CanAct)
        {
            var state = GetGameStateTx(conn, tx);
            var (season, year) = GameCalendar.Next(state.Season, state.Year);
            return Texts.Get("whipCount.refusal.actionUsed", ("season", GameCalendar.Label(season, year)), ("when", Display.NextResolution(state)));
        }
        if (QueryReadingsTx(conn, tx, "id = $id AND status = 'Open'", ("$id", readingId)).FirstOrDefault() is not { } reading)
            return Texts.Get("whipCount.refusal.notOpen");
        if (GetSittingCabinetTx(conn, tx)?.Holders.Contains(ministerId) != true)
            return Texts.Get("whipCount.refusal.notCabinet", ("member", $"<@{ministerId}>"));
        var votes = VotingSeatsTx(conn, tx).ContainsKey(ministerId)
            || (reading.Kind != ReadingKind.Override && SenateVotingSeatsTx(conn, tx).ContainsKey(ministerId));
        if (!votes) return Texts.Get("whipCount.refusal.noSeats", ("member", $"<@{ministerId}>"));
        if (side == ExpectedVoteTx(conn, tx, reading))
            return Texts.Get("whipCount.refusal.withTheLine", ("choice", AssemblyPublisher.ChoiceName(side)));
        var already = QueryWhipCountsTx(conn, tx, "faction_id = $faction AND reading_id = $reading AND minister_faction_id = $minister",
            ("$faction", factionId.ToString()), ("$reading", readingId), ("$minister", ministerId.ToString()));
        return already.Count > 0 ? Texts.Get("whipCount.refusal.already", ("member", $"<@{ministerId}>")) : null;
    }

    public WhipCountResult MakeWhipCount(ulong factionId, long readingId, ulong ministerId, VoteChoice side, GameConstants constants)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        if (WhipCountRefusalTx(conn, tx, factionId, readingId, ministerId, side, constants) is { } refusal) return new WhipCountResult(refusal);

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO whip_counts (faction_id, reading_id, minister_faction_id, side, made_at)
            VALUES ($faction, $reading, $minister, $side, $now) RETURNING id;";
        cmd.Parameters.AddWithValue("$faction", factionId.ToString());
        cmd.Parameters.AddWithValue("$reading", readingId);
        cmd.Parameters.AddWithValue("$minister", ministerId.ToString());
        cmd.Parameters.AddWithValue("$side", side.ToString());
        cmd.Parameters.AddWithValue("$now", Now());
        var id = (long)cmd.ExecuteScalar()!;
        RecordOppositionActionTx(conn, tx, factionId, OppositionActionKind.WhipCount, id);
        LogTx(conn, tx, LogKinds.StateChange, "opposition whipcount", $"{factionId} predicted {ministerId} votes {side} on reading {readingId}.", factionId);

        var reading = QueryReadingsTx(conn, tx, "id = $id", ("$id", readingId)).Single();
        var title = QueryBillsTx(conn, tx, "id = $id", ("$id", reading.BillId)).Single().Title;
        var count = QueryWhipCountsTx(conn, tx, "id = $id", ("$id", id)).Single();
        tx.Commit();
        return new WhipCountResult(null, count, title);
    }

    private static void ScoreWhipCountsTx(
        SqliteConnection conn, SqliteTransaction tx, Reading reading, Bill bill, IReadOnlyDictionary<ulong, VoteChoice> counted, GameConstants constants, ResolutionReport report)
    {
        foreach (var whip in QueryWhipCountsTx(conn, tx, "reading_id = $reading", ("$reading", reading.Id)))
        {
            VoteChoice? actual = counted.TryGetValue(whip.MinisterId, out var choice) ? choice : null;
            var change = Formulas.WhipCountChange(constants, whip.Side, actual);
            var applied = change == 0 ? 0 : PopularityChangeTx(conn, tx, whip.FactionId, change, constants.Popularity);
            report.WhipCounts.Add(new WhipCountReport(bill.Title, whip.FactionId, whip.MinisterId, whip.Side, actual, applied));
            LogTx(conn, tx, LogKinds.Resolution, "whip count",
                $"{whip.FactionId} predicted {whip.MinisterId} votes {whip.Side} on reading {reading.Id}; they voted {actual?.ToString() ?? "nothing"}: {applied:+0.##;-0.##;0}.",
                whip.FactionId, isPublic: true);
        }
    }
}
