using System.Globalization;
using Discord;
using Discord.Interactions;

public enum PeekMode
{
    [ChoiceDisplay("Hidden state")] State,
    [ChoiceDisplay("Election preview")] Election,
    [ChoiceDisplay("Party strength working")] Strength,
    [ChoiceDisplay("Opinion by province")] Opinion,
    [ChoiceDisplay("Pending actions")] Actions,
}

public partial class GmModule
{
    [SlashCommand("peek", "Show the hidden state, previews and the working behind them, privately")]
    public async Task PeekAsync(
        [Summary("mode", "What to show (default: the hidden state)")] PeekMode mode = PeekMode.State,
        [Summary("province", "Strength or opinion mode: only this province"), Autocomplete(typeof(ProvinceAutocomplete))] string? province = null)
    {
        await DeferAsync(ephemeral: true);
        if (mode == PeekMode.Election)
        {
            await PeekElectionAsync();
            return;
        }

        ProvinceDefinition? found = null;
        if (province is not null && (found = ProvinceAutocomplete.Find(_constants.Config, province)) is null)
        {
            await FollowupAsync(Texts.Get("gm.peek.noProvince", ("province", province)), ephemeral: true, allowedMentions: AllowedMentions.None);
            return;
        }

        var text = mode switch
        {
            PeekMode.Strength => found is null ? StrengthOverview() : StrengthWorking(found),
            PeekMode.Opinion => OpinionByProvince(found),
            PeekMode.Actions => PendingActions(),
            _ => HiddenState(),
        };
        foreach (var chunk in Publisher.Chunk(text))
            await FollowupAsync(chunk, ephemeral: true, allowedMentions: AllowedMentions.None);
    }

