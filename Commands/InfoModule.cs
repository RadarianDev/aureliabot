using System.Text;
using Discord;
using Discord.Interactions;

public class InfoAutocomplete : AutocompleteHandler
{
    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        var data = (DatabaseService)services.GetService(typeof(DatabaseService))!;
        var constants = (ConstantsStore)services.GetService(typeof(ConstantsStore))!;
        var typed = autocompleteInteraction.Data.Current.Value?.ToString() ?? "";

        var entries = new List<(string Label, string Value)>();
        foreach (var party in data.GetAllParties())
            entries.Add((Texts.Get("info.kind.party", ("name", party.Name)), $"party:{party.Id}"));
        foreach (var faction in data.GetActiveFactions())
            entries.Add((Texts.Get("info.kind.faction", ("name", FactionLabel(faction, context))), $"faction:{faction.UserId}"));
        foreach (var bill in data.GetBills())
            entries.Add((Texts.Get("info.kind.bill", ("name", bill.Title)), $"bill:{bill.Id}"));
        foreach (var issue in data.GetAllIssues())
            entries.Add((Texts.Get("info.kind.issue", ("name", issue.Title)), $"issue:{issue.Id}"));
        foreach (var province in constants.Config.Provinces)
            entries.Add((Texts.Get("info.kind.province", ("name", province.Name)), $"province:{province.Id}"));

        var results = entries
            .Where(e => e.Label.Contains(typed, StringComparison.OrdinalIgnoreCase))
            .Take(25)
            .Select(e => new AutocompleteResult(Display.Truncate(e.Label, 100), e.Value));
        return Task.FromResult(AutocompletionResult.FromSuccess(results));
    }

    private static string FactionLabel(Faction faction, IInteractionContext context)
    {
        var user = (context.Guild as Discord.WebSocket.SocketGuild)?.GetUser(faction.UserId);
        var name = user?.DisplayName ?? faction.UserId.ToString();
        return string.IsNullOrWhiteSpace(faction.CharacterName) ? name : $"{faction.CharacterName} ({name})";
    }
}

