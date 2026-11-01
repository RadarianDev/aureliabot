using Microsoft.Data.Sqlite;

namespace AureliaBot.Tests;

public sealed class DatabaseFixture : IDisposable
{
    public DatabaseService Db { get; }
    public string ConnectionString { get; }
    private readonly string _dbPath;

    private static readonly Lazy<string> TemplatePath = new(BuildTemplate);

    public DatabaseFixture()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"aureliabot-test-{Guid.NewGuid():N}.db");
        File.Copy(TemplatePath.Value, _dbPath);
        ConnectionString = $"Data Source={_dbPath};Foreign Keys=True";
        Db = new DatabaseService(ConnectionString);
    }

    private static string BuildTemplate()
    {
        var path = Path.Combine(Path.GetTempPath(), $"aureliabot-test-template-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={path};Foreign Keys=True";
        _ = new DatabaseService(connectionString);
        using (var pooled = new SqliteConnection(connectionString)) SqliteConnection.ClearPool(pooled);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { File.Delete(path); } catch (IOException) { } };
        return path;
    }

    public static GameConfig Config { get; } = ConstantsStore.Load(Path.Combine(AppContext.BaseDirectory, "config"));

    public static GameConstants Constants => Config.Constants;

    public void Dispose()
    {
        using (var pooled = new SqliteConnection(ConnectionString)) SqliteConnection.ClearPool(pooled);
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
