using Discord;
using Discord.Interactions;

public class MapModule : InteractionModuleBase<SocketInteractionContext>
{
    private readonly DatabaseService _data;
    private readonly ConstantsStore _constants;

    public MapModule(DatabaseService data, ConstantsStore constants)
    {
        _data = data;
        _constants = constants;
    }

    [SlashCommand("map", "Show the province map from the latest election")]
    public async Task MapAsync()
    {
        var rows = _data.GetProvinceResults();
        if (rows.Count == 0)
        {
            await RespondAsync(Texts.Get("map.noElection"), ephemeral: true);
            return;
        }

        await DeferAsync();
        var config = _constants.Config;
        var parties = _data.GetPartySnapshots();
        var winners = ElectionPublisher.WinnersFromRows(rows);
        using var images = ElectionPublisher.Render(null, parties, winners, config, out var failure);
        if (images.MapPath is null)
        {
            Console.WriteLine($"[MapModule] Rendering failed: {failure}");
            await DeleteOriginalResponseAsync();
            await FollowupAsync(Texts.Get("map.renderFailed"), ephemeral: true);
            return;
        }

        var attachments = images.Attachments();
        try
        {
            var embed = ElectionPublisher.MapEmbed(winners, parties, config, Texts.Get("map.title", ("term", rows[0].TermNumber)));
            await FollowupWithFilesAsync(attachments, embed: embed, allowedMentions: AllowedMentions.None);
        }
        finally
        {
            foreach (var attachment in attachments) attachment.Dispose();
        }
    }
}
