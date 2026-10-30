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

public sealed record LiveTarget(TemplateKind Kind, string LiveId, Template Current, long? RowId);

public sealed record LiveEditResult(bool Ok, string Message, string? News, long? AmendedCrisisId = null);

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

    public static EditOutcome SaveEdit(DatabaseService data, EffectContext context, string originalId, string yaml)
    {
        var file = $"{originalId}.yaml";
        if (TemplateCodec.Parse(yaml, file, context).Template is { } parsed && parsed.Id != originalId && data.GetTemplate(parsed.Id) is not null)
            return new(new ImportOutcome(file, parsed.Id, parsed.Kind, false, null,
                [new TemplateParseError(file, null, Texts.Get("content.import.idTaken", ("id", parsed.Id)))]), originalId);
        return new(ImportOne(data, context, new ImportFile(file, yaml)), originalId);
    }

    public static string FormatImport(IReadOnlyList<ImportOutcome> outcomes, IReadOnlyList<string>? skipped = null)
    {
        var imported = outcomes.Count(o => o.Imported && !o.Replaced);
        var replaced = outcomes.Count(o => o.Imported && o.Replaced);
        var failed = outcomes.Count(o => !o.Imported);
        var sb = new StringBuilder();
        sb.AppendLine(Texts.Get("content.import.summary", ("imported", imported), ("replaced", replaced), ("failed", failed)));
        foreach (var outcome in outcomes)
            sb.AppendLine(Describe(outcome));
        foreach (var line in skipped ?? [])
            sb.AppendLine(Bullet(line));
        return sb.ToString().TrimEnd();
    }

    public static string Describe(ImportOutcome outcome)
    {
        if (!outcome.Imported)
            return Texts.Get("content.import.failed", ("file", outcome.File)) + "\n"
                + string.Join("\n", outcome.Errors.Select(e => Texts.Get("content.subBullet", ("item", e))));
        var what = Texts.Get("content.import.what", ("id", outcome.Id), ("kind", TemplateCodec.KindName(outcome.Kind!.Value)));
        if (!outcome.Replaced) return Texts.Get("content.import.imported", ("what", what));
        if (outcome.PreviousStatus != TemplateStatus.Ready) return Texts.Get("content.import.replaced", ("what", what));
        return outcome.Kind == TemplateKind.Event
            ? Texts.Get("content.import.replacedEventToDraft", ("what", what))
            : Texts.Get("content.import.replacedToDraft", ("what", what));
    }

    public static string Bullet(object item) => Texts.Get("content.bullet", ("item", item));

    public static string BulletList(IEnumerable<object> items) => string.Join("\n", items.Select(Bullet));

    public static string TimesPosted(int count) =>
        count == 1 ? Texts.Get("content.posted.once") : Texts.Get("content.posted.times", ("count", count));

    public static IReadOnlyList<string> ReadinessProblems(Template template, Func<string, TemplateRow?> find)
    {
        var problems = new List<string>();
        foreach (var id in template.References.Distinct())
        {
            if (id == template.Id) continue;
            var row = find(id);
            var role = template is BillTemplate bill && bill.Issue == id || template is CrisisTemplate crisis && crisis.Issue == id
                ? Texts.Get("content.readiness.issueRole")
                : Texts.Get("content.readiness.templateRole");
            if (row is null)
                problems.Add(Texts.Get("content.readiness.missing", ("role", role), ("id", id)));
            else if (row.Status != TemplateStatus.Ready)
                problems.Add(Texts.Get("content.readiness.notReady", ("role", role), ("id", id), ("kind", TemplateCodec.KindName(row.Kind)), ("status", row.Status)));
        }
        return problems;
    }

    public static (long? FireAt, string? Problem) ScheduleFireAt(GameState state, EventSchedule schedule)
    {
        if (EventScheduling.FireAtFor(state, schedule) is { } fireAt) return (fireAt, null);
        var turn = schedule.Season is { } season && schedule.Year is { } year
            ? GameCalendar.Label(season, year)
            : Texts.Get("content.schedule.itsTurn");
        return (null, state.Started
            ? Texts.Get("content.schedule.started", ("turn", turn), ("now", state.Label))
            : Texts.Get("content.schedule.beforeStart", ("turn", turn), ("start", state.Label)));
    }

    public static (Season Season, int Year) TurnOf(GameState state, long fireAt) =>
        GameCalendar.Add(state.Season, state.Year, (int)(fireAt - state.ResolutionNumber));

    public static string DescribeFireAt(GameState state, long fireAt)
    {
        if (!state.Started && fireAt == 0) return Texts.Get("content.schedule.atGameStart");
        var (season, year) = TurnOf(state, fireAt);
        return Texts.Get("content.schedule.atTurn", ("turn", GameCalendar.Label(season, year)));
    }

    public static ContentResult MakeReady(DatabaseService data, GameConfig config, string id)
    {
        if (data.GetTemplate(id) is not { } row) return new ContentResult(false, Texts.Get("content.noTemplate", ("id", id)));
        var parsed = TemplateCodec.Parse(row.Yaml, $"{id}.yaml", data.BuildEffectContext(config));
        if (!parsed.IsValid)
            return new ContentResult(false, Texts.Get("content.ready.parseErrors", ("id", id)) + "\n" + BulletList(parsed.Errors));
        var template = parsed.Template!;

        var problems = ReadinessProblems(template, data.GetTemplate).ToList();
        long? fireAt = null;
        string? note = null;
        if (template is EventTemplate { Schedule: { } schedule })
        {
            if (data.HasScheduledEventFired(id))
            {
                note = Texts.Get("content.ready.alreadyFired");
            }
            else
            {
                var (at, problem) = ScheduleFireAt(data.GetGameState(), schedule);
                if (problem is not null) problems.Add(problem);
                fireAt = at;
            }
        }
        if (problems.Count > 0)
            return new ContentResult(false, Texts.Get("content.ready.problems", ("id", id)) + "\n" + BulletList(problems));

        data.SetTemplateStatus(id, TemplateStatus.Ready);
        var message = Texts.Get("content.ready.done", ("id", id), ("kind", TemplateCodec.KindName(template.Kind)));
        if (template is EventTemplate ev)
        {
            if (fireAt is { } at)
            {
                data.ScheduleEvent(ev, at);
                message += " " + Texts.Get("content.ready.queued", ("when", DescribeFireAt(data.GetGameState(), at)));
            }
            else if (ev.Schedule is null && data.CancelScheduledEvents(id) > 0)
            {
                message += " " + Texts.Get("content.ready.scheduleCancelled");
            }
            else if (ev.Schedule is null)
            {
                message += " " + Texts.Get("content.ready.noSchedule");
            }
        }
        if (note is not null) message += " " + note;
        return new ContentResult(true, message);
    }

    public static ContentResult Remove(DatabaseService data, string id)
    {
        if (data.GetTemplate(id) is not { } row) return new ContentResult(false, Texts.Get("content.noTemplate", ("id", id)));
        if (row.Status == TemplateStatus.Draft && row.TimesPosted == 0)
        {
            data.DeleteTemplate(id);
            return new ContentResult(true, Texts.Get("content.remove.deleted", ("id", id)));
        }
        data.SetTemplateStatus(id, TemplateStatus.Archived);
        var cancelled = row.Kind == TemplateKind.Event ? data.CancelScheduledEvents(id) : 0;
        return new ContentResult(true, cancelled > 0
            ? Texts.Get("content.remove.archivedCancelled", ("id", id))
            : Texts.Get("content.remove.archived", ("id", id)));
    }

    public static LiveTarget? FindLive(DatabaseService data, string id, out string? refusal)
    {
        refusal = null;
        var kind = data.GetTemplate(id)?.Kind;

        if (kind is TemplateKind.Crisis)
        {
            var live = data.GetCrises(CrisisStatus.Live).Where(c => c.TemplateId == id).ToList();
            if (live.FirstOrDefault(c => c.ChosenAnswer is null) is { } open)
                return CrisisTarget(open);
            refusal = live.Count == 0
                ? Texts.Get("liveEdit.noCrisis", ("id", id))
                : Texts.Get("gmCrisis.edit.locked", ("crisis", live[0].Title));
            return null;
        }

        if (kind is TemplateKind.Project)
        {
            if (data.GetLiveProjects().FirstOrDefault(p => p.TemplateId == id) is { } project)
                return ProjectTarget(project, data.GetParsedTemplate(id) as ProjectTemplate);
            refusal = Texts.Get("liveEdit.noProject", ("id", id));
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
            TemplateKind.Issue => Texts.Get("content.live.noIssue", ("id", id)),
            TemplateKind.Bill => Texts.Get("content.live.noBill", ("id", id)),
            TemplateKind.Event => Texts.Get("content.live.noEvent", ("id", id)),
            _ => Texts.Get("content.live.nothing", ("id", id)),
        };
        return null;
    }

    public static LiveTarget? FindLiveCrisis(DatabaseService data, long crisisId, out string? refusal)
    {
        refusal = data.GetCrisis(crisisId) switch
        {
            null => Texts.Get("crisis.refusal.gone"),
            { Status: not CrisisStatus.Live } closed => Texts.Get("crisis.refusal.closed", ("crisis", closed.Title)),
            { ChosenAnswer: not null } answered => Texts.Get("gmCrisis.edit.locked", ("crisis", answered.Title)),
            _ => null,
        };
        return refusal is null ? CrisisTarget(data.GetCrisis(crisisId)!) : null;
    }

    private static LiveTarget CrisisTarget(Crisis crisis) =>
        new(TemplateKind.Crisis, crisis.TemplateId, DatabaseService.LiveCopyOf(crisis), crisis.Id);

    private static LiveTarget ProjectTarget(LiveProject project, ProjectTemplate? template)
    {
        var bonus = project.BonusDueResolution is { } due
            ? new ProjectBonus(null, null, (int)Math.Max(1, due - project.StartedResolution), project.BonusText ?? "", project.ParsedBonusEffects)
            : null;
        return new LiveTarget(TemplateKind.Project, project.TemplateId,
            new ProjectTemplate(project.TemplateId, project.Title, project.Purse, project.ProvinceId, project.Price, project.Term,
                project.ParsedEffects, bonus, template?.Ministerial ?? false, template?.Requires),
            project.Id);
    }

    private static bool KeepsProjectShape(ProjectTemplate edited, ProjectTemplate old) =>
        edited.Title == old.Title && edited.Purse == old.Purse && edited.Province == old.Province && edited.Price == old.Price
        && edited.Term == old.Term && edited.Ministerial == old.Ministerial && edited.Requires == old.Requires
        && (edited.Bonus is null) == (old.Bonus is null) && edited.Bonus?.Within == old.Bonus?.Within && edited.Bonus?.BySeason is null;

    public static LiveEditResult ApplyLiveEdit(DatabaseService data, GameConfig config, LiveTarget target, string yaml)
    {
        var parsed = TemplateCodec.Parse(yaml, $"{target.Current.Id}.yaml", data.BuildEffectContext(config));
        if (!parsed.IsValid)
            return new LiveEditResult(false, Texts.Get("content.live.nothingChanged") + "\n" + BulletList(parsed.Errors), null);
        var edited = parsed.Template!;
        if (edited.Kind != target.Kind)
            return new LiveEditResult(false, Texts.Get("content.live.kindChanged", ("kind", TemplateCodec.KindName(target.Kind))), null);
        if (edited.Id != target.Current.Id)
            return new LiveEditResult(false, Texts.Get("content.live.idChanged"), null);
        var problems = ReadinessProblems(edited, data.GetTemplate);
        if (problems.Count > 0)
            return new LiveEditResult(false, Texts.Get("content.live.nothingChanged") + "\n" + BulletList(problems), null);

        switch (edited, target.Current)
        {
            case (IssueTemplate issue, IssueTemplate old):
            {
                if (!data.UpdateIssueLive(target.LiveId, issue.Title, issue.For, issue.Against, issue.Opinion, issue.Salience))
                    return new LiveEditResult(false, Texts.Get("content.live.issueGone", ("id", target.LiveId)), null);
                var news = issue.For != old.For || issue.Against != old.Against
                    ? Texts.Get("content.live.issueNews", ("title", issue.Title), ("for", issue.For), ("against", issue.Against))
                    : null;
                return new LiveEditResult(true, news is null
                    ? Texts.Get("content.live.issueEdited", ("id", target.LiveId))
                    : Texts.Get("content.live.issueEditedPosted", ("id", target.LiveId)), news);
            }
            case (BillTemplate bill, BillTemplate old):
            {
                if (bill.Issue != old.Issue || bill.Lean != old.Lean)
                    return new LiveEditResult(false, Texts.Get("content.live.billKeeps"), null);
                if (!data.UpdateBillLive(target.LiveId, bill.Title, bill.Motion, bill.PassText, bill.PassEffects))
                    return new LiveEditResult(false, Texts.Get("content.live.billLocked", ("id", target.LiveId)), null);
                var news = bill.Motion != old.Motion
                    ? Texts.Get("content.live.billNews", ("title", bill.Title), ("motion", bill.Motion))
                    : null;
                return new LiveEditResult(true, news is null
                    ? Texts.Get("content.live.billEdited", ("id", target.LiveId))
                    : Texts.Get("content.live.billEditedPosted", ("id", target.LiveId)), news);
            }
            case (EventTemplate ev, EventTemplate):
            {
                if (target.RowId is not { } eventId || !data.UpdateQueuedEvent(eventId, ev.Title, ev.Text, ev.Effects))
                    return new LiveEditResult(false, Texts.Get("content.live.eventGone"), null);
                return new LiveEditResult(true, ev.Schedule is null
                    ? Texts.Get("content.live.eventEdited", ("eventId", eventId), ("id", ev.Id))
                    : Texts.Get("content.live.eventEditedScheduled", ("eventId", eventId), ("id", ev.Id)), null);
            }
            case (ProjectTemplate project, ProjectTemplate old):
            {
                if (!KeepsProjectShape(project, old))
                    return new LiveEditResult(false, Texts.Get("liveEdit.projectKeeps"), null);
                if (target.RowId is not { } projectId)
                    return new LiveEditResult(false, Texts.Get("projects.refusal.noProject"), null);
                if (data.EditLiveProject(projectId, project.Effects, project.Bonus?.Text, project.Bonus?.Effects) is { } refusal)
                    return new LiveEditResult(false, refusal, null);
                return new LiveEditResult(true, Texts.Get("liveEdit.projectEdited", ("project", project.Title), ("id", project.Id)), null);
            }
            case (CrisisTemplate crisis, CrisisTemplate):
            {
                if (target.RowId is not { } crisisId)
                    return new LiveEditResult(false, Texts.Get("crisis.refusal.gone"), null);
                if (data.EditLiveCrisis(crisisId, crisis) is { } refusal)
                    return new LiveEditResult(false, refusal, null);
                return new LiveEditResult(true, Texts.Get("liveEdit.crisisEdited", ("crisis", crisis.Title), ("id", crisis.Id)), null, crisisId);
            }
            default:
                return new LiveEditResult(false, Texts.Get("content.live.unsupported"), null);
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
