using Discord;
using Discord.Interactions;

public partial class GmModule
{
    private const string ProclaimModalId = "gm_proclaim";

    [SlashCommand("proclaim", "Post a royal proclamation to #news")]
    public async Task ProclaimAsync()
    {
        var modal = new ModalBuilder()
            .WithTitle(Texts.Get("gm.proclaim.modalTitle"))
            .WithCustomId(ProclaimModalId)
            .AddTextInput(Texts.Get("gm.proclaim.titleLabel"), ProclamationModal.TitleId, TextInputStyle.Short, maxLength: 200, required: false)
            .AddTextInput(Texts.Get("gm.proclaim.textLabel"), ProclamationModal.TextId, TextInputStyle.Paragraph, maxLength: 1800, required: true)
            .Build();
        await RespondWithModalAsync(modal);
    }

    [ModalInteraction(ProclaimModalId, ignoreGroupNames: true)]
    public async Task ProclaimSubmittedAsync(ProclamationModal modal)
    {
        var title = modal.ProclamationTitle?.Trim() ?? "";
        var text = modal.Text.Trim();
        if (text.Length == 0)
        {
            await RespondAsync(Texts.Get("gm.proclaim.empty"), ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);
        var post = await _publisher.PostAsync(ChannelRole.News, title.Length == 0
            ? Texts.Get("gm.proclaim.post", ("text", text))
            : Texts.Get("gm.proclaim.postTitled", ("title", title), ("text", text)), "gm proclaim");
        _data.Log(LogKinds.GmOverride, "gm proclaim", title.Length == 0 ? text : $"{title}: {text}", isPublic: true);
        await FollowupAsync(post is null ? Texts.Get("gm.proclaim.noNews") : Texts.Get("gm.proclaim.posted"), ephemeral: true);
    }
}

public class ProclamationModal : IModal
{
    public const string TitleId = "title";
    public const string TextId = "text";

    public string Title => Texts.Get("gm.proclaim.modalTitle");

    [RequiredInput(false)]
    [ModalTextInput(TitleId, TextInputStyle.Short)]
    public string? ProclamationTitle { get; set; }

    [ModalTextInput(TextId, TextInputStyle.Paragraph)]
    public string Text { get; set; } = "";
}
