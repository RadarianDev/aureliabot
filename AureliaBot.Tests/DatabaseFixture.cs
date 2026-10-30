using Microsoft.Data.Sqlite;

namespace AureliaBot.Tests;

public sealed class DatabaseFixture : IDisposable
{
    public DatabaseService Db { get; }
    public string ConnectionString { get; }
    private readonly string _dbPath;

    public DatabaseFixture()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"aureliabot-test-{Guid.NewGuid():N}.db");
        ConnectionString = $"Data Source={_dbPath};Foreign Keys=True";
        Db = new DatabaseService(ConnectionString);
    }

    public static GameConfig Config { get; } = ConstantsStore.Load(Path.Combine(AppContext.BaseDirectory, "config"));

    public static GameConstants Constants => Config.Constants;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (File.Exists(_dbPath)) File.Delete(_dbPath);
                return;
            }
            catch (IOException)
            {
                if (attempt == 9) return;
                Thread.Sleep(30);
            }
        }
    }
}
