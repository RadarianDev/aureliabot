using System.Text;
using Discord;
using Discord.Interactions;

[Group("content", "Write the game's issues, bills, projects, events and crises (GM only)")]
[DefaultMemberPermissions(GuildPermission.ManageGuild)]
[RequireUserPermission(GuildPermission.ManageGuild)]
public class ContentModule : InteractionModuleBase<SocketInteractionContext>
{
    private const string EditModalPrefix = "content_edit";
    private const long MaxDownloadBytes = 8_000_000;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly DatabaseService _data;
    private readonly ConstantsStore _constants;
    private readonly Confirmations _confirmations;
    private readonly Publisher _publisher;

    public ContentModule(DatabaseService data, ConstantsStore constants, Confirmations confirmations, Publisher publisher)
    {
        _data = data;
        _constants = constants;
        _confirmations = confirmations;
        _publisher = publisher;
    }

    [SlashCommand("import", "Import YAML templates, or a zip from /content export")]
    public async Task ImportAsync(
        [Summary("file1", "A .yaml or .yml template, or a .zip of them")] IAttachment file1,
        [Summary("file2", "Another file")] IAttachment? file2 = null,
        [Summary("file3", "Another file")] IAttachment? file3 = null,
        [Summary("file4", "Another file")] IAttachment? file4 = null,
        [Summary("file5", "Another file")] IAttachment? file5 = null,
        [Summary("file6", "Another file")] IAttachment? file6 = null,
        [Summary("file7", "Another file")] IAttachment? file7 = null,
        [Summary("file8", "Another file")] IAttachment? file8 = null,
        [Summary("file9", "Another file")] IAttachment? file9 = null,
        [Summary("file10", "Another file")] IAttachment? file10 = null)
    {
        await DeferAsync(ephemeral: true);
        var files = new List<ImportFile>();
        var skipped = new List<string>();
        foreach (var attachment in new[] { file1, file2, file3, file4, file5, file6, file7, file8, file9, file10 }.OfType<IAttachment>())
        {
            var name = attachment.Filename;
            var isZip = name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
            if (!isZip && !ContentRules.IsYamlName(name))
            {
                skipped.Add($"`{name}`: skipped; only .yaml, .yml and .zip files import.");
                continue;
            }
            if (attachment.Size > MaxDownloadBytes)
            {
                skipped.Add($"`{name}`: skipped; it is too large.");
                continue;
            }

            byte[] bytes;
            try
            {
                bytes = await Http.GetByteArrayAsync(attachment.Url);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                skipped.Add($"`{name}`: couldn't be downloaded ({ex.Message}).");
                continue;
            }

            if (!isZip)
            {
                files.Add(new ImportFile(name, ContentRules.DecodeText(bytes)));
                continue;
            }
            try
            {
                var entries = ContentRules.ReadArchive(bytes);
                if (entries.Count == 0) skipped.Add($"`{name}`: holds no .yaml files.");
                files.AddRange(entries.Select(e => e with { Name = $"{name}/{e.Name}" }));
            }
            catch (InvalidDataException)
            {
                skipped.Add($"`{name}`: isn't a readable zip.");
            }
        }

        var outcomes = ContentRules.Import(_data, ContentRules.ImportContext(_data, _constants.Config), files);
        await SendAsync(ContentRules.FormatImport(outcomes, skipped));
    }

    [SlashCommand("edit", "Edit a template's YAML in a form; with live, edit what is already in play")]
    public async Task EditAsync(
        [Summary("id", "The template"), Autocomplete(typeof(ContentAutocomplete))] string id,
        [Summary("live", "Edit the live copy instead of the template")] bool live = false)
    {
        id = id.Trim();
        string yaml;
        if (live)
        {
            if (ContentRules.FindLive(_data, id, out var refusal) is not { } target)
            {
                await RespondAsync(refusal, ephemeral: true);
                return;
            }
            yaml = TemplateCodec.Serialize(target.Current);
        }
        else
        {
            if (_data.GetTemplate(id) is not { } row)
            {
                await RespondAsync($"There is no template `{id}`.", ephemeral: true);
                return;
            }
            yaml = row.Yaml;
        }

        if (yaml.Length > ContentRules.ModalLimit)
        {
            await RespondAsync(
                $"`{id}` is {yaml.Length} characters, more than a form holds ({ContentRules.ModalLimit}). " +
                "Edit it with `/content export`, then `/content import` the changed file.",
                ephemeral: true);
            return;
        }

        var modal = new ModalBuilder()
            .WithTitle(Display.Truncate(live ? $"Edit live {id}" : $"Edit {id}", 45))
            .WithCustomId($"{EditModalPrefix}:{(live ? "live" : "template")}:{id}")
            .AddTextInput("YAML", ContentEditModal.YamlInputId, TextInputStyle.Paragraph, value: yaml, maxLength: ContentRules.ModalLimit, required: true)
            .Build();
        await RespondWithModalAsync(modal);
    }

