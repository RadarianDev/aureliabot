using Discord;
using Discord.WebSocket;

public class ForcesPublisher
{
    private readonly DiscordSocketClient _client;
    private readonly DatabaseService _data;
    private readonly ConstantsStore _constants;
    private readonly AlertService _alerts;

    public ForcesPublisher(DiscordSocketClient client, DatabaseService data, ConstantsStore constants, AlertService alerts)
    {
        _client = client;
        _data = data;
        _constants = constants;
        _alerts = alerts;
    }

    public async Task PublishAsync(ResolutionReport report, string source)
    {
        foreach (var crackdown in report.Crackdowns)
        {
            try
            {
                await PostAsync(CrackdownPost(crackdown), source);
            }
            catch (Exception ex)
            {
                await _alerts.PostAsync(Texts.Get("crackdown.alert.postFailed", ("error", ex.Message)));
            }
        }
    }

    public string CrackdownPost(CrackdownReport crackdown)
    {
        var config = _constants.Config;
        var province = Display.Province(config, crackdown.ProvinceId);
        var roll = Texts.Get("crackdown.post.roll", ("total", crackdown.GendarmerieTotal), ("roll", crackdown.DieRoll), ("sides", crackdown.DieSides));
        var story = crackdown.Success
            ? Texts.Get("crackdown.post.success", ("province", province))
            : Texts.Get("crackdown.post.failure", ("province", province));
        return Publisher.FormatEffectPost(Texts.Get("crackdown.post.heading", ("province", province)), [roll], story,
            EffectSummary.Lines(crackdown.Effects, Publisher.Names(_data, config), config.Constants.Display));
    }

    private async Task PostAsync(string text, string source)
    {
        if (_data.GetChannel(ChannelRole.News) is not { } channelId || _client.GetChannel(channelId) is not IMessageChannel channel)
        {
            Console.WriteLine($"[ForcesPublisher] No News channel is set; skipped a post from {source}.");
            return;
        }

        foreach (var chunk in Publisher.Chunk(text))
        {
            var message = await channel.SendMessageAsync(chunk, allowedMentions: AllowedMentions.None);
            _data.LogPost(channelId, message.Id, source);
        }
    }
}
