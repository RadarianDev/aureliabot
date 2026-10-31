using Xunit;

namespace AureliaBot.Tests;

public class BattleTests : AssemblyTestBase
{
    private static readonly Unit Guards = new(1, UnitType.Division, "Royal Guard", 8, "aurenten");
    private static readonly Unit Lancers = new(2, UnitType.Division, "Lancers", 6, "aurenten");
    private static readonly Unit Militia = new(3, UnitType.Gendarmerie, "City Militia", 6, "iracterie");
    private static readonly IReadOnlyList<Unit> Units = [Guards, Lancers, Militia];
    private static readonly CrackdownConstants Crackdown = new() { DieBase = 4, DiePerDiscontent = 1 };

    private static BattleSide Side(string text) => BattleRolls.ParseSide(text, Units).Side!;

    [Theory]
    [InlineData("6d12+2", 6, 12, 2)]
    [InlineData("d6", 1, 6, 0)]
    [InlineData("3D8-1", 3, 8, -1)]
    public void Dice_AreParsed(string text, int count, int sides, int bonus)
    {
        Assert.True(BattleRolls.TryParseDice(text, out var dice));
        Assert.Equal(new BattleDice(count, sides, bonus), dice);
    }

    [Theory]
    [InlineData("6d0")]
    [InlineData("0d6")]
    [InlineData("abc")]
    [InlineData("101d6")]
    [InlineData("6d12+")]
    public void BadDice_AreRejected(string text) => Assert.False(BattleRolls.TryParseDice(text, out _));

    [Fact]
    public void ASide_TakesIds_Names_AndDice()
    {
        var side = Side("1, Lancers, #3 2d6+1");

        Assert.Equal([Guards, Lancers, Militia], side.Units);
        Assert.Equal([new BattleDice(2, 6, 1)], side.Dice);
    }

    [Theory]
    [InlineData("1, Hussars")]
    [InlineData("1, 1")]
    [InlineData(" , ")]
    [InlineData("99")]
    public void ASide_IsRefused_ForUnknownEntries_RepeatsAndNothing(string text)
    {
        var parsed = BattleRolls.ParseSide(text, Units);

        Assert.Null(parsed.Side);
        Assert.False(string.IsNullOrWhiteSpace(parsed.Error));
    }

    [Fact]
    public void DiceWrittenWithSpaces_AreParsed() =>
        Assert.Equal((6, 12, 2), Side("6d12 + 2, 1").Dice.Select(d => (d.Count, d.Sides, d.Bonus)).Single());

    [Fact]
    public void AUnit_CannotFightOnBothSides() =>
        Assert.Equal("Lancers", BattleRolls.SharedUnit(Side("1, 2"), Side("2, 3d6")));

    [Fact]
    public void TheHigherTotalWins_AndALoserWithOnlyDiceLosesNothing()
    {
        var result = BattleRolls.Fight(Side("2d6+1"), Side("1, 2, 3"), new ScriptedRandomSource(6, 5, 7, 2, 4));

        Assert.Equal(12, result.Attackers.Total);
        Assert.Equal(13, result.Defenders.Total);
        Assert.False(result.AttackersWin);
        Assert.Equal([6, 5], result.Attackers.Dice.Single().Rolls);
        Assert.False(result.Rout);
        Assert.Empty(result.Losses);
    }

    [Fact]
    public void ADoubleTotal_CostsTheLoserItsTwoLowestUnits()
    {
        var result = BattleRolls.Fight(Side("1, 2, 3"), Side("2d10"), new ScriptedRandomSource(1, 4, 1, 5, 5));

        Assert.Equal(6, result.Attackers.Total);
        Assert.Equal(10, result.Defenders.Total);
        Assert.False(result.AttackersWin);
        Assert.False(result.Rout);
        Assert.Equal([Militia], result.Losses);

        var rout = BattleRolls.Fight(Side("1, 2, 3"), Side("2d10"), new ScriptedRandomSource(1, 4, 1, 9, 9));

        Assert.True(rout.Rout);
        Assert.Equal([Militia, Guards], rout.Losses);
    }

    [Fact]
    public void ATie_GoesToTheDefender()
    {
        var result = BattleRolls.Fight(Side("1"), Side("2"), new ScriptedRandomSource(4, 4));

        Assert.False(result.AttackersWin);
        Assert.False(result.Rout);
        Assert.Equal([Guards], result.Losses);
    }

    [Fact]
    public void AdHocDice_AddTheirBonusOnce_AndAreNeverLost()
    {
        var result = BattleRolls.Fight(Side("1"), Side("3d4+2"), new ScriptedRandomSource(8, 1, 1, 1));

        Assert.Equal(5, result.Defenders.Total);
        Assert.True(result.AttackersWin);
        Assert.Empty(result.Losses);
    }

    [Fact]
    public void ARiot_RollsTheGendarmerieAgainstTheDiscontentDie()
    {
        var result = BattleRolls.Riot([Militia, Militia with { Id = 4 }], 4.5, Crackdown, new ScriptedRandomSource(3, 4, 7));

        Assert.Equal(7, result.Total);
        Assert.Equal(9, result.DieSides);
        Assert.Equal(7, result.DieRoll);
        Assert.True(result.Held);
    }

    [Fact]
    public void ARiot_WithoutGendarmerie_RollsZero()
    {
        var result = BattleRolls.Riot([], 0.4, Crackdown, new ScriptedRandomSource(1));

        Assert.Equal(0, result.Total);
        Assert.Equal(4, result.DieSides);
        Assert.False(result.Held);
    }

    [Fact]
    public void ConfirmedLosses_RemoveTheUnits_AsAGmOverride()
    {
        var units = Db.GetUnits().Take(2).ToList();

        var removed = Db.RemoveUnitsAsGm(units.Select(u => u.Id).Append(units[0].Id), 77, "gm roll battle");

        Assert.Equal(units, removed);
        Assert.All(units, u => Assert.Null(Db.GetUnit(u.Id)));
        Assert.Equal(2, Db.GetLogEntries(LogKinds.GmOverride, DateTimeOffset.UtcNow.AddMinutes(-5)).Count(e => e.Source == "gm roll battle"));
        Assert.Empty(Db.RemoveUnitsAsGm(units.Select(u => u.Id), 77, "gm roll battle"));
    }
}
