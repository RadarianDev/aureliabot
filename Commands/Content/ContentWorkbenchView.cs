using System.Collections.Concurrent;
using System.Text;
using Discord;

public enum WorkbenchTab { Yaml, Preview, Problems, Links }

public sealed record WorkbenchState(TemplateKind? Kind, TemplateStatus? Status, int Page, WorkbenchTab Tab, int YamlPage, string? TemplateId)
{
    public static readonly WorkbenchState Start = new(null, null, 0, WorkbenchTab.Yaml, 0, null);

    public string Pack() =>
        $"{KindCode(Kind)}{StatusCode(Status)}{TabCode(Tab)}.{Page}.{YamlPage}";

    public static WorkbenchState? Unpack(string packed, string? templateId)
    {
        var fields = packed.Split('.');
        if (fields.Length != 3 || fields[0].Length != 3
            || !int.TryParse(fields[1], out var page) || !int.TryParse(fields[2], out var yamlPage))
            return null;
        var kind = Enum.GetValues<TemplateKind>().Cast<TemplateKind?>().FirstOrDefault(k => KindCode(k) == fields[0][0]);
        var status = Enum.GetValues<TemplateStatus>().Cast<TemplateStatus?>().FirstOrDefault(s => StatusCode(s) == fields[0][1]);
        var tab = Enum.GetValues<WorkbenchTab>().FirstOrDefault(t => TabCode(t) == fields[0][2]);
        return new WorkbenchState(kind, status, page, tab, yamlPage, string.IsNullOrEmpty(templateId) ? null : templateId);
    }

    public WorkbenchState Open(string id) => this with { TemplateId = id, Tab = WorkbenchTab.Yaml, YamlPage = 0 };

    public WorkbenchState Browse() => this with { TemplateId = null, Tab = WorkbenchTab.Yaml, YamlPage = 0 };

    private static char KindCode(TemplateKind? kind) => kind is { } k ? k.ToString()[0] : '-';

    private static char StatusCode(TemplateStatus? status) => status is { } s ? s.ToString()[0] : '-';

    public static char TabCode(WorkbenchTab tab) => tab switch
    {
        WorkbenchTab.Preview => 'p',
        WorkbenchTab.Problems => 'x',
        WorkbenchTab.Links => 'l',
        _ => 'y',
    };
}

public sealed record WorkbenchDraft(string Action, string Arg, WorkbenchState State, string Text);

public class WorkbenchSessions
{
    private static readonly TimeSpan IdLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan DraftLifetime = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, (DateTimeOffset ExpiresAt, string TemplateId)> _ids = new();
    private readonly ConcurrentDictionary<string, (DateTimeOffset ExpiresAt, WorkbenchDraft Draft)> _drafts = new();

    public string TokenFor(string templateId)
    {
        Sweep();
        var token = Guid.NewGuid().ToString("N")[..12];
        _ids[token] = (DateTimeOffset.UtcNow + IdLifetime, templateId);
        return token;
    }

    public string? TemplateIdOf(string token) =>
        _ids.TryGetValue(token, out var entry) && entry.ExpiresAt > DateTimeOffset.UtcNow ? entry.TemplateId : null;

    public string Hold(WorkbenchDraft draft)
    {
        Sweep();
        var token = Guid.NewGuid().ToString("N")[..12];
        _drafts[token] = (DateTimeOffset.UtcNow + DraftLifetime, draft);
        return token;
    }

    public WorkbenchDraft? Draft(string token) =>
        _drafts.TryGetValue(token, out var entry) && entry.ExpiresAt > DateTimeOffset.UtcNow ? entry.Draft : null;

    private void Sweep()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var key in _ids.Where(e => e.Value.ExpiresAt < now).Select(e => e.Key).ToList()) _ids.TryRemove(key, out _);
        foreach (var key in _drafts.Where(e => e.Value.ExpiresAt < now).Select(e => e.Key).ToList()) _drafts.TryRemove(key, out _);
    }
}

public sealed record WorkbenchNotice(string Text, bool IsDiff = false, string? ReopenToken = null);

