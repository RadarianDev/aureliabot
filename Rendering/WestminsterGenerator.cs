using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkiaSharp;

public class WestminsterGenerator
{
    public class Options
    {
        public bool Cozy { get; set; } = false;
        public bool FullWidth { get; set; } = false;
        public int CenterCols { get; set; } = 0;
        public int WingRows { get; set; } = 0;
        public double Radius { get; set; } = 0.5;
        public double Spacing { get; set; } = 0.1;
    }

    public class Position2D
    {
        public double X { get; set; }
        public double Y { get; set; }
    }

    private Dictionary<string, int> sumDelegates = new Dictionary<string, int>();
    private Dictionary<string, int> emptySeats = new Dictionary<string, int>();
    private Dictionary<string, int> wingRowsMap = new Dictionary<string, int>();
    private int wingCols;
    private int centerRows;
    private int totalCols;
    private int leftOffset;
    private int totalRows;
    private double blockSize;
    private double radius;
    private float svgWidth;
    private float svgHeight;

    private Dictionary<string, List<Position2D>> posList = new Dictionary<string, List<Position2D>>
    {
        { "left", new List<Position2D>() },
        { "right", new List<Position2D>() },
        { "center", new List<Position2D>() },
        { "head", new List<Position2D>() }
    };

    public void Generate(List<PartyData> partyData, Options options, string savePath)
    {
        var inputParties = partyData
            .Where(p => p.SeatCount > 0)
            .Select(p =>
            {
                if (!new[] { "left", "right", "center", "head" }.Contains(p.Position?.ToLower()))
                {
                    p.Position = "left";
                }
                p.Position = p.Position.ToLower();
                p.EmptySeats = 0;
                return p;
            }).ToList();

        if (inputParties.Count == 0) return;

        CalculateLayout(inputParties, options);
        BuildCoordinates(inputParties, options);

        float renderScale = 3.0f;
        int width = (int)(svgWidth * renderScale);
        int height = (int)(svgHeight * renderScale);

        using (var bitmap = new SKBitmap(width, height))
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);

