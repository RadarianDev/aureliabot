using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;

public static class FactionGate
{
    public const string NotRegistered = "You are not registered yet; ask the GM.";
    public const string Retired = "Your faction has retired from politics.";

    public static async Task<string?> RefusalAsync(IServiceProvider services, IUser user)
    {
        var data = services.GetRequiredService<DatabaseService>();
        var faction = data.GetFaction(user.Id);
        if (faction is { IsActive: true }) return null;
        if (faction is not null) return Retired;

        if (data.TryMarkUnregisteredAlert(user.Id))
            await services.GetRequiredService<AlertService>().PostAsync(
                $"{user.Mention} ({user.Username}) tried a game command but has no faction. Register them with `/gm faction create`.");
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
            ? PreconditionResult.FromError("This needs a party member; you are an independent.")
            : PreconditionResult.FromSuccess();
    }
}
