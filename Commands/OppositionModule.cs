using Discord;
using Discord.Interactions;

[Group("opposition", "The opposition's actions")]
public class OppositionModule : InteractionModuleBase<SocketInteractionContext>
{
    private const int QuestionLimit = 1500;

    private readonly DatabaseService _data;
    private readonly ConstantsStore _constants;
    private readonly GovernmentPublisher _government;

    public OppositionModule(DatabaseService data, ConstantsStore constants, GovernmentPublisher government)
    {
        _data = data;
        _constants = constants;
        _government = government;
    }

    [SlashCommand("condemn", "Secretly back a side on a live crisis, revealed when it resolves; spends your opposition action")]
    [RequireFaction]
    public async Task CondemnAsync(
        [Summary("crisis", "A live crisis"), Autocomplete(typeof(LiveCrisisAutocomplete))] string crisis,
        [Summary("side", "The side of the crisis's issue you stand on")] CondemnSide side)
    {
        var target = ResponsibleCrisisAutocomplete.Find(_data, _data.GetCrises(CrisisStatus.Live), crisis);
        if (target is null)
        {
            await RespondAsync(_data.CondemnRefusal(Context.User.Id, null, _constants.Current) ?? Texts.Get("crisis.refusal.gone"),
                ephemeral: true, allowedMentions: AllowedMentions.None);
            return;
        }

        await DeferAsync(ephemeral: true);
        var result = _data.Condemn(Context.User.Id, target.Id, side == CondemnSide.For ? Lean.For : Lean.Against, _constants.Current);
        if (result is not { Crisis: { } condemned, Condemnation: not null })
        {
            await FollowupAsync(result.Refusal ?? Texts.Get("crisis.refusal.gone"), ephemeral: true, allowedMentions: AllowedMentions.None);
            return;
        }

        await FollowupAsync(Texts.Get("condemn.done", ("crisis", condemned.Title), ("when", Display.NextResolution(_data.GetGameState()))),
            ephemeral: true, allowedMentions: AllowedMentions.None);
    }

    [SlashCommand("whipcount", "Secretly predict a cabinet member breaking ranks on a bill; spends the season's opposition action")]
    [RequireFaction]
    public async Task WhipCountAsync(
        [Summary("bill", "A bill the Assembly is voting on"), Autocomplete(typeof(OpenReadingAutocomplete))] string bill,
        [Summary("member", "The cabinet member"), Autocomplete(typeof(CabinetMemberAutocomplete))] string member,
        [Summary("vote", "How you predict they will vote")] WhipCountSide vote)
    {
        if (VoteSubjectAutocomplete.Parse(bill) is not (VoteSubject.Reading, var readingId))
        {
            await RespondAsync(Texts.Get("whipCount.refusal.notOpen"), ephemeral: true);
            return;
        }
        if (FactionAutocomplete.Find(_data, member) is not { } target)
        {
            await RespondAsync(Texts.Get("whipCount.refusal.noMember"), ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);
        var result = _data.MakeWhipCount(Context.User.Id, readingId, target.UserId, vote == WhipCountSide.For ? VoteChoice.Yes : VoteChoice.No, _constants.Current);
        if (result.Count is not { } count)
        {
            await FollowupAsync(result.Refusal ?? Texts.Get("whipCount.refusal.notOpen"), ephemeral: true, allowedMentions: AllowedMentions.None);
            return;
        }

        await FollowupAsync(Texts.Get("whipCount.done",
                ("member", Display.Faction(_data, count.MinisterId)), ("choice", AssemblyPublisher.ChoiceName(count.Side)), ("bill", result.BillTitle),
                ("when", Display.NextResolution(_data.GetGameState()))),
            ephemeral: true, allowedMentions: AllowedMentions.None);
    }

    [SlashCommand("question", "Question a minister publicly; spends the season's opposition action")]
    [RequireFaction]
    public async Task QuestionAsync([Summary("ministry", "The ministry to question")] CabinetMinistryChoice ministry)
    {
        var target = ministry.ToMinistry();
        if (_data.QuestionRefusal(Context.User.Id, target, _constants.Current) is { } refusal)
        {
            await RespondAsync(refusal, ephemeral: true, allowedMentions: AllowedMentions.None);
            return;
        }

        var modal = new ModalBuilder()
            .WithTitle(Display.Truncate(Texts.Get("questions.modal.askTitle", ("ministry", target.DisplayName())), 45))
            .WithCustomId($"question:ask:{target}")
            .AddTextInput(Texts.Get("questions.modal.askLabel"), GovernmentTextModal.InputId, TextInputStyle.Paragraph, maxLength: QuestionLimit, required: true)
            .Build();
        await RespondWithModalAsync(modal);
    }

    [ModalInteraction("question:ask:*", ignoreGroupNames: true)]
    public async Task AskAsync(string rawMinistry, GovernmentTextModal modal)
    {
        if (!Enum.TryParse<Ministry>(rawMinistry, out var ministry))
        {
            await RespondAsync(Texts.Get("questions.refusal.notAMinistry"), ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);
        var result = _data.AskQuestion(Context.User.Id, ministry, modal.Text, _constants.Current);
        if (result.Question is not { } question)
        {
            await FollowupAsync(result.Refusal ?? Texts.Get("questions.refusal.gone"), ephemeral: true, allowedMentions: AllowedMentions.None);
            return;
        }

        await _government.PostQuestionAsync(question, "opposition question");
        var minister = question.MinisterAtAsk is { } id ? Display.Faction(_data, id) : Texts.Get("cabinet.vacant");
        await FollowupAsync(Texts.Get("questions.asked",
                ("ministry", ministry.DisplayName()), ("minister", minister),
                ("due", GovernmentPublisher.Deadline(_data.GetGameState(), question.DueResolution))),
            ephemeral: true, allowedMentions: AllowedMentions.None);
    }

    [ComponentInteraction("question:answer:*", ignoreGroupNames: true)]
    public async Task AnswerButtonAsync(string rawId)
    {
        if (!long.TryParse(rawId, out var questionId) || _data.GetQuestion(questionId) is not { } question)
        {
            await RespondAsync(Texts.Get("questions.refusal.gone"), ephemeral: true);
            return;
        }
        if (_data.AnswerRefusal(questionId, Context.User.Id) is { } refusal)
        {
            await RespondAsync(refusal, ephemeral: true, allowedMentions: AllowedMentions.None);
            return;
        }

        var modal = new ModalBuilder()
            .WithTitle(Display.Truncate(Texts.Get("questions.modal.answerTitle", ("ministry", question.Ministry.DisplayName())), 45))
            .WithCustomId($"question:reply:{questionId}")
            .AddTextInput(Texts.Get("questions.modal.answerLabel"), GovernmentTextModal.InputId, TextInputStyle.Paragraph, maxLength: QuestionLimit, required: true)
            .Build();
        await RespondWithModalAsync(modal);
    }

    [ModalInteraction("question:reply:*", ignoreGroupNames: true)]
    public async Task ReplyAsync(string rawId, GovernmentTextModal modal)
    {
        if (!long.TryParse(rawId, out var questionId))
        {
            await RespondAsync(Texts.Get("questions.refusal.gone"), ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);
        var result = _data.AnswerQuestion(questionId, Context.User.Id, modal.Text);
        if (result.Question is not { } question)
        {
            await FollowupAsync(result.Refusal ?? Texts.Get("questions.refusal.gone"), ephemeral: true, allowedMentions: AllowedMentions.None);
            return;
        }

        await _government.PostAnswerAsync(question, "question answer");
        await FollowupAsync(Texts.Get("questions.answered"), ephemeral: true);
    }
}

public class GovernmentTextModal : IModal
{
    public const string InputId = "text";

    public string Title => Texts.Get("questions.modal.title");

    [ModalTextInput(InputId, TextInputStyle.Paragraph)]
    public string Text { get; set; } = "";
}

public enum CondemnSide { For, Against }

public enum WhipCountSide { For, Against }
