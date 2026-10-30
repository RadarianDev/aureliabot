using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;

public static class FactionGate
{
    public static string NotRegistered => Texts.Get("common.notRegistered");
    public static string Retired => Texts.Get("common.retired");

    public static async Task<string?> RefusalAsync(IServiceProvider services, IUser user)
    {
        var data = services.GetRequiredService<DatabaseService>();
        var faction = data.GetFaction(user.Id);
        if (faction is { IsActive: true }) return null;
        if (faction is not null) return Retired;

        if (data.TryMarkUnregisteredAlert(user.Id))
            await services.GetRequiredService<AlertService>().PostAsync(
                Texts.Get("common.unregisteredAlert", ("user", user.Mention), ("username", user.Username)));
        return NotRegistered;
    }
}

public class RequireFactionAttribute : PreconditionAttribute
{
    public override async Task<PreconditionResult> CheckRequirementsAsync(
        IInteractionContext context, ICommandInfo commandInfo, IServiceProvider services)
    {
        var refusal = await FactionGate.RefusalAsync(services, context.User);
        return refusal is null ? PreconditionResult.FromSuccess() : PreconditionResult.FromError(refusal);
    }
}

public class RequirePartyMemberAttribute : PreconditionAttribute
{
    public override async Task<PreconditionResult> CheckRequirementsAsync(
        IInteractionContext context, ICommandInfo commandInfo, IServiceProvider services)
    {
        var refusal = await FactionGate.RefusalAsync(services, context.User);
        if (refusal is not null) return PreconditionResult.FromError(refusal);
        var faction = services.GetRequiredService<DatabaseService>().GetFaction(context.User.Id)!;
        return faction.IsIndependent
            ? PreconditionResult.FromError(Texts.Get("common.needsPartyMember"))
            : PreconditionResult.FromSuccess();
    }
}
