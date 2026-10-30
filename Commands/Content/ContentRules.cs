using System.IO.Compression;
using System.Text;

public sealed record ImportFile(string Name, string Yaml);

public sealed record ImportOutcome(
    string File, string? Id, TemplateKind? Kind, bool Replaced, TemplateStatus? PreviousStatus, IReadOnlyList<TemplateParseError> Errors)
{
    public bool Imported => Errors.Count == 0;
}

public sealed record EditOutcome(ImportOutcome Outcome, string OriginalId)
{
    public bool Copy => Outcome.Id is { } id && id != OriginalId;
}

public sealed record ContentResult(bool Ok, string Message);

public sealed record LiveTarget(TemplateKind Kind, string LiveId, Template Current, long? EventId);

public sealed record LiveEditResult(bool Ok, string Message, string? News);

public static class ContentRules
{
    public const int ModalLimit = 4000;
    private const int MaxArchiveEntries = 1000;
    private const long MaxEntryBytes = 1_000_000;

    public static EffectContext ImportContext(DatabaseService data, GameConfig config) =>
        TemplateCodec.ImportContext(data.BuildEffectContext(config));

    public static List<ImportOutcome> Import(DatabaseService data, EffectContext context, IEnumerable<ImportFile> files) =>
        files.Select(file => ImportOne(data, context, file)).ToList();

    public static ImportOutcome ImportOne(DatabaseService data, EffectContext context, ImportFile file)
    {
        var parsed = TemplateCodec.Parse(file.Yaml, file.Name, context);
        if (!parsed.IsValid) return new ImportOutcome(file.Name, null, null, false, null, parsed.Errors);
        var template = parsed.Template!;
        var saved = data.SaveTemplate(template.Id, template.Kind, file.Yaml);
        if (saved.Replaced) data.CancelScheduledEvents(template.Id);
        return new ImportOutcome(file.Name, template.Id, template.Kind, saved.Replaced, saved.PreviousStatus, []);
    }

    public static EditOutcome SaveEdit(DatabaseService data, EffectContext context, string originalId, string yaml) =>
        new(ImportOne(data, context, new ImportFile($"{originalId}.yaml", yaml)), originalId);

    public static string FormatImport(IReadOnlyList<ImportOutcome> outcomes, IReadOnlyList<string>? skipped = null)
    {
        var imported = outcomes.Count(o => o.Imported && !o.Replaced);
        var replaced = outcomes.Count(o => o.Imported && o.Replaced);
        var failed = outcomes.Count(o => !o.Imported);
        var sb = new StringBuilder();
        sb.AppendLine($"**{imported} imported, {replaced} replaced, {failed} with errors.**");
        foreach (var outcome in outcomes)
            sb.AppendLine(Describe(outcome));
        foreach (var line in skipped ?? [])
            sb.AppendLine($"- {line}");
        return sb.ToString().TrimEnd();
    }

    public static string Describe(ImportOutcome outcome)
    {
        if (!outcome.Imported)
            return $"- `{outcome.File}`: not imported.\n" + string.Join("\n", outcome.Errors.Select(e => $"  - {e}"));
        var what = $"`{outcome.Id}` ({TemplateCodec.KindName(outcome.Kind!.Value)})";
        if (!outcome.Replaced) return $"- {what}: imported as a Draft.";
        return outcome.PreviousStatus == TemplateStatus.Ready
            ? $"- {what}: replaced, and returned to Draft."
            : $"- {what}: replaced.";
    }

    public static IReadOnlyList<string> ReadinessProblems(Template template, Func<string, TemplateRow?> find)
    {
        var problems = new List<string>();
        foreach (var id in template.References.Distinct())
        {
            if (id == template.Id) continue;
            var row = find(id);
            var role = template is BillTemplate bill && bill.Issue == id || template is CrisisTemplate crisis && crisis.Issue == id
                ? "Its issue template"
                : "The template";
            if (row is null)
                problems.Add($"{role} `{id}` does not exist.");
            else if (row.Status != TemplateStatus.Ready)
                problems.Add($"{role} `{id}` ({TemplateCodec.KindName(row.Kind)}) is {row.Status}, not Ready.");
        }
        return problems;
    }

    public static (long? FireAt, string? Problem) ScheduleFireAt(GameState state, EventSchedule schedule)
    {
        if (EventScheduling.FireAtFor(state, schedule) is { } fireAt) return (fireAt, null);
        var turn = schedule.Season is { } season && schedule.Year is { } year ? GameCalendar.Label(season, year) : "its turn";
        return (null, state.Started
            ? $"Its scheduled turn, {turn}, has already started: it is now {state.Label}."
            : $"Its scheduled turn, {turn}, is before the game starts in {state.Label}.");
    }

