using Discord;
using Discord.Interactions;

public partial class GmModule
{
    [SlashCommand("chart", "Graph hidden values over the turns, privately: Favour and Threat, or discontent")]
    public async Task ChartAsync(
        [Summary("what", "What to graph")] HiddenChart what,
        [Summary("province", "Discontent: only this province (default: the most discontented)"), Autocomplete(typeof(ProvinceAutocomplete))] string? province = null)
    {
        await DeferAsync(ephemeral: true);
        ProvinceDefinition? found = null;
        if (province is not null && (found = ProvinceAutocomplete.Find(_data, _constants.Config, province)) is null)
        {
            await FollowupAsync(Texts.Get("gm.peek.noProvince", ("province", province)), ephemeral: true, allowedMentions: AllowedMentions.None);
            return;
        }
        await ChartRequests.SendAsync(Context.Interaction, ChartRequests.Hidden(_data, _constants.Config, what, found), ephemeral: true);
    }
}
