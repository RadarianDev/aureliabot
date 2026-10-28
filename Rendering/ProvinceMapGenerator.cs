using SkiaSharp;

public class ProvinceMapGenerator
{
    private static readonly string MapDirectory = Path.Combine(AppContext.BaseDirectory, "assets", "map");
    private const float FillLevel = 128f;

    public void Generate(IReadOnlyDictionary<string, string> partyColourByMapKey, string savePath)
    {
        var colourByKey = partyColourByMapKey.ToDictionary(
            entry => SKColor.Parse(entry.Key).WithAlpha(255),
            entry => SKColor.Parse(entry.Value));

        using var map = LoadAsset("base.png");
        using var index = LoadAsset("provinces.png");
        using var overlay = LoadAsset("overlay.png");

        if (index.Width != map.Width || index.Height != map.Height
            || overlay.Width != map.Width || overlay.Height != map.Height)
            throw new InvalidOperationException("Map assets must all be the same size.");

        var pixels = map.Pixels;
        var keys = index.Pixels;
        for (int i = 0; i < pixels.Length; i++)
        {
            if (keys[i].Alpha == 0 || !colourByKey.TryGetValue(keys[i].WithAlpha(255), out var partyColour))
                continue;

            pixels[i] = Tint(pixels[i], partyColour);
        }
        map.Pixels = pixels;

        using (var canvas = new SKCanvas(map))
        {
            canvas.DrawBitmap(overlay, 0, 0);
        }

        using var image = SKImage.FromBitmap(map);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(savePath);
        data.SaveTo(stream);
    }

    private static SKColor Tint(SKColor pixel, SKColor partyColour) => new(
        Scale(pixel.Red, partyColour.Red),
        Scale(pixel.Green, partyColour.Green),
        Scale(pixel.Blue, partyColour.Blue),
        pixel.Alpha);

    private static byte Scale(byte channel, byte partyChannel) =>
        (byte)Math.Min(255, Math.Round(channel * partyChannel / FillLevel));

    private static SKBitmap LoadAsset(string fileName) =>
        SKBitmap.Decode(Path.Combine(MapDirectory, fileName))
            ?? throw new FileNotFoundException("Map asset is missing or unreadable.", fileName);
}
