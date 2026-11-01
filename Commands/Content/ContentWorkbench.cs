using System.Text;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;

public partial class ContentModule
{
    private const int ModalParts = 5;
    private const string UploadInputId = "file";
    private const string NewIdInputId = "id";

    [SlashCommand("browse", "Browse, preview and edit templates in a private panel")]
    public async Task BrowseAsync(
        [Summary("kind", "Only this kind")] TemplateKind? kind = null,
        [Summary("status", "Only this status")] TemplateStatus? status = null,
        [Summary("id", "Open straight on this template"), Autocomplete(typeof(ContentAutocomplete))] string? id = null)
    {
        await DeferAsync(ephemeral: true);
        var state = WorkbenchState.Start with { Kind = kind, Status = status };
        var panel = string.IsNullOrWhiteSpace(id) ? Browser(state) : Panel(state.Open(id.Trim()));
        await FollowupAsync(components: panel, ephemeral: true, flags: MessageFlags.ComponentsV2);
    }

    [ComponentInteraction(ContentWorkbenchView.ButtonPrefix + ":*", ignoreGroupNames: true)]
    public async Task WorkbenchButtonAsync(string rest)
    {
        var (action, arg, state) = ContentWorkbenchView.Parse(_sessions, rest);
        if (state is null)
        {
            await ShowAsync(ContentWorkbenchView.Message(Texts.Get("content.workbench.expired")));
            return;
        }

        switch (action)
        {
            case "page":
                await ShowAsync(Browser(state with { Page = int.TryParse(arg, out var page) ? page : 0 }));
                break;
            case "back":
                await ShowAsync(Browser(state.Browse()));
                break;
            case "tab":
                var tab = Enum.GetValues<WorkbenchTab>().FirstOrDefault(t => WorkbenchState.TabCode(t).ToString() == arg);
                await ShowAsync(Panel(state with { Tab = tab, YamlPage = 0 }));
                break;
            case "yp":
                await ShowAsync(Panel(state with { YamlPage = int.TryParse(arg, out var yamlPage) ? yamlPage : 0 }));
                break;
            case "import":
                await RespondWithModalAsync(UploadModal("import", "", state, Texts.Get("content.workbench.import.title")));
                break;
            case "export":
                await DeferAsync(ephemeral: true);
                await FollowupExportAsync(_data.GetTemplates(state.Kind, state.Status), "content.zip");
                break;
            case "reopen":
                await ReopenAsync(arg);
                break;
            default:
                await TemplateActionAsync(action, state);
                break;
        }
    }

    private async Task TemplateActionAsync(string action, WorkbenchState state)
    {
        if (state.TemplateId is not { } id || _data.GetTemplate(id) is not { } row)
        {
            await ShowAsync(Browser(state.Browse(), Texts.Get("content.noTemplate", ("id", state.TemplateId ?? ""))));
            return;
        }

        switch (action)
        {
            case "edit":
                if (WholeModal(state, row, row.Yaml) is { } modal)
                    await RespondWithModalAsync(modal);
                else
                    await RespondAsync(Texts.Get("content.workbench.edit.tooLong", ("id", id), ("length", row.Yaml.Length)), ephemeral: true);
                break;
            case "ready":
                var ready = ContentRules.MakeReady(_data, _constants.Config, id);
                await ShowAsync(Panel(ready.Ok ? state : state with { Tab = WorkbenchTab.Problems }, ready.Message));
                break;
            case "dup":
                await RespondWithModalAsync(new ModalBuilder()
                    .WithTitle(Display.Truncate(Texts.Get("content.workbench.duplicate.title", ("id", id)), 45))
                    .WithCustomId(ContentWorkbenchView.CustomId(_sessions, ContentWorkbenchView.ModalPrefix, "dup", "", state))
                    .AddTextInput(Texts.Get("content.workbench.duplicate.label"), NewIdInputId, TextInputStyle.Short,
                        value: $"{id}_copy", maxLength: 60, required: true)
                    .Build());
                break;
            case "dl":
                await DeferAsync(ephemeral: true);
                using (var stream = new MemoryStream(new UTF8Encoding(false).GetBytes(row.Yaml)))
                    await FollowupWithFileAsync(stream, $"{id}.yaml", Texts.Get("content.workbench.download", ("id", id)), ephemeral: true);
                break;
            case "rm":
                await ConfirmRemoveAsync(id);
                break;
            case "live":
                var (liveModal, refusal) = LiveModal(id);
                if (liveModal is null)
                    await RespondAsync(refusal, ephemeral: true);
                else
                    await RespondWithModalAsync(liveModal);
                break;
            case "upload":
                await RespondWithModalAsync(UploadModal("upload", ContentRules.EditStamp(row), state, Texts.Get("content.workbench.upload.title", ("id", id))));
                break;
            case "share":
                var posted = await _publisher.PostAsync(ChannelRole.Gm, ContentPreview.Render(_data, _constants.Config, id), $"content browse share {id}");
                await ShowAsync(Panel(state, posted is null
                    ? Texts.Get("content.workbench.preview.shareFailed")
                    : Texts.Get("content.workbench.preview.shared")));
                break;
            default:
                await DeferAsync();
                break;
        }
    }

