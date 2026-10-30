using Discord;
using Discord.Interactions;

public partial class GmModule
{
    private const int CleanupListLimit = 15;

    [SlashCommand("cleanup", "Delete every party role, party chat and faction chat after testing; asks first")]
    public async Task CleanupAsync()
    {
        await DeferAsync(ephemeral: true);
        var guild = Context.Guild;
        var gmId = Context.User.Id;
        var plan = _cleanup.Plan(guild);

        if (plan.Channels.Any(c => c.Id == Context.Channel.Id))
        {
            await FollowupAsync(Texts.Get("cleanup.wrongChannel"), ephemeral: true);
            return;
        }
        if (plan.IsEmpty)
        {
            await FollowupAsync(CleanupPreview(plan, Texts.Get("cleanup.nothing")), ephemeral: true, allowedMentions: AllowedMentions.None);
            return;
        }

        var confirm = _confirmations.Create(gmId, async () =>
        {
            var result = await _cleanup.RunAsync(guild, plan, gmId);
            var lines = new List<string>
            {
                Texts.Get("cleanup.done",
                    ("channels", result.DeletedChannels.Count), ("roles", result.DeletedRoles.Count),
                    ("parties", result.Cleared.Parties), ("factions", result.Cleared.Factions)),
            };
            if (result.Failures.Count > 0)
            {
                lines.Add(Texts.Get("cleanup.failuresHeading"));
                lines.AddRange(result.Failures.Select(f => Texts.Get("cleanup.failure", ("name", f.Name), ("error", f.Error))));
            }
            if (plan.Refused.Count > 0)
                lines.Add(RefusedLine(plan));
            lines.Add(Texts.Get("cleanup.recreate"));
            return Display.Truncate(string.Join("\n", lines));
        }, Texts.Get("cleanup.button"));

        await FollowupAsync(CleanupPreview(plan, Texts.Get("cleanup.preview")), components: confirm, ephemeral: true, allowedMentions: AllowedMentions.None);
    }

    private static string CleanupPreview(CleanupPlan plan, string heading)
    {
        var lines = new List<string> { heading };
        var partyChats = plan.Channels.Where(c => c.Kind == CleanupChannelKind.PartyChat).ToList();
        var factionChats = plan.Channels.Where(c => c.Kind == CleanupChannelKind.FactionChat).ToList();
        if (partyChats.Count > 0)
            lines.Add(Texts.Get("cleanup.partyChats", ("count", partyChats.Count),
                ("names", CleanupList(partyChats.Select(c => Texts.Get("cleanup.channelName", ("name", c.Name)))))));
        if (factionChats.Count > 0)
            lines.Add(Texts.Get("cleanup.factionChats", ("count", factionChats.Count),
                ("names", CleanupList(factionChats.Select(c => Texts.Get("cleanup.channelName", ("name", c.Name)))))));
        if (plan.Roles.Count > 0)
            lines.Add(Texts.Get("cleanup.roles", ("count", plan.Roles.Count),
                ("names", CleanupList(plan.Roles.Select(r => Texts.Get("cleanup.roleName", ("name", r.Name)))))));
        if (plan.Refused.Count > 0)
            lines.Add(RefusedLine(plan));
        lines.Add(Texts.Get("cleanup.kept"));
        return Display.Truncate(string.Join("\n", lines));
    }

    private static string RefusedLine(CleanupPlan plan) =>
        Texts.Get("cleanup.refused", ("count", plan.Refused.Count),
            ("names", CleanupList(plan.Refused.Select(r => Texts.Get("cleanup.refusedRole",
                ("name", Texts.Get("cleanup.roleName", ("name", r.Name))), ("reason", RefusalText(r.Reason)))))));

    private static string RefusalText(CleanupRefusal reason) => reason switch
    {
        CleanupRefusal.Everyone => Texts.Get("cleanup.reason.everyone"),
        CleanupRefusal.GmRole => Texts.Get("cleanup.reason.gmRole"),
        CleanupRefusal.CabinetRole => Texts.Get("cleanup.reason.cabinetRole"),
        CleanupRefusal.Managed => Texts.Get("cleanup.reason.managed"),
        _ => Texts.Get("cleanup.reason.aboveBot"),
    };

    private static string CleanupList(IEnumerable<string> names)
    {
        var all = names.ToList();
        var shown = string.Join(", ", all.Take(CleanupListLimit));
        return all.Count <= CleanupListLimit ? shown : Texts.Get("cleanup.more", ("names", shown), ("count", all.Count - CleanupListLimit));
    }
}
