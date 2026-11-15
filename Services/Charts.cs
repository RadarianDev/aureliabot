using Discord;
using SkiaSharp;

public sealed record ChartSeries(string Name, string Key);

public sealed record RenderedChart(Embed Embed, MemoryStream Image, string FileName) : IDisposable
{
    public FileAttachment Attachment() => ElectionImages.Attach(Image, FileName);

    public void Dispose() => Image.Dispose();
}

public static class Charts
{
    private static readonly (string Hex, Func<string> Square)[] Palette =
    [
        ("#DD2E44", () => Texts.Get("chart.square.red")),
        ("#55ACEE", () => Texts.Get("chart.square.blue")),
        ("#FDCB58", () => Texts.Get("chart.square.yellow")),
        ("#78B159", () => Texts.Get("chart.square.green")),
        ("#AA8ED6", () => Texts.Get("chart.square.purple")),
        ("#F4900C", () => Texts.Get("chart.square.orange")),
        ("#C1694F", () => Texts.Get("chart.square.brown")),
    ];

    public static int MaxSeries => Palette.Length;

    public const string DefaultFileName = "chart.png";

    public static RenderedChart? Build(
        History history, string title, IReadOnlyList<ChartSeries> series, Func<double, string> format, double? floor = null, string fileName = DefaultFileName)
    {
        var shown = series.Take(Palette.Length).Where(s => history.Of(s.Key).Count > 0).ToList();
        if (history.Turns.Count == 0 || shown.Count == 0) return null;

        var turns = history.Turns;
        var values = shown.Select(s => turns.Select(t => history.Of(s.Key).TryGetValue(t.Resolution, out var v) ? v : (double?)null).ToList()).ToList();
        var all = values.SelectMany(v => v).OfType<double>().ToList();
        var scale = ChartScale.Nice(floor is { } f ? Math.Min(f, all.Min()) : all.Min(), all.Max());

        var lines = shown.Select((s, i) => new ChartLine(SKColor.Parse(Palette[i].Hex), values[i])).ToList();
        var yearStarts = turns.Select((t, i) => i > 0 && t.Season == Season.Spring).ToList();
        var image = LineChartGenerator.Render(lines, yearStarts, scale);

        var legend = shown.Select((s, i) =>
        {
            var known = values[i].OfType<double>().ToList();
            return Texts.Get("chart.legend",
                ("square", Palette[i].Square()), ("name", s.Name), ("first", format(known[0])), ("last", format(known[^1])));
        });
        var embed = new EmbedBuilder()
            .WithTitle(title)
            .WithColor(new Color(0xB8, 0x86, 0x0B))
            .WithDescription(Display.Truncate(string.Join("\n", legend), 4096))
            .WithImageUrl($"attachment://{fileName}")
            .WithFooter(Texts.Get("chart.footer",
                ("from", GameCalendar.Label(turns[0].Season, turns[0].Year)),
                ("to", GameCalendar.Label(turns[^1].Season, turns[^1].Year)),
                ("step", format(scale.Step)), ("low", format(scale.Low)), ("high", format(scale.High))))
            .Build();
        return new RenderedChart(embed, image, fileName);
    }
}
