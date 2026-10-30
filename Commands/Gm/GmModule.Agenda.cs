using Discord;
using Discord.Interactions;

public static class GmAgenda
{
    private const int QuestionPreview = 80;

    public static string Build(DatabaseService data, GameConfig config)
    {
        var state = data.GetGameState();
        var lines = new List<string> { Texts.Get("agenda.header", ("when", Display.NextResolution(state))) };
        Section(lines, Texts.Get("agenda.events.heading"), Events(data, state));
        Section(lines, Texts.Get("agenda.crises.heading"), Crises(data, state), Texts.Get("agenda.crises.none"));
        Section(lines, Texts.Get("agenda.questions.heading"), Questions(data, state), Texts.Get("agenda.questions.none"));
        Section(lines, Texts.Get("agenda.vacant.heading"), Vacancies(data), Texts.Get("agenda.vacant.none"));
        Section(lines, Texts.Get("agenda.joins.heading"), Joins(data), Texts.Get("agenda.joins.none"));
        Section(lines, Texts.Get("agenda.scheduled.heading"), Scheduled(data, state), Texts.Get("agenda.scheduled.none"));
        Section(lines, Texts.Get("agenda.votes.heading"), Votes(data, state, config.Constants), Texts.Get("agenda.votes.none"));
        return string.Join("\n", lines);
    }

    private static void Section(List<string> lines, string heading, List<string> items, string? empty = null)
    {
        lines.Add("");
        lines.Add(heading);
        if (items.Count == 0 && empty is not null) lines.Add(empty);
        lines.AddRange(items);
    }

    public static long NextFiring(GameState state) => state.Started ? state.ResolutionNumber + 1 : 0;

    private static List<string> Events(DatabaseService data, GameState state)
    {
        var queued = data.GetQueuedEvents().OrderBy(e => e.FireAtResolution).ThenBy(e => e.Id).ToList();
        var lines = new List<string>();
        if (queued.All(e => e.FireAtResolution > NextFiring(state)))
            lines.Add(state.Started ? Texts.Get("agenda.events.emptyNext") : Texts.Get("agenda.events.emptyStart"));
        lines.AddRange(queued.Select(e => Texts.Get("agenda.events.line",
            ("title", e.Title), ("template", e.TemplateId), ("id", e.Id), ("when", ContentRules.DescribeFireAt(state, e.FireAtResolution)))));
        return lines;
    }

    private static string Who(DatabaseService data, ulong? factionId) =>
        factionId is { } id ? Display.Faction(data, id) : Texts.Get("agenda.nobody");

    private static List<string> Crises(DatabaseService data, GameState state) =>
        data.GetCrises(CrisisStatus.Live)
            .Select(c => (Crisis: c, Minister: data.ResponsibleMinister(c.Ministry)))
            .Where(x => x.Minister is null || x.Crisis.ChosenAnswer is null || x.Crisis.ResponderId != x.Minister)
            .Select(x => Texts.Get("agenda.crises.line", ("crisis", x.Crisis.Title), ("id", x.Crisis.Id), ("ministry", x.Crisis.Ministry.DisplayName()),
                ("minister", Who(data, x.Minister)), ("due", GovernmentPublisher.Deadline(state, x.Crisis.DueResolution))))
            .ToList();

    private static List<string> Questions(DatabaseService data, GameState state) =>
        data.GetOpenQuestions()
            .Select(q => Texts.Get("agenda.questions.line", ("asker", Display.Faction(data, q.AskerId)), ("ministry", q.Ministry.DisplayName()),
                ("minister", Who(data, data.ResponsibleMinister(q.Ministry))), ("due", GovernmentPublisher.Deadline(state, q.DueResolution)),
                ("text", Display.Truncate(q.Text.ReplaceLineEndings(" "), QuestionPreview))))
            .ToList();

