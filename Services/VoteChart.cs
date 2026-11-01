public static class VoteChart
{
    public const string YesColour = "#2E9E44";
    public const string AsideColour = "#9A9A9A";
    public const string NoColour = "#C0392B";

    public static int ChamberSize(DatabaseService data) =>
        Math.Max(data.GetGameState().Seats, (int)data.GetAllFactions().Sum(f => f.Seats));

    public static List<PartyData> Blocs(int yes, int no, int seats) =>
        new[]
        {
            new PartyData { Name = "Yes", SeatCount = yes, HexColor = YesColour },
            new PartyData { Name = "Aside", SeatCount = Math.Max(0, seats - yes - no), HexColor = AsideColour },
            new PartyData { Name = "No", SeatCount = no, HexColor = NoColour },
        }.Where(b => b.SeatCount > 0).ToList();

    public static string? Render(DatabaseService data, int yes, int no)
    {
        try
        {
            return ElectionPublisher.RenderSeatChart(Blocs(yes, no, ChamberSize(data)));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[VoteChart] Rendering failed: {ex.Message}");
            return null;
        }
    }

    public static void Discard(string? path)
    {
        if (path is null) return;
        try { File.Delete(path); } catch (IOException) { }
    }
}
