using SkiaSharp;

public class ProvinceMapGenerator
{
    private static readonly string MapDirectory = Path.Combine(AppContext.BaseDirectory, "assets", "map");
    private const float FillLevel = 128f;

    public static string Folder(string? variant)
    {
        if (variant is null) return MapDirectory;
        var folder = Path.Combine(MapDirectory, variant);
        return Directory.Exists(folder) ? folder : MapDirectory;
    }

    public MemoryStream Generate(IReadOnlyDictionary<string, string> partyColourByMapKey, string? variant = null)
    {
        var colourByKey = partyColourByMapKey.ToDictionary(
            entry => SKColor.Parse(entry.Key).WithAlpha(255),
            entry => SKColor.Parse(entry.Value));

        var folder = Folder(variant);
        using var map = LoadAsset(folder, "base.png");
        using var index = LoadAsset(folder, "provinces.png");
        using var overlay = LoadAsset(folder, "overlay.png");

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
            using var overlayImage = SKImage.FromBitmap(overlay);
            canvas.DrawImage(overlayImage, 0, 0, SKSamplingOptions.Default);
        }

        return Png.Encode(map);
    }

    private static SKColor Tint(SKColor pixel, SKColor partyColour) => new(
        Scale(pixel.Red, partyColour.Red),
        Scale(pixel.Green, partyColour.Green),
        Scale(pixel.Blue, partyColour.Blue),
        pixel.Alpha);

    private static byte Scale(byte channel, byte partyChannel) =>
        (byte)Math.Min(255, Math.Round(channel * partyChannel / FillLevel));

    private static SKBitmap LoadAsset(string folder, string fileName) =>
        SKBitmap.Decode(Path.Combine(folder, fileName))
            ?? throw new FileNotFoundException("Map asset is missing or unreadable.", fileName);
}
