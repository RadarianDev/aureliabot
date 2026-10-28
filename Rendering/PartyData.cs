public class PartyData
{
    public string Name { get; set; } = "";
    public int SeatCount { get; set; }
    public string HexColor { get; set; } = "";

    // Generalized field used by Westminster layout engine.
    // Left defaults to "left" so it can safely pass into ArchGenerator unassigned.
    public string Position { get; set; } = "left";

    // Calculated tracker state tracking for Westminster alignment logic
    public int EmptySeats { get; set; } = 0;
}