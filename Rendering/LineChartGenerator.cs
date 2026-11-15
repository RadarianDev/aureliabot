using SkiaSharp;

public sealed record ChartLine(SKColor Colour, IReadOnlyList<double?> Values);

public sealed record ChartScale(double Low, double High, double Step)
{
    public static ChartScale Nice(double min, double max, int ticks = 5)
    {
        if (max - min < 1e-9)
        {
            var pad = Math.Max(Math.Abs(max) * 0.1, 1);
            min -= pad;
            max += pad;
        }
        var rough = (max - min) / ticks;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        var residual = rough / magnitude;
        var step = (residual > 5 ? 10 : residual > 2 ? 5 : residual > 1 ? 2 : 1) * magnitude;
        return new ChartScale(Math.Floor(min / step) * step, Math.Ceiling(max / step) * step, step);
    }
}

public static class LineChartGenerator
{
    private const int Width = 1000;
    private const int Height = 500;
    private const float Margin = 36;

    private static readonly SKColor Background = SKColor.Parse("#232428");
    private static readonly SKColor Grid = SKColor.Parse("#3A3C43");
    private static readonly SKColor Baseline = SKColor.Parse("#6D6F78");
    private static readonly SKColor YearLine = SKColor.Parse("#2E3036");

    public static MemoryStream Render(IReadOnlyList<ChartLine> lines, IReadOnlyList<bool> yearStarts, ChartScale scale)
    {
        using var bitmap = new SKBitmap(Width, Height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(Background);
            var points = Math.Max(yearStarts.Count, 1);
            float X(int index) => points == 1 ? Width / 2f : Margin + index * (Width - 2 * Margin) / (points - 1);
            float Y(double value) => (float)(Height - Margin - (value - scale.Low) / (scale.High - scale.Low) * (Height - 2 * Margin));

            using var yearPaint = new SKPaint { Color = YearLine, StrokeWidth = 2, IsAntialias = true, PathEffect = SKPathEffect.CreateDash([8, 8], 0) };
            for (var i = 0; i < yearStarts.Count; i++)
                if (yearStarts[i]) canvas.DrawLine(X(i), Margin, X(i), Height - Margin, yearPaint);

            using var gridPaint = new SKPaint { Color = Grid, StrokeWidth = 1.5f, IsAntialias = true };
            using var basePaint = new SKPaint { Color = Baseline, StrokeWidth = 2.5f, IsAntialias = true };
            for (var value = scale.Low; value <= scale.High + scale.Step / 2; value += scale.Step)
                canvas.DrawLine(Margin, Y(value), Width - Margin, Y(value), Math.Abs(value - scale.Low) < scale.Step / 2 ? basePaint : gridPaint);

            foreach (var line in lines)
            {
                using var stroke = new SKPaint
                {
                    Color = line.Colour, StrokeWidth = 5, IsAntialias = true, Style = SKPaintStyle.Stroke,
                    StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round,
                };
                using var dot = new SKPaint { Color = line.Colour, IsAntialias = true, Style = SKPaintStyle.Fill };
                using var path = new SKPath();
                var drawing = false;
                for (var i = 0; i < line.Values.Count; i++)
                {
                    if (line.Values[i] is not { } value)
                    {
                        drawing = false;
                        continue;
                    }
                    if (drawing) path.LineTo(X(i), Y(value));
                    else path.MoveTo(X(i), Y(value));
                    drawing = true;
                    if (points <= 40) canvas.DrawCircle(X(i), Y(value), 6, dot);
                }
                canvas.DrawPath(path, stroke);
            }
        }

        return Png.Encode(bitmap);
    }
}