public static class ContentWorkbenchView
{
    public const string ButtonPrefix = "cw";
    public const string SelectPrefix = "cwsel";
    public const string ModalPrefix = "cwmod";
    public const int MaxCustomId = 100;
    public const int PageSize = 25;
    public const int TextBudget = 4000;
    private const int Margin = 100;

    public static string CustomId(WorkbenchSessions sessions, string prefix, string action, string arg, WorkbenchState state)
    {
        var id = state.TemplateId ?? "";
        var full = $"{prefix}:{action}:{arg}:{state.Pack()}:{id}";
        return full.Length <= MaxCustomId ? full : $"{prefix}:{action}:{arg}:{state.Pack()}:~{sessions.TokenFor(id)}";
    }

    public static (string Action, string Arg, WorkbenchState? State) Parse(WorkbenchSessions sessions, string rest)
    {
        var fields = rest.Split(':', 4);
        if (fields.Length != 4) return ("", "", null);
        var id = fields[3].StartsWith('~') ? sessions.TemplateIdOf(fields[3][1..]) : fields[3];
        if (id is null) return (fields[0], fields[1], null);
        return (fields[0], fields[1], WorkbenchState.Unpack(fields[2], id));
    }

    public static MessageComponent Message(string text) =>
        new ComponentBuilderV2().WithComponents([new ContainerBuilder().WithComponents([new TextDisplayBuilder(text)])]).Build();