    public static (Season Season, int Year) TurnOf(GameState state, long fireAt) =>
        GameCalendar.Add(state.Season, state.Year, (int)(fireAt - state.ResolutionNumber));

    public static string DescribeFireAt(GameState state, long fireAt)
    {
        if (!state.Started && fireAt == 0) return "at /gm start";
        var (season, year) = TurnOf(state, fireAt);
        return $"at the start of {GameCalendar.Label(season, year)}";
    }

    public static ContentResult MakeReady(DatabaseService data, GameConfig config, string id)
    {
        if (data.GetTemplate(id) is not { } row) return new ContentResult(false, $"There is no template `{id}`.");
        var parsed = TemplateCodec.Parse(row.Yaml, $"{id}.yaml", data.BuildEffectContext(config));
        if (!parsed.IsValid)
            return new ContentResult(false, $"`{id}` can't be marked Ready:\n" + string.Join("\n", parsed.Errors.Select(e => $"- {e}")));
        var template = parsed.Template!;

        var problems = ReadinessProblems(template, data.GetTemplate).ToList();
        long? fireAt = null;
        string? note = null;
        if (template is EventTemplate { Schedule: { } schedule })
        {
            if (data.HasScheduledEventFired(id))
            {
                note = "Its scheduled event has already fired, so the schedule does nothing more.";
            }
            else
            {
                var (at, problem) = ScheduleFireAt(data.GetGameState(), schedule);
                if (problem is not null) problems.Add(problem);
                fireAt = at;
            }
        }
        if (problems.Count > 0)
            return new ContentResult(false, $"`{id}` can't be marked Ready yet:\n" + string.Join("\n", problems.Select(p => $"- {p}")));

        data.SetTemplateStatus(id, TemplateStatus.Ready);
        var message = $"`{id}` ({TemplateCodec.KindName(template.Kind)}) is Ready.";
        if (template is EventTemplate ev)
        {
            if (fireAt is { } at)
            {
                data.ScheduleEvent(ev, at);
                message += $" Its event is queued to fire {DescribeFireAt(data.GetGameState(), at)}.";
            }
            else if (ev.Schedule is null && data.CancelScheduledEvents(id) > 0)
            {
                message += " It no longer has a schedule, so its scheduled event was cancelled.";
            }
            else if (ev.Schedule is null)
            {
                message += " It has no schedule: queue it with `/gm event queue`.";
            }
        }
        if (note is not null) message += " " + note;
        return new ContentResult(true, message);
    }

    public static ContentResult Remove(DatabaseService data, string id)
    {
        if (data.GetTemplate(id) is not { } row) return new ContentResult(false, $"There is no template `{id}`.");
        if (row.Status == TemplateStatus.Draft && row.TimesPosted == 0)
        {
            data.DeleteTemplate(id);
            return new ContentResult(true, $"Deleted `{id}`: it was a Draft that was never posted.");
        }
        data.SetTemplateStatus(id, TemplateStatus.Archived);
        var cancelled = row.Kind == TemplateKind.Event ? data.CancelScheduledEvents(id) : 0;
        return new ContentResult(true, $"Archived `{id}`: it is out of use, but kept for the log." +
            (cancelled > 0 ? " Its queued scheduled event was cancelled." : ""));
    }

    public static LiveTarget? FindLive(DatabaseService data, string id, out string? refusal)
    {
        refusal = null;
        var kind = data.GetTemplate(id)?.Kind;
        if (kind is TemplateKind.Project or TemplateKind.Crisis)
        {
            refusal = "Editing live projects and crises comes in a later stage.";
            return null;
        }

        if (kind is null or TemplateKind.Issue
            && (data.GetIssue(id) ?? data.GetAllIssues().FirstOrDefault(i => i.TemplateId == id)) is { } issue)
            return new LiveTarget(TemplateKind.Issue, issue.Id,
                new IssueTemplate(issue.TemplateId, issue.Title, issue.ForText, issue.AgainstText, issue.Opinion, issue.Salience), null);

        if (kind is null or TemplateKind.Bill
            && (data.GetBill(id) ?? data.GetBills().FirstOrDefault(b => b.TemplateId == id)) is { } bill)
            return new LiveTarget(TemplateKind.Bill, bill.Id,
                new BillTemplate(bill.TemplateId, bill.Title, bill.IssueId, bill.Lean, bill.Motion, bill.PassText, bill.ParsedPassEffects), null);

        if (kind is null or TemplateKind.Event
            && data.GetQueuedEvents().Where(e => e.TemplateId == id).MaxBy(e => e.Id) is { } queued)
            return new LiveTarget(TemplateKind.Event, queued.TemplateId,
                new EventTemplate(queued.TemplateId, queued.Title, queued.Text, queued.ParsedEffects, null), queued.Id);

        refusal = kind switch
        {
            TemplateKind.Issue => $"`{id}` has no live issue: it hasn't been opened.",
            TemplateKind.Bill => $"`{id}` has no live bill: it hasn't been made available.",
            TemplateKind.Event => $"`{id}` has no queued event to edit.",
            _ => $"There is nothing live called `{id}`.",
        };
        return null;
    }

