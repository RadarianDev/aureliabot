using System.Globalization;
using Discord;
using Discord.Interactions;

public partial class GmModule
{
    [SlashCommand("peek", "Show the hidden state privately, or run the election on the current state")]
    public async Task PeekAsync(
        [Summary("election", "Run the election on the current state instead")] bool election = false)
    {
        await DeferAsync(ephemeral: true);
        if (election)
            await PeekElectionAsync();
        else
            foreach (var chunk in Publisher.Chunk(HiddenState()))
                await FollowupAsync(chunk, ephemeral: true, allowedMentions: AllowedMentions.None);
    }

    private static string Fixed(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private string HiddenState()
    {
        var config = _constants.Config;
        var state = _data.GetGameState();
        var snapshot = _data.LoadOpinionSnapshot(config);
        var lines = new List<string>
        {
            Texts.Get("gm.peek.header", ("label", state.Label)),
            Texts.Get("gm.peek.kingdom",
                ("favour", Fixed(state.Favour)), ("baseline", Fixed(state.FavourBaseline)), ("threat", Fixed(state.Threat)),
                ("economy", Fixed(state.Economy)), ("tax", Fixed(state.TaxRate)), ("reserve", Display.Crowns(state.Reserve))),
            "",
            Texts.Get("gm.peek.issuesHeading"),
        };

        var issues = _data.GetOpenIssues();
        lines.AddRange(issues.Select(i => Texts.Get("gm.peek.issueLine",
            ("issue", i.Title), ("base", Fixed(i.Opinion)), ("national", Fixed(snapshot.National(i.Id))), ("salience", Fixed(i.Salience)))));
        if (issues.Count == 0) lines.Add(Texts.Get("gm.peek.noIssues"));

        var variables = _data.GetVariables(config);
        if (variables.Count > 0)
        {
            lines.Add("");
            lines.Add(Texts.Get("gm.peek.variablesHeading"));
            lines.AddRange(variables.Select(v => Texts.Get("gm.peek.variableLine",
                ("name", v.Definition.Id), ("value", EffectParser.FormatNumber(v.Value)), ("description", v.Definition.Description))));
        }

        lines.Add("");
        lines.Add(Texts.Get("gm.peek.provincesHeading"));
        var conditions = _data.GetProvinceConditions();
        foreach (var province in _data.GetProvinces())
        {
            var names = conditions.GetValueOrDefault(province.Id, []).Select(id => config.Condition(id)?.Name ?? id).ToList();
            lines.Add(Texts.Get("gm.peek.provinceLine",
                ("province", province.Name),
                ("discontent", Fixed(province.Discontent)),
                ("conditions", names.Count == 0 ? Texts.Get("gm.peek.noConditions") : string.Join(", ", names))));
        }
        return string.Join("\n", lines);
    }

    private async Task PeekElectionAsync()
    {
        var config = _constants.Config;
        var result = ElectionCalculator.Run(_data.BuildElectionInput(config));
        var parties = _data.GetPartySnapshots();
        var text = Texts.Get("gm.peek.electionHeader") + "\n"
            + ElectionPublisher.ResultsBody(result, parties, config, id => Display.Faction(_data, id));
        foreach (var chunk in Publisher.Chunk(text))
            await FollowupAsync(chunk, ephemeral: true, allowedMentions: AllowedMentions.None);

        var winners = ElectionPublisher.WinnersFromResult(result);
        using var images = ElectionPublisher.Render(result, parties, winners, config, out var failure);
        if (failure is not null)
            await FollowupAsync(Texts.Get("gm.peek.renderFailed", ("error", failure)), ephemeral: true);

        var attachments = images.Attachments();
        if (attachments.Count == 0) return;
        try
        {
            var embeds = ElectionPublisher.Embeds(images, winners, parties, config, Texts.Get("gm.peek.chartTitle"));
            await FollowupWithFilesAsync(attachments, embeds: embeds, ephemeral: true, allowedMentions: AllowedMentions.None);
        }
        finally
        {
            foreach (var attachment in attachments) attachment.Dispose();
        }
    }
}