    [ComponentInteraction(ContentWorkbenchView.SelectPrefix + ":*", ignoreGroupNames: true)]
    public async Task WorkbenchSelectAsync(string rest, string[] values)
    {
        var (action, _, state) = ContentWorkbenchView.Parse(_sessions, rest);
        var value = values.FirstOrDefault() ?? "";
        if (state is null)
        {
            await ShowAsync(ContentWorkbenchView.Message(Texts.Get("content.workbench.expired")));
            return;
        }

        switch (action)
        {
            case "kind":
                await ShowAsync(Browser(state with { Kind = Enum.TryParse<TemplateKind>(value, out var kind) ? kind : null, Page = 0 }));
                break;
            case "status":
                await ShowAsync(Browser(state with { Status = Enum.TryParse<TemplateStatus>(value, out var status) ? status : null, Page = 0 }));
                break;
            case "open":
                await ShowAsync(Panel(state.Open(value)));
                break;
            case "new":
                if (!Enum.TryParse<TemplateKind>(value, out var newKind) || Skeleton(newKind) is not { } skeleton)
                {
                    await ShowAsync(Browser(state, Texts.Get("content.workbench.new.noSkeleton", ("file", SkeletonPath(newKind)))));
                    return;
                }
                await RespondWithModalAsync(NewModal(state, newKind, skeleton));
                break;
            case "part":
            case "problem":
                await OpenPartAsync(state, value[(value.IndexOf(':') + 1)..]);
                break;
            default:
                await DeferAsync();
                break;
        }
    }

    private async Task OpenPartAsync(WorkbenchState state, string key)
    {
        if (state.TemplateId is not { } id || _data.GetTemplate(id) is not { } row)
        {
            await ShowAsync(Browser(state.Browse(), Texts.Get("content.noTemplate", ("id", state.TemplateId ?? ""))));
            return;
        }
        if (YamlParts.Split(row.Yaml).FirstOrDefault(p => p.Key == key) is not { } part)
        {
            await ShowAsync(Panel(state, Texts.Get("content.workbench.part.gone", ("key", key))));
            return;
        }
        if (PartModal(state, row, key, part.Body.TrimEnd('\r', '\n')) is { } modal)
            await RespondWithModalAsync(modal);
        else
            await RespondAsync(Texts.Get("content.workbench.part.tooLong", ("key", key)), ephemeral: true);
    }

