using Xunit;

namespace AureliaBot.Tests;

public class PartyColourTests
{
    [Fact]
    public void IdenticalColours_HaveNoDistance()
    {
        Assert.Equal(0, PartySyncService.ColourDistance("#C0392B", "#c0392b"), 6);
    }

    [Fact]
    public void BlackAndWhite_AreAHundredApart()
    {
        Assert.Equal(100, PartySyncService.ColourDistance("#000000", "#FFFFFF"), 1);
    }

    [Fact]
    public void Distance_IsSymmetric()
    {
        Assert.Equal(
            PartySyncService.ColourDistance("#1F77B4", "#FF7F0E"),
            PartySyncService.ColourDistance("#FF7F0E", "#1F77B4"),
            9);
    }

    [Theory]
    [InlineData("#C0392B", "#C43C2E")]
    [InlineData("#2E86C1", "#3A8FCB")]
    [InlineData("#27AE60", "#2ECC71")]
    public void NearbyShades_AreSimilar(string a, string b)
    {
        Assert.True(PartySyncService.AreSimilarColours(a, b), $"{a} and {b}: {PartySyncService.ColourDistance(a, b)}");
    }

    [Theory]
    [InlineData("#C0392B", "#2E86C1")]
    [InlineData("#F1C40F", "#8E44AD")]
    [InlineData("#000000", "#7F7F7F")]
    [InlineData("#E74C3C", "#E67E22")]
    public void DistinctColours_AreNotSimilar(string a, string b)
    {
        Assert.False(PartySyncService.AreSimilarColours(a, b), $"{a} and {b}: {PartySyncService.ColourDistance(a, b)}");
    }
}
