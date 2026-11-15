using Discord;
using Discord.Interactions;

public enum PublicChart
{
    [ChoiceDisplay("Economy")] Economy,
    [ChoiceDisplay("Budget, reserve and purses")] Money,
    [ChoiceDisplay("Popularity")] Popularity,
}

public enum HiddenChart
{
    [ChoiceDisplay("Anzarian Favour and Foreign Threat")] Realm,
    [ChoiceDisplay("Discontent by province")] Discontent,
}

public static class ChartRequests
{
    public static RenderedChart? Public(DatabaseService data, PublicChart what, long? partyId, string fileName = Charts.DefaultFileName)
    {
        data.RecordHistoryIfMissing();
        var history = data.GetHistory();
        static string Money(double v) => Display.Crowns((decimal)v);
        return what switch
        {
            PublicChart.Economy => Charts.Build(history, Texts.Get("chart.title.economy"),
                [new(Texts.Get("chart.series.economy"), HistorySeries.Economy)], Display.Number, fileName: fileName),
            PublicChart.Money => Charts.Build(history, Texts.Get("chart.title.money"),
                [
                    new(Texts.Get("chart.series.budget"), HistorySeries.Budget),
                    new(Texts.Get("chart.series.reserve"), HistorySeries.Reserve),
                    .. GameEnumExtensions.CabinetMinistries.Select(m => new ChartSeries(m.DisplayName(), HistorySeries.Purse(m))),
                ], Money, floor: 0, fileName: fileName),
            _ => Charts.Build(history,
                partyId is { } id && data.GetParty(id) is { } party
                    ? Texts.Get("chart.title.partyPopularity", ("party", party.Name))
                    : Texts.Get("chart.title.popularity", ("count", Charts.MaxSeries)),
                (partyId is { } pid ? data.GetPartyMembers(pid) : data.GetActiveFactions())
                    .Take(Charts.MaxSeries)
                    .Select(f => new ChartSeries(Display.Faction(f), HistorySeries.Popularity(f.UserId)))
                    .ToList(),
                Display.Number, floor: 0, fileName: fileName),
        };
    }

    public static RenderedChart? Hidden(DatabaseService data, GameConfig config, HiddenChart what, ProvinceDefinition? province)
    {
        data.RecordHistoryIfMissing();
        var history = data.GetHistory();
        if (what == HiddenChart.Realm)
            return Charts.Build(history, Texts.Get("chart.title.realm"),
                [new(Texts.Get("chart.series.favour"), HistorySeries.Favour), new(Texts.Get("chart.series.threat"), HistorySeries.Threat)],
                Display.Number, floor: 0);

        var provinces = province is not null
            ? [new ChartSeries(province.Name, HistorySeries.Discontent(province.Id))]
            : data.GetProvinces().OrderByDescending(p => p.Discontent).ThenBy(p => p.Id).Take(Charts.MaxSeries)
                .Select(p => new ChartSeries(p.Name, HistorySeries.Discontent(p.Id))).ToList();
        return Charts.Build(history,
            province is null ? Texts.Get("chart.title.discontent", ("count", Charts.MaxSeries)) : Texts.Get("chart.title.discontentIn", ("province", province.Name)),
            provinces, Display.Number, floor: 0);
    }

    public static async Task SendAsync(IDiscordInteraction interaction, RenderedChart? chart, bool ephemeral)
    {
        if (chart is null)
        {
            await interaction.FollowupAsync(Texts.Get("chart.noHistory"), ephemeral: true);
            return;
        }
        using (chart)
        using (var attachment = chart.Attachment())
            await interaction.FollowupWithFileAsync(attachment, embed: chart.Embed, ephemeral: ephemeral, allowedMentions: AllowedMentions.None);
    }
}

public class ChartModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly DatabaseService _data;

    public ChartModule(DatabaseService data)
    {
        _data = data;
    }

    [SlashCommand("chart", "Graph the economy, the money or popularity over the turns")]
    public async Task ChartAsync(
        [Summary("what", "What to graph")] PublicChart what,
        [Summary("party", "Popularity: only this party's members (default: the most popular factions)"), Autocomplete(typeof(PartyAutocomplete))] long? party = null,
        [Summary("public", "Post it in the channel for everyone (default: only you see it)")] bool shared = false)
    {
        await DeferAsync(ephemeral: !shared);
        await ChartRequests.SendAsync(Context.Interaction, ChartRequests.Public(_data, what, party), !shared);
    }
}
