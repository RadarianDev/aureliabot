public static class AgendaWording
{
    public static string Track(AgendaTrack track) => track switch
    {
        AgendaTrack.Economy => Texts.Get("secretAgenda.track.economy"),
        AgendaTrack.Reserve => Texts.Get("secretAgenda.track.reserve"),
        AgendaTrack.Army => Texts.Get("secretAgenda.track.army"),
        AgendaTrack.Discontent => Texts.Get("secretAgenda.track.discontent"),
        AgendaTrack.Favour => Texts.Get("secretAgenda.track.favour"),
        _ => Texts.Get("secretAgenda.track.threat"),
    };

    public static string Wish(AgendaWish wish) => wish.Direction == AgendaDirection.Up
        ? Texts.Get("secretAgenda.wish.up", ("track", Track(wish.Track)))
        : Texts.Get("secretAgenda.wish.down", ("track", Track(wish.Track)));

    public static string Wishes(AgendaDefinition agenda) =>
        string.Join(Texts.Get("secretAgenda.wish.and"), agenda.Wishes.Select(Wish));

    public static string Result(WishResult result) => (result.Wish.Direction, result.Met) switch
    {
        (AgendaDirection.Up, true) => Texts.Get("secretAgenda.result.rose", ("track", Track(result.Wish.Track))),
        (AgendaDirection.Up, false) => Texts.Get("secretAgenda.result.didNotRise", ("track", Track(result.Wish.Track))),
        (AgendaDirection.Down, true) => Texts.Get("secretAgenda.result.fell", ("track", Track(result.Wish.Track))),
        _ => Texts.Get("secretAgenda.result.didNotFall", ("track", Track(result.Wish.Track))),
    };

    public static string Dealt(AgendaDefinition agenda, AgendaRewardFile reward) =>
        Texts.Get("secretAgenda.dealt",
            ("agenda", agenda.Name), ("description", agenda.Description), ("wishes", Wishes(agenda)),
            ("perWish", Display.Number(reward.PerWish)), ("allBonus", Display.Number(reward.AllBonus)));

    public static string Future(AgendaDefinition agenda, AgendaRewardFile reward) =>
        Texts.Get("secretAgenda.future",
            ("agenda", agenda.Name), ("description", agenda.Description), ("wishes", Wishes(agenda)),
            ("perWish", Display.Number(reward.PerWish)), ("allBonus", Display.Number(reward.AllBonus)));

    public static string FutureBegins(AgendaDefinition agenda) =>
        Texts.Get("secretAgenda.futureBegins", ("agenda", agenda.Name), ("wishes", Wishes(agenda)));

    public static string Reveal(AgendaReveal reveal, string faction) =>
        Texts.Get("secretAgenda.reveal.line",
            ("faction", faction), ("agenda", reveal.AgendaName),
            ("results", string.Join(Texts.Get("secretAgenda.wish.separator"), reveal.Results.Select(Result))),
            ("reward", reveal.Reward > 0
                ? Texts.Get("secretAgenda.reveal.reward", ("popularity", Display.Number(reveal.Reward)))
                : Texts.Get("secretAgenda.reveal.noReward")));
}