    public static MessageComponent Browser(DatabaseService data, GameConfig config, WorkbenchSessions sessions, WorkbenchState state, WorkbenchNotice? notice = null)
    {
        var all = data.GetTemplates();
        var rows = all.Where(r => (state.Kind is null || r.Kind == state.Kind) && (state.Status is null || r.Status == state.Status)).ToList();
        var pages = Math.Max(1, (rows.Count + PageSize - 1) / PageSize);
        state = state with { Page = Math.Clamp(state.Page, 0, pages - 1) };
        var shown = rows.Skip(state.Page * PageSize).Take(PageSize).ToList();
        string Id(string action, string arg = "") => CustomId(sessions, ButtonPrefix, action, arg, state);
        string Select(string action) => CustomId(sessions, SelectPrefix, action, "", state);

        var header = Texts.Get("content.workbench.browser.header",
            ("total", all.Count),
            ("drafts", all.Count(r => r.Status == TemplateStatus.Draft)),
            ("ready", all.Count(r => r.Status == TemplateStatus.Ready)),
            ("archived", all.Count(r => r.Status == TemplateStatus.Archived)));

        var readyContext = data.BuildEffectContext(config);
        var gameState = data.GetGameState();
        var listing = new StringBuilder();
        foreach (var row in shown)
        {
            var problems = row.Status == TemplateStatus.Draft ? ContentRules.Problems(data, readyContext, gameState, row).Count : 0;
            listing.AppendLine(Texts.Get("content.workbench.browser.line",
                ("marker", Marker(row.Status)), ("id", row.Id), ("kind", TemplateCodec.KindName(row.Kind)),
                ("title", Display.Truncate(TitleOf(row), 40)),
                ("posted", row.TimesPosted == 0 ? "" : Texts.Get("content.workbench.browser.posted", ("times", row.TimesPosted))),
                ("problems", problems == 0 ? "" : Texts.Get("content.workbench.browser.problems", ("count", problems)))));
        }
        var listingText = rows.Count == 0 ? Texts.Get("content.workbench.browser.empty") : listing.ToString().TrimEnd();
        listingText = Display.Truncate(listingText, TextBudget - header.Length - (notice?.Text.Length ?? 0) - Margin);

        var kindMenu = new SelectMenuBuilder().WithCustomId(Select("kind"))
            .AddOption(Texts.Get("content.workbench.browser.kindAll", ("count", all.Count)), "-", isDefault: state.Kind is null);
        foreach (var kind in Enum.GetValues<TemplateKind>())
            kindMenu.AddOption(Texts.Get("content.workbench.browser.kindOption", ("kind", TemplateCodec.KindName(kind)), ("count", all.Count(r => r.Kind == kind))),
                kind.ToString(), isDefault: state.Kind == kind);

        var statusMenu = new SelectMenuBuilder().WithCustomId(Select("status"))
            .AddOption(Texts.Get("content.workbench.browser.statusAny"), "-", isDefault: state.Status is null);
        foreach (var status in Enum.GetValues<TemplateStatus>())
            statusMenu.AddOption(Texts.Get("content.workbench.browser.statusOption", ("marker", Marker(status)), ("status", status)),
                status.ToString(), isDefault: state.Status == status);

        var container = new ContainerBuilder();
        container.AddComponent(new TextDisplayBuilder(header));
        AddNotice(container, notice, sessions, state);
        container.AddComponent(new ActionRowBuilder().WithSelectMenu(kindMenu));
        container.AddComponent(new ActionRowBuilder().WithSelectMenu(statusMenu));
        container.AddComponent(new SeparatorBuilder());
        container.AddComponent(new TextDisplayBuilder(listingText));
        if (shown.Count > 0)
        {
            var open = new SelectMenuBuilder().WithCustomId(Select("open")).WithPlaceholder(Texts.Get("content.workbench.browser.open"));
            foreach (var row in shown)
                open.AddOption(Display.Truncate($"{row.Id}", 100), row.Id,
                    Display.Truncate(Texts.Get("content.workbench.browser.openDescription", ("kind", TemplateCodec.KindName(row.Kind)), ("status", row.Status), ("title", TitleOf(row))), 100));
            container.AddComponent(new ActionRowBuilder().WithSelectMenu(open));
        }

        var newMenu = new SelectMenuBuilder().WithCustomId(Select("new")).WithPlaceholder(Texts.Get("content.workbench.browser.new"));
        foreach (var kind in Enum.GetValues<TemplateKind>())
            newMenu.AddOption(TemplateCodec.KindName(kind), kind.ToString());
        container.AddComponent(new ActionRowBuilder().WithSelectMenu(newMenu));

        container.AddComponent(new ActionRowBuilder()
            .WithButton("◀", Id("page", (state.Page - 1).ToString()), ButtonStyle.Secondary, disabled: state.Page == 0)
            .WithButton(Texts.Get("content.workbench.browser.page", ("page", state.Page + 1), ("pages", pages)), Id("noop"), ButtonStyle.Secondary, disabled: true)
            .WithButton("▶", Id("page", (state.Page + 1).ToString()), ButtonStyle.Secondary, disabled: state.Page >= pages - 1)
            .WithButton(Texts.Get("content.workbench.browser.import"), Id("import"), ButtonStyle.Primary)
            .WithButton(Texts.Get("content.workbench.browser.export"), Id("export"), ButtonStyle.Secondary, disabled: rows.Count == 0));

        return new ComponentBuilderV2().WithComponents([container]).Build();
    }

