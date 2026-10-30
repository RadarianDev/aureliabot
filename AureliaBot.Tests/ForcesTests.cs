using Xunit;

namespace AureliaBot.Tests;

public class ForcesTests : AssemblyTestBase
{
    private const ulong WarMinister = Minister;
    private const ulong InteriorMinister = Backbencher;

    public ForcesTests()
    {
        SeatCabinet(Chancellor, (Ministry.War, WarMinister), (Ministry.Interior, InteriorMinister));
        SetPurse(Ministry.War, 10);
        SetPurse(Ministry.Interior, 5);
    }

    private void SetPurse(Ministry ministry, decimal amount) =>
        Db.ApplyEffects([new Effect($"purse.{ministry}", (double)(amount - Db.GetPurseBalance(ministry)))], Config, new EffectSource("test", "gm", "test"));

    private Unit Division(string name) => Db.GetUnits(UnitType.Division).Single(u => u.Name == name);

    private Unit FirstDivision => Db.GetUnits(UnitType.Division).First();

    private void SetSides(long unitId, int sides) => Sql("UPDATE units SET sides = $sides WHERE id = $id;", ("$id", unitId), ("$sides", sides));

    private void SetDiscontent(string province, double value) =>
        Sql("UPDATE provinces SET discontent = $value WHERE id = $id;", ("$id", province), ("$value", value));

    private double Discontent(string province) => Db.GetProvince(province)!.Discontent;

    private ResolutionReport ApplyOrders(params int[] rolls) => Db.ApplyOrders(Config, new ScriptedRandomSource(rolls));

    [Fact]
    public void Raise_PaysAtOnce_AndFormsTheUnitAtTheNextResolution()
    {
        var before = Db.GetUnits(UnitType.Division).Count;
        var result = Db.OrderRaise(UnitType.Division, WarMinister, "1st Guards", "tovrerie", Config);

        Assert.Null(result.Refusal);
        Assert.Equal(6m, Db.GetPurseBalance(Ministry.War));
        Assert.Equal(6m, result.Balance);
        Assert.Contains(Db.GetLedger(LedgerAccount.War), e => e.Kind == LedgerKind.Purchase && e.Amount == -4m);
        Assert.Equal(before, Db.GetUnits(UnitType.Division).Count);
        Assert.Single(Db.GetPendingOrders(Ministry.War));
        Assert.Empty(Db.GetPendingOrders(Ministry.Interior));

        var report = Db.Resolve(Config, new ScriptedRandomSource(), 1, DateTimeOffset.UtcNow);

        var guards = Division("1st Guards");
        Assert.Equal(C.Division.BaseSides, guards.Sides);
        Assert.Equal("tovrerie", guards.ProvinceId);
        Assert.Empty(Db.GetPendingOrders());
        Assert.Contains(report.PrivateMessages[WarMinister], m => m.Contains("1st Guards"));
    }

    [Fact]
    public void Raise_IsRefused_WhenThePurseCannotPay_OrForAnotherMinister()
    {
        SetPurse(Ministry.War, 3);
        Assert.NotNull(Db.OrderRaise(UnitType.Division, WarMinister, "1st Guards", "tovrerie", Config).Refusal);
        Assert.NotNull(Db.OrderRaise(UnitType.Division, InteriorMinister, "1st Guards", "tovrerie", Config).Refusal);
        Assert.NotNull(Db.OrderRaise(UnitType.Gendarmerie, WarMinister, "Tovrerie Watch", "tovrerie", Config).Refusal);
        Assert.Empty(Db.GetPendingOrders());
        Assert.Equal(3m, Db.GetPurseBalance(Ministry.War));
    }

    [Fact]
    public void Chancellor_ActsForAVacantMinistry()
    {
        SeatCabinet(Chancellor, (Ministry.Interior, InteriorMinister));
        Assert.Null(Db.OrderRaise(UnitType.Division, Chancellor, "1st Guards", "tovrerie", Config).Refusal);
        Assert.NotNull(Db.OrderRaise(UnitType.Gendarmerie, Chancellor, "Tovrerie Watch", "tovrerie", Config).Refusal);
    }