public class InfoModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly DatabaseService _data;
    private readonly ConstantsStore _constants;

    public InfoModule(DatabaseService data, ConstantsStore constants)
    {
        _data = data;
        _constants = constants;
    }

    [SlashCommand("info", "Show any game object: a faction, party, issue or province")]
    public async Task InfoAsync(
        [Summary("thing", "What to show; leave empty for what is live"), Autocomplete(typeof(InfoAutocomplete))] string? thing = null)
    {
        var text = thing is null ? Live() : Describe(thing.Trim());
        await RespondAsync(Display.Truncate(text), ephemeral: true, allowedMentions: AllowedMentions.None);
    }

    private string Describe(string thing)
    {
        var separator = thing.IndexOf(':');
        var kind = separator > 0 ? thing[..separator] : "";
        var id = separator > 0 ? thing[(separator + 1)..] : thing;

        return kind switch
        {
            "faction" when ulong.TryParse(id, out var userId) => FactionInfo(userId),
            "party" when long.TryParse(id, out var partyId) => PartyInfo(partyId),
            "issue" => IssueInfo(id),
            "province" => ProvinceInfo(id),
            "bill" => BillInfo(id),
            _ => _data.GetAllParties().FirstOrDefault(p => p.Name.Equals(thing, StringComparison.OrdinalIgnoreCase)) is { } party
                ? PartyInfo(party.Id)
                : _data.GetIssue(thing) is not null ? IssueInfo(thing)
                : _constants.Config.Province(thing) is not null ? ProvinceInfo(thing)
                : Texts.Get("info.notFound", ("thing", thing)),
        };
    }

    private string Live()
    {
        var state = _data.GetGameState();
        var sb = new StringBuilder();
        sb.AppendLine(state.IsPreElection
            ? Texts.Get("info.live.headingPreElection", ("season", state.Label))
            : Texts.Get("info.live.heading", ("season", state.Label)));
        sb.AppendLine(Texts.Get("info.live.next", ("when", Display.NextResolution(state))));
        var issues = _data.GetOpenIssues();
        sb.AppendLine();
        sb.AppendLine(Texts.Get("info.live.openIssues"));
        sb.AppendLine(issues.Count == 0 ? Texts.Get("info.live.noneYet") : string.Join("\n", issues.Select(i => $"- {i.Title}")));
        var parties = _data.GetAllParties();
        sb.AppendLine();
        sb.AppendLine(Texts.Get("info.live.parties"));
        sb.AppendLine(parties.Count == 0 ? Texts.Get("info.live.noneYet") : string.Join("\n", parties.Select(p => $"- {Display.Party(p)}")));
        return sb.ToString();
    }

    private string FactionInfo(ulong userId)
    {
        if (_data.GetFaction(userId) is not { } faction) return Texts.Get("info.faction.missing");
        var party = faction.PartyId is { } partyId ? _data.GetParty(partyId) : null;
        var sb = new StringBuilder();
        sb.AppendLine($"## {Display.Faction(faction)}");
        if (!faction.IsActive) sb.AppendLine(Texts.Get("info.faction.retired"));
        sb.AppendLine(Texts.Get("info.faction.popularity", ("popularity", Display.Number(faction.Popularity))));
        sb.AppendLine(Texts.Get("info.faction.party", ("party", party is null ? Display.Independent : Display.Party(party))));
        sb.AppendLine(Texts.Get("info.seats", ("seats", faction.Seats)));
        sb.AppendLine(Texts.Get("info.faction.home", ("province", Display.Province(_constants.Config, faction.HomeProvinceId))));

        var log = _data.GetLatestStances(userId);
        if (log.Count > 0)
        {
            var titles = _data.GetAllIssues().ToDictionary(i => i.Id, i => i.Title);
            sb.AppendLine();
            sb.AppendLine(Texts.Get("info.faction.stances"));
            foreach (var entry in log)
            {
                var issue = titles.GetValueOrDefault(entry.IssueId, entry.IssueId);
                var stance = entry.Stance.DisplayName();
                var source = entry.Source.ToString().ToLowerInvariant();
                var season = GameCalendar.Label(entry.Season, entry.Year);
                sb.AppendLine(entry.OffProgramme
                    ? Texts.Get("info.faction.stanceOff", ("issue", issue), ("stance", stance), ("source", source), ("season", season))
                    : Texts.Get("info.faction.stance", ("issue", issue), ("stance", stance), ("source", source), ("season", season)));
            }
        }
        return sb.ToString();
    }

    private string PartyInfo(long partyId)
    {
        if (_data.GetParty(partyId) is not { } party) return Texts.Get("info.party.missing");
        var members = _data.GetPartyMembers(partyId);
        var inGovernment = _data.GetGoverningPartyIds().Contains(partyId);
        var sb = new StringBuilder();
        sb.AppendLine($"## {Display.Party(party)}");
        sb.AppendLine(inGovernment
            ? Texts.Get("info.party.colourGovernment", ("colour", party.Colour))
            : Texts.Get("info.party.colourOpposition", ("colour", party.Colour)));
        if (party.CreatedSeason is { } season && party.CreatedYear is { } year)
            sb.AppendLine(Texts.Get("info.party.founded", ("season", GameCalendar.Label(season, year))));
        sb.AppendLine(Texts.Get("info.seats", ("seats", members.Sum(m => m.Seats))));
        sb.AppendLine();
        sb.AppendLine(Texts.Get("info.party.members"));
        foreach (var member in members)
            sb.AppendLine(Texts.Get("info.party.member",
                ("faction", Display.Faction(member)), ("popularity", Display.Number(member.Popularity)), ("seats", member.Seats)));

        sb.AppendLine();
        sb.AppendLine(Texts.Get("info.party.programme"));
        sb.Append(ProgrammeText(partyId));
        return sb.ToString();
    }

    private string ProgrammeText(long partyId)
    {
        var programme = _data.GetProgramme(partyId);
        var open = _data.GetOpenIssues();
        if (open.Count == 0) return Texts.Get("info.party.noOpenIssues");
        return string.Join("\n", open.Select(i =>
            $"- {i.Title}: {(programme.TryGetValue(i.Id, out var stance) ? stance.DisplayName() : Texts.Get("info.party.noStance"))}"));
    }

    private string IssueInfo(string issueId)
    {
        if (_data.GetIssue(issueId) is not { } issue) return Texts.Get("info.issue.missing");
        var sb = new StringBuilder();
        sb.AppendLine($"## {issue.Title}");
        sb.AppendLine(Texts.Get("info.issue.for", ("text", issue.ForText)));
        sb.AppendLine(Texts.Get("info.issue.against", ("text", issue.AgainstText)));
        sb.AppendLine(Texts.Get("info.issue.status", ("status", issue.Status)));

        var bills = _data.GetBills(issueId);
        if (bills.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(Texts.Get("info.issue.bills"));
            foreach (var bill in bills)
                sb.AppendLine($"{bill.Title} · {bill.Lean} · {BillStatusText(bill)}");
        }

        sb.AppendLine();
        foreach (var party in _data.GetAllParties())
        {
            var stance = _data.GetProgramme(party.Id).TryGetValue(issueId, out var s) ? s : Stance.Silent;
            sb.AppendLine($"{party.Name}: {stance.DisplayName()}");
        }
        return sb.ToString();
    }

    private string BillStatusText(Bill bill)
    {
        var state = _data.GetGameState();
        return bill.Status switch
        {
            BillStatus.Available => Texts.Get("info.bill.available"),
            BillStatus.Voting => Texts.Get("info.bill.voting"),
            BillStatus.Cooldown when bill.CooldownUntil is { } until =>
                Texts.Get("info.bill.resting", ("season", GameCalendar.Label(GameCalendar.Add(state.Season, state.Year, (int)(until - state.ResolutionNumber)).Season, GameCalendar.Add(state.Season, state.Year, (int)(until - state.ResolutionNumber)).Year))),
            BillStatus.Passed => Texts.Get("info.bill.passed"),
            BillStatus.Withdrawn => Texts.Get("info.bill.withdrawn"),
            _ => bill.Status.ToString(),
        };
    }

    private string BillInfo(string billId)
    {
        if (_data.GetBill(billId) is not { } bill) return Texts.Get("info.bill.missing");
        var issue = _data.GetIssue(bill.IssueId);
        var sb = new StringBuilder();
        sb.AppendLine($"## {bill.Title}");
        sb.AppendLine($"{issue?.Title ?? bill.IssueId} · {bill.Lean} · {BillStatusText(bill)}");
        sb.AppendLine();
        sb.AppendLine($"*{bill.Motion}*");
        return sb.ToString();
    }

    private string ProvinceInfo(string provinceId)
    {
        if (_constants.Config.Province(provinceId) is not { } definition) return Texts.Get("info.province.missing");
        var live = _data.GetProvince(provinceId);
        var conditionIds = live is null ? definition.Conditions : _data.GetProvinceConditions().GetValueOrDefault(provinceId, []);
        var sb = new StringBuilder();
        sb.AppendLine($"## {definition.Name}");
        sb.AppendLine(definition.Description);
        if (live is not null) sb.AppendLine(Texts.Get("info.province.discontent", ("discontent", Wording.Discontent(live.Discontent))));
        sb.AppendLine();
        sb.AppendLine(Texts.Get("info.province.conditions"));
        foreach (var conditionId in conditionIds)
            if (_constants.Config.Condition(conditionId) is { } condition)
                sb.AppendLine($"{condition.Name}: {condition.Description}");

        var units = _data.GetUnits().Where(u => u.ProvinceId == provinceId).ToList();
        if (units.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine(Texts.Get("info.province.stationed",
                ("units", string.Join(", ", units.Select(u => Texts.Get("info.province.unit", ("name", u.Name), ("sides", u.Sides)))))));
        }

        var results = _data.GetProvinceResults().Where(r => r.ProvinceId == provinceId && r.Seats > 0).OrderByDescending(r => r.Seats).ToList();
        if (results.Count > 0)
        {
            if (units.Count == 0) sb.AppendLine();
            sb.AppendLine(Texts.Get("info.province.lastElection",
                ("results", string.Join(", ", results.Select(r => Texts.Get("info.province.result", ("party", r.PartyName), ("seats", r.Seats)))))));
        }
        return sb.ToString();
    }
}