    public static MessageComponent Template(DatabaseService data, GameConfig config, WorkbenchSessions sessions, WorkbenchState state, WorkbenchNotice? notice = null)
    {
        if (state.TemplateId is not { } templateId || data.GetTemplate(templateId) is not { } row)
            return Browser(data, config, sessions, state.Browse(),
                new WorkbenchNotice(Texts.Get("content.noTemplate", ("id", state.TemplateId ?? ""))));

        string Id(string action, string arg = "") => CustomId(sessions, ButtonPrefix, action, arg, state);
        string Select(string action) => CustomId(sessions, SelectPrefix, action, "", state);

        var problems = ContentRules.Problems(data, data.BuildEffectContext(config), data.GetGameState(), row);
        var live = ContentRules.FindLive(data, row.Id, out _) is not null;
        var header = Texts.Get("content.workbench.template.header", ("title", TitleOf(row))) + "\n"
            + Texts.Get("content.workbench.template.meta",
                ("id", row.Id), ("kind", TemplateCodec.KindName(row.Kind)), ("marker", Marker(row.Status)), ("status", row.Status),
                ("posted", row.TimesPosted == 0 ? Texts.Get("content.workbench.template.neverPosted") : Texts.Get("content.workbench.browser.posted", ("times", row.TimesPosted))),
                ("edited", row.UpdatedAt.ToUnixTimeSeconds()), ("chars", Display.Number(row.Yaml.Length)));

        var container = new ContainerBuilder();
        container.AddComponent(new TextDisplayBuilder(header));
        container.AddComponent(new ActionRowBuilder()
            .WithButton(Texts.Get("content.workbench.tab.yaml"), Id("tab", "y"), ButtonStyle.Secondary, disabled: state.Tab == WorkbenchTab.Yaml)
            .WithButton(Texts.Get("content.workbench.tab.preview"), Id("tab", "p"), ButtonStyle.Secondary, disabled: state.Tab == WorkbenchTab.Preview)
            .WithButton(problems.Count == 0 ? Texts.Get("content.workbench.tab.noProblems") : Texts.Get("content.workbench.tab.problems", ("count", problems.Count)),
                Id("tab", "x"), problems.Count == 0 ? ButtonStyle.Secondary : ButtonStyle.Danger, disabled: state.Tab == WorkbenchTab.Problems)
            .WithButton(Texts.Get("content.workbench.tab.links"), Id("tab", "l"), ButtonStyle.Secondary, disabled: state.Tab == WorkbenchTab.Links));
        container.AddComponent(new SeparatorBuilder());
        AddNotice(container, notice, sessions, state);

        var budget = TextBudget - header.Length - (notice?.Text.Length ?? 0) - Margin;
        switch (state.Tab)
        {
            case WorkbenchTab.Yaml:
                AddYaml(container, row, state, budget, Id);
                break;
            case WorkbenchTab.Preview:
                AddPreview(container, data, config, row, budget, Id);
                break;
            case WorkbenchTab.Problems:
                AddProblems(container, row, problems, budget, Select);
                break;
            case WorkbenchTab.Links:
                AddLinks(container, data, row, live, budget, Select);
                break;
        }

        container.AddComponent(new SeparatorBuilder());
        container.AddComponent(new ActionRowBuilder()
            .WithButton(Texts.Get("content.workbench.action.edit"), Id("edit"), ButtonStyle.Primary)
            .WithButton(Texts.Get("content.workbench.action.ready"), Id("ready"), ButtonStyle.Success, disabled: row.Status == TemplateStatus.Ready)
            .WithButton(Texts.Get("content.workbench.action.duplicate"), Id("dup"), ButtonStyle.Secondary)
            .WithButton(Texts.Get("content.workbench.action.download"), Id("dl"), ButtonStyle.Secondary)
            .WithButton(Texts.Get("content.workbench.action.remove"), Id("rm"), ButtonStyle.Danger, disabled: row.Status == TemplateStatus.Archived));

        var parts = YamlParts.Split(row.Yaml).Where(p => p.Key != YamlParts.PreambleKey).DistinctBy(p => p.Key).Take(PageSize).ToList();
        if (parts.Count > 0)
        {
            var partMenu = new SelectMenuBuilder().WithCustomId(Select("part")).WithPlaceholder(Texts.Get("content.workbench.action.editPart"));
            foreach (var part in parts)
                partMenu.AddOption(part.Key, part.Key, Texts.Get("content.workbench.action.partDescription", ("line", part.FirstLine), ("chars", part.Body.Length)));
            container.AddComponent(new ActionRowBuilder().WithSelectMenu(partMenu));
        }

        var last = new ActionRowBuilder();
        if (live) last.WithButton(Texts.Get("content.workbench.action.editLive"), Id("live"), ButtonStyle.Primary);
        last.WithButton(Texts.Get("content.workbench.action.upload"), Id("upload"), ButtonStyle.Secondary);
        last.WithButton(Texts.Get("content.workbench.action.back"), Id("back"), ButtonStyle.Secondary);
        container.AddComponent(last);

        return new ComponentBuilderV2().WithComponents([container]).Build();
    }

    private static void AddNotice(ContainerBuilder container, WorkbenchNotice? notice, WorkbenchSessions sessions, WorkbenchState state)
    {
        if (notice is null) return;
        container.AddComponent(new TextDisplayBuilder(notice.Text));
        if (notice.ReopenToken is { } token)
            container.AddComponent(new ActionRowBuilder()
                .WithButton(Texts.Get("content.workbench.edit.reopen"), CustomId(sessions, ButtonPrefix, "reopen", token, state), ButtonStyle.Primary));
        container.AddComponent(new SeparatorBuilder());
    }

