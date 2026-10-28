using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkiaSharp;

public class ArchGenerator
{
    private static readonly int[] Totals = {
        3, 15, 33, 61, 95, 138, 189, 247, 313, 388, 469, 559, 657, 762, 876, 997,
        1126, 1263, 1408, 1560, 1722, 1889, 2066, 2250, 2442, 2641, 2850, 3064,
        3289, 3519, 3759, 4005, 4261, 4522, 4794, 5071, 5358, 5652, 5953, 6263,
        6581, 6906, 7239, 7581, 7929, 8287, 8650, 9024, 9404, 9793, 10187, 10594,
        11003, 11425, 11850, 12288, 12729, 13183, 13638, 14109, 14580, 15066, 15553,
        16055, 16557, 17075, 17592, 18126, 18660, 19208, 19758, 20323, 20888, 21468,
        22050, 22645, 23243, 23853, 24467, 25094, 25723, 26364, 27011, 27667, 28329,
        29001, 29679, 30367, 31061
    };

    public class SeatPosition
    {
        public double Angle { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
    }

    private int totalSeats;
    private int rowCount;
    private double circleRadius;
    private bool denseRows = false;

    public void Generate(List<PartyData> partyData, string savePath)
    {
        if (partyData.Count == 0) return;

        totalSeats = partyData.Sum(p => p.SeatCount);
        if (totalSeats <= 0) return;

        rowCount = Array.FindIndex(Totals, el => el >= totalSeats) + 1;
        if (rowCount <= 0) return;

        circleRadius = 0.4 / rowCount;
        List<SeatPosition> seatPositions = GetSpotCentres();

        float renderScale = 3.0f;
        int width = (int)(360 * renderScale);
        int height = (int)(185 * renderScale);

        using (var bitmap = new SKBitmap(width, height))
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);

            using (var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill })
            {
                int drawnSeats = 0;
                foreach (var party in partyData)
                {
                    paint.Color = SKColor.Parse(party.HexColor);

                    for (int i = drawnSeats; i < drawnSeats + party.SeatCount; i++)
                    {
                        if (i >= seatPositions.Count) break;

                        float cx = (float)(5.0 + 100.0 * seatPositions[i].X) * renderScale;
                        float cy = (float)(5.0 + 100.0 * (1.75 - seatPositions[i].Y)) * renderScale;
                        float r = (float)(circleRadius * 100.0) * renderScale;

                        canvas.DrawCircle(cx, cy, r, paint);
                    }
                    drawnSeats += party.SeatCount;
                }
            }

            using (var image = SKImage.FromBitmap(bitmap))
            using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
            using (var stream = File.OpenWrite(savePath))
            {
                data.SaveTo(stream);
            }
        }
        Console.WriteLine($"Successfully saved to {savePath}");
    }

    private Tuple<int, double> OptimiseRows()
    {
        int handledSpots = 0;
        for (int i = rowCount; i > 0; i--)
        {
            double magicNumber = 3 * rowCount + 4 * i - 2;
            double maximumSeatsInRow = Math.PI / (2 * Math.Asin(2 / magicNumber));
            handledSpots += (int)Math.Truncate(maximumSeatsInRow);

            if (handledSpots >= totalSeats)
            {
                int wastedRows = i - 1;
                double diagramFullness = (double)totalSeats / handledSpots;
                return Tuple.Create(wastedRows, diagramFullness);
            }
        }
        return Tuple.Create(0, 0.0);
    }

    private List<SeatPosition> AppendSeatPositions(List<SeatPosition> positions, int seatsInRow, double rowRadius)
    {
        double ratio = Math.Sin(circleRadius / rowRadius);
        for (int i = 0; i < seatsInRow; i++)
        {
            double angle = 0;
            if (seatsInRow == 1)
            {
                angle = Math.PI / 2;
            }
            else
            {
                angle = i * (Math.PI - 2 * ratio) / (seatsInRow - 1) + ratio;
            }

            positions.Add(new SeatPosition
            {
                Angle = angle,
                X = rowRadius * Math.Cos(angle) + 1.75,
                Y = rowRadius * Math.Sin(angle)
            });
        }
        return positions;
    }

    private List<SeatPosition> AppendRowSeats(List<SeatPosition> positions, int index, double diagramFullness)
    {
        double magicNumber = 3.0 * rowCount + 4.0 * index - 2.0;
        double maximumSeatsInRow = Math.PI / (2 * Math.Asin(2.0 / magicNumber));

        int seatsInCurrentRow = (int)Math.Truncate(diagramFullness * maximumSeatsInRow);
        double currentRowRadius = magicNumber / (4.0 * rowCount);

        return AppendSeatPositions(positions, seatsInCurrentRow, currentRowRadius);
    }

    private List<SeatPosition> AppendFinalSeats(List<SeatPosition> positions)
    {
        int leftoverSeats = totalSeats - positions.Count;
        double finalRowRadius = (7.0 * rowCount - 2.0) / (4.0 * rowCount);
        return AppendSeatPositions(positions, leftoverSeats, finalRowRadius);
    }

    private List<SeatPosition> GetSpotCentres()
    {
        int discardRows = 0;
        double diagramFullness = 0;

        if (denseRows)
        {
            var optimization = OptimiseRows();
            discardRows = optimization.Item1;
            diagramFullness = optimization.Item2;
        }
        else
        {
            diagramFullness = (double)totalSeats / Totals[rowCount - 1];
        }

        List<SeatPosition> positions = new List<SeatPosition>();
        for (int i = discardRows + 1; i < rowCount; i++)
        {
            positions = AppendRowSeats(positions, i, diagramFullness);
        }
        positions = AppendFinalSeats(positions);

        positions.Sort((left, right) =>
        {
            double cmpAngle = right.Angle - left.Angle;
            if (cmpAngle != 0) return cmpAngle > 0 ? 1 : -1;

            double cmpX = right.X - left.X;
            if (cmpX != 0) return cmpX > 0 ? 1 : -1;

            double cmpY = right.Y - left.Y;
            if (cmpY != 0) return cmpY > 0 ? 1 : -1;

            return 0;
        });

        return positions;
    }
}
