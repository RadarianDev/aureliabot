using System.Globalization;
using Discord;
using Discord.Interactions;

[Group("treasury", "The Treasury: the yearly budget and the reserve")]
public class TreasuryModule : InteractionModuleBase<SocketInteractionContext>
{
    private const string ModalId = "treasury:allocate";

    private readonly DatabaseService _data;
    private readonly Confirmations _confirmations;

    public TreasuryModule(DatabaseService data, Confirmations confirmations)
    {
        _data = data;
        _confirmations = confirmations;
    }

    [SlashCommand("allocate", "During Winter, divide next year's budget between the ministries and the Navy contribution")]
    [RequireFaction]
    public async Task AllocateAsync()
    {
        var window = _data.OpenAllocation(Context.User.Id);
        if (window.Refusal is not null)
        {
            await RespondAsync(window.Refusal, ephemeral: true, allowedMentions: AllowedMentions.None);
            return;
        }

        var budget = Display.Crowns(window.Budget);
        var modal = new ModalBuilder()
            .WithTitle(Display.Truncate(Texts.Get("treasury.modal.title", ("year", window.Year)), 45))
            .WithCustomId(ModalId);
        foreach (var ministry in DatabaseService.Purses)
            modal.AddTextInput(Display.Truncate(Texts.Get("treasury.modal.label", ("ministry", ministry.DisplayName()), ("budget", budget)), 45),
                AllocationModal.InputId(ministry), TextInputStyle.Short, maxLength: 12, required: true,
                value: window.Prefill.GetValueOrDefault(ministry).ToString("0.##", CultureInfo.InvariantCulture));
        await RespondWithModalAsync(modal.Build());
    }

    [ModalInteraction(ModalId, ignoreGroupNames: true)]
    public async Task SubmitAllocationAsync(AllocationModal modal)
    {
        var amounts = new Dictionary<Ministry, decimal>();
        foreach (var ministry in DatabaseService.Purses)
        {
            var raw = modal.ValueOf(ministry);
            if (ParseCrowns(raw) is not { } amount)
            {
                await RespondAsync(Texts.Get("treasury.refusal.notNumber", ("ministry", ministry.DisplayName()), ("value", raw)), ephemeral: true);
                return;
            }
            amounts[ministry] = amount;
        }

        var userId = Context.User.Id;
        var preview = _data.PreviewAllocation(userId, amounts);
        if (preview.Refusal is not null)
        {
            await RespondAsync(preview.Refusal, ephemeral: true, allowedMentions: AllowedMentions.None);
            return;
        }

        var state = _data.GetGameState();
        var lines = new List<string> { Texts.Get("treasury.preview.heading", ("year", preview.Year), ("budget", Display.Crowns(preview.Budget))) };
        lines.AddRange(DatabaseService.Purses.Where(m => m != Ministry.Navy).Select(m =>
            Texts.Get("treasury.preview.line", ("ministry", m.DisplayName()), ("amount", Display.Crowns(preview.Amounts[m])),
                ("share", Share(preview.Amounts[m], preview.Budget)))));
        lines.Add(Texts.Get("treasury.preview.navyLine", ("amount", Display.Crowns(preview.Amounts[Ministry.Navy])),
            ("share", Percent(preview.NavyShare)), ("last", Percent(preview.LastNavyShare))));
        lines.Add(Texts.Get("treasury.preview.reserve", ("amount", Display.Crowns(preview.ToReserve))));
        lines.Add(preview.Reaction switch
        {
            NavyReaction.Pleased => Texts.Get("treasury.preview.pleased"),
            NavyReaction.Insulted => Texts.Get("treasury.preview.insulted"),
            _ => Texts.Get("treasury.preview.indifferent"),
        });
        lines.Add(Texts.Get("treasury.preview.confirm", ("when", Display.NextResolution(state))));

        await RespondAsync(string.Join("\n", lines),
            components: _confirmations.Create(userId, () => Task.FromResult(Submit(userId, amounts)), Texts.Get("treasury.preview.button"), ButtonStyle.Success),
            ephemeral: true, allowedMentions: AllowedMentions.None);
    }

    private string Submit(ulong userId, IReadOnlyDictionary<Ministry, decimal> amounts)
    {
        var result = _data.SubmitAllocation(userId, amounts);
        if (result.Refusal is not null) return result.Refusal;
        var when = Display.NextResolution(_data.GetGameState());
        return result.Replaced
            ? Texts.Get("treasury.resubmitted", ("year", result.Year), ("when", when))
            : Texts.Get("treasury.submitted", ("year", result.Year), ("when", when));
    }

    [SlashCommand("grant", "Grant money from the reserve to a ministry's purse")]
    [RequireFaction]
    public async Task GrantAsync(
        [Summary("ministry", "The ministry to receive the money")] CabinetMinistryChoice ministry,
        [Summary("amount", "Crowns to grant from the reserve"), MinValue(0.01)] double amount)
    {
        var result = _data.Grant(Context.User.Id, ministry.ToMinistry(), (decimal)amount);
        await RespondAsync(result.Refusal ?? Texts.Get("treasury.grant.done", ("amount", Display.Crowns(result.Amount)),
                ("ministry", result.Ministry.DisplayName()), ("balance", Display.Crowns(result.Balance)), ("reserve", Display.Crowns(result.Reserve))),
            ephemeral: true, allowedMentions: AllowedMentions.None);
    }

    private static decimal? ParseCrowns(string raw)
    {
        var text = raw.Trim().Replace(',', '.');
        if (text.EndsWith("cr", StringComparison.OrdinalIgnoreCase)) text = text[..^2].Trim();
        if (text.Length == 0) return 0;
        return decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private static string Share(decimal amount, decimal budget) => Percent(budget <= 0 ? 0 : (double)(amount / budget));

    private static string Percent(double share) => share.ToString("0.#%", CultureInfo.InvariantCulture);
}

public class AllocationModal : IModal
{
    public static string InputId(Ministry ministry) => ministry.ToString().ToLowerInvariant();

    public string Title => Texts.Get("treasury.modal.fallbackTitle");

    [ModalTextInput("war")]
    public string War { get; set; } = "";

    [ModalTextInput("interior")]
    public string Interior { get; set; } = "";

    [ModalTextInput("commercelabor")]
    public string CommerceLabor { get; set; } = "";

    [ModalTextInput("treasury")]
    public string Treasury { get; set; } = "";

    [ModalTextInput("navy")]
    public string Navy { get; set; } = "";

    public string ValueOf(Ministry ministry) => ministry switch
    {
        Ministry.War => War,
        Ministry.Interior => Interior,
        Ministry.CommerceLabor => CommerceLabor,
        Ministry.Treasury => Treasury,
        _ => Navy,
    };
}
