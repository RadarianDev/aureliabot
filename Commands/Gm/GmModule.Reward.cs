using Discord;
using Discord.Interactions;

public partial class GmModule
{
    [SlashCommand("reward", "Reward excellent roleplay or great deeds, announced as a royal commendation")]
    public async Task RewardAsync(
        [Summary("faction", "The faction"), Autocomplete(typeof(FactionAutocomplete))] string faction,
        [Summary("amount", "Popularity or prestige to add (positive)")] double amount,
        [Summary("reason", "What earned it, e.g. \"a stirring speech at Carden\""), MaxLength(300)] string reason,
        [Summary("kind", "Popularity (fades, wins votes) or prestige (lasting, ranks the leaderboard); default popularity")] RewardKind kind = RewardKind.Popularity)
    {
        if (amount < 0)
        {
            await RespondAsync(Texts.Get("gm.reward.negative"), ephemeral: true);
            return;
        }
        if (amount == 0 || double.IsNaN(amount) || double.IsInfinity(amount))
        {
            await RespondAsync(Texts.Get("gm.reward.notPositive"), ephemeral: true);
            return;
        }
        if (FactionAutocomplete.Find(_data, faction) is not { IsActive: true } target)
        {
            await RespondAsync(Texts.Get("gm.reward.noActiveFaction"), ephemeral: true);
            return;
        }
        reason = reason.Trim().TrimEnd('.');
        if (reason.Length == 0)
        {
            await RespondAsync(Texts.Get("gm.reward.noReason"), ephemeral: true);
            return;
        }

        if (kind == RewardKind.Prestige)
            await RewardPrestigeAsync(target, amount, reason);
        else
            await RewardPopularityAsync(target, amount, reason);
    }

    private async Task RewardPopularityAsync(Faction target, double amount, string reason)
    {
        if (_data.AddPopularity(target.UserId, amount, _constants.Current.Popularity) is not { } updated)
        {
            await RespondAsync(Texts.Get("gm.reward.noFaction"), ephemeral: true);
            return;
        }
        var gained = updated - target.Popularity;
        _data.Log(LogKinds.GmOverride, "gm reward", $"{target.UserId}: {Display.Signed(gained)} popularity for {reason}.", target.UserId, isPublic: true);

        var post = await _publisher.PostAsync(
            ChannelRole.Notices,
            Texts.Get("gm.reward.commendation", ("faction", Display.Faction(target)), ("reason", reason), ("gained", Display.Signed(gained))),
            "gm reward");

        var capped = gained < amount ? Texts.Get("gm.reward.capped", ("max", Display.Number(_constants.Current.Popularity.Max))) : "";
        var values = new (string, object?)[]
        {
            ("faction", Display.Faction(target)), ("from", Display.Number(target.Popularity)), ("to", Display.Number(updated)), ("capped", capped),
        };
        await RespondAsync(
            post is null ? Texts.Get("gm.reward.rewardedNotPosted", values) : Texts.Get("gm.reward.rewardedPosted", values),
            ephemeral: true,
            allowedMentions: AllowedMentions.None);
    }

    private async Task RewardPrestigeAsync(Faction target, double amount, string reason)
    {
        if (_data.AddPrestige(target.UserId, amount) is not { } updated)
        {
            await RespondAsync(Texts.Get("gm.reward.noFaction"), ephemeral: true);
            return;
        }
        _data.Log(LogKinds.GmOverride, "gm reward", $"{target.UserId}: {Display.Signed(amount)} prestige for {reason}.", target.UserId, isPublic: true);

        var post = await _publisher.PostAsync(
            ChannelRole.Notices,
            Texts.Get("gm.reward.prestigeCommendation", ("faction", Display.Faction(target)), ("reason", reason), ("gained", Display.Signed(amount))),
            "gm reward");

        var values = new (string, object?)[]
        {
            ("faction", Display.Faction(target)), ("from", Display.Number(target.Prestige)), ("to", Display.Number(updated)),
        };
        await RespondAsync(
            post is null ? Texts.Get("gm.reward.prestigeNotPosted", values) : Texts.Get("gm.reward.prestigePosted", values),
            ephemeral: true,
            allowedMentions: AllowedMentions.None);
    }
}