    [ModalInteraction(ContentWorkbenchView.ModalPrefix + ":*", ignoreGroupNames: true)]
    public async Task WorkbenchModalAsync(string rest, WorkbenchModal _)
    {
        var (action, arg, state) = ContentWorkbenchView.Parse(_sessions, rest);
        var submitted = (SocketModal)Context.Interaction;
        if (state is null)
        {
            await submitted.UpdateAsync(p => Show(p, ContentWorkbenchView.Message(Texts.Get("content.workbench.expired"))));
            return;
        }
        var inputs = submitted.Data.Components
            .Where(c => c.CustomId is not null)
            .GroupBy(c => c.CustomId)
            .ToDictionary(g => g.Key, g => g.First().Value ?? "");

        switch (action)
        {
            case "edit":
                var whole = YamlParts.JoinGroups(Enumerable.Range(1, ModalParts).Select(n => inputs.GetValueOrDefault($"p{n}", "")));
                await SaveAsync(submitted, state, "edit", arg, whole, _ => whole);
                break;
            case "part":
                var dot = arg.IndexOf('.');
                var key = dot < 0 ? "" : arg[(dot + 1)..];
                var body = inputs.GetValueOrDefault("p1", "");
                await SaveAsync(submitted, state, "part", arg, body, current => YamlParts.Splice(current, key, body));
                break;
            case "new":
                await SaveNewAsync(submitted, state, "new", arg, inputs.GetValueOrDefault("p1", ""));
                break;
            case "dup":
                if (state.TemplateId is not { } source || _data.GetTemplate(source) is not { } original)
                {
                    await submitted.UpdateAsync(p => Show(p, Browser(state.Browse(), Texts.Get("content.noTemplate", ("id", state.TemplateId ?? "")))));
                    return;
                }
                await SaveNewAsync(submitted, state, "new", original.Kind.ToString(),
                    YamlParts.SetId(original.Yaml, inputs.GetValueOrDefault(NewIdInputId, "").Trim()));
                break;
            case "upload":
            case "import":
                await UploadAsync(submitted, state, action, arg);
                break;
        }
    }

    private async Task SaveAsync(SocketModal submitted, WorkbenchState state, string action, string arg, string typed, Func<string, string?> build)
    {
        var id = state.TemplateId ?? "";
        var row = _data.GetTemplate(id);
        var yaml = row is null ? null : build(row.Yaml);
        var refusal = (row, yaml) switch
        {
            (null, _) => Texts.Get("content.noTemplate", ("id", id)),
            _ when ContentRules.EditStamp(row) != arg.Split('.')[0] => Texts.Get("content.workbench.edit.stale", ("id", id)),
            (_, null) => Texts.Get("content.workbench.part.gone", ("key", arg[(arg.IndexOf('.') + 1)..])),
            _ => null,
        };
        if (refusal is null)
        {
            var edit = ContentRules.SaveEdit(_data, ContentRules.ImportContext(_data, _constants.Config), id, yaml!);
            if (edit.Outcome.Imported)
            {
                var notice = ContentRules.Describe(edit.Outcome).TrimStart('-', ' ') + "\n" + (edit.Copy
                    ? Texts.Get("content.edit.copy", ("id", id))
                    : ContentWorkbenchView.DiffNotice(row!.Yaml, yaml!, DiffBudget));
                await submitted.UpdateAsync(p => Show(p, Panel(state with { Tab = WorkbenchTab.Yaml, YamlPage = 0 }, notice)));
                return;
            }
            refusal = ContentRules.Describe(edit.Outcome);
        }

        await FailAsync(submitted, state with { Tab = WorkbenchTab.Problems }, id, refusal, new WorkbenchDraft(action, arg, state, typed));
    }

    private async Task SaveNewAsync(SocketModal submitted, WorkbenchState state, string action, string arg, string yaml)
    {
        var outcome = ContentRules.SaveNew(_data, ContentRules.ImportContext(_data, _constants.Config), yaml);
        if (outcome.Imported)
        {
            var message = ContentRules.Describe(outcome).TrimStart('-', ' ');
            await submitted.UpdateAsync(p => Show(p, Panel(state.Open(outcome.Id!), message)));
            return;
        }
        await FailAsync(submitted, state, outcome.Id ?? "new", ContentRules.Describe(outcome), new WorkbenchDraft(action, arg, state, yaml));
    }

