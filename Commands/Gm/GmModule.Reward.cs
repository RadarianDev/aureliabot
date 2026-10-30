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
            await RespondAsync("A reward can't be negative; use `/gm apply` for penalties.", ephemeral: true);
            return;
        }
        if (amount == 0 || double.IsNaN(amount) || double.IsInfinity(amount))
        {
            await RespondAsync("The amount must be positive.", ephemeral: true);
            return;
        }
        if (FactionAutocomplete.Find(_data, faction) is not { IsActive: true } target)
        {
            await RespondAsync("No such active faction. Pick one from the list.", ephemeral: true);
            return;
        }
        reason = reason.Trim().TrimEnd('.');
        if (reason.Length == 0)
        {
            await RespondAsync("Give a reason.", ephemeral: true);
            return;
        }

        if (_data.AddPopularity(target.UserId, amount, _constants.Current.Popularity) is not { } updated)
        {
            await RespondAsync("No such faction.", ephemeral: true);
            return;
        }
        var gained = updated - target.Popularity;
        _data.Log(LogKinds.GmOverride, "gm reward", $"{target.UserId}: {Display.Signed(gained)} popularity for {reason}.", target.UserId, isPublic: true);

        var post = await _publisher.PostAsync(
            ChannelRole.News,
            $"## By His Majesty's Command\nHis Majesty is pleased to commend {Display.Faction(target)} for {reason}.\n*Popularity {Display.Signed(gained)}*",
            "gm reward");

        await RespondAsync(
            $"Rewarded {Display.Faction(target)}: popularity {Display.Number(target.Popularity)} → {Display.Number(updated)}"
            + (gained < amount ? $" (capped at {Display.Number(_constants.Current.Popularity.Max)})" : "")
            + (post is null ? ".\n⚠️ No #news channel is set, so the commendation was not posted." : ". The commendation is in #news."),
            ephemeral: true,
            allowedMentions: AllowedMentions.None);
    }
}
