public partial class GmModule
{
    private string OpinionByProvince(ProvinceDefinition? only)
    {
        var config = _constants.Config;
        var snapshot = _data.LoadOpinionSnapshot(config);
        var conditions = _data.GetProvinceConditions();
        var provinces = snapshot.Provinces.Where(p => only is null || p.Id == only.Id).ToList();
        var issues = _data.GetOpenIssues();
        var lines = new List<string>
        {
            only is null
                ? Texts.Get("gm.peek.opinion.header")
                : Texts.Get("gm.peek.opinion.headerProvince", ("province", only.Name)),
        };

        foreach (var issue in issues)
        {
            lines.Add("");
            lines.Add(Texts.Get("gm.peek.opinion.issue",
                ("issue", issue.Title), ("base", SignedFixed(issue.Opinion)),
                ("national", SignedFixed(snapshot.National(issue.Id))), ("salience", Fixed(issue.Salience))));
            foreach (var province in provinces)
            {
                var biases = conditions.GetValueOrDefault(province.Id, [])
                    .Select(id => (Id: id, Definition: config.Condition(id)))
                    .Where(c => c.Definition is not null && c.Definition.BiasOn(issue.Id) != 0)
                    .Select(c => Texts.Get("gm.peek.opinion.bias", ("condition", c.Definition!.Name), ("bias", SignedFixed(c.Definition.BiasOn(issue.Id)))))
                    .ToList();
                var opinion = SignedFixed(snapshot.Provincial(issue.Id, province.Id));
                lines.Add(biases.Count == 0
                    ? Texts.Get("gm.peek.opinion.provinceBase", ("province", province.Name), ("opinion", opinion))
                    : Texts.Get("gm.peek.opinion.provinceBiased",
                        ("province", province.Name), ("opinion", opinion), ("biases", string.Join(" ", biases))));
            }
        }
        if (issues.Count == 0) lines.Add(Texts.Get("gm.peek.noIssues"));
        else lines.Add(Texts.Get("gm.peek.opinion.footer"));
        return string.Join("\n", lines);
    }

    private string PendingActions()
    {
        var config = _constants.Config;
        var c = config.Constants;
        var preview = _data.PreviewActions(config);
        var plan = preview.Plan;
        var snapshot = preview.Snapshot;
        string Title(string issueId) => snapshot.Issue(issueId)?.Title ?? issueId;

        var lines = new List<string> { Texts.Get("gm.peek.actions.header"), Texts.Get("gm.peek.actions.caveat") };
        if (plan.Outcomes.Count == 0)
        {
            lines.Add(Texts.Get("gm.peek.actions.none"));
            return string.Join("\n", lines);
        }

        foreach (var group in plan.Outcomes.GroupBy(o => o.Action.FactionId))
        {
            lines.Add("");
            lines.Add(Texts.Get("gm.peek.actions.factionHeading", ("faction", Display.Faction(_data, group.Key))));
            foreach (var outcome in group)
                lines.Add(ActionLine(outcome, plan, snapshot, c, Title(outcome.Action.IssueId)));
        }

        lines.Add("");
        lines.Add(Texts.Get("gm.peek.actions.popularityHeading"));
        foreach (var (factionId, delta) in plan.PopularityDeltas.OrderBy(kv => kv.Value))
        {
            var before = _data.GetFaction(factionId)?.Popularity ?? 0;
            lines.Add(Texts.Get("gm.peek.actions.popularityLine",
                ("faction", Display.Faction(_data, factionId)), ("before", Fixed(before)), ("change", SignedFixed(delta)),
                ("after", Fixed(Math.Clamp(before + delta, c.Popularity.Min, c.Popularity.Max)))));
        }
        if (plan.PopularityDeltas.Count == 0) lines.Add(Texts.Get("gm.peek.actions.noChange"));

        if (plan.SalienceRaises.Count > 0)
            lines.Add(Texts.Get("gm.peek.actions.salience",
                ("issues", string.Join(", ", plan.SalienceRaises.Select(Title))), ("raise", Fixed(c.Salience.CampaignRaise)),
                ("max", Fixed(c.Salience.Max))));
        return string.Join("\n", lines);
    }

    private static string KindName(ActionKind kind) => kind switch
    {
        ActionKind.Campaign => Texts.Get("gm.peek.actions.kind.campaign"),
        ActionKind.Attack => Texts.Get("gm.peek.actions.kind.attack"),
        _ => Texts.Get("gm.peek.actions.kind.poll"),
    };

    private string ActionLine(ActionOutcome outcome, ActionPlan plan, OpinionSnapshot snapshot, GameConstants c, string issue)
    {
        var action = outcome.Action;
        if (outcome.Fizzled)
            return Texts.Get("gm.peek.actions.fizzled", ("kind", KindName(action.Kind)), ("issue", issue), ("result", outcome.Result));

        var national = snapshot.National(action.IssueId);
        var salience = snapshot.Salience(action.IssueId);
        switch (action.Kind)
        {
            case ActionKind.Campaign:
            {
                var stance = action.Stance!.Value;
                var crowd = c.Campaign.Crowding ? ActionPlanner.SameSideCampaigners(plan.StanceLogs, action.IssueId, stance) : 1;
                var line = Texts.Get("gm.peek.actions.campaign",
                    ("issue", issue), ("stance", stance.DisplayName()),
                    ("factor", Fixed(c.Campaign.Factor)), ("score", SignedFixed(Formulas.StanceScore(stance, national, c.StrongStancePenalty))),
                    ("salience", Fixed(salience)), ("crowd", crowd), ("change", SignedFixed(outcome.ActorChange)),
                    ("opinion", SignedFixed(national)));
                return action.OffProgramme ? line + Texts.Get("gm.peek.actions.offProgramme") : line;
            }
            case ActionKind.Attack:
            {
                var stance = outcome.TargetStance!.Value;
                var target = Display.Faction(_data, action.TargetFactionId!.Value);
                var score = SignedFixed(Formulas.StanceScore(stance, national, c.StrongStancePenalty));
                return outcome.Succeeded
                    ? Texts.Get("gm.peek.actions.attackHits",
                        ("target", target), ("stance", stance.DisplayName()), ("issue", issue), ("opinion", SignedFixed(national)),
                        ("score", score), ("factor", Fixed(c.Attack.Factor)), ("salience", Fixed(salience)), ("change", SignedFixed(outcome.TargetChange)))
                    : Texts.Get("gm.peek.actions.attackBackfires",
                        ("target", target), ("stance", stance.DisplayName()), ("issue", issue), ("opinion", SignedFixed(national)),
                        ("score", score), ("change", SignedFixed(outcome.ActorChange)));
            }
            default:
            {
                var opinion = action.ProvinceId is { } provinceId ? snapshot.Provincial(action.IssueId, provinceId) : national;
                return Texts.Get("gm.peek.actions.poll",
                    ("issue", issue), ("where", action.ProvinceId is { } id ? Display.Province(_constants.Config, id) : Texts.Get("gm.peek.actions.nation")),
                    ("opinion", SignedFixed(opinion)), ("salience", Fixed(salience)),
                    ("opinionNoise", Fixed(c.Poll.OpinionNoise)), ("salienceNoise", Fixed(c.Poll.SalienceNoise)));
            }
        }
    }
}