    private async Task FailAsync(SocketModal submitted, WorkbenchState state, string id, string failure, WorkbenchDraft draft)
    {
        var token = _sessions.Hold(draft);
        var notice = new WorkbenchNotice(Display.Truncate(failure, DiffBudget), ReopenToken: token);
        await submitted.UpdateAsync(p => Show(p, state.TemplateId is null
            ? ContentWorkbenchView.Browser(_data, _constants.Config, _sessions, state, notice)
            : ContentWorkbenchView.Template(_data, _constants.Config, _sessions, state, notice)));
        using var stream = new MemoryStream(new UTF8Encoding(false).GetBytes(draft.Text));
        await submitted.FollowupWithFileAsync(stream, $"{id}.yaml", Texts.Get("content.edit.attached"), ephemeral: true, allowedMentions: AllowedMentions.None);
    }

    private async Task ReopenAsync(string token)
    {
        if (_sessions.Draft(token) is not { } draft)
        {
            await RespondAsync(Texts.Get("content.workbench.edit.draftExpired"), ephemeral: true);
            return;
        }
        var row = draft.State.TemplateId is { } id ? _data.GetTemplate(id) : null;
        Modal? modal = draft.Action switch
        {
            "new" => Enum.TryParse<TemplateKind>(draft.Arg, out var kind) ? NewModal(draft.State, kind, draft.Text) : null,
            "part" when row is not null => PartModal(draft.State, row, draft.Arg[(draft.Arg.IndexOf('.') + 1)..], draft.Text, draft.Arg.Split('.')[0]),
            "edit" when row is not null => WholeModal(draft.State, row, draft.Text, draft.Arg),
            _ => null,
        };
        if (modal is null)
            await RespondAsync(Texts.Get("content.workbench.edit.cantReopen"), ephemeral: true);
        else
            await RespondWithModalAsync(modal);
    }

    private async Task UploadAsync(SocketModal submitted, WorkbenchState state, string action, string arg)
    {
        await submitted.DeferAsync(ephemeral: true);
        var (files, skipped) = await DownloadAsync(submitted.Data.Attachments);
        var context = ContentRules.ImportContext(_data, _constants.Config);
        string notice;
        if (action == "import")
        {
            notice = ContentRules.FormatImport(ContentRules.Import(_data, context, files), skipped);
            await submitted.ModifyOriginalResponseAsync(p => Show(p, Browser(state, Display.Truncate(notice, DiffBudget))));
            return;
        }

        if (state.TemplateId is not { } id || _data.GetTemplate(id) is not { } row)
            notice = Texts.Get("content.noTemplate", ("id", state.TemplateId ?? ""));
        else if (ContentRules.EditStamp(row) != arg)
            notice = Texts.Get("content.workbench.edit.stale", ("id", id));
        else if (files.Count != 1)
            notice = string.Join("\n", skipped.Prepend(Texts.Get("content.workbench.upload.oneFile")));
        else
        {
            var edit = ContentRules.SaveEdit(_data, context, id, files[0].Yaml);
            notice = ContentRules.Describe(edit.Outcome).TrimStart('-', ' ');
            if (edit.Copy) notice += "\n" + Texts.Get("content.edit.copy", ("id", id));
            else if (edit.Outcome.Imported) notice += "\n" + ContentWorkbenchView.DiffNotice(row.Yaml, files[0].Yaml, DiffBudget);
        }
        await submitted.ModifyOriginalResponseAsync(p => Show(p, Panel(state, Display.Truncate(notice, ContentWorkbenchView.TextBudget / 2))));
    }

    private const int DiffBudget = 1200;

    private MessageComponent Browser(WorkbenchState state, string? notice = null) =>
        ContentWorkbenchView.Browser(_data, _constants.Config, _sessions, state, notice is null ? null : new WorkbenchNotice(notice));

    private MessageComponent Panel(WorkbenchState state, string? notice = null) =>
        state.TemplateId is null
            ? Browser(state, notice)
            : ContentWorkbenchView.Template(_data, _constants.Config, _sessions, state, notice is null ? null : new WorkbenchNotice(notice));

