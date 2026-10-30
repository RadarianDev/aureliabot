using System.Globalization;
using System.Reflection;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;

public class ResubmitModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly IServiceProvider _services;
    private readonly DatabaseService _data;
    private readonly ResubmitService _resubmit;

    public ResubmitModule(IServiceProvider services, DatabaseService data)
    {
        _services = services;
        _data = data;
        _resubmit = new ResubmitService(data, null);
    }

    [ComponentInteraction(ResubmitService.ButtonPrefix + ":*:*", ignoreGroupNames: true)]
    public async Task ResubmitAsync(string rawId, string rawAt)
    {
        if (!long.TryParse(rawId, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            || !long.TryParse(rawAt, NumberStyles.None, CultureInfo.InvariantCulture, out var at))
        {
            await Reply(Texts.Get("resubmit.gone"));
            return;
        }

        var peek = _resubmit.Peek(id, at, Context.User.Id);
        if (Refusal(peek) is { } refusal)
        {
            await Reply(refusal);
            return;
        }

        var payload = peek.Payload!;
        if (ResubmitService.Find(payload.Command) is not { } target
            || ResubmitService.Arguments(target.Method, payload.Options) is not { } arguments)
        {
            await Reply(Texts.Get("resubmit.cannot", ("command", payload.Display)));
            return;
        }

        foreach (var precondition in Preconditions(target))
        {
            var check = await precondition.CheckRequirementsAsync(Context, null!, _services);
            if (!check.IsSuccess)
            {
                await Reply(check.ErrorReason);
                return;
            }
        }

        var claim = _resubmit.Claim(id, at, Context.User.Id);
        if (Refusal(claim) is { } late)
        {
            await Reply(late);
            return;
        }

        var module = (IInteractionModuleBase)ActivatorUtilities.CreateInstance(_services, target.Module);
        module.SetContext(Context);
        if (target.Method.Invoke(module, arguments) is Task run) await run;
        _data.Log(LogKinds.Command, payload.Command, payload.ToJson(), Context.User.Id);
    }

    private static IEnumerable<PreconditionAttribute> Preconditions(ResubmitTarget target)
    {
        for (var type = target.Module; type is not null; type = type.DeclaringType)
            foreach (var attribute in type.GetCustomAttributes<PreconditionAttribute>())
                yield return attribute;
        foreach (var attribute in target.Method.GetCustomAttributes<PreconditionAttribute>())
            yield return attribute;
    }

    private static string? Refusal(ResubmitClaim claim) => claim.Outcome switch
    {
        ResubmitClaimOutcome.Gone => Texts.Get("resubmit.gone"),
        ResubmitClaimOutcome.NotYours => Texts.Get("resubmit.notYours", ("faction", $"<@{claim.Owner}>")),
        ResubmitClaimOutcome.Used => Texts.Get("resubmit.used", ("command", claim.Payload?.Display ?? "")),
        _ => null,
    };

    private Task Reply(string text) => RespondAsync(Display.Truncate(text), ephemeral: true, allowedMentions: AllowedMentions.None);
}
