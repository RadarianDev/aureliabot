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
    public void Blocs_LeaveOutEmptyOnes()
    {
        Assert.Equal([VoteChart.YesColour, VoteChart.NoColour], VoteChart.Blocs(100, 80, 180).Select(b => b.HexColor));
        Assert.Equal([VoteChart.AsideColour], VoteChart.Blocs(0, 0, 180).Select(b => b.HexColor));
    }

    [Fact]
    public void Render_DrawsAPng()
    {
        var path = ElectionPublisher.RenderSeatChart(VoteChart.Blocs(90, 60, 180));
        try
        {
            Assert.NotNull(path);
            Assert.True(new FileInfo(path!).Length > 0);
        }
        finally
        {
            VoteChart.Discard(path);
        }
    }
}