    public static LiveEditResult ApplyLiveEdit(DatabaseService data, GameConfig config, LiveTarget target, string yaml)
    {
        var parsed = TemplateCodec.Parse(yaml, $"{target.Current.Id}.yaml", data.BuildEffectContext(config));
        if (!parsed.IsValid)
            return new LiveEditResult(false, "Nothing was changed:\n" + string.Join("\n", parsed.Errors.Select(e => $"- {e}")), null);
        var edited = parsed.Template!;
        if (edited.Kind != target.Kind)
            return new LiveEditResult(false, $"A live {TemplateCodec.KindName(target.Kind)} must stay a {TemplateCodec.KindName(target.Kind)}. Nothing was changed.", null);
        if (edited.Id != target.Current.Id)
            return new LiveEditResult(false, "The `id` of a live item can't change. Nothing was changed.", null);
        var problems = ReadinessProblems(edited, data.GetTemplate);
        if (problems.Count > 0)
            return new LiveEditResult(false, "Nothing was changed:\n" + string.Join("\n", problems.Select(p => $"- {p}")), null);

        switch (edited, target.Current)
        {
            case (IssueTemplate issue, IssueTemplate old):
            {
                if (!data.UpdateIssueLive(target.LiveId, issue.Title, issue.For, issue.Against, issue.Opinion, issue.Salience))
                    return new LiveEditResult(false, $"The live issue `{target.LiveId}` is gone. Nothing was changed.", null);
                var news = issue.For != old.For || issue.Against != old.Against
                    ? $"## {issue.Title}\nThe question now reads:\nFor: {issue.For}\nAgainst: {issue.Against}"
                    : null;
                return new LiveEditResult(true, $"Edited the live issue `{target.LiveId}`." +
                    (news is null ? "" : " The new For and Against text is posted to #news."), news);
            }
            case (BillTemplate bill, BillTemplate old):
            {
                if (bill.Issue != old.Issue || bill.Lean != old.Lean)
                    return new LiveEditResult(false, "A live bill keeps its `issue` and `lean`. Nothing was changed.", null);
                if (!data.UpdateBillLive(target.LiveId, bill.Title, bill.Motion, bill.PassText, bill.PassEffects))
                    return new LiveEditResult(false,
                        $"The live bill `{target.LiveId}` can only be edited while it is available or resting after a rejection. Nothing was changed.", null);
                var news = bill.Motion != old.Motion ? $"## {bill.Title}\nThe motion now reads:\n> {bill.Motion}" : null;
                return new LiveEditResult(true, $"Edited the live bill `{target.LiveId}`." +
                    (news is null ? "" : " The new motion is posted to #news."), news);
            }
            case (EventTemplate ev, EventTemplate):
            {
                if (target.EventId is not { } eventId || !data.UpdateQueuedEvent(eventId, ev.Title, ev.Text, ev.Effects))
                    return new LiveEditResult(false, "That event is no longer queued. Nothing was changed.", null);
                return new LiveEditResult(true, $"Edited queued event {eventId} from `{ev.Id}`." +
                    (ev.Schedule is null ? "" : " A queued copy has no schedule: edit the template and mark it Ready again to move it."), null);
            }
            default:
                return new LiveEditResult(false, "That kind of live item can't be edited yet.", null);
        }
    }

    public static byte[] Export(IEnumerable<TemplateRow> rows)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var row in rows)
            {
                var entry = zip.CreateEntry(EntryName(row), CompressionLevel.Optimal);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write(row.Yaml);
            }
        }
        return buffer.ToArray();
    }

    public static string EntryName(TemplateRow row) => $"{TemplateCodec.FolderOf(row.Kind)}/{row.Id}.yaml";

    public static List<ImportFile> ReadArchive(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var files = new List<ImportFile>();
        foreach (var entry in zip.Entries.Take(MaxArchiveEntries))
        {
            if (!IsYamlName(entry.FullName) || entry.Length > MaxEntryBytes) continue;
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            files.Add(new ImportFile(entry.FullName, reader.ReadToEnd()));
        }
        return files;
    }

    public static bool IsYamlName(string name) =>
        name.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".yml", StringComparison.OrdinalIgnoreCase);

    public static string DecodeText(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
