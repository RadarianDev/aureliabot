using Microsoft.Extensions.Configuration;

public class BotConfig
{
    public string Token { get; set; } = "";
    public ulong GuildId { get; set; }
    public string DatabasePath { get; set; } = "aurelia.db";
    public string GameRoot { get; set; } = AppContext.BaseDirectory;

    public string ConfigFolder => Path.Combine(GameRoot, "config");

    public string ContentFolder => Path.Combine(GameRoot, "content");

    public static BotConfig Load()
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

        return new BotConfig
        {
            Token = config["Discord:Token"]
                ?? Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN")
                ?? "",
            GuildId = ParseUlong(config["Discord:GuildId"]),
            DatabasePath = config["Database:Path"] is { Length: > 0 } p ? p : "aurelia.db",
            GameRoot = config["Game:Root"] is { Length: > 0 } root ? Path.GetFullPath(root) : FindGameRoot(),
        };
    }

    private static string FindGameRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "AureliaBot.csproj")) && Directory.Exists(Path.Combine(dir.FullName, "config")))
                return dir.FullName;
        return AppContext.BaseDirectory;
    }

    private static ulong ParseUlong(string? s) => ulong.TryParse(s, out var v) ? v : 0UL;
}
