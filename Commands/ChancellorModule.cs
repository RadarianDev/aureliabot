using Discord;
using Discord.Interactions;

[Group("chancellor", "The Chancellor's commands")]
public class ChancellorModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly DatabaseService _data;
    private readonly GovernmentPublisher _government;

    public ChancellorModule(DatabaseService data, GovernmentPublisher government)
    {
        _data = data;
        _government = government;
    }

    [SlashCommand("nominate", "Nominate the four ministers and open the Assembly's approval vote")]
    [RequireFaction]
    public async Task NominateAsync(
        [Summary("treasury", "Minister of the Treasury"), Autocomplete(typeof(FactionAutocomplete))] string treasury,
        [Summary("war", "Minister of War"), Autocomplete(typeof(FactionAutocomplete))] string war,
        [Summary("interior", "Minister of the Interior"), Autocomplete(typeof(FactionAutocomplete))] string interior,
        [Summary("commerce", "Minister of Commerce and Labor"), Autocomplete(typeof(FactionAutocomplete))] string commerce)
    {
        var chosen = new (Ministry Ministry, string Text)[]
        {
            (Ministry.Treasury, treasury), (Ministry.War, war), (Ministry.Interior, interior), (Ministry.CommerceLabor, commerce),
        };
        var posts = new Dictionary<Ministry, ulong>();
        foreach (var (ministry, text) in chosen)
        {
            if (FactionAutocomplete.Find(_data, text) is not { } faction)
            {
                await RespondAsync(Texts.Get("cabinet.refusal.noFaction", ("text", text), ("ministry", ministry.DisplayName())),
                    ephemeral: true, allowedMentions: AllowedMentions.None);
                return;
            }
            posts[ministry] = faction.UserId;
        }

        await DeferAsync(ephemeral: true);
        var result = _data.Nominate(Context.User.Id, posts);
        if (result.Nomination is not { } nomination)
        {
            await FollowupAsync(result.Refusal ?? Texts.Get("cabinet.vote.closed"), ephemeral: true, allowedMentions: AllowedMentions.None);
            return;
        }

        await _government.PostNominationAsync(nomination, "chancellor nominate");
        await FollowupAsync(Texts.Get("cabinet.nominate.done",
                ("cabinet", _government.CabinetLines(nomination)),
                ("due", GovernmentPublisher.Deadline(_data.GetGameState(), nomination.DueResolution))),
            ephemeral: true, allowedMentions: AllowedMentions.None);
    }

    [ComponentInteraction("vote:cabinet:*:*", ignoreGroupNames: true)]
    public async Task VoteAsync(string choice, string rawId)
    {
        if (_data.GetFaction(Context.User.Id) is not { IsActive: true })
        {
            await RespondAsync(_data.GetFaction(Context.User.Id) is null ? FactionGate.NotRegistered : FactionGate.Retired, ephemeral: true);
            return;
        }
        if (!long.TryParse(rawId, out var cabinetId) || !Enum.TryParse<VoteChoice>(choice, ignoreCase: true, out var vote))
        {
            await RespondAsync(Texts.Get("cabinet.vote.closed"), ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);
        var result = _data.CastCabinetVote(cabinetId, Context.User.Id, vote);
        if (result.Refusal is not null)
        {
            await FollowupAsync(result.Refusal, ephemeral: true);
            return;
        }

        await _government.RefreshNominationAsync(cabinetId);
        var chancellor = result.Nomination?.ChancellorId is { } id ? Display.Faction(_data, id) : Texts.Get("cabinet.vacant");
        await FollowupAsync(
            result.Previous is { } previous && previous != vote
                ? Texts.Get("cabinet.vote.changed", ("choice", GovernmentPublisher.ChoiceName(vote)), ("previous", GovernmentPublisher.ChoiceName(previous)), ("chancellor", chancellor))
                : Texts.Get("cabinet.vote.recorded", ("choice", GovernmentPublisher.ChoiceName(vote)), ("chancellor", chancellor)),
            ephemeral: true, allowedMentions: AllowedMentions.None);
    }
}