    private static void AddYaml(ContainerBuilder container, TemplateRow row, WorkbenchState state, int budget, Func<string, string, string> id)
    {
        var fence = "```yaml\n\n```";
        var pages = YamlParts.Pages(row.Yaml, budget - fence.Length - 60);
        var index = Math.Clamp(state.YamlPage, 0, pages.Count - 1);
        var page = pages[index];
        container.AddComponent(new TextDisplayBuilder($"```yaml\n{EscapeFence(page.Text)}\n```"));
        var label = Texts.Get("content.workbench.yaml.lines", ("first", page.FirstLine), ("last", page.LastLine), ("total", pages[^1].LastLine));
        if (pages.Count == 1)
        {
            container.AddComponent(new TextDisplayBuilder(Texts.Get("content.workbench.yaml.footer", ("lines", label))));
            return;
        }
        container.AddComponent(new ActionRowBuilder()
            .WithButton("◀", id("yp", (index - 1).ToString()), ButtonStyle.Secondary, disabled: index == 0)
            .WithButton(label, id("noop", ""), ButtonStyle.Secondary, disabled: true)
            .WithButton("▶", id("yp", (index + 1).ToString()), ButtonStyle.Secondary, disabled: index >= pages.Count - 1));
    }

    private static void AddPreview(ContainerBuilder container, DatabaseService data, GameConfig config, TemplateRow row, int budget, Func<string, string, string> id)
    {
        var preview = ContentPreview.Render(data, config, row.Id);
        container.AddComponent(new TextDisplayBuilder(Display.Truncate(preview, budget)));
        var buttons = new ActionRowBuilder();
        if (TemplateCodec.Parse(row.Yaml, $"{row.Id}.yaml").Template is CrisisTemplate crisis)
        {
            var number = 0;
            foreach (var answer in crisis.Answers.Take(4))
                buttons.WithButton(Display.Truncate(Texts.Get("content.workbench.preview.answer", ("number", ++number), ("label", answer.Label)), 80),
                    id("noop", number.ToString()), ButtonStyle.Secondary, disabled: true);
            container.AddComponent(buttons);
            buttons = new ActionRowBuilder();
        }
        buttons.WithButton(Texts.Get("content.workbench.preview.share"), id("share", ""), ButtonStyle.Secondary);
        container.AddComponent(buttons);
    }

    private static void AddProblems(ContainerBuilder container, TemplateRow row, List<ContentProblem> problems, int budget, Func<string, string> select)
    {
        if (problems.Count == 0)
        {
            container.AddComponent(new TextDisplayBuilder(row.Status == TemplateStatus.Ready
                ? Texts.Get("content.workbench.problems.noneReady")
                : Texts.Get("content.workbench.problems.none")));
            return;
        }
        var sb = new StringBuilder();
        foreach (var problem in problems)
        {
            var entry = problem.Line is { } line
                ? Texts.Get("content.workbench.problems.atLine", ("line", line), ("message", problem.Message)) + $"\n```diff\n{EscapeFence(YamlParts.Excerpt(row.Yaml, line))}\n```\n"
                : Texts.Get("content.workbench.problems.noLine", ("message", problem.Message)) + "\n";
            if (sb.Length + entry.Length > budget)
            {
                sb.Append(Texts.Get("content.workbench.problems.more", ("count", problems.Count - problems.IndexOf(problem))));
                break;
            }
            sb.Append(entry);
        }
        container.AddComponent(new TextDisplayBuilder(sb.ToString().TrimEnd()));

        var located = problems
            .Select((p, i) => (Problem: p, Index: i, Key: p.Line is { } l ? YamlParts.KeyAtLine(row.Yaml, l) : null))
            .Where(x => x.Key is not null)
            .Take(PageSize)
            .ToList();
        if (located.Count == 0) return;
        var menu = new SelectMenuBuilder().WithCustomId(select("problem")).WithPlaceholder(Texts.Get("content.workbench.problems.fix"));
        foreach (var x in located)
            menu.AddOption(Display.Truncate(Texts.Get("content.workbench.problems.option", ("line", x.Problem.Line), ("key", x.Key)), 100),
                $"{x.Index}:{x.Key}", Display.Truncate(x.Problem.Message, 100));
        container.AddComponent(new ActionRowBuilder().WithSelectMenu(menu));
    }