    private Task ShowAsync(MessageComponent panel) =>
        ((SocketMessageComponent)Context.Interaction).UpdateAsync(p => Show(p, panel));

    private static void Show(MessageProperties properties, MessageComponent panel)
    {
        properties.Components = panel;
        properties.Embeds = Array.Empty<Embed>();
        properties.Flags = MessageFlags.ComponentsV2;
    }

    private Modal? WholeModal(WorkbenchState state, TemplateRow row, string yaml, string? stamp = null)
    {
        if (YamlParts.Group(yaml, ContentRules.ModalLimit, ModalParts) is not { } groups) return null;
        var builder = new ModalBuilder()
            .WithTitle(Display.Truncate(Texts.Get("content.edit.title", ("id", row.Id)), 45))
            .WithCustomId(ContentWorkbenchView.CustomId(_sessions, ContentWorkbenchView.ModalPrefix, "edit", stamp ?? ContentRules.EditStamp(row), state));
        for (var i = 0; i < groups.Count; i++)
            builder.AddTextInput(
                groups.Count == 1 ? Texts.Get("content.edit.yamlLabel") : Texts.Get("content.workbench.edit.part", ("number", i + 1), ("count", groups.Count)),
                $"p{i + 1}", TextInputStyle.Paragraph, value: groups[i], maxLength: ContentRules.ModalLimit, required: i == 0);
        return builder.Build();
    }

    private Modal? PartModal(WorkbenchState state, TemplateRow row, string key, string body, string? stamp = null)
    {
        if (body.Length > ContentRules.ModalLimit) return null;
        return new ModalBuilder()
            .WithTitle(Display.Truncate(Texts.Get("content.workbench.part.title", ("id", row.Id), ("key", key)), 45))
            .WithCustomId(ContentWorkbenchView.CustomId(_sessions, ContentWorkbenchView.ModalPrefix, "part", $"{stamp ?? ContentRules.EditStamp(row)}.{key}", state))
            .AddTextInput(key, "p1", TextInputStyle.Paragraph, value: body, maxLength: ContentRules.ModalLimit, required: false)
            .Build();
    }

    private Modal NewModal(WorkbenchState state, TemplateKind kind, string yaml) =>
        new ModalBuilder()
            .WithTitle(Display.Truncate(Texts.Get("content.workbench.new.title", ("kind", TemplateCodec.KindName(kind))), 45))
            .WithCustomId(ContentWorkbenchView.CustomId(_sessions, ContentWorkbenchView.ModalPrefix, "new", kind.ToString(), state))
            .AddTextInput(Texts.Get("content.edit.yamlLabel"), "p1", TextInputStyle.Paragraph,
                value: Display.Truncate(yaml, ContentRules.ModalLimit), maxLength: ContentRules.ModalLimit, required: true)
            .Build();

    private Modal UploadModal(string action, string stamp, WorkbenchState state, string title) =>
        new ModalBuilder()
            .WithTitle(Display.Truncate(title, 45))
            .WithCustomId(ContentWorkbenchView.CustomId(_sessions, ContentWorkbenchView.ModalPrefix, action, stamp, state))
            .AddFileUpload(Texts.Get("content.workbench.upload.label"),
                new FileUploadComponentBuilder(UploadInputId, 1, action == "import" ? 10 : 1, true, null),
                Texts.Get("content.workbench.upload.description"), null)
            .Build();

    private string SkeletonPath(TemplateKind kind) =>
        Path.Combine(_constants.Folder, "skeletons", $"{kind.ToString().ToLowerInvariant()}.yaml");

    private string? Skeleton(TemplateKind kind) =>
        File.Exists(SkeletonPath(kind)) ? File.ReadAllText(SkeletonPath(kind)) : null;
}

public class WorkbenchModal : IModal
{
    public string Title => Texts.Get("content.edit.modalTitle");
}
