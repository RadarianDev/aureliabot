using Xunit;

namespace AureliaBot.Tests;

public class GameStartTests : IDisposable
{
    private readonly DatabaseFixture _fixture = new();
    private DatabaseService Db => _fixture.Db;

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void Start_OpensTheFoundingPreElection()
    {
        var result = Db.StartGame(DatabaseFixture.Config, DateTimeOffset.UtcNow);

        Assert.Null(result.Refusal);
        var state = Db.GetGameState();
        Assert.True(state.Started);
        Assert.True(state.IsPreElection);
        Assert.Equal(Season.Spring, state.Season);
        Assert.Equal(1850, state.Year);
        Assert.Equal(0, state.ResolutionNumber);
        Assert.Equal(180, state.Seats);
        Assert.NotNull(state.NextResolutionAt);
    }

    [Fact]
    public void Start_LoadsProvincesConditionsAndForces()
    {
        Db.StartGame(DatabaseFixture.Config, DateTimeOffset.UtcNow);

        Assert.Equal(10, Db.GetProvinces().Count);
        Assert.Contains("coastal", Db.GetProvinceConditions()["north_arpal"]);
        Assert.Equal(6, Db.GetUnits(UnitType.Division).Count);
        Assert.Equal(3, Db.GetUnits(UnitType.Gendarmerie).Count);
        Assert.All(Db.GetProvinces(), p => Assert.Equal(DatabaseFixture.Constants.Discontent.Start, p.Discontent));
    }

    [Fact]
    public void Start_FundsThePursesAndKeepsTheNavyShare()
    {
        Db.StartGame(DatabaseFixture.Config, DateTimeOffset.UtcNow);
        var state = Db.GetGameState();

        var balances = Db.GetPurseBalances();
        Assert.True(balances[Ministry.War] > 0);
        Assert.Equal(0, balances[Ministry.Navy]);
        Assert.True(state.Reserve >= DatabaseFixture.Constants.Reserve.Start);
        Assert.Equal(DatabaseFixture.Constants.Favour.Start, state.Favour, 6);
        foreach (var account in Enum.GetValues<LedgerAccount>())
        {
            var balance = account == LedgerAccount.Reserve ? state.Reserve : balances[(Ministry)(int)account];
            Assert.Equal(balance, Db.GetLedger(account).Sum(e => e.Amount));
        }
    }

    [Fact]
    public void Start_IsRefusedTheSecondTime()
    {
        Db.StartGame(DatabaseFixture.Config, DateTimeOffset.UtcNow);
        var again = Db.StartGame(DatabaseFixture.Config, DateTimeOffset.UtcNow);
        Assert.NotNull(again.Refusal);
    }
}