    [Fact]
    public void Train_PaysTheNextStep_AndRaisesSidesAtTheNextResolution()
    {
        var unit = FirstDivision;
        var result = Db.OrderTrain(UnitType.Division, WarMinister, unit.Id, Config);

        Assert.Null(result.Refusal);
        Assert.Equal(10m - C.Division.TrainCosts[0], Db.GetPurseBalance(Ministry.War));
        Assert.Equal(unit.Sides, Db.GetUnit(unit.Id)!.Sides);
        Assert.NotNull(Db.OrderTrain(UnitType.Division, WarMinister, unit.Id, Config).Refusal);

        ApplyOrders();

        Assert.Equal(unit.Sides + C.Division.SidesStep, Db.GetUnit(unit.Id)!.Sides);
        Assert.Empty(Db.GetPendingOrders());
    }

    [Fact]
    public void Train_IsRefusedAtMaxSides()
    {
        var unit = FirstDivision;
        SetSides(unit.Id, C.Division.MaxSides);

        var result = Db.OrderTrain(UnitType.Division, WarMinister, unit.Id, Config);

        Assert.NotNull(result.Refusal);
        Assert.Empty(Db.GetPendingOrders());
        Assert.Equal(10m, Db.GetPurseBalance(Ministry.War));
    }

    [Fact]
    public void Retraining_ALostStep_CostsThatStepAgain()
    {
        var unit = FirstDivision;
        SetSides(unit.Id, C.Division.BaseSides + C.Division.SidesStep);

        Assert.Null(Db.OrderTrain(UnitType.Division, WarMinister, unit.Id, Config).Refusal);

        Assert.Equal(10m - C.Division.TrainCosts[1], Db.GetPurseBalance(Ministry.War));
        ApplyOrders();
        Assert.Equal(C.Division.BaseSides + 2 * C.Division.SidesStep, Db.GetUnit(unit.Id)!.Sides);
    }

    [Fact]
    public void Station_IsFree_AndMovesTheUnitAtTheNextResolution()
    {
        var unit = FirstDivision;
        Assert.NotNull(Db.OrderStation(UnitType.Division, WarMinister, unit.Id, unit.ProvinceId, Config).Refusal);

        Assert.Null(Db.OrderStation(UnitType.Division, WarMinister, unit.Id, "tovrerie", Config).Refusal);
        var replaced = Db.OrderStation(UnitType.Division, WarMinister, unit.Id, "silvicarie", Config);

        Assert.True(replaced.Replaced);
        Assert.Single(Db.GetPendingOrders(Ministry.War));
        Assert.Equal(10m, Db.GetPurseBalance(Ministry.War));
        Assert.Equal(unit.ProvinceId, Db.GetUnit(unit.Id)!.ProvinceId);

        ApplyOrders();

        Assert.Equal("silvicarie", Db.GetUnit(unit.Id)!.ProvinceId);
    }

    [Fact]
    public void Disband_IsImmediate_CancelsItsOrders_AndRefundsNothing()
    {
        var unit = FirstDivision;
        Db.OrderTrain(UnitType.Division, WarMinister, unit.Id, Config);
        var balance = Db.GetPurseBalance(Ministry.War);

        var result = Db.Disband(UnitType.Division, WarMinister, unit.Id);

        Assert.Null(result.Refusal);
        Assert.Equal(1, result.CancelledOrders);
        Assert.Null(Db.GetUnit(unit.Id));
        Assert.Empty(Db.GetPendingOrders());
        Assert.Equal(balance, Db.GetPurseBalance(Ministry.War));
    }

