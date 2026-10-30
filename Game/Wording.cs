public static class Wording
{
    public static string Opinion(double opinion) => opinion switch
    {
        >= 0.6 => "strongly for",
        >= 0.2 => "for",
        > -0.2 => "divided",
        >= -0.6 => "against",
        _ => "strongly against",
    };

    public static string Salience(double salience) =>
        Math.Clamp((int)Math.Round(salience, MidpointRounding.AwayFromZero), 0, 5) switch
        {
            0 => "forgotten",
            1 => "a minor matter",
            2 => "discussed",
            3 => "much discussed",
            4 => "a burning question",
            _ => "the talk of the nation",
        };

    public static string Favour(double favour) => favour switch
    {
        >= 70 => "content",
        >= 50 => "calm",
        >= 30 => "restless",
        >= 10 => "resentful",
        _ => "defiant",
    };

    public static string Discontent(double discontent) => discontent switch
    {
        < 2 => "quiet",
        < 4 => "grumbling",
        < 6 => "restless",
        < 8 => "unruly",
        _ => "rioting",
    };

    public static string PollLine(string? provinceName, string issueTitle, double opinion, double salience)
    {
        var place = provinceName is null ? "Across the kingdom" : $"In {provinceName}";
        var mood = Opinion(opinion);
        var people = mood == "divided" ? "the people are *divided* on it" : $"the people are *{mood}* it";
        return $"{place}, {issueTitle} is *{Salience(salience)}*, and {people}.";
    }
}
