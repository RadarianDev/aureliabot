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
            entries.Add(($"Party · {party.Name}", $"party:{party.Id}"));
        foreach (var faction in data.GetActiveFactions())
            entries.Add(($"Faction · {FactionLabel(faction, context)}", $"faction:{faction.UserId}"));
        foreach (var bill in data.GetBills())
            entries.Add(($"Bill · {bill.Title}", $"bill:{bill.Id}"));
        foreach (var issue in data.GetAllIssues())
            entries.Add(($"Issue · {issue.Title}", $"issue:{issue.Id}"));
        foreach (var province in constants.Config.Provinces)
            entries.Add(($"Province · {province.Name}", $"province:{province.Id}"));

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
                : $"Nothing called `{thing}` was found. Pick something from the list.",
        };
    }

    private string Live()
    {
        var state = _data.GetGameState();
        var sb = new StringBuilder();
        sb.AppendLine($"## {state.Label}{(state.IsPreElection ? " · Pre-election" : "")}");
        sb.AppendLine($"Next resolution: {Display.NextResolution(state)}");
        var issues = _data.GetOpenIssues();
        sb.AppendLine();
        sb.AppendLine("**Open issues**");
        sb.AppendLine(issues.Count == 0 ? "None yet." : string.Join("\n", issues.Select(i => $"- {i.Title}")));
        var parties = _data.GetAllParties();
        sb.AppendLine();
        sb.AppendLine("**Parties**");
        sb.AppendLine(parties.Count == 0 ? "None yet." : string.Join("\n", parties.Select(p => $"- {Display.Party(p)}")));
        return sb.ToString();
    }

    private string FactionInfo(ulong userId)
    {
        if (_data.GetFaction(userId) is not { } faction) return "That faction doesn't exist.";
        var party = faction.PartyId is { } partyId ? _data.GetParty(partyId) : null;
        var sb = new StringBuilder();
        sb.AppendLine($"## {Display.Faction(faction)}");
        if (!faction.IsActive) sb.AppendLine("*Retired from politics.*");
        sb.AppendLine($"Popularity: {Display.Number(faction.Popularity)}");
        sb.AppendLine($"Party: {(party is null ? "independent" : Display.Party(party))}");
        sb.AppendLine($"Seats: {faction.Seats}");
        sb.AppendLine($"Home province: {Display.Province(_constants.Config, faction.HomeProvinceId)}");

        var log = _data.GetLatestStances(userId);
        if (log.Count > 0)
        {
            var titles = _data.GetAllIssues().ToDictionary(i => i.Id, i => i.Title);
            sb.AppendLine();
            sb.AppendLine("**Stances**");
            foreach (var entry in log)
                sb.AppendLine($"- {titles.GetValueOrDefault(entry.IssueId, entry.IssueId)}: {entry.Stance.DisplayName()} " +
                              $"({entry.Source.ToString().ToLowerInvariant()}, {GameCalendar.Label(entry.Season, entry.Year)}" +
                              $"{(entry.OffProgramme ? ", off-programme" : "")})");
        }
        return sb.ToString();
    }

    private string PartyInfo(long partyId)
    {
        if (_data.GetParty(partyId) is not { } party) return "That party doesn't exist.";
        var members = _data.GetPartyMembers(partyId);
        var inGovernment = _data.GetGoverningPartyIds().Contains(partyId);
        var sb = new StringBuilder();
        sb.AppendLine($"## {Display.Party(party)}");
        sb.AppendLine($"Colour: `{party.Colour}` · {(inGovernment ? "in government" : "in opposition")}");
        if (party.CreatedSeason is { } season && party.CreatedYear is { } year)
            sb.AppendLine($"Founded {GameCalendar.Label(season, year)}");
        sb.AppendLine($"Seats: {members.Sum(m => m.Seats)}");
        sb.AppendLine();
        sb.AppendLine("**Members**");
        foreach (var member in members)
            sb.AppendLine($"- {Display.Faction(member)} · popularity {Display.Number(member.Popularity)} · {member.Seats} seats");

        sb.AppendLine();
        sb.AppendLine("**Programme**");
        sb.Append(ProgrammeText(partyId));
        return sb.ToString();
    }

    private string ProgrammeText(long partyId)
    {
        var programme = _data.GetProgramme(partyId);
        var open = _data.GetOpenIssues();
        if (open.Count == 0) return "No issues are open yet.";
        return string.Join("\n", open.Select(i =>
            $"- {i.Title}: {(programme.TryGetValue(i.Id, out var stance) ? stance.DisplayName() : "no stance")}"));
    }

    private string IssueInfo(string issueId)
    {
        if (_data.GetIssue(issueId) is not { } issue) return "That issue doesn't exist.";
        var sb = new StringBuilder();
        sb.AppendLine($"## {issue.Title}");
        sb.AppendLine($"For: {issue.ForText}");
        sb.AppendLine($"Against: {issue.AgainstText}");
        sb.AppendLine($"Status: {issue.Status}");

        var bills = _data.GetBills(issueId);
        if (bills.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**Bills**");
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
            BillStatus.Available => "available",
            BillStatus.Voting => "before the Assembly",
            BillStatus.Cooldown when bill.CooldownUntil is { } until =>
                $"resting until {GameCalendar.Label(GameCalendar.Add(state.Season, state.Year, (int)(until - state.ResolutionNumber)).Season, GameCalendar.Add(state.Season, state.Year, (int)(until - state.ResolutionNumber)).Year)}",
            BillStatus.Passed => "passed into law",
            BillStatus.Withdrawn => "withdrawn",
            _ => bill.Status.ToString(),
        };
    }

    private string BillInfo(string billId)
    {
        if (_data.GetBill(billId) is not { } bill) return "That bill doesn't exist.";
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
        if (_constants.Config.Province(provinceId) is not { } definition) return "That province doesn't exist.";
        var live = _data.GetProvince(provinceId);
        var conditionIds = live is null ? definition.Conditions : _data.GetProvinceConditions().GetValueOrDefault(provinceId, []);
        var sb = new StringBuilder();
        sb.AppendLine($"## {definition.Name}");
        sb.AppendLine(definition.Description);
        if (live is not null) sb.AppendLine($"Discontent: {Wording.Discontent(live.Discontent)}");
        sb.AppendLine();
        sb.AppendLine("**Conditions**");
        foreach (var conditionId in conditionIds)
            if (_constants.Config.Condition(conditionId) is { } condition)
                sb.AppendLine($"{condition.Name}: {condition.Description}");

        var units = _data.GetUnits().Where(u => u.ProvinceId == provinceId).ToList();
        if (units.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Stationed: " + string.Join(", ", units.Select(u => $"{u.Name} · d{u.Sides}")));
        }
        return sb.ToString();
    }
}
