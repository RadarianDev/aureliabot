using Xunit;

namespace AureliaBot.Tests;

public class ChatTranscriptTests
{
    private static readonly DateTimeOffset Cleared = new(2026, 11, 4, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Render_ListsMessagesOldestFirst_WithAttachmentsEmbedsAndStickers()
    {
        var text = ChatTranscript.Render("liberal-union-foreign-affairs", "Lord Halvard", Cleared,
        [
            new(Cleared.AddMinutes(-5), "Baroness Edel (@edel)", "Agreed.", [], [], ["thumbs"]),
            new(Cleared.AddMinutes(-10), "Lord Halvard (@radar)", "Will you back the Ten Hours Bill?",
                ["https://cdn.example/terms.png"], ["Draft terms"], []),
        ]);

        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        Assert.Contains("#liberal-union-foreign-affairs, 2 message(s), saved by Lord Halvard on 2026-11-04 09:30 UTC", lines[0]);
        Assert.Equal("[2026-11-04 09:20 UTC] Lord Halvard (@radar): Will you back the Ten Hours Bill?", lines[2]);
        Assert.Equal("    attachment: https://cdn.example/terms.png", lines[3]);
        Assert.Equal("    embed: Draft terms", lines[4]);
        Assert.Equal("[2026-11-04 09:25 UTC] Baroness Edel (@edel): Agreed.", lines[5]);
        Assert.Equal("    sticker: thumbs", lines[6]);
    }

    [Fact]
    public void Render_SaysWhenTheChannelWasEmpty_AndNamesTheFileByChannelAndTime()
    {
        Assert.Contains(Texts.Get("transcript.empty"), ChatTranscript.Render("x-foreign-affairs", "A", Cleared, []));
        Assert.Equal("x-foreign-affairs-2026-11-04-0930.txt", ChatTranscript.FileName("x-foreign-affairs", Cleared));
    }
}
