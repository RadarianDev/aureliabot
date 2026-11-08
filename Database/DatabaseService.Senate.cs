using System.Collections.Generic;
using System.Linq;
using Microsoft.Data.Sqlite;

public partial class DatabaseService
{
    private static Dictionary<ulong, int> SenateVotingSeatsTx(SqliteConnection conn, SqliteTransaction? tx) =>
        GetGameStateTx(conn, tx).SenateEstablished
            ? QueryFactionsTx(conn, tx, "status = 'Active' AND senate_seats > 0").ToDictionary(f => f.UserId, f => (int)f.SenateSeats)
            : new Dictionary<ulong, int>();

    private static void RunSenateElectionTx(SqliteConnection conn, SqliteTransaction tx, GameState state, GameConfig config, ResolutionReport report)
    {
        DropPendingJoinRequestsTx(conn, tx);
        var input = BuildElectionInputTx(conn, tx, config);
        var seatsByProvince = ElectionCalculator.SenateSeats(config);
        var result = ElectionCalculator.RunSenate(input, seatsByProvince);

        foreach (var province in result.Provinces)
        {
            var boroughs = province.BoroughSeatsByParty.Values.Sum();
            var seats = seatsByProvince.GetValueOrDefault(province.ProvinceId);
            if (boroughs > seats)
                report.Alerts.Add(Texts.Get("alerts.election.senateOverflow",
                    ("province", config.Province(province.ProvinceId)?.Name ?? province.ProvinceId), ("seats", seats), ("boroughs", boroughs)));
        }

        Exec(conn, tx, "UPDATE factions SET senate_seats = 0;");
        foreach (var (factionId, seats) in result.FactionSeats)
            Exec(conn, tx, "UPDATE factions SET senate_seats = $seats WHERE user_id = $id;", ("$id", factionId.ToString()), ("$seats", seats));

        var parties = PartySnapshotsTx(conn, tx);
        var names = parties.ToDictionary(p => p.Id, p => p.Name);
        var resolution = state.ResolutionNumber + 1;
        foreach (var province in result.Provinces)
            foreach (var (partyId, strength) in province.Strength)
                Exec(conn, tx, @"
                    INSERT INTO senate_results (resolution_number, province_id, party_id, party_name, strength, contested_seats, borough_seats)
                    VALUES ($resolution, $province, $party, $name, $strength, $contested, $boroughs);",
                    ("$resolution", resolution), ("$province", province.ProvinceId), ("$party", partyId),
                    ("$name", names.GetValueOrDefault(partyId, partyId.ToString())), ("$strength", strength),
                    ("$contested", province.ContestedSeatsByParty.GetValueOrDefault(partyId)),
                    ("$boroughs", province.BoroughSeatsByParty.GetValueOrDefault(partyId)));

        report.SenateElection = new ElectionReport(state.TermNumber, result, parties.Where(p => result.PartySeats.ContainsKey(p.Id)).ToList());
        LogTx(conn, tx, LogKinds.Resolution, "senate election",
            "Senate election: " + string.Join(", ", result.PartySeats.Select(kv => $"{names.GetValueOrDefault(kv.Key)} {kv.Value}")));
    }

    private static void ScheduleOverrideTx(SqliteConnection conn, SqliteTransaction tx, Reading reading, long resolution)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO readings (bill_id, sponsor_faction_id, kind, proposed_at, proposed_resolution, opens_resolution, due_resolution, status)
            VALUES ($bill, $faction, $kind, $now, $proposed, $opens, $due, 'Scheduled');";
        cmd.Parameters.AddWithValue("$bill", reading.BillId);
        cmd.Parameters.AddWithValue("$faction", reading.SponsorId.ToString());
        cmd.Parameters.AddWithValue("$kind", nameof(ReadingKind.Override));
        cmd.Parameters.AddWithValue("$now", Now());
        cmd.Parameters.AddWithValue("$proposed", resolution - 1);
        cmd.Parameters.AddWithValue("$opens", resolution);
        cmd.Parameters.AddWithValue("$due", resolution + 1);
        cmd.ExecuteNonQuery();
    }
}