    [Fact]
    public void Crackdown_Success_LowersDiscontent_AndLiftsLawless()
    {
        SetDiscontent("aurenten", 5);
        Db.ApplyEffects([new Effect("condition.aurenten.lawless", 1)], Config, new EffectSource("test", "gm", "test"));
        var popularity = Db.GetFaction(InteriorMinister)!.Popularity;

        Assert.Null(Db.OrderCrackdown(InteriorMinister, "aurenten", Config).Refusal);
        Assert.Equal(5m - C.Crackdown.Cost, Db.GetPurseBalance(Ministry.Interior));
        Assert.Contains(Db.GetLedger(LedgerAccount.Interior), e => e.Kind == LedgerKind.Crackdown);

        var report = ApplyOrders(6, 4);

        var crackdown = Assert.Single(report.Crackdowns);
        Assert.True(crackdown.Success);
        Assert.Equal(6, crackdown.GendarmerieTotal);
        Assert.Equal(10, crackdown.DieSides);
        Assert.Equal(4, crackdown.DieRoll);
        Assert.Equal(5 - C.Crackdown.Reduction, Discontent("aurenten"), 6);
        Assert.DoesNotContain("lawless", Db.GetProvinceConditions().GetValueOrDefault("aurenten", []));
        Assert.Equal(popularity, Db.GetFaction(InteriorMinister)!.Popularity);
        Assert.Empty(report.Alerts);
        Assert.Equal(["discontent.aurenten", "condition.aurenten.lawless"], crackdown.Effects.Select(e => e.Key));
        Assert.Equal(-1, crackdown.Effects[1].Amount);
        Assert.Empty(Db.GetPendingOrders());
    }

    [Fact]
    public void Crackdown_Failure_RaisesDiscontent_CostsTheMinister_AndAlertsTheGm()
    {
        SetDiscontent("aurenten", 4.5);
        var popularity = Db.GetFaction(InteriorMinister)!.Popularity;
        Db.OrderCrackdown(InteriorMinister, "aurenten", Config);

        var report = ApplyOrders(1, 8);

        var crackdown = Assert.Single(report.Crackdowns);
        Assert.False(crackdown.Success);
        Assert.Equal(9, crackdown.DieSides);
        Assert.Equal(4.5 + C.Crackdown.Backlash, Discontent("aurenten"), 6);
        Assert.Equal(popularity - C.Crackdown.FailurePopularity, Db.GetFaction(InteriorMinister)!.Popularity, 6);
        Assert.Single(report.Alerts);
        Assert.Contains(report.PrivateMessages[InteriorMinister], m => m.Contains("failed"));
        Assert.Contains(crackdown.Effects, e => e.Key == $"popularity.{InteriorMinister}" && Math.Abs(e.Amount + C.Crackdown.FailurePopularity) < 1e-9);
        Assert.DoesNotContain(crackdown.Effects, e => e.Key.StartsWith("condition."));
    }


    [Fact]
    public void Crackdown_IsCalledOffAndRefunded_WhenItsGendarmerieLeave()
    {
        SetDiscontent("aurenten", 4);
        var corps = Db.GetUnits(UnitType.Gendarmerie).Where(u => u.ProvinceId == "aurenten").ToList();
        var balance = Db.GetPurseBalance(Ministry.Interior);
        Db.OrderCrackdown(InteriorMinister, "aurenten", Config);
        foreach (var unit in corps)
            Db.OrderStation(UnitType.Gendarmerie, InteriorMinister, unit.Id, "iracterie", Config);

        var report = ApplyOrders(1, 1, 1, 1);

        Assert.Empty(report.Crackdowns);
        Assert.Equal(4, Discontent("aurenten"), 6);
        Assert.Equal(balance, Db.GetPurseBalance(Ministry.Interior));
        Assert.Contains(report.PrivateMessages[InteriorMinister], m => m.Contains("called off"));
    }