    [ModalInteraction(EditModalPrefix + ":*:*", ignoreGroupNames: true)]
    public async Task SaveEditAsync(string mode, string id, ContentEditModal modal)
    {
        await DeferAsync(ephemeral: true);
        var config = _constants.Config;
        if (mode == "live")
        {
            if (ContentRules.FindLive(_data, id, out var refusal) is not { } target)
            {
                await FailedEditAsync(refusal ?? "Nothing live to edit.", id, modal.Yaml);
                return;
            }
            var result = ContentRules.ApplyLiveEdit(_data, config, target, modal.Yaml);
            if (!result.Ok)
            {
                await FailedEditAsync(result.Message, id, modal.Yaml);
                return;
            }
            if (result.News is { } news)
                await _publisher.PostAsync(ChannelRole.News, news, $"content edit live {id}");
            await SendAsync(result.Message);
            return;
        }

        var edit = ContentRules.SaveEdit(_data, ContentRules.ImportContext(_data, config), id, modal.Yaml);
        if (!edit.Outcome.Imported)
        {
            await FailedEditAsync(ContentRules.Describe(edit.Outcome), id, modal.Yaml);
            return;
        }
        var message = ContentRules.Describe(edit.Outcome).TrimStart('-', ' ');
        if (edit.Copy) message += $"\nThe id changed, so this is a copy: `{id}` is unchanged.";
        await SendAsync(message);
    }

    private async Task FailedEditAsync(string message, string id, string yaml)
    {
        using var stream = new MemoryStream(new UTF8Encoding(false).GetBytes(yaml));
        await FollowupWithFileAsync(stream, $"{id}.yaml",
            Display.Truncate(message + "\nYour YAML is attached, so the edit isn't lost.", 2000),
            ephemeral: true, allowedMentions: AllowedMentions.None);
    }

    [SlashCommand("preview", "See privately what players will see from a template")]
    public async Task PreviewAsync([Summary("id", "The template"), Autocomplete(typeof(ContentAutocomplete))] string id)
    {
        await DeferAsync(ephemeral: true);
        await SendAsync(ContentPreview.Render(_data, _constants.Config, id.Trim()));
    }

    [SlashCommand("ready", "Validate a template and mark it Ready; a scheduled event is queued")]
    public async Task ReadyAsync([Summary("id", "The template"), Autocomplete(typeof(ContentAutocomplete))] string id)
    {
        await DeferAsync(ephemeral: true);
        await SendAsync(ContentRules.MakeReady(_data, _constants.Config, id.Trim()).Message);
    }

    [SlashCommand("list", "List templates")]
    public async Task ListAsync(
        [Summary("kind", "Only this kind")] TemplateKind? kind = null,
        [Summary("status", "Only this status")] TemplateStatus? status = null)
    {
        var rows = _data.GetTemplates(kind, status);
        if (rows.Count == 0)
        {
            await RespondAsync("No templates match.", ephemeral: true);
            return;
        }
        var sb = new StringBuilder($"**{rows.Count} template{(rows.Count == 1 ? "" : "s")}**\n");
        var shown = 0;
        foreach (var row in rows)
        {
            var line = $"`{row.Id}` · {TemplateCodec.KindName(row.Kind)} · {row.Status} · posted " +
                       $"{(row.TimesPosted == 1 ? "once" : $"{row.TimesPosted} times")}\n";
            if (sb.Length + line.Length > 1900) break;
            sb.Append(line);
            shown++;
        }
        if (shown < rows.Count) sb.Append($"…and {rows.Count - shown} more. Narrow it with `kind` or `status`.");
        await RespondAsync(sb.ToString().TrimEnd(), ephemeral: true);
    }

    [SlashCommand("remove", "Delete a Draft that was never posted; archive anything else")]
    public async Task RemoveAsync([Summary("id", "The template"), Autocomplete(typeof(ContentAutocomplete))] string id)
    {
        id = id.Trim();
        if (_data.GetTemplate(id) is not { } row)
        {
            await RespondAsync($"There is no template `{id}`.", ephemeral: true);
            return;
        }
        var deletes = row.Status == TemplateStatus.Draft && row.TimesPosted == 0;
        var components = _confirmations.Create(Context.User.Id, () => Task.FromResult(ContentRules.Remove(_data, id).Message),
            deletes ? "Delete" : "Archive");
        await RespondAsync(
            deletes
                ? $"Delete `{id}` ({TemplateCodec.KindName(row.Kind)})? It is a Draft that was never posted, so it goes for good."
                : $"Archive `{id}` ({TemplateCodec.KindName(row.Kind)})? It is {row.Status} or has been posted, so it is kept for the log, out of use.",
            components: components, ephemeral: true);
    }

    [SlashCommand("export", "Download every template, or one, as YAML files in a zip")]
    public async Task ExportAsync([Summary("id", "Only this template"), Autocomplete(typeof(ContentAutocomplete))] string? id = null)
    {
        await DeferAsync(ephemeral: true);
        List<TemplateRow> rows;
        if (id is null)
        {
            rows = _data.GetTemplates();
        }
        else if (_data.GetTemplate(id.Trim()) is { } row)
        {
            rows = [row];
        }
        else
        {
            await SendAsync($"There is no template `{id.Trim()}`.");
            return;
        }
        if (rows.Count == 0)
        {
            await SendAsync("There are no templates to export.");
            return;
        }

        using var stream = new MemoryStream(ContentRules.Export(rows));
        var name = rows.Count == 1 && id is not null ? $"{rows[0].Id}.zip" : "content.zip";
        await FollowupWithFileAsync(stream, name,
            $"{rows.Count} template{(rows.Count == 1 ? "" : "s")}. Edit the files and `/content import` the zip, or single files, to bring them back.",
            ephemeral: true);
    }

    private async Task SendAsync(string text)
    {
        foreach (var chunk in Publisher.Chunk(text))
            await FollowupAsync(chunk, ephemeral: true, allowedMentions: AllowedMentions.None);
    }
}

public class ContentEditModal : IModal
{
    public const string YamlInputId = "yaml";

    public string Title => "Edit template";

    [ModalTextInput(YamlInputId, TextInputStyle.Paragraph, maxLength: ContentRules.ModalLimit)]
    public string Yaml { get; set; } = "";
}