    private static void AddLinks(ContainerBuilder container, DatabaseService data, TemplateRow row, bool live, int budget, Func<string, string> select)
    {
        var all = data.GetTemplates();
        var byId = all.ToDictionary(r => r.Id);
        var outgoing = TemplateCodec.Parse(row.Yaml, $"{row.Id}.yaml").Template?.References.Distinct().Where(r => r != row.Id).ToList() ?? [];
        var incoming = all.Where(r => r.Id != row.Id
                && TemplateCodec.Parse(r.Yaml, $"{r.Id}.yaml").Template?.References.Contains(row.Id) == true)
            .Select(r => r.Id).ToList();

        string Line(string id) => byId.TryGetValue(id, out var linked)
            ? Texts.Get("content.workbench.links.line", ("marker", Marker(linked.Status)), ("id", id), ("kind", TemplateCodec.KindName(linked.Kind)), ("status", linked.Status))
            : Texts.Get("content.workbench.links.missing", ("id", id));

        var sb = new StringBuilder();
        sb.AppendLine(Texts.Get("content.workbench.links.outgoing"));
        sb.AppendLine(outgoing.Count == 0 ? Texts.Get("content.workbench.links.none") : string.Join("\n", outgoing.Select(Line)));
        sb.AppendLine(Texts.Get("content.workbench.links.incoming"));
        sb.AppendLine(incoming.Count == 0 ? Texts.Get("content.workbench.links.none") : string.Join("\n", incoming.Select(Line)));
        sb.AppendLine(live ? Texts.Get("content.workbench.links.live") : Texts.Get("content.workbench.links.notLive"));
        container.AddComponent(new TextDisplayBuilder(Display.Truncate(sb.ToString().TrimEnd(), budget)));

        var targets = outgoing.Concat(incoming).Distinct().Where(byId.ContainsKey).Take(PageSize).ToList();
        if (targets.Count == 0) return;
        var menu = new SelectMenuBuilder().WithCustomId(select("open")).WithPlaceholder(Texts.Get("content.workbench.links.jump"));
        foreach (var target in targets)
            menu.AddOption(target, target, Display.Truncate(Texts.Get("content.workbench.browser.openDescription",
                ("kind", TemplateCodec.KindName(byId[target].Kind)), ("status", byId[target].Status), ("title", TitleOf(byId[target]))), 100));
        container.AddComponent(new ActionRowBuilder().WithSelectMenu(menu));
    }

    public static string DiffNotice(string before, string after, int budget)
    {
        var lines = YamlParts.Diff(before, after);
        if (lines.Count == 0) return Texts.Get("content.workbench.edit.unchanged");
        var sb = new StringBuilder();
        var shown = 0;
        foreach (var line in lines)
        {
            if (sb.Length + line.Length + 1 > budget) break;
            sb.Append(EscapeFence(line)).Append('\n');
            shown++;
        }
        var text = Texts.Get("content.workbench.edit.saved") + $"\n```diff\n{sb.ToString().TrimEnd('\n')}\n```";
        return shown < lines.Count ? text + "\n" + Texts.Get("content.workbench.edit.diffMore", ("count", lines.Count - shown)) : text;
    }

    public static string TitleOf(TemplateRow row) =>
        TemplateCodec.Parse(row.Yaml, $"{row.Id}.yaml").Template?.Title ?? Texts.Get("content.workbench.untitled");

    private static string Marker(TemplateStatus status) => status switch
    {
        TemplateStatus.Draft => "📝",
        TemplateStatus.Ready => "✅",
        _ => "🗄️",
    };

    private static string EscapeFence(string text) => text.Replace("```", "`​``");
}
