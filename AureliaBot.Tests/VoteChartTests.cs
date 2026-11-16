using Xunit;

namespace AureliaBot.Tests;

public class VoteChartTests
{
    [Fact]
    public void Blocs_RunYesThenAsideThenNo_AndFillTheChamber()
    {
        var blocs = VoteChart.Blocs(90, 60, 180);

        Assert.Equal([VoteChart.YesColour, VoteChart.AsideColour, VoteChart.NoColour], blocs.Select(b => b.HexColor));
        Assert.Equal([90, 30, 60], blocs.Select(b => b.SeatCount));
    }

    [Fact]
    public void TheChamber_IsTheSeatsSittingNow_NotASizeAwaitingTheNextElection()
    {
        using var fixture = new DatabaseFixture();
        var db = fixture.Db;
        db.StartGame(DatabaseFixture.Config, DateTimeOffset.UtcNow);
        db.SaveGameState(db.GetGameState() with { Seats = 210 });
        db.CreateFaction(1, "A", "aurenten", 10);
        db.CreateFaction(2, "B", "iracterie", 10);
        db.SetFactionSeats(1, 100);
        db.SetFactionSeats(2, 80);

        Assert.Equal(180, VoteChart.ChamberSize(db));
    }

    [Fact]
    public void Blocs_LeaveOutEmptyOnes()
    {
        Assert.Equal([VoteChart.YesColour, VoteChart.NoColour], VoteChart.Blocs(100, 80, 180).Select(b => b.HexColor));
        Assert.Equal([VoteChart.AsideColour], VoteChart.Blocs(0, 0, 180).Select(b => b.HexColor));
    }

    [Fact]
    public void Render_DrawsAPng_InMemory()
    {
        using var image = ElectionPublisher.RenderSeatChart(VoteChart.Blocs(90, 60, 180));

        Assert.NotNull(image);
        Assert.Equal(0, image!.Position);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, image.ToArray()[..4]);
    }

    [Fact]
    public void AnImageAlreadyRead_IsRewoundBeforeEveryUpload()
    {
        using var image = ElectionPublisher.RenderSeatChart(VoteChart.Blocs(90, 60, 180))!;
        image.CopyTo(Stream.Null);

        Assert.Equal(0, VoteChart.Rewound(image).Position);
        image.CopyTo(Stream.Null);
        using var attachment = ElectionImages.Attach(image, "seats.png");
        Assert.Equal(0, attachment.Stream.Position);
    }

    [Fact]
    public void NothingToDraw_GivesNoImage() =>
        Assert.Null(ElectionPublisher.RenderSeatChart([]));
}
