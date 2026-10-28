using Microsoft.Extensions.Configuration;

// Strongly-typed bot configuration, loaded once at startup and registered as a DI singleton.
// Values come from (in increasing priority): appsettings.json, appsettings.Local.json,
// and environment variables. The token is a secret and should live in appsettings.Local.json
// (gitignored) or the DISCORD_BOT_TOKEN environment variable — never in committed source.
public class BotConfig
{
    public string Token { get; set; } = "";
    public ulong GuildId { get; set; }
    public string DatabasePath { get; set; } = "aurelia.db";

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
            // Env var "Discord__Token" also binds to "Discord:Token"; DISCORD_BOT_TOKEN is a
            // convenience fallback.
            Token = config["Discord:Token"]
                ?? Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN")
                ?? "",
            GuildId = ParseUlong(config["Discord:GuildId"]),
            DatabasePath = config["Database:Path"] is { Length: > 0 } p ? p : "aurelia.db",
        };
    }

    private static ulong ParseUlong(string? s) => ulong.TryParse(s, out var v) ? v : 0UL;
}
