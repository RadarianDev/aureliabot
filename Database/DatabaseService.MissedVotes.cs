using Microsoft.Data.Sqlite;

public partial class DatabaseService
{
    private static void PenaliseMissedVotesTx(
        SqliteConnection conn, SqliteTransaction tx, IEnumerable<ulong> entitled, VoteSubject subject, long subjectId,
        string vote, GameConstants constants, ResolutionReport report)
    {
        var cast = GetVotesTx(conn, tx, subject, subjectId);
        var followers = PartyFollowersTx(conn, tx, subject, subjectId);
        foreach (var factionId in entitled.Distinct().Where(id => !cast.ContainsKey(id)).OrderBy(id => id))
        {
            var followed = followers.ContainsKey(factionId);
            var penalty = followed ? constants.Assembly.FollowedVotePenalty : constants.Assembly.MissedVotePenalty;
            if (penalty <= 0) continue;
            var change = PopularityChangeTx(conn, tx, factionId, -penalty, constants.Popularity);
            report.MissedVotes.Add(new MissedVoteReport(factionId, vote, change, followed));
            report.AddPrivate(factionId, followed
                ? Texts.Get("vote.followedCost", ("vote", vote), ("change", Display.Signed(change)))
                : Texts.Get("vote.missed", ("vote", vote), ("change", Display.Signed(change))));
            LogTx(conn, tx, LogKinds.Resolution, "missed vote",
                $"{factionId} {(followed ? "followed the party on" : "missed")} the vote on {vote}: {change:+0.##;-0.##;0}.", factionId);
        }
    }
}
