using System.Globalization;
using System.Text;
using Discord;

public sealed record TranscriptMessage(
    DateTimeOffset At,
    string Author,
    string Content,
    IReadOnlyList<string> Attachments,
    IReadOnlyList<string> Embeds,
    IReadOnlyList<string> Stickers);

public static class ChatTranscript
{
    public static string FileName(string channelName, DateTimeOffset at) =>
        $"{channelName}-{at.UtcDateTime.ToString("yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture)}.txt";

    public static string Render(string channelName, string clearedBy, DateTimeOffset at, IEnumerable<TranscriptMessage> messages)
    {
        var ordered = messages.OrderBy(m => m.At).ToList();
        var sb = new StringBuilder();
        sb.AppendLine(Texts.Get("transcript.header", ("channel", channelName), ("by", clearedBy), ("when", Stamp(at)), ("count", ordered.Count)));
        sb.AppendLine();
        if (ordered.Count == 0) sb.AppendLine(Texts.Get("transcript.empty"));
        foreach (var message in ordered)
        {
            sb.AppendLine(Texts.Get("transcript.line", ("when", Stamp(message.At)), ("author", message.Author), ("content", message.Content)));
            foreach (var url in message.Attachments) sb.AppendLine(Texts.Get("transcript.attachment", ("url", url)));
            foreach (var title in message.Embeds) sb.AppendLine(Texts.Get("transcript.embed", ("title", title)));
            foreach (var name in message.Stickers) sb.AppendLine(Texts.Get("transcript.sticker", ("name", name)));
        }
        return sb.ToString();
    }

    public static TranscriptMessage From(IMessage message) => new(
        message.Timestamp,
        message.Author is IGuildUser member ? $"{member.DisplayName} (@{message.Author.Username})" : message.Author.Username,
        message is IUserMessage user
            ? user.Resolve(TagHandling.FullName, TagHandling.NameNoPrefix, TagHandling.Name, TagHandling.FullName, TagHandling.FullNameNoPrefix)
            : message.Content,
        message.Attachments.Select(a => a.Url).ToList(),
        message.Embeds.Select(e => e.Title ?? e.Description ?? e.Url ?? "").ToList(),
        message.Stickers.Select(s => s.Name).ToList());

    private static string Stamp(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC";
}
