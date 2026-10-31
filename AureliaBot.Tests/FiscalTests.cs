using Microsoft.Data.Sqlite;
using Xunit;

namespace AureliaBot.Tests;

public class FiscalTests : IDisposable
{
    private static double Navy => Config.Constants.DefaultAllocation[Ministry.Navy];
    private const ulong Chancellor = 1;
    private const ulong Treasurer = 2;
    private const ulong WarMinister = 3;

    private readonly DatabaseFixture _fixture = new();
    private DatabaseService Db => _fixture.Db;

    public void Dispose() => _fixture.Dispose();

    private static GameConfig Config => DatabaseFixture.Config;

    private void Start(bool treasuryVacant = false)
    {
        Db.StartGame(Config, DateTimeOffset.UtcNow);
        for (ulong id = 1; id <= 5; id++)
        {
            Db.CreateFaction(id, $"F{id}", "aurenten", 20);
            Db.SetFactionSeats(id, 10);
        }
        Assert.Null(Db.ImposeCabinet(Chancellor, new Dictionary<Ministry, ulong?>
        {
            [Ministry.Treasury] = treasuryVacant ? null : Treasurer, [Ministry.War] = WarMinister, [Ministry.Interior] = 4, [Ministry.CommerceLabor] = 5,
        }).Refusal);
    }

    private void SetSeason(Season season) => Db.SaveGameState(Db.GetGameState() with { Season = season });

    private ResolutionReport Resolve() =>
        Db.Resolve(Config, new ScriptedRandomSource([], Enumerable.Repeat(0.5, 20)), 1, DateTimeOffset.UtcNow);

    private ResolutionReport ToWinter()
    {
        SetSeason(Season.Autumn);
        var report = Resolve();
        Assert.Equal(Season.Winter, Db.GetGameState().Season);
        return report;
    }

