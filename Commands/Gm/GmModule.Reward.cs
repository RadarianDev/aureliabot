using Discord;
using Discord.Interactions;

public partial class GmModule
{
    [SlashCommand("reward", "Reward excellent roleplay with popularity, announced as a royal commendation")]
    public async Task RewardAsync(
        [Summary("faction", "The faction"), Autocomplete(typeof(FactionAutocomplete))] string faction,
        [Summary("amount", "Popularity to add (positive)")] double amount,
        [Summary("reason", "What earned it, e.g. \"a stirring speech at Carden\""), MaxLength(300)] string reason)
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

        if (_data.AddPopularity(target.UserId, amount, _constants.Current.Popularity) is not { } updated)
        {
            await RespondAsync(Texts.Get("gm.reward.noFaction"), ephemeral: true);
            return;
        }
        var gained = updated - target.Popularity;
        _data.Log(LogKinds.GmOverride, "gm reward", $"{target.UserId}: {Display.Signed(gained)} popularity for {reason}.", target.UserId, isPublic: true);

        var post = await _publisher.PostAsync(
            ChannelRole.News,
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
}
