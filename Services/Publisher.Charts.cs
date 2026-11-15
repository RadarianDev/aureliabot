using Discord;

public partial class Publisher
{
    private async Task PostYearlyChartsAsync(ResolutionReport report, string source)
    {
        if (report.ToSeason != Season.Spring) return;
        if (_data.GetPostingChannel(ChannelRole.News) is not { } channelId || _client.GetChannel(channelId) is not IMessageChannel channel) return;
        foreach (var what in new[] { PublicChart.Economy, PublicChart.Money })
        {
            try
            {
                using var chart = ChartRequests.Public(_data, what, null);
                if (chart is null) continue;
                using var attachment = chart.Attachment();
                var message = await channel.SendFileAsync(attachment, embed: chart.Embed, allowedMentions: AllowedMentions.None);
                _data.LogPost(channelId, message.Id, source);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Publisher] Yearly {what} chart failed: {ex.Message}");
            }
        }
    }
}
