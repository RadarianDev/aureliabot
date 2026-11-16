using Microsoft.Data.Sqlite;

public partial class DatabaseService
{
    private static void PenaliseMissedVotesTx(
        SqliteConnection conn, SqliteTransaction tx, IEnumerable<ulong> entitled, IReadOnlyDictionary<ulong, VoteChoice> counted,
        string vote, GameConstants constants, ResolutionReport report)
    {
        if (constants.Assembly.MissedVotePenalty <= 0) return;
        foreach (var factionId in entitled.Distinct().Where(id => !counted.ContainsKey(id)).OrderBy(id => id))
        {
            var change = PopularityChangeTx(conn, tx, factionId, -constants.Assembly.MissedVotePenalty, constants.Popularity);
            report.MissedVotes.Add(new MissedVoteReport(factionId, vote, change));
            report.AddPrivate(factionId, Texts.Get("vote.missed", ("vote", vote), ("change", Display.Signed(change))));
            LogTx(conn, tx, LogKinds.Resolution, "missed vote", $"{factionId} missed the vote on {vote}: {change:+0.##;-0.##;0}.", factionId);
        }
    }
}
