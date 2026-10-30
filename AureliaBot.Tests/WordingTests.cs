using Xunit;

namespace AureliaBot.Tests;

public class WordingTests
{
    [Theory]
    [InlineData(1.0, "strongly for")]
    [InlineData(0.6, "strongly for")]
    [InlineData(0.5999, "for")]
    [InlineData(0.2, "for")]
    [InlineData(0.1999, "divided")]
    [InlineData(0.0, "divided")]
    [InlineData(-0.1999, "divided")]
    [InlineData(-0.2, "against")]
    [InlineData(-0.6, "against")]
    [InlineData(-0.6001, "strongly against")]
    [InlineData(-1.0, "strongly against")]
    public void Opinion_thresholds(double opinion, string expected) =>
        Assert.Equal(expected, Wording.Opinion(opinion));

    [Theory]
    [InlineData(-1.0, "forgotten")]
    [InlineData(0.0, "forgotten")]
    [InlineData(0.49, "forgotten")]
    [InlineData(0.5, "a minor matter")]
    [InlineData(1.0, "a minor matter")]
    [InlineData(2.0, "discussed")]
    [InlineData(2.5, "much discussed")]
    [InlineData(3.0, "much discussed")]
    [InlineData(3.6, "a burning question")]
    [InlineData(4.0, "a burning question")]
    [InlineData(4.5, "the talk of the nation")]
    [InlineData(5.0, "the talk of the nation")]
    [InlineData(7.0, "the talk of the nation")]
    public void Salience_rounds_and_clamps(double salience, string expected) =>
        Assert.Equal(expected, Wording.Salience(salience));

    [Theory]
    [InlineData(100, "content")]
    [InlineData(70, "content")]
    [InlineData(69.99, "calm")]
    [InlineData(50, "calm")]
    [InlineData(49.99, "restless")]
    [InlineData(30, "restless")]
    [InlineData(29.99, "resentful")]
    [InlineData(10, "resentful")]
    [InlineData(9.99, "defiant")]
    [InlineData(0, "defiant")]
    public void Favour_thresholds(double favour, string expected) =>
        Assert.Equal(expected, Wording.Favour(favour));

    [Theory]
    [InlineData(0, "quiet")]
    [InlineData(1.99, "quiet")]
    [InlineData(2, "grumbling")]
    [InlineData(3.99, "grumbling")]
    [InlineData(4, "restless")]
    [InlineData(5.99, "restless")]
    [InlineData(6, "unruly")]
    [InlineData(7.99, "unruly")]
    [InlineData(8, "rioting")]
    [InlineData(10, "rioting")]
    public void Discontent_thresholds(double discontent, string expected) =>
        Assert.Equal(expected, Wording.Discontent(discontent));

    [Fact]
    public void PollLine_in_a_province() =>
        Assert.Equal(
            "In North Arpal, The Condition of Labour is *a burning question*, and the people are *strongly for* it.",
            Wording.PollLine("North Arpal", "The Condition of Labour", 0.7, 4.2));

    [Fact]
    public void PollLine_nationally() =>
        Assert.Equal(
            "Across the kingdom, Free Trade is *discussed*, and the people are *against* it.",
            Wording.PollLine(null, "Free Trade", -0.3, 2));

    [Fact]
    public void PollLine_divided() =>
        Assert.Equal(
            "Across the kingdom, Free Trade is *forgotten*, and the people are *divided* on it.",
            Wording.PollLine(null, "Free Trade", 0.05, 0));
}