    private void Sql(string sql)
    {
        using var conn = new SqliteConnection(_fixture.ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static Dictionary<Ministry, decimal> Split(decimal budget, double war, double interior, double commerce, double treasury, double navy) => new()
    {
        [Ministry.War] = Math.Round(budget * (decimal)war, 2, MidpointRounding.ToZero),
        [Ministry.Interior] = Math.Round(budget * (decimal)interior, 2, MidpointRounding.ToZero),
        [Ministry.CommerceLabor] = Math.Round(budget * (decimal)commerce, 2, MidpointRounding.ToZero),
        [Ministry.Treasury] = Math.Round(budget * (decimal)treasury, 2, MidpointRounding.ToZero),
        [Ministry.Navy] = Math.Round(budget * (decimal)navy, 2, MidpointRounding.AwayFromZero),
    };

    private decimal NextBudget() => Db.GetFiscalYear(Db.GetGameState().Year + 1)!.Budget;

    [Fact]
    public void RaisingTheNavyShare_PleasesTheNobles()
    {
        Start();
        ToWinter();
        var budget = NextBudget();
        var amounts = Split(budget, 0.25, 0.2, 0.2, 0.1, 0.25);

        var preview = Db.PreviewAllocation(Treasurer, amounts);
        Assert.Null(preview.Refusal);
        Assert.Equal(NavyReaction.Pleased, preview.Reaction);
        Assert.Null(Db.SubmitAllocation(Treasurer, amounts).Refusal);
        Resolve();

        var share = (double)(amounts[Ministry.Navy] / budget);
        var state = Db.GetGameState();
        Assert.Equal(60 + (share - Navy) * Config.Constants.Favour.NavyFactor, state.Favour, 9);
        Assert.Equal(share, state.LastNavyShare, 12);
    }

    [Fact]
    public void CuttingTheNavyShare_InsultsTheNobles()
    {
        Start();
        ToWinter();
        var amounts = Split(NextBudget(), 0.3, 0.2, 0.25, 0.1, 0);

        Assert.Equal(NavyReaction.Insulted, Db.PreviewAllocation(Treasurer, amounts).Reaction);
        Db.SubmitAllocation(Treasurer, amounts);
        Resolve();

        Assert.Equal(60 - Navy * Config.Constants.Favour.NavyFactor, Db.GetGameState().Favour, 9);
        Assert.Equal(60 - Navy * Config.Constants.Favour.NavyFactor, Db.GetGameState().FavourBaseline, 9);
        Assert.Equal(0, Db.GetGameState().LastNavyShare);
    }

    [Fact]
    public void AnUnchangedNavyShare_LeavesFavourAlone()
    {
        Start();
        ToWinter();
        var amounts = Split(NextBudget(), 0.25, 0.15, 0.3, 0.1, 0.2);

        Assert.Equal(NavyReaction.Indifferent, Db.PreviewAllocation(Treasurer, amounts).Reaction);
        Db.SubmitAllocation(Treasurer, amounts);
        var report = Resolve();

        Assert.Equal(60, Db.GetGameState().Favour);
        Assert.Equal(Navy, Db.GetGameState().LastNavyShare);
        Assert.DoesNotContain(report.News, line => line.Contains("Anzarian nobles"));
    }

    [Fact]
    public void WithNoAllocation_FavourIsLeftAlone()
    {
        Start();
        ToWinter();
        Resolve();
        Assert.Equal(60, Db.GetGameState().Favour);
        Assert.Equal(Navy, Db.GetGameState().LastNavyShare);
    }

    [Fact]
    public void TheUnrestDrag_IsWeightedByPopulation_AndCountsOnlyDiscontentAboveRest()
    {
        Start();
        var rest = Config.Constants.Discontent.Rest;

        Sql($"UPDATE provinces SET discontent = {rest};");
        var calm = Db.GrowthRate(Config);
        Sql($"UPDATE provinces SET discontent = {rest - 2};");
        Assert.Equal(calm, Db.GrowthRate(Config), 12);

        Sql("UPDATE provinces SET discontent = 7;");
        Assert.Equal(0.02, calm - Db.GrowthRate(Config), 12);

        Sql($"UPDATE provinces SET discontent = {rest};");
        Sql("UPDATE provinces SET discontent = 7 WHERE id = 'aurenten';");
        var provinces = Db.GetProvinces();
        var share = provinces.Single(p => p.Id == "aurenten").Population / provinces.Sum(p => p.Population);
        Assert.Equal(0.02 * share, calm - Db.GrowthRate(Config), 12);
    }

    [Fact]
    public void TheBudget_IsComputedAtTheStartOfWinter_FromTheGrownEconomy()
    {
        Start();
        Sql("UPDATE provinces SET discontent = 0;");
        var economy = Db.GetGameState().Economy;
        var growth = Db.GrowthRate(Config);

        ToWinter();

        var year = Db.GetFiscalYear(1851)!;
        Assert.Equal(economy * (1 + growth), year.Economy, 9);
        Assert.Equal(Math.Round((decimal)(year.Economy * year.TaxRate), 2), year.Budget);
        Assert.Equal(year.Economy, Db.GetGameState().Economy, 9);
    }

    [Fact]
    public void UnpaidUpkeep_EmptiesThePurse_AndCostsEveryUnitOneStep_NeverBelowBase()
    {
        Start();
        var division = Config.Constants.Division;
        var divisions = Db.GetUnits(UnitType.Division);
        Sql($"UPDATE units SET sides = {division.BaseSides + 2 * division.SidesStep} WHERE type = 'Division' AND id <> {divisions[0].Id};");
        var gendarmerieBefore = Db.GetUnits(UnitType.Gendarmerie).Select(u => u.Sides).ToList();
        ToWinter();
        var amounts = Split(NextBudget(), 0, 0.15, 0.3, 0.1, 0.2);
        amounts[Ministry.War] = 1;
        Db.SubmitAllocation(Treasurer, amounts);

        var report = Resolve();

        Assert.Equal(0, Db.GetPurseBalance(Ministry.War));
        Assert.Contains(report.Alerts, a => a.Contains(Ministry.War.DisplayName()));
        var after = Db.GetUnits(UnitType.Division);
        Assert.Equal(division.BaseSides, after.Single(u => u.Id == divisions[0].Id).Sides);
        Assert.All(after.Where(u => u.Id != divisions[0].Id), u => Assert.Equal(division.BaseSides + division.SidesStep, u.Sides));
        Assert.Equal(gendarmerieBefore, Db.GetUnits(UnitType.Gendarmerie).Select(u => u.Sides).ToList());
        var upkeep = Assert.Single(Db.GetLedger(LedgerAccount.War), e => e.Kind == LedgerKind.Upkeep && e.SourceId == "1851");
        Assert.Equal(-1m, upkeep.Amount);
    }

    [Fact]
    public void TheLedger_HasAnEntryForEveryChange_SummingToEachBalance()
    {
        Start();
        ToWinter();
        Db.SubmitAllocation(Treasurer, Split(NextBudget(), 0.25, 0.2, 0.2, 0.1, 0.2));
        Assert.Null(Db.Grant(Treasurer, Ministry.War, 2).Refusal);
        Resolve();
        Assert.Null(Db.Grant(Treasurer, Ministry.Interior, 1.5m).Refusal);

        foreach (var ministry in Enum.GetValues<Ministry>())
            Assert.Equal(Db.GetPurseBalance(ministry), Db.GetLedger((LedgerAccount)(int)ministry).Sum(e => e.Amount));
        Assert.Equal(Db.GetGameState().Reserve, Db.GetLedger(LedgerAccount.Reserve).Sum(e => e.Amount));
        Assert.Equal(0, Db.GetPurseBalance(Ministry.Navy));
    }

    [Fact]
    public void AGrant_StaysPrivateUntilTheNextResolution()
    {
        Start();
        SetSeason(Season.Summer);
        var reserve = Db.GetGameState().Reserve;

        var result = Db.Grant(Treasurer, Ministry.CommerceLabor, 3);

        Assert.Null(result.Refusal);
        Assert.Equal(reserve - 3, result.Reserve);
        var now = Db.GetGameState().ResolutionNumber;
        var grants = Db.GetLedger().Where(e => e.Kind == LedgerKind.Grant).ToList();
        Assert.Equal(2, grants.Count);
        Assert.All(grants, e => Assert.True(e.PublicFromResolution > now));
        Assert.Equal(0, grants.Sum(e => e.Amount));

        Resolve();

        var after = Db.GetGameState().ResolutionNumber;
        Assert.All(Db.GetLedger().Where(e => e.Kind == LedgerKind.Grant), e => Assert.True(e.PublicFromResolution <= after));
    }

    [Fact]
    public void AGrant_IsRefusedBeyondTheReserve_ToTheNavy_AndToOtherMinisters()
    {
        Start();
        var reserve = Db.GetGameState().Reserve;

        Assert.NotNull(Db.Grant(Treasurer, Ministry.War, reserve + 1).Refusal);
        Assert.NotNull(Db.Grant(Treasurer, Ministry.Navy, 1).Refusal);
        Assert.NotNull(Db.Grant(WarMinister, Ministry.War, 1).Refusal);
        Assert.NotNull(Db.Grant(Chancellor, Ministry.War, 1).Refusal);
        Assert.Equal(reserve, Db.GetGameState().Reserve);
    }

    [Fact]
    public void WithTheTreasuryVacant_TheChancellorAllocates_AndTheGmIsAlerted()
    {
        Start(treasuryVacant: true);
        var report = ToWinter();

        Assert.Contains(report.Alerts, a => a.Contains("No Treasury minister"));
        Assert.True(report.PrivateMessages.ContainsKey(Chancellor));
        Assert.Null(Db.OpenAllocation(Chancellor).Refusal);
        Assert.Null(Db.Grant(Chancellor, Ministry.War, 1).Refusal);
    }

    [Fact]
    public void LastYearsShares_Repeat_WhenNoAllocationIsSubmitted()
    {
        Start();
        ToWinter();
        var budget1851 = NextBudget();
        var submitted = Split(budget1851, 0.4, 0.1, 0.2, 0.1, 0.1);
        Db.SubmitAllocation(Treasurer, submitted);
        var report1851 = Resolve();
        Assert.DoesNotContain(report1851.News, line => line.Contains("no new budget"));

        ToWinter();
        var budget1852 = NextBudget();
        var report = Resolve();

        Assert.Contains(report.News, line => line.Contains("no new budget"));
        var allocation = Db.GetAllocation(1852);
        foreach (var ministry in Enum.GetValues<Ministry>().Where(m => m != Ministry.Navy))
            Assert.Equal((double)(budget1852 * submitted[ministry] / budget1851), (double)allocation[ministry], 0.01);
        Assert.Equal(Math.Round(budget1852 * (decimal)Db.GetGameState().LastNavyShare, 2, MidpointRounding.AwayFromZero), allocation[Ministry.Navy]);
        Assert.False(Db.IsAllocationSubmitted(1852));
    }

    [Fact]
    public void TheFirstYear_UsesTheDefaultAllocation()
    {
        Start();
        var budget = Db.GetFiscalYear(1850)!.Budget;
        var allocation = Db.GetAllocation(1850);
        foreach (var (ministry, share) in Config.Constants.DefaultAllocation)
            Assert.Equal((double)(budget * (decimal)share), (double)allocation[ministry], 0.01);
        Assert.Equal(Math.Round(budget * (decimal)Navy, 2, MidpointRounding.AwayFromZero), allocation[Ministry.Navy]);
        Assert.True(allocation.Values.Sum() <= budget);
        Assert.Equal(Navy, Db.GetGameState().LastNavyShare);
        Assert.Equal(Config.Constants.Favour.Start, Db.GetGameState().Favour);
    }

    [Fact]
    public void ASubmittedAllocation_TakesEffectAtTheWinterResolution()
    {
        Start();
        ToWinter();
        var budget = NextBudget();
        var amounts = Split(budget, 0.3, 0.3, 0.2, 0.05, 0.1);
        amounts[Ministry.Treasury] -= 1;
        Db.SubmitAllocation(Treasurer, Split(budget, 0.1, 0.1, 0.1, 0.1, 0.1));
        var second = Db.SubmitAllocation(Treasurer, amounts);
        Assert.Null(second.Refusal);
        Assert.True(second.Replaced);
        Assert.Equal(budget - amounts.Values.Sum(), second.ToReserve);

        var reserveBefore = Db.GetGameState().Reserve;
        var leftovers = Enum.GetValues<Ministry>().Where(m => m != Ministry.Navy).Sum(m => Db.GetPurseBalance(m));

        Resolve();

        Assert.Equal(Season.Spring, Db.GetGameState().Season);
        Assert.Equal(amounts, Db.GetAllocation(1851));
        Assert.Equal(amounts[Ministry.CommerceLabor], Db.GetPurseBalance(Ministry.CommerceLabor));
        Assert.Equal(amounts[Ministry.Treasury], Db.GetPurseBalance(Ministry.Treasury));
        var units = Db.GetUnits();
        var warUpkeep = Config.Constants.Division.Upkeep * units.Count(u => u.Type == UnitType.Division);
        Assert.Equal(amounts[Ministry.War] - warUpkeep, Db.GetPurseBalance(Ministry.War));
        var withLeftovers = reserveBefore + leftovers;
        var interest = Math.Round(withLeftovers * (decimal)Config.Constants.Reserve.Interest, 2, MidpointRounding.AwayFromZero);
        Assert.Equal(withLeftovers + interest + second.ToReserve, Db.GetGameState().Reserve);
    }

    [Fact]
    public void AnAllocation_IsRefusedOutsideWinter_OverBudget_AndFromOtherMinisters()
    {
        Start();
        Assert.NotNull(Db.OpenAllocation(Treasurer).Refusal);
        SetSeason(Season.Autumn);
        Assert.NotNull(Db.OpenAllocation(Treasurer).Refusal);
        Assert.NotNull(Db.PreviewAllocation(Treasurer, Split(20, 0.1, 0.1, 0.1, 0.1, 0.1)).Refusal);

        Resolve();
        var budget = NextBudget();
        Assert.Null(Db.OpenAllocation(Treasurer).Refusal);
        Assert.NotNull(Db.OpenAllocation(WarMinister).Refusal);
        Assert.NotNull(Db.OpenAllocation(Chancellor).Refusal);

        var over = Split(budget, 0.3, 0.2, 0.25, 0.1, 0.15);
        over[Ministry.War] += 1;
        Assert.NotNull(Db.SubmitAllocation(Treasurer, over).Refusal);
        var negative = Split(budget, 0.3, 0.2, 0.25, 0.1, 0.15);
        negative[Ministry.Navy] = -1;
        Assert.NotNull(Db.SubmitAllocation(Treasurer, negative).Refusal);
        Assert.Empty(Db.GetAllocation(1851));

        Assert.Null(Db.SubmitAllocation(Treasurer, Split(budget, 0.3, 0.2, 0.25, 0.1, 0.15)).Refusal);
    }

    [Fact]
    public void TheAllocationForm_IsPrefilledWithLastYearsCrowns()
    {
        Start();
        ToWinter();
        var window = Db.OpenAllocation(Treasurer);
        Assert.Equal(Db.GetAllocation(1850), window.Prefill);
        Assert.Equal(1851, window.Year);
        Assert.Equal(NextBudget(), window.Budget);
    }
}
