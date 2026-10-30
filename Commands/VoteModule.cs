using Discord;
using Discord.Interactions;

public class VoteModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly DatabaseService _data;
    private readonly AssemblyPublisher _assembly;
    private readonly GovernmentPublisher _government;

    public VoteModule(DatabaseService data, AssemblyPublisher assembly, GovernmentPublisher government)
    {
        _data = data;
        _assembly = assembly;
        _government = government;
    }

    [SlashCommand("vote", "Cast or change your vote on an open reading or no-confidence motion")]
    [RequireFaction]
    public async Task VoteAsync(
        [Summary("subject", "The open vote"), Autocomplete(typeof(VoteSubjectAutocomplete))] string subject,
        [Summary("choice", "Yes, No or Abstain")] VoteChoice choice)
    {
        if (VoteSubjectAutocomplete.Parse(subject) is not { } parsed)
        {
            await RespondAsync(Texts.Get("vote.noSuchVote"), ephemeral: true);
            return;
        }
        if (parsed.Subject == VoteSubject.Cabinet)
        {
            var cast = _data.CastCabinetVote(parsed.Id, Context.User.Id, choice);
            if (cast.Refusal is { } refusal)
            {
                await RespondAsync(refusal, ephemeral: true);
                return;
            }
            await RespondAsync(Texts.Get("vote.cabinetCast", ("choice", AssemblyPublisher.ChoiceName(choice))), ephemeral: true);
            await _government.RefreshNominationAsync(parsed.Id);
            return;
        }
        await CastAsync(parsed.Subject, parsed.Id, choice);
    }

    [ComponentInteraction("vote:reading:*:*", ignoreGroupNames: true)]
    [RequireFaction]
    public Task ReadingButtonAsync(string rawChoice, string rawId) => ButtonAsync(VoteSubject.Reading, rawChoice, rawId);

    [ComponentInteraction("vote:nc:*:*", ignoreGroupNames: true)]
    [RequireFaction]
    public Task MotionButtonAsync(string rawChoice, string rawId) => ButtonAsync(VoteSubject.NoConfidence, rawChoice, rawId);

    private async Task ButtonAsync(VoteSubject subject, string rawChoice, string rawId)
    {
        if (!Enum.TryParse<VoteChoice>(rawChoice, out var choice) || !long.TryParse(rawId, out var id))
        {
            await RespondAsync(Texts.Get("vote.noSuchVote"), ephemeral: true);
            return;
        }
        await CastAsync(subject, id, choice);
    }

    private async Task CastAsync(VoteSubject subject, long id, VoteChoice choice)
    {
        var result = _data.CastAssemblyVote(subject, id, Context.User.Id, choice);
        var state = _data.GetGameState();
        string reply;
        switch (result.Outcome)
        {
            case AssemblyVoteOutcome.NotActive:
                reply = FactionGate.Retired;
                break;
            case AssemblyVoteOutcome.NoSeats:
                reply = Texts.Get("vote.noSeats");
                break;
            case AssemblyVoteOutcome.NotOpen:
                reply = Texts.Get("vote.closed");
                break;
            default:
                var lines = new List<string>();
                var what = result.Bill?.Title ?? Texts.Get("vote.theMotion");
                var choiceName = AssemblyPublisher.ChoiceName(choice);
                lines.Add(result.Previous is { } previous && previous != choice
                    ? Texts.Get("vote.changed", ("what", what), ("previous", AssemblyPublisher.ChoiceName(previous)), ("choice", choiceName))
                    : Texts.Get("vote.recorded", ("what", what), ("choice", choiceName)));
                if (result.Logged is { } logged && result.Bill is { } bill)
                    lines.Add(Texts.Get("vote.stanceLogged", ("stance", logged.DisplayName()), ("issue", _data.GetIssue(bill.IssueId)?.Title ?? bill.IssueId)));
                if (result.OffProgramme && result.ProgrammeStance is { } programme)
                    lines.Add(Texts.Get("vote.offProgramme", ("programme", programme.DisplayName())));
                lines.Add(Texts.Get("vote.counted", ("when", subject == VoteSubject.Reading ? Display.NextResolution(state) : MotionDue(id, state))));
                reply = string.Join("\n", lines);
                break;
        }

        await RespondAsync(reply, ephemeral: true, allowedMentions: AllowedMentions.None);
        if (result.Outcome != AssemblyVoteOutcome.Recorded) return;
        if (subject == VoteSubject.Reading) await _assembly.RefreshReadingAsync(id);
        else await _assembly.RefreshMotionAsync(id);
    }

    private string MotionDue(long motionId, GameState state) =>
        _data.GetMotion(motionId)?.DueResolution is { } due && due > state.ResolutionNumber + 1 && state.NextResolutionAt is { } next
            ? Texts.Get("assembly.afterNext", ("when", Display.When(next)))
            : Display.NextResolution(state);
}