    [Fact]
    public void Crackdown_Failure_CostsTheMinisterWhoOrderedIt()
    {
        SetDiscontent("aurenten", 4.5);
        Db.OrderCrackdown(InteriorMinister, "aurenten", Config);
        SeatCabinet(Chancellor, (Ministry.War, WarMinister), (Ministry.Interior, Chancellor));
        var ordering = Db.GetFaction(InteriorMinister)!.Popularity;
        var holder = Db.GetFaction(Chancellor)!.Popularity;

        ApplyOrders(1, 8);

        Assert.Equal(ordering - C.Crackdown.FailurePopularity, Db.GetFaction(InteriorMinister)!.Popularity, 6);
        Assert.Equal(holder, Db.GetFaction(Chancellor)!.Popularity, 6);
    }
    [Fact]
    public void Crackdown_RollsEveryCorpsInTheProvince_AfterStationing()
    {
        SetDiscontent("aurenten", 3);
        var iracterie = Db.GetUnits(UnitType.Gendarmerie).Single(u => u.ProvinceId == "iracterie");
        Db.OrderStation(UnitType.Gendarmerie, InteriorMinister, iracterie.Id, "aurenten", Config);
        Db.OrderCrackdown(InteriorMinister, "aurenten", Config);

        var report = ApplyOrders(2, 3, 4);

        var crackdown = Assert.Single(report.Crackdowns);
        Assert.Equal(5, crackdown.GendarmerieTotal);
        Assert.Equal(6, crackdown.DieSides);
        Assert.True(crackdown.Success);
    }

    [Fact]
    public void Crackdown_IsRefused_WithoutGendarmerie_AndTwiceInASeason()
    {
        Assert.NotNull(Db.OrderCrackdown(InteriorMinister, "tovrerie", Config).Refusal);
        Assert.Null(Db.OrderCrackdown(InteriorMinister, "aurenten", Config).Refusal);
        Assert.NotNull(Db.OrderCrackdown(InteriorMinister, "aurenten", Config).Refusal);
        Assert.NotNull(Db.OrderCrackdown(WarMinister, "iracterie", Config).Refusal);
        Assert.Single(Db.GetPendingOrders(Ministry.Interior));
        Assert.Equal(5m - C.Crackdown.Cost, Db.GetPurseBalance(Ministry.Interior));
    }

    [Fact]
    public void Crackdown_IsRefused_WhenThePurseCannotPay()
    {
        SetPurse(Ministry.Interior, 0);
        Assert.NotNull(Db.OrderCrackdown(InteriorMinister, "aurenten", Config).Refusal);
    }

    [Fact]
    public void DivisionEffect_RaisesUnits_AndRemovesTheWeakestFirst()
    {
        var before = Db.GetUnits(UnitType.Division).Count(u => u.ProvinceId == "aurenten");
        var raised = Db.ApplyEffects([new Effect("division.aurenten", 2)], Config, new EffectSource("test", "gm", "test"));

        Assert.Empty(raised.Warnings);
        var units = Db.GetUnits(UnitType.Division).Where(u => u.ProvinceId == "aurenten").ToList();
        Assert.Equal(before + 2, units.Count);
        Assert.Equal(units.Count, units.Select(u => u.Name).Distinct().Count());
        foreach (var unit in units)
            SetSides(unit.Id, C.Division.MaxSides);
        var weakest = units[1];
        SetSides(weakest.Id, C.Division.BaseSides);

        var removed = Db.ApplyEffects([new Effect("division.aurenten", -1)], Config, new EffectSource("test", "gm", "test"));

        Assert.Empty(removed.Warnings);
        Assert.Null(Db.GetUnit(weakest.Id));
        Assert.Equal(before + 1, Db.GetUnits(UnitType.Division).Count(u => u.ProvinceId == "aurenten"));
    }

    [Fact]
    public void GendarmerieEffect_NamesExtraCorpsApart()
    {
        Db.ApplyEffects([new Effect("gendarmerie.aurenten", 2)], Config, new EffectSource("test", "gm", "test"));

        var names = Db.GetUnits(UnitType.Gendarmerie).Where(u => u.ProvinceId == "aurenten").Select(u => u.Name).ToList();
        Assert.Equal(3, names.Count);
        Assert.Equal(3, names.Distinct().Count());
    }
}
