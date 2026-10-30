using Microsoft.Data.Sqlite;

public partial class DatabaseService
{
    public IReadOnlyList<(VariableDefinition Definition, double Value)> GetVariables(GameConfig config)
    {
        using var conn = Open();
        var stored = GetStoredVariablesTx(conn, null);
        return config.Variables.Select(v => (v, stored.TryGetValue(v.Id, out var value) ? value : v.Start)).ToList();
    }

    private static Dictionary<string, double> GetStoredVariablesTx(SqliteConnection conn, SqliteTransaction? tx)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT name, value FROM variables;";
        var values = new Dictionary<string, double>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) values[r.GetString(0)] = r.GetDouble(1);
        return values;
    }

    private static double GetVariableTx(SqliteConnection conn, SqliteTransaction? tx, GameConfig config, string name) =>
        GetStoredVariablesTx(conn, tx).TryGetValue(name, out var value) ? value : config.Variable(name)?.Start ?? 0;

    private static void SetVariableTx(SqliteConnection conn, SqliteTransaction tx, string name, double value) =>
        Exec(conn, tx, "INSERT INTO variables (name, value) VALUES ($name, $value) ON CONFLICT(name) DO UPDATE SET value = excluded.value;",
            ("$name", name), ("$value", value));
}
