public abstract record Template(string Id, string Title)
{
    public abstract TemplateKind Kind { get; }

    public abstract IEnumerable<string> References { get; }

    public string? Summary { get; init; }
}

public sealed record IssueTemplate(string Id, string Title, string For, string Against, double Opinion, double Salience)
    : Template(Id, Title)
{
    public override TemplateKind Kind => TemplateKind.Issue;

    public override IEnumerable<string> References => [];
}

public sealed record BillTemplate(
    string Id, string Title, string Issue, Lean Lean, string Motion, string PassText, IReadOnlyList<Effect> PassEffects)
    : Template(Id, Title)
{
    public override TemplateKind Kind => TemplateKind.Bill;

    public override IEnumerable<string> References => EffectParser.ReferencedTemplates(PassEffects).Prepend(Issue);
}

public sealed record ProjectBonus(Season? BySeason, int? ByYear, int? Within, string Text, IReadOnlyList<Effect> Effects);

public sealed record ProjectTemplate(
    string Id,
    string Title,
    Ministry Purse,
    string? Province,
    decimal Price,
    int Term,
    IReadOnlyList<Effect> Effects,
    ProjectBonus? Bonus,
    bool Ministerial,
    string? Requires)
    : Template(Id, Title)
{
    public override TemplateKind Kind => TemplateKind.Project;

    public override IEnumerable<string> References =>
        EffectParser.ReferencedTemplates(Effects.Concat(Bonus?.Effects ?? []));

    public decimal Pace => Term > 0 ? Price / Term : Price;
}

public sealed record EventSchedule(Season? Season, int? Year, int? Delay);

public sealed record EventTemplate(string Id, string Title, string Text, IReadOnlyList<Effect> Effects, EventSchedule? Schedule)
    : Template(Id, Title)
{
    public override TemplateKind Kind => TemplateKind.Event;

    public override IEnumerable<string> References => EffectParser.ReferencedTemplates(Effects);
}

public sealed record CrisisAnswer(Lean Lean, string Label, decimal Price, string Text, IReadOnlyList<Effect> Effects);

public sealed record CrisisOutcomeText(string Text, IReadOnlyList<Effect> Effects);

public sealed record CrisisTemplate(
    string Id,
    string Title,
    Ministry Ministry,
    string Province,
    string Issue,
    IReadOnlyList<CrisisAnswer> Answers,
    CrisisOutcomeText? Complication,
    CrisisOutcomeText Default)
    : Template(Id, Title)
{
    public override TemplateKind Kind => TemplateKind.Crisis;

    public override IEnumerable<string> References =>
        EffectParser.ReferencedTemplates(Answers.SelectMany(a => a.Effects)
                .Concat(Complication?.Effects ?? [])
                .Concat(Default.Effects))
            .Prepend(Issue);
}

public static class EventScheduling
{
    public static long FireAtForDelay(GameState state, int delay) =>
        state.Started ? state.ResolutionNumber + 1 + Math.Max(0, delay) : Math.Max(0, delay);

    public static long? FireAtFor(GameState state, EventSchedule schedule)
    {
        if (schedule.Delay is { } delay) return FireAtForDelay(state, delay);
        if (schedule.Season is not { } season || schedule.Year is not { } year) return null;
        var fireAt = GameCalendar.ResolutionForTurn(state, season, year);
        var earliest = state.Started ? state.ResolutionNumber + 1 : 0;
        return fireAt < earliest ? null : fireAt;
    }
}

public sealed record TemplateParseError(string File, int? Line, string Message)
{
    public override string ToString() => Line is { } line ? $"{File}, line {line}: {Message}" : $"{File}: {Message}";
}

public sealed record TemplateParseResult(Template? Template, IReadOnlyList<TemplateParseError> Errors)
{
    public bool IsValid => Template is not null && Errors.Count == 0;
}
