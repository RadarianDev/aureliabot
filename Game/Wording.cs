public static class Wording
{
    public static string Opinion(double opinion) => opinion switch
    {
        >= 0.6 => Texts.Get("wording.opinion.stronglyFor"),
        >= 0.2 => Texts.Get("wording.opinion.for"),
        > -0.2 => Texts.Get("wording.opinion.divided"),
        >= -0.6 => Texts.Get("wording.opinion.against"),
        _ => Texts.Get("wording.opinion.stronglyAgainst"),
    };

    public static string Salience(double salience) =>
        Math.Clamp((int)Math.Round(salience, MidpointRounding.AwayFromZero), 0, 5) switch
        {
            0 => Texts.Get("wording.salience.forgotten"),
            1 => Texts.Get("wording.salience.minor"),
            2 => Texts.Get("wording.salience.discussed"),
            3 => Texts.Get("wording.salience.muchDiscussed"),
            4 => Texts.Get("wording.salience.burning"),
            _ => Texts.Get("wording.salience.talkOfTheNation"),
        };

    public static string Favour(double favour) => favour switch
    {
        >= 70 => Texts.Get("wording.favour.content"),
        >= 50 => Texts.Get("wording.favour.calm"),
        >= 30 => Texts.Get("wording.favour.restless"),
        >= 10 => Texts.Get("wording.favour.resentful"),
        _ => Texts.Get("wording.favour.defiant"),
    };

    public static string Discontent(double discontent) => discontent switch
    {
        < 2 => Texts.Get("wording.discontent.quiet"),
        < 4 => Texts.Get("wording.discontent.grumbling"),
        < 6 => Texts.Get("wording.discontent.restless"),
        < 8 => Texts.Get("wording.discontent.unruly"),
        _ => Texts.Get("wording.discontent.rioting"),
    };

    public static string PollLine(string? provinceName, string issueTitle, double opinion, double salience)
    {
        var place = provinceName is null
            ? Texts.Get("wording.poll.kingdom")
            : Texts.Get("wording.poll.province", ("province", provinceName));
        var people = IsDivided(opinion)
            ? Texts.Get("wording.poll.divided")
            : Texts.Get("wording.poll.mood", ("mood", Opinion(opinion)));
        return Texts.Get("wording.poll.line", ("place", place), ("issue", issueTitle), ("salience", Salience(salience)), ("people", people));
    }

    private static bool IsDivided(double opinion) => opinion is < 0.2 and > -0.2;
}
