using Microsoft.Extensions.Configuration;

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
            Token = config["Discord:Token"]
                ?? Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN")
                ?? "",
            GuildId = ParseUlong(config["Discord:GuildId"]),
            DatabasePath = config["Database:Path"] is { Length: > 0 } p ? p : "aurelia.db",
        };
    }

    private static ulong ParseUlong(string? s) => ulong.TryParse(s, out var v) ? v : 0UL;
}
