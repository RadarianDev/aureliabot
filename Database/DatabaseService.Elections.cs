using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

public record ProvinceResultRow(int TermNumber, string ProvinceId, long PartyId, string PartyName, double Strength, int Seats, bool Winner = false);

public partial class DatabaseService
{
    public ElectionInput BuildElectionInput(GameConfig config)
    {
        using var conn = Open();
        return BuildElectionInputTx(conn, null, config);
    }

    private static ElectionInput BuildElectionInputTx(SqliteConnection conn, SqliteTransaction? tx, GameConfig config)
    {
        var c = config.Constants;
        var state = GetGameStateTx(conn, tx);
        var snapshot = LoadOpinionSnapshotTx(conn, tx, config);
        var active = QueryFactionsTx(conn, tx, "status = 'Active' AND party_id IS NOT NULL");
        var standing = active.Select(f => f.PartyId!.Value).Distinct().OrderBy(id => id).ToList();
        var issues = QueryIssuesTx(conn, tx, "status = 'Open'");

        return new ElectionInput(
            state.Seats,
            snapshot.Provinces.Select(p => new ElectionProvince(p.Id, p.Population)).ToList(),
            standing.Select(id => new ElectionParty(id, GetProgrammeTx(conn, tx, id))).ToList(),
            active.Select(f => new ElectionFaction(f.UserId, f.PartyId!.Value, f.Popularity, f.HomeProvinceId ?? "",
                GetRegionalPopularityTx(conn, tx, f.UserId, c.Popularity))).ToList(),
            issues.Select(i => new ElectionIssue(i.Id, i.Salience,
                snapshot.Provinces.ToDictionary(p => p.Id, p => snapshot.Provincial(i.Id, p.Id)))).ToList(),
            c.Election,
            c.StrongStancePenalty,
            state.SenateEstablished,
            QueryFactionsTx(conn, tx, "status = 'Active' AND party_id IS NULL")
                .Select(f => new ElectionIndependent(f.UserId, f.HomeProvinceId ?? "")).ToList());
    }

    public List<ElectionPartySnapshot> GetPartySnapshots() =>
        GetAllParties().Select(p => new ElectionPartySnapshot(p.Id, p.Name, p.Colour, p.EmojiId)).ToList();

    private static void RunElectionTx(SqliteConnection conn, SqliteTransaction tx, GameState state, GameConfig config, ResolutionReport report)
    {
        var input = BuildElectionInputTx(conn, tx, config);
        var result = ElectionCalculator.Run(input);
        if (!input.SenateEstablished && input.Seats < input.Factions.Count)
            report.Alerts.Add(Texts.Get("alerts.election.tooFewSeats", ("seats", input.Seats), ("boroughs", input.Factions.Count)));
        var term = state.TermNumber + 1;

        Exec(conn, tx, "UPDATE factions SET seats = 0;");
        foreach (var (factionId, seats) in result.FactionSeats)
            Exec(conn, tx, "UPDATE factions SET seats = $seats WHERE user_id = $id;", ("$id", factionId.ToString()), ("$seats", seats));

        var parties = PartySnapshotsTx(conn, tx);
        var names = parties.ToDictionary(p => p.Id, p => p.Name);

        Exec(conn, tx, "DELETE FROM province_results WHERE term_number = $term;", ("$term", term));
        foreach (var province in result.Provinces)
            foreach (var (partyId, strength) in province.Strength)
            {
                var seats = province.ContestedSeatsByParty.GetValueOrDefault(partyId) + province.BoroughSeatsByParty.GetValueOrDefault(partyId);
                Exec(conn, tx, @"
                    INSERT INTO province_results (term_number, province_id, party_id, party_name, strength, seats, winner)
                    VALUES ($term, $province, $party, $name, $strength, $seats, $winner);",
                    ("$term", term), ("$province", province.ProvinceId), ("$party", partyId),
                    ("$name", names.GetValueOrDefault(partyId, partyId.ToString())), ("$strength", strength), ("$seats", seats),
                    ("$winner", province.WinnerPartyId == partyId ? 1 : 0));
            }

        Exec(conn, tx, "UPDATE cabinets SET status = 'Caretaker' WHERE kind = 'Sitting' AND status = 'Approved';");
        report.Election = new ElectionReport(term, result, parties.Where(p => result.PartySeats.ContainsKey(p.Id) || result.VoteShare.ContainsKey(p.Id)).ToList());
        LogTx(conn, tx, LogKinds.Resolution, "election",
            $"Election for term {term}: " + string.Join(", ", result.PartySeats.Select(kv => $"{names.GetValueOrDefault(kv.Key)} {kv.Value}")));
    }