    private static string Fixed(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string SignedFixed(double value) =>
        (value >= 0 ? "+" : "−") + Math.Abs(value).ToString("0.000", CultureInfo.InvariantCulture);

    private string StrengthOverview()
    {
        var config = _constants.Config;
        var input = _data.BuildElectionInput(config);
        var parties = _data.GetPartySnapshots().ToDictionary(p => p.Id);
        var lines = new List<string>
        {
            Texts.Get("gm.peek.strength.overviewHeader"),
            Constants(input),
            "",
            Texts.Get("gm.peek.strength.partiesHeading"),
        };

        foreach (var party in input.Parties.OrderBy(p => p.Id))
        {
            var members = input.Factions.Where(f => f.PartyId == party.Id).OrderByDescending(f => f.Popularity).ToList();
            lines.Add(Texts.Get("gm.peek.strength.partyLine",
                ("party", PartyLabel(parties, party.Id)),
                ("popularity", Fixed(members.Sum(f => f.Popularity))),
                ("members", string.Join(", ", members.Select(f => Texts.Get("gm.peek.strength.memberHome",
                    ("faction", Display.Faction(_data, f.Id)), ("popularity", Fixed(f.Popularity)),
                    ("home", Display.Province(config, f.HomeProvinceId))))))));
        }
        if (input.Parties.Count == 0) lines.Add(Texts.Get("gm.peek.strength.noParties"));

        lines.Add("");
        lines.Add(Texts.Get("gm.peek.strength.provincesHeading"));
        foreach (var province in input.Provinces)
        {
            var cells = ElectionCalculator.Explain(input, province.Id)
                .OrderByDescending(b => b.Strength)
                .ThenBy(b => b.PartyId)
                .Select(b => Texts.Get("gm.peek.strength.cell",
                    ("party", PartyEmoji(parties, b.PartyId)), ("strength", Fixed(b.Strength)), ("multiplier", Fixed(b.Multiplier))));
            lines.Add(Texts.Get("gm.peek.strength.provinceLine",
                ("province", Display.Province(config, province.Id)), ("cells", string.Join(Texts.Get("election.provinceSeparator"), cells))));
        }
        lines.Add("");
        lines.Add(Texts.Get("gm.peek.strength.overviewHint"));
        return string.Join("\n", lines);
    }

    private string StrengthWorking(ProvinceDefinition province)
    {
        var config = _constants.Config;
        var input = _data.BuildElectionInput(config);
        var parties = _data.GetPartySnapshots().ToDictionary(p => p.Id);
        var titles = _data.GetOpenIssues().ToDictionary(i => i.Id, i => i.Title);
        var outcome = ElectionCalculator.Run(input).Provinces.FirstOrDefault(p => p.ProvinceId == province.Id);
        var c = input.Constants;
        var lines = new List<string>
        {
            Texts.Get("gm.peek.strength.workingHeader", ("province", province.Name)),
            Constants(input),
        };

        var breakdowns = ElectionCalculator.Explain(input, province.Id).OrderByDescending(b => b.Strength).ThenBy(b => b.PartyId).ToList();
        var total = breakdowns.Sum(b => b.Strength);
        foreach (var b in breakdowns)
        {
            lines.Add("");
            lines.Add(Texts.Get("gm.peek.strength.partyHeading",
                ("party", PartyLabel(parties, b.PartyId)),
                ("strength", Fixed(b.Strength)),
                ("share", (total > 0 ? b.Strength / total * 100 : 0).ToString("0.0", CultureInfo.InvariantCulture)),
                ("contested", outcome?.ContestedSeatsByParty.GetValueOrDefault(b.PartyId) ?? 0),
                ("boroughs", outcome?.BoroughSeatsByParty.GetValueOrDefault(b.PartyId) ?? 0)));
            if (outcome?.WinnerPartyId == b.PartyId) lines.Add(Texts.Get("gm.peek.strength.winner"));

            var members = b.Members.Select(m => m.AtHome
                ? Texts.Get("gm.peek.strength.memberAtHome", ("faction", Display.Faction(_data, m.FactionId)),
                    ("popularity", Fixed(m.Popularity)), ("home", Fixed(c.HomeMultiplier)), ("weighted", Fixed(m.Weighted)))
                : Texts.Get("gm.peek.strength.memberAway", ("faction", Display.Faction(_data, m.FactionId)), ("popularity", Fixed(m.Popularity))));
            lines.Add(Texts.Get("gm.peek.strength.popularity",
                ("members", string.Join(" + ", members)), ("weighted", Fixed(b.Weighted)),
                ("exponent", Fixed(c.BaseExponent)), ("base", Fixed(b.BaseStrength))));

            lines.Add(Texts.Get("gm.peek.strength.programme",
                ("score", SignedFixed(b.ProgrammeScore)), ("weight", Fixed(c.ProgrammeWeight)),
                ("min", Fixed(c.MultiplierMin)), ("max", Fixed(c.MultiplierMax)), ("multiplier", Fixed(b.Multiplier))));
            lines.AddRange(b.Issues.Select(t => Texts.Get("gm.peek.strength.issue",
                ("issue", titles.GetValueOrDefault(t.IssueId, t.IssueId)), ("stance", t.Stance.DisplayName()),
                ("opinion", SignedFixed(t.Opinion)), ("share", Fixed(t.SalienceShare)), ("score", SignedFixed(t.StanceScore)),
                ("crowd", t.Crowd), ("contribution", SignedFixed(t.Contribution)))));
            if (b.Issues.Count == 0) lines.Add(Texts.Get("gm.peek.strength.noStances"));

            lines.Add(Texts.Get("gm.peek.strength.result",
                ("base", Fixed(b.BaseStrength)), ("multiplier", Fixed(b.Multiplier)), ("strength", Fixed(b.Strength))));
        }
        if (breakdowns.Count == 0) lines.Add(Texts.Get("gm.peek.strength.noParties"));
        return string.Join("\n", lines);
    }

    private static string Constants(ElectionInput input)
    {
        var c = input.Constants;
        (string, object?)[] values =
        [
            ("exponent", Fixed(c.BaseExponent)), ("home", Fixed(c.HomeMultiplier)), ("weight", Fixed(c.ProgrammeWeight)),
            ("min", Fixed(c.MultiplierMin)), ("max", Fixed(c.MultiplierMax)), ("penalty", Fixed(input.StrongStancePenalty)),
        ];
        return c.Crowding ? Texts.Get("gm.peek.strength.constantsCrowding", values) : Texts.Get("gm.peek.strength.constants", values);
    }

    private static string PartyLabel(IReadOnlyDictionary<long, ElectionPartySnapshot> parties, long id) =>
        parties.TryGetValue(id, out var party) ? ElectionPublisher.PartyName(party) : Texts.Get("election.unknownParty", ("id", id));

    private static string PartyEmoji(IReadOnlyDictionary<long, ElectionPartySnapshot> parties, long id) =>
        parties.TryGetValue(id, out var party) ? ElectionPublisher.PartyEmoji(party) : Texts.Get("election.unknownParty", ("id", id));

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
            + ElectionPublisher.ResultsBody(result, parties, config);
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