    private static List<string> Vacancies(DatabaseService data)
    {
        var cabinet = data.GetSittingCabinet();
        var chancellor = cabinet?.ChancellorId;
        var lines = new List<string>();
        if (chancellor is null) lines.Add(Texts.Get("agenda.vacant.chancellor"));
        foreach (var ministry in GameEnumExtensions.CabinetMinistries.Where(m => cabinet?.HolderOf(m) is null))
            lines.Add(chancellor is { } c
                ? Texts.Get("agenda.vacant.covered", ("ministry", ministry.DisplayName()), ("chancellor", Display.Faction(data, c)))
                : Texts.Get("agenda.vacant.uncovered", ("ministry", ministry.DisplayName())));
        return lines;
    }

    private static List<string> Joins(DatabaseService data) =>
        data.GetPendingJoinRequests()
            .Select(r =>
            {
                var answers = data.GetJoinAnswers(r.Id);
                return Texts.Get("agenda.joins.line", ("faction", Display.Faction(data, r.FactionId)),
                    ("party", data.GetParty(r.PartyId) is { } party ? Display.Party(party) : r.PartyId.ToString()),
                    ("accepts", answers.Count(a => a.Value)), ("declines", answers.Count(a => !a.Value)), ("members", data.GetPartyMembers(r.PartyId).Count));
            })
            .ToList();

    private static string BillTitle(DatabaseService data, string billId) => data.GetBill(billId)?.Title ?? billId;

    private static List<string> Scheduled(DatabaseService data, GameState state) =>
        data.GetReadings(ReadingStatus.Scheduled)
            .Select(r =>
            {
                var bill = BillTitle(data, r.BillId);
                var sponsor = Display.Faction(data, r.SponsorId);
                var opens = GovernmentPublisher.Deadline(state, r.OpensResolution);
                return r.Kind == ReadingKind.Government
                    ? Texts.Get("agenda.scheduled.government", ("bill", bill), ("sponsor", sponsor), ("opens", opens))
                    : Texts.Get("agenda.scheduled.private", ("bill", bill), ("sponsor", sponsor), ("opens", opens));
            })
            .ToList();

    private static string Tally(DatabaseService data, VoteSubject subject, long id)
    {
        var tally = data.TallyOf(subject, id);
        return Texts.Get("agenda.votes.tally", ("yes", tally.YesSeats), ("no", tally.NoSeats), ("abstain", tally.AbstainSeats));
    }

    private static List<string> Votes(DatabaseService data, GameState state, GameConstants constants)
    {
        var lines = new List<string>();
        lines.AddRange(data.GetReadings(ReadingStatus.Open).Select(r => Texts.Get("agenda.votes.reading",
            ("bill", BillTitle(data, r.BillId)), ("tally", Tally(data, VoteSubject.Reading, r.Id)), ("due", GovernmentPublisher.Deadline(state, r.DueResolution)))));
        lines.AddRange(data.GetMotions(NoConfidenceStatus.Voting).Select(m => Texts.Get("agenda.votes.noConfidence",
            ("tabler", Display.Faction(data, m.TablerId)), ("tally", Tally(data, VoteSubject.NoConfidence, m.Id)),
            ("due", GovernmentPublisher.Deadline(state, m.DueResolution)))));
        lines.AddRange(data.GetMotions(NoConfidenceStatus.Gathering).Select(m =>
        {
            var support = data.GetMotionSupport(m.Id, constants);
            return Texts.Get("agenda.votes.gathering", ("tabler", Display.Faction(data, m.TablerId)),
                ("supporters", support.Supporters), ("threshold", support.Threshold), ("until", GovernmentPublisher.Deadline(state, m.GatherUntilResolution)));
        }));
        lines.AddRange(data.GetCabinetsInVote().Select(c => Texts.Get("agenda.votes.cabinet",
            ("chancellor", Who(data, c.ChancellorId)), ("tally", Tally(data, VoteSubject.Cabinet, c.Id)),
            ("due", GovernmentPublisher.Deadline(state, c.DueResolution)))));
        return lines;
    }
}

public partial class GmModule
{
    [SlashCommand("agenda", "Privately show what needs the GM before the next resolution")]
    public async Task AgendaAsync()
    {
        await DeferAsync(ephemeral: true);
        foreach (var chunk in Publisher.Chunk(GmAgenda.Build(_data, _constants.Config)))
            await FollowupAsync(chunk, ephemeral: true, allowedMentions: AllowedMentions.None);
    }
}
