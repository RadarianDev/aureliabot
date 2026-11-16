public static class Division
{
    public static string? Line(DatabaseService data, VoteSubject subject, long subjectId)
    {
        var votes = data.GetVotes(subject, subjectId);
        var followers = data.GetPartyFollowers(subject, subjectId);
        var groups = new List<string>();
        foreach (var choice in new[] { VoteChoice.Yes, VoteChoice.No, VoteChoice.Abstain })
        {
            var side = SideName(choice);
            var voters = string.Join(", ", votes.Where(v => v.Value == choice).Select(v => Display.Faction(data, v.Key)));
            var following = string.Join(", ", followers.Where(v => v.Value == choice).Select(v => Display.Faction(data, v.Key)));
            if (voters.Length > 0 && following.Length > 0)
                groups.Add(Texts.Get("assembly.division.groupAndFollowing", ("side", side), ("voters", voters), ("following", following)));
            else if (voters.Length > 0)
                groups.Add(Texts.Get("assembly.division.group", ("side", side), ("voters", voters)));
            else if (following.Length > 0)
                groups.Add(Texts.Get("assembly.division.groupFollowing", ("side", side), ("following", following)));
        }
        return groups.Count == 0 ? null : Texts.Get("assembly.division.line", ("groups", string.Join(Texts.Get("assembly.division.separator"), groups)));
    }

    private static string SideName(VoteChoice choice) => choice switch
    {
        VoteChoice.Yes => Texts.Get("assembly.division.ayes"),
        VoteChoice.No => Texts.Get("assembly.division.noes"),
        _ => Texts.Get("assembly.division.abstaining"),
    };
}