            using (var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill })
            {
                DrawPositionGroup(canvas, paint, inputParties, "head", options, renderScale);
                DrawPositionGroup(canvas, paint, inputParties, "left", options, renderScale, jumpLogic: !options.FullWidth && !options.Cozy);
                DrawPositionGroup(canvas, paint, inputParties, "right", options, renderScale, jumpLogic: !options.FullWidth && !options.Cozy);
                DrawPositionGroup(canvas, paint, inputParties, "center", options, renderScale);
            }

            using (var image = SKImage.FromBitmap(bitmap))
            using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
            using (var stream = File.OpenWrite(savePath))
            {
                data.SaveTo(stream);
            }
        }
        Console.WriteLine($"Successfully saved Westminster layout to {savePath}");
    }

    private void CalculateLayout(List<PartyData> inputParties, Options options)
    {
        string[] positions = { "left", "right", "center", "head" };
        foreach (var pos in positions)
        {
            sumDelegates[pos] = inputParties.Where(p => p.Position == pos).Sum(p => p.SeatCount);
            emptySeats[pos] = 0;
        }

        int calculatedWingRows = options.WingRows;
        if (calculatedWingRows == 0)
        {
            calculatedWingRows = (int)Math.Ceiling(Math.Sqrt(Math.Max(1, Math.Max(sumDelegates["left"], sumDelegates["right"])) / 20.0)) * 2;
        }
        wingRowsMap["left"] = calculatedWingRows;
        wingRowsMap["right"] = calculatedWingRows;

        if (options.Cozy)
        {
            wingCols = (int)Math.Ceiling((double)Math.Max(sumDelegates["left"], sumDelegates["right"]) / wingRowsMap["left"]);
        }
        else
        {
            foreach (var wing in new[] { "left", "right" })
            {
                foreach (var party in inputParties.Where(p => p.Position == wing))
                {
                    party.EmptySeats = (wingRowsMap[wing] + ((-party.SeatCount) % wingRowsMap[wing])) % wingRowsMap[wing];
                    emptySeats[wing] += party.EmptySeats;
                }
            }
            wingCols = (int)Math.Ceiling((double)Math.Max(sumDelegates["left"] + emptySeats["left"], sumDelegates["right"] + emptySeats["right"]) / wingRowsMap["left"]);
        }

        if (options.FullWidth)
        {
            foreach (var wing in new[] { "left", "right" })
            {
                if (!options.Cozy)
                {
                    for (int i = wingRowsMap[wing]; i > 1; i--)
                    {
                        int tempGaps = 0;
                        foreach (var party in inputParties.Where(p => p.Position == wing))
                        {
                            tempGaps += ((i - 1) + ((-party.SeatCount) % (i - 1))) % (i - 1);
                        }

                        if (sumDelegates[wing] + tempGaps > wingCols * (i - 1))
                        {
                            break;
                        }
                        else
                        {
                            emptySeats[wing] = tempGaps;
                            wingRowsMap[wing] = i - 1;
                            foreach (var party in inputParties.Where(p => p.Position == wing))
                            {
                                party.EmptySeats = ((i - 1) + ((-party.SeatCount) % (i - 1))) % (i - 1);
                            }
                        }
                    }
                }
                else
                {
                    wingRowsMap[wing] = (int)Math.Ceiling((double)sumDelegates[wing] / wingCols);
                }
            }
        }

        int optCenterCols = options.CenterCols == 0 ? (int)Math.Ceiling(Math.Sqrt(sumDelegates["center"] / 4.0)) : options.CenterCols;
        centerRows = optCenterCols == 0 ? 0 : (int)Math.Ceiling((double)sumDelegates["center"] / optCenterCols);

        if (sumDelegates["head"] > 0)
        {
            totalCols = Math.Max(wingCols + 1, sumDelegates["head"]);
            leftOffset = 1;
        }
        else
        {
            leftOffset = 0;
            totalCols = wingCols;
        }

        if (sumDelegates["center"] > 0)
        {
            totalCols += 1 + optCenterCols;
        }

        totalRows = Math.Max(wingRowsMap["left"] + wingRowsMap["right"] + 2, centerRows);
        blockSize = 350.0 / Math.Max(totalCols, totalRows);

        double baseRadius = Math.Min(0.5, options.Radius);
        radius = baseRadius * blockSize * (1.0 - options.Spacing);

        svgWidth = (float)(blockSize * totalCols + 10.0);
        svgHeight = (float)(blockSize * totalRows + 10.0);
    }

    private void BuildCoordinates(List<PartyData> inputParties, Options options)
    {
        double centerTop = svgHeight / 2.0 - blockSize * (1.0 - options.Spacing) / 2.0;
        centerTop += (wingRowsMap["left"] - wingRowsMap["right"]) * blockSize / 2.0;

        for (int x = 0; x < sumDelegates["head"]; x++)
        {
            posList["head"].Add(new Position2D { X = 5.0 + blockSize * (x + options.Spacing / 2.0), Y = centerTop });
        }

        int optCenterCols = options.CenterCols == 0 ? (int)Math.Ceiling(Math.Sqrt(sumDelegates["center"] / 4.0)) : options.CenterCols;
        for (int x = 0; x < optCenterCols; x++)
        {
            int thisCol = Math.Min(centerRows, sumDelegates["center"] - x * centerRows);
            for (int y = 0; y < thisCol; y++)
            {
                posList["center"].Add(new Position2D
                {
                    X = svgWidth - 5.0 - (optCenterCols - x - options.Spacing / 2.0) * blockSize,
                    Y = ((svgHeight - thisCol * blockSize) / 2.0) + blockSize * (y + options.Spacing / 2.0)
                });
            }
        }
        posList["center"].Sort((a, b) => a.Y.CompareTo(b.Y));

        for (int x = 0; x < wingCols; x++)
        {
            for (int y = 0; y < wingRowsMap["left"]; y++)
            {
                posList["left"].Add(new Position2D
                {
                    X = 5.0 + (leftOffset + x + options.Spacing / 2.0) * blockSize,
                    Y = centerTop - (1.5 + y) * blockSize
                });
            }
            for (int y = 0; y < wingRowsMap["right"]; y++)
            {
                posList["right"].Add(new Position2D
                {
                    X = 5.0 + (leftOffset + x + options.Spacing / 2.0) * blockSize,
                    Y = centerTop + (1.5 + y) * blockSize
                });
            }
        }

        foreach (var wing in new[] { "left", "right" })
        {
            if (options.FullWidth && wingRowsMap[wing] > 1)
            {
                // 1. Initial baseline sort: Align coordinates into strict vertical columns
                if (wing == "right")
                    posList[wing].Sort((a, b) => a.Y.CompareTo(b.Y));
                else
                    posList[wing].Sort((a, b) => b.Y.CompareTo(a.Y));

                if (options.Cozy)
                {
                    if (posList[wing].Count > sumDelegates[wing])
                    {
                        posList[wing] = posList[wing].GetRange(0, sumDelegates[wing]);
                    }
                    posList[wing].Sort((a, b) => a.X.CompareTo(b.X));
                }
                else
                {
                    // --- FIX APPLIED HERE ---
                    // Instead of calculating fragmented gaps per party, we sort the physical grid coordinates
                    // to ensure empty seats are pushed exclusively to the outermost trailing edge.

                    posList[wing].Sort((a, b) => a.X.CompareTo(b.X));

                    int currentRows = wingRowsMap[wing];

                    // Re-order coordinates so they fill columns from the inside out (back rows to front rows)
                    // but ensure that empty slots accumulate purely at the bottom-right/top-right outer fringes.
                    posList[wing] = posList[wing]
                        .Select((pos, idx) => new { pos, idx })
                        .OrderBy(item => item.idx / currentRows) // Group by column block
                        .ThenBy(item => wing == "right" ? item.pos.Y : -item.pos.Y) // Stack cleanly top-to-bottom
                        .Select(item => item.pos)
                        .ToList();

                    int totalAvailableSlots = posList[wing].Count;
                    int totalSeatsNeeded = sumDelegates[wing];
                    int globalGaps = totalAvailableSlots - totalSeatsNeeded;

                    // Mask out the absolute excess seats from the very end of the coordinates list
                    // rather than scattering them between party boundaries.
                    for (int i = totalSeatsNeeded; i < totalAvailableSlots; i++)
                    {
                        if (i < posList[wing].Count)
                        {
                            posList[wing][i].X = 999; // Mask out-of-bounds slot
                        }
                    }
                }
            }
            else
            {
                posList[wing].Sort((a, b) => a.X.CompareTo(b.X));
            }
        }
    }

    private void DrawPositionGroup(SKCanvas canvas, SKPaint paint, List<PartyData> parties, string position, Options options, float scale, bool jumpLogic = false)
    {
        int counter = 0;
        var positions = posList[position];

        foreach (var party in parties.Where(p => p.Position == position))
        {
            paint.Color = SKColor.Parse(party.HexColor);
            int limit = counter + party.SeatCount;

            for (; counter < limit; counter++)
            {
                if (counter >= positions.Count || positions[counter].X == 999) continue;

                float x = (float)positions[counter].X * scale;
                float y = (float)positions[counter].Y * scale;
                float w = (float)(blockSize * (1.0 - options.Spacing)) * scale;
                float h = (float)(blockSize * (1.0 - options.Spacing)) * scale;
                float r = (float)radius * scale;

                var rect = new SKRect(x, y, x + w, y + h);
                canvas.DrawRoundRect(rect, r, r, paint);
            }

            if (jumpLogic)
            {
                int currentWingRows = wingRowsMap[position];
                counter += (currentWingRows + ((-party.SeatCount) % currentWingRows)) % currentWingRows;
            }
        }
    }
}