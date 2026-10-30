using Microsoft.Data.Sqlite;
using Xunit;

namespace AureliaBot.Tests;

public class FactionTests : IDisposable
{
    private const ulong Alice = 101, Bob = 102, Carol = 103, Dave = 104;

    private readonly DatabaseFixture _fixture = new();
    private DatabaseService Db => _fixture.Db;
    private static double Start => DatabaseFixture.Constants.Popularity.Start;

    public void Dispose() => _fixture.Dispose();

    private void Register(params ulong[] users)
    {
        foreach (var user in users)
            Assert.True(Db.CreateFaction(user, $"Character {user}", "aurenten", Start));
    }

    private long Party(string name, params ulong[] founders)
    {
        var result = Db.CreateParty(name, "#AA0000", null, founders);
        Assert.Equal(PartyCreateOutcome.Created, result.Outcome);
        return result.PartyId!.Value;
    }

    private void Sql(string sql, params (string Name, object Value)[] parameters)
    {
        using var conn = new SqliteConnection(_fixture.ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        cmd.ExecuteNonQuery();
    }

    private T Scalar<T>(string sql, params (string Name, object Value)[] parameters)
    {
        using var conn = new SqliteConnection(_fixture.ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        return (T)Convert.ChangeType(cmd.ExecuteScalar()!, typeof(T));
    }

    private long Cabinet(string kind, string status, ulong? chancellor, params (Ministry Ministry, ulong Holder)[] posts)
    {
        using var conn = new SqliteConnection(_fixture.ConnectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO cabinets (kind, chancellor_faction_id, status, created_at, created_resolution)
            VALUES ($kind, $chancellor, $status, 0, 0) RETURNING id;";
        cmd.Parameters.AddWithValue("$kind", kind);
        cmd.Parameters.AddWithValue("$chancellor", (object?)chancellor?.ToString() ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$status", status);
        var id = (long)cmd.ExecuteScalar()!;
        foreach (var (ministry, holder) in posts)
            Sql("INSERT INTO cabinet_posts (cabinet_id, ministry, faction_id) VALUES ($id, $ministry, $holder);",
                ("$id", id), ("$ministry", ministry.ToString()), ("$holder", holder.ToString()));
        return id;
    }

    [Fact]
    public void CreateFaction_StartsIndependentAtStartingPopularity()
    {
        Assert.True(Db.CreateFaction(Alice, "Lady Aurelia", "aurenten", Start));

        var faction = Db.GetFaction(Alice)!;
        Assert.Equal("Lady Aurelia", faction.CharacterName);
        Assert.Equal("aurenten", faction.HomeProvinceId);
        Assert.Equal(Start, faction.Popularity);
        Assert.Equal(0, faction.Seats);
        Assert.True(faction.IsIndependent);
        Assert.True(faction.IsActive);
        Assert.Null(faction.PrivateChannelId);
    }

    [Fact]
    public void CreateFaction_RefusesADuplicate()
    {
        Assert.True(Db.CreateFaction(Alice, "Lady Aurelia", "aurenten", Start));
        Assert.False(Db.CreateFaction(Alice, "Someone Else", "iracterie", Start + 10));

        var faction = Db.GetFaction(Alice)!;
        Assert.Equal("Lady Aurelia", faction.CharacterName);
        Assert.Equal("aurenten", faction.HomeProvinceId);
    }

    [Fact]
    public void CreateFaction_RefusesARetiredPlayer()
    {
        Register(Alice);
        Assert.NotNull(Db.RetireFaction(Alice));
        Assert.False(Db.CreateFaction(Alice, "Comeback", "aurenten", Start));
        Assert.Equal(FactionStatus.Retired, Db.GetFaction(Alice)!.Status);
    }

    [Fact]
    public void UpdateFaction_ChangesOnlyWhatIsGiven()
    {
        Register(Alice);
        Assert.True(Db.UpdateFaction(Alice, null, "iracterie"));
        var faction = Db.GetFaction(Alice)!;
        Assert.Equal($"Character {Alice}", faction.CharacterName);
        Assert.Equal("iracterie", faction.HomeProvinceId);

        Assert.True(Db.UpdateFaction(Alice, "Renamed", null));
        faction = Db.GetFaction(Alice)!;
        Assert.Equal("Renamed", faction.CharacterName);
        Assert.Equal("iracterie", faction.HomeProvinceId);
    }

    [Fact]
    public void AddPopularity_ClampsToTheRange()
    {
        Register(Alice);
        var range = DatabaseFixture.Constants.Popularity;
        Assert.Equal(range.Max, Db.AddPopularity(Alice, range.Max * 10, range));
        Assert.Equal(range.Max, Db.GetFaction(Alice)!.Popularity);
        Assert.Null(Db.AddPopularity(Dave, 1, range));
    }

    [Fact]
    public void SetFactionPrivateChannel_RoundTrips()
    {
        Register(Alice);
        Db.SetFactionPrivateChannel(Alice, 987654321012345678UL);
        Assert.Equal(987654321012345678UL, Db.GetFaction(Alice)!.PrivateChannelId);
        Db.SetFactionPrivateChannel(Alice, null);
        Assert.Null(Db.GetFaction(Alice)!.PrivateChannelId);
    }

    [Fact]
    public void RetireFaction_RefusesUnknownOrAlreadyRetired()
    {
        Assert.Null(Db.RetireFaction(Alice));
        Register(Alice);
        Assert.NotNull(Db.RetireFaction(Alice));
        Assert.Null(Db.RetireFaction(Alice));
    }

    [Fact]
    public void RetireFaction_ClearsPartyAndSittingCabinetPosts()
    {
        Register(Alice, Bob, Carol);
        var party = Party("Liberal Union", Alice, Bob, Carol);
        var sitting = Cabinet("Sitting", "Approved", Alice, (Ministry.Treasury, Alice), (Ministry.War, Bob));

        var result = Db.RetireFaction(Alice)!;

        Assert.Null(result.DissolvedParty);
        Assert.Equal(party, result.Faction.PartyId);
        var alice = Db.GetFaction(Alice)!;
        Assert.Equal(FactionStatus.Retired, alice.Status);
        Assert.Null(alice.PartyId);
        Assert.NotNull(Db.GetParty(party));

        var cabinet = Db.GetSittingCabinet()!;
        Assert.Equal(sitting, cabinet.Id);
        Assert.Null(cabinet.ChancellorId);
        Assert.Null(cabinet.HolderOf(Ministry.Treasury));
        Assert.Equal(Bob, cabinet.HolderOf(Ministry.War));
    }

    [Fact]
    public void RetireFaction_CancelsANominatedCabinetItSitsIn()
    {
        Register(Alice, Bob);
        var nominated = Cabinet("Nominated", "Voting", Bob, (Ministry.Interior, Alice));

        Db.RetireFaction(Alice);

        Assert.Equal("Cancelled", Scalar<string>("SELECT status FROM cabinets WHERE id = $id;", ("$id", nominated)));
    }

    [Fact]
    public void RetireFaction_DropsPendingActionsAndJoinRequests()
    {
        Register(Alice, Bob, Carol);
        var party = Party("Liberal Union", Bob, Carol);
        Sql(@"INSERT INTO issues (id, template_id, title, for_text, against_text, opinion, salience, opened_resolution)
              VALUES ('labour', 'labour', 'The Condition of Labour', 'For', 'Against', 0, 1, 0);");
        Sql("INSERT INTO actions (faction_id, kind, issue_id, submitted_at) VALUES ($user, 'Campaign', 'labour', 0);", ("$user", Alice.ToString()));
        Sql("INSERT INTO actions (faction_id, kind, issue_id, submitted_at, resolved) VALUES ($user, 'Campaign', 'labour', 0, 1);", ("$user", Alice.ToString()));
        Assert.Equal(JoinRequestOutcome.Created, Db.CreateJoinRequest(Alice, party).Outcome);

        Db.RetireFaction(Alice);

        Assert.Equal(0L, Scalar<long>("SELECT COUNT(*) FROM actions WHERE faction_id = $user AND resolved = 0;", ("$user", Alice.ToString())));
        Assert.Equal(1L, Scalar<long>("SELECT COUNT(*) FROM actions WHERE faction_id = $user AND resolved = 1;", ("$user", Alice.ToString())));
        Assert.Empty(Db.GetPendingJoinRequests());
        Assert.Equal("Dropped", Scalar<string>("SELECT status FROM party_join_requests WHERE faction_id = $user;", ("$user", Alice.ToString())));
    }

    [Fact]
    public void RetireFaction_DissolvesAPartyLeftWithOneMember_WhoKeepsTheirSeats()
    {
        Register(Alice, Bob);
        var party = Party("Liberal Union", Alice, Bob);
        Assert.True(Db.SetFactionSeats(Alice, 7));
        Assert.True(Db.SetFactionSeats(Bob, 5));

        var result = Db.RetireFaction(Alice)!;

        Assert.NotNull(result.DissolvedParty);
        Assert.Equal(party, result.DissolvedParty!.Id);
        Assert.Null(Db.GetParty(party));
        var bob = Db.GetFaction(Bob)!;
        Assert.True(bob.IsIndependent);
        Assert.True(bob.IsActive);
        Assert.Equal(5, bob.Seats);
        var alice = Db.GetFaction(Alice)!;
        Assert.Equal(7, alice.Seats);
        Assert.Equal(FactionStatus.Retired, alice.Status);
    }

    [Fact]
    public void RetireFaction_ALeaverOutsideAPartyDissolvesNothing()
    {
        Register(Alice, Bob, Carol);
        var party = Party("Liberal Union", Bob, Carol);

        var result = Db.RetireFaction(Alice)!;

        Assert.Null(result.DissolvedParty);
        Assert.NotNull(Db.GetParty(party));
        Assert.Equal(2, Db.GetPartyMembers(party).Count);
    }

    [Fact]
    public void RetiredFactions_AreNotActive()
    {
        Register(Alice, Bob);
        Db.RetireFaction(Alice);
        Assert.Equal([Bob], Db.GetActiveFactions().Select(f => f.UserId));
        Assert.Equal(2, Db.GetAllFactions().Count);
    }

    [Theory]
    [InlineData("Lady Aurelia", "lady-aurelia")]
    [InlineData("  Sir Édouard d'Arpal  ", "sir-édouard-d-arpal")]
    [InlineData("!!!", "")]
    [InlineData("Count  Vex -- III", "count-vex-iii")]
    public void Slug_MakesDiscordChannelNames(string name, string expected)
    {
        Assert.Equal(expected, FactionChannelService.Slug(name));
    }
}