    private static List<ElectionPartySnapshot> PartySnapshotsTx(SqliteConnection conn, SqliteTransaction tx)
    {
        var parties = new List<ElectionPartySnapshot>();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT id, name, colour, emoji_id FROM parties ORDER BY id;";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            parties.Add(new ElectionPartySnapshot(r.GetInt64(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : ulong.Parse(r.GetString(3))));
        return parties;
    }

    private static void CancelAtElectionTx(SqliteConnection conn, SqliteTransaction tx, ResolutionReport report)
    {
        var readings = new List<(long Id, string BillId, string Title)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
                SELECT r.id, r.bill_id, b.title FROM readings r JOIN bills b ON b.id = r.bill_id
                WHERE r.status IN ('Scheduled', 'Open');";
            using var r = cmd.ExecuteReader();
            while (r.Read()) readings.Add((r.GetInt64(0), r.GetString(1), r.GetString(2)));
        }
        foreach (var reading in readings)
        {
            Exec(conn, tx, "UPDATE readings SET status = 'Cancelled' WHERE id = $id;", ("$id", reading.Id));
            Exec(conn, tx, "UPDATE bills SET status = 'Available', cooldown_until = NULL WHERE id = $bill AND status = 'Voting';", ("$bill", reading.BillId));
            Exec(conn, tx, "UPDATE opposition_actions SET cancelled = 1 WHERE kind = 'Bill' AND subject_id = $id;", ("$id", reading.Id));
            report.ReadingsCancelled.Add(new ReadingCancelledReport(reading.Id, reading.BillId, reading.Title, Texts.Get("data.election.readingCancelled")));
        }
        using (var motions = conn.CreateCommand())
        {
            motions.Transaction = tx;
            motions.CommandText = "SELECT id, tabler_faction_id FROM no_confidence WHERE status IN ('Gathering', 'Voting');";
            using var r = motions.ExecuteReader();
            while (r.Read())
                report.NoConfidenceMotions.Add(new NoConfidenceReport(r.GetInt64(0), ulong.Parse(r.GetString(1)), NoConfidenceStatus.Cancelled, 0, 0, 0));
        }
        Exec(conn, tx, "UPDATE no_confidence SET status = 'Cancelled' WHERE status IN ('Gathering', 'Voting');");
        Exec(conn, tx, "UPDATE cabinets SET status = 'Cancelled' WHERE kind = 'Nominated' AND status IN ('Forming', 'Voting');");
        DropPendingJoinRequestsTx(conn, tx);
    }

    public List<ProvinceResultRow> GetProvinceResults(int? term = null)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT term_number, province_id, party_id, party_name, strength, seats, winner FROM province_results
            WHERE term_number = COALESCE($term, (SELECT MAX(term_number) FROM province_results))
            ORDER BY province_id, strength DESC;";
        cmd.Parameters.AddWithValue("$term", (object?)term ?? DBNull.Value);
        var list = new List<ProvinceResultRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new ProvinceResultRow(r.GetInt32(0), r.GetString(1), r.GetInt64(2), r.GetString(3), r.GetDouble(4), r.GetInt32(5), r.GetInt64(6) != 0));
        return list;
    }
}
