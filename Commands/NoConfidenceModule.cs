using Discord;
using Discord.Interactions;

public class NoConfidenceModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly DatabaseService _data;
    private readonly ConstantsStore _constants;
    private readonly AssemblyPublisher _assembly;

    public NoConfidenceModule(DatabaseService data, ConstantsStore constants, AssemblyPublisher assembly)
    {
        _data = data;
        _constants = constants;
        _assembly = assembly;
    }

    [SlashCommand("noconfidence", "Table a motion of no confidence in His Majesty's government")]
    [RequireFaction]
    public async Task TableAsync()
    {
        var c = _constants.Current;
        var result = _data.TableNoConfidence(Context.User.Id, c);
        if (result.Motion is not { } motion || result.Outcome is not (NoConfidenceOutcome.Tabled or NoConfidenceOutcome.WentToVote))
        {
            await RespondAsync(Refusal(result), ephemeral: true, allowedMentions: AllowedMentions.None);
            return;
        }

        var reply = result.Outcome == NoConfidenceOutcome.WentToVote
            ? Texts.Get("noConfidence.tabledToVote", ("supporters", result.Supporters), ("threshold", result.Threshold))
            : Texts.Get("noConfidence.tabled", ("supporters", result.Supporters), ("threshold", result.Threshold),
                ("resolutions", c.Assembly.NoConfidenceGatherResolutions));
        await RespondAsync(reply, ephemeral: true, allowedMentions: AllowedMentions.None);
        await _assembly.PostMotionAsync(motion.Id);
        if (result.Outcome == NoConfidenceOutcome.WentToVote) await AlertVoteAsync(motion.Id);
    }

    [ComponentInteraction("nc:cosign:*", ignoreGroupNames: true)]
    [RequireFaction]
    public async Task CosignAsync(string rawId)
    {
        if (!long.TryParse(rawId, out var motionId))
        {
            await RespondAsync(Texts.Get("noConfidence.refuse.notFound"), ephemeral: true);
            return;
        }

        var result = _data.CosignNoConfidence(motionId, Context.User.Id, _constants.Current);
        if (result.Outcome is not (NoConfidenceOutcome.Cosigned or NoConfidenceOutcome.WentToVote))
        {
            await RespondAsync(Refusal(result), ephemeral: true, allowedMentions: AllowedMentions.None);
            return;
        }

        await RespondAsync(result.Outcome == NoConfidenceOutcome.WentToVote
                ? Texts.Get("noConfidence.cosignedToVote", ("supporters", result.Supporters), ("threshold", result.Threshold))
                : Texts.Get("noConfidence.cosigned", ("supporters", result.Supporters), ("threshold", result.Threshold)),
            ephemeral: true, allowedMentions: AllowedMentions.None);
        await _assembly.RefreshMotionAsync(motionId);
        if (result.Outcome == NoConfidenceOutcome.WentToVote) await AlertVoteAsync(motionId);
    }

    private Task AlertVoteAsync(long motionId) =>
        _assembly.AlertAsync(Texts.Get("noConfidence.alert.toVote", ("id", motionId),
            ("tabler", _data.GetMotion(motionId) is { } motion ? Display.Faction(_data, motion.TablerId) : "")));

    private string Refusal(NoConfidenceResult result)
    {
        var state = _data.GetGameState();
        return result.Outcome switch
        {
            NoConfidenceOutcome.NotActive => FactionGate.Retired,
            NoConfidenceOutcome.InCabinet => Texts.Get("noConfidence.refuse.inCabinet"),
            NoConfidenceOutcome.NoCabinet => Texts.Get("noConfidence.refuse.noCabinet"),
            NoConfidenceOutcome.PreElection => Texts.Get("noConfidence.refuse.preElection",
                ("season", GameCalendar.Label(GameCalendar.Next(state.PreElectionSeason, state.PreElectionYear).Season,
                    GameCalendar.Next(state.PreElectionSeason, state.PreElectionYear).Year)),
                ("when", Display.NextResolution(state))),
            NoConfidenceOutcome.Cooldown => Texts.Get("noConfidence.refuse.cooldown", ("season", CooldownSeason(state))),
            NoConfidenceOutcome.MotionPending => Texts.Get("noConfidence.refuse.pending"),
            NoConfidenceOutcome.NotGathering => Texts.Get("noConfidence.refuse.notGathering"),
            NoConfidenceOutcome.IsTabler => Texts.Get("noConfidence.refuse.isTabler"),
            NoConfidenceOutcome.AlreadySigned => Texts.Get("noConfidence.refuse.alreadySigned"),
            _ => Texts.Get("noConfidence.refuse.notFound"),
        };
    }

    private static string CooldownSeason(GameState state)
    {
        var until = state.NoConfidenceCooldownUntil ?? state.ResolutionNumber;
        var (season, year) = GameCalendar.Add(state.Season, state.Year, (int)(until - state.ResolutionNumber));
        return GameCalendar.Label(season, year);
    }
}
