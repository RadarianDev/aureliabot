using Discord;
using Discord.WebSocket;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;

public class Bot
{
    private readonly DiscordSocketClient _client;
    private readonly InteractionService _interactions;
    private readonly IServiceProvider _services;
    private readonly BotConfig _config;
    private readonly DatabaseService _data;

    public Bot()
    {
        _config = BotConfig.Load();

        _client = new DiscordSocketClient(new DiscordSocketConfig
        {
            LogLevel = LogSeverity.Info,
            GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.GuildMembers | GatewayIntents.MessageContent,
            AlwaysDownloadUsers = true
        });

        _interactions = new InteractionService(_client.Rest);

        _services = new ServiceCollection()
            .AddSingleton(_config)
            .AddSingleton(_client)
            .AddSingleton(_interactions)
            .AddSingleton<DatabaseService>()
            .AddSingleton<ConstantsStore>()
            .AddSingleton<AlertService>()
            .AddSingleton<SnapshotService>()
            .AddSingleton<Publisher>()
            .AddSingleton<TurnTrackerService>()
            .AddSingleton<PartySyncService>()
            .AddSingleton<CabinetSyncService>()
            .AddSingleton<FactionChannelService>()
            .AddSingleton<ResolutionService>()
            .BuildServiceProvider();

        _data = _services.GetRequiredService<DatabaseService>();
        _services.GetRequiredService<ConstantsStore>();
    }

    public async Task RunAsync()
    {
        if (string.IsNullOrWhiteSpace(_config.Token))
        {
            Console.Error.WriteLine(
                "No Discord token configured. Set \"Discord:Token\" in appsettings.Local.json " +
                "or the DISCORD_BOT_TOKEN environment variable.");
            return;
        }

        _client.Log += LogAsync;
        _interactions.Log += LogAsync;
        _client.Ready += ReadyAsync;
        _client.InteractionCreated += interaction =>
        {
            _ = Task.Run(async () =>
            {
                try { await HandleInteractionAsync(interaction); }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Unhandled] Interaction dispatch threw: {ex}");
                }
            });
            return Task.CompletedTask;
        };

        await _interactions.AddModulesAsync(
            assembly: System.Reflection.Assembly.GetEntryAssembly(),
            services: _services
        );

        await _client.LoginAsync(TokenType.Bot, _config.Token);
        await _client.StartAsync();

        await Task.Delay(Timeout.Infinite);
    }

    private async Task ReadyAsync()
    {
        if (_config.GuildId != 0)
            await _interactions.RegisterCommandsToGuildAsync(_config.GuildId);
        else
            await _interactions.RegisterCommandsGloballyAsync();

        Console.WriteLine($"Bot is ready! Logged in as {_client.CurrentUser.Username}");
    }

    private const string ChannelWhitelistCommandName = "whitelist";

    private async Task HandleInteractionAsync(SocketInteraction interaction)
    {
        if (interaction.Type == InteractionType.ApplicationCommand
            && interaction is SocketSlashCommand slashCommand
            && interaction.Channel is SocketGuildChannel guildChannel
            && !string.Equals(slashCommand.Data.Name, ChannelWhitelistCommandName, StringComparison.OrdinalIgnoreCase)
            && !_data.IsChannelAllowed(guildChannel.Guild.Id, guildChannel.Id))
        {
            await interaction.RespondAsync(
                "This channel isn't whitelisted for bot commands. Ask a server manager to run `/whitelist add` here.",
                ephemeral: true);
            return;
        }

        var ctx = new SocketInteractionContext(_client, interaction);
        var result = await _interactions.ExecuteCommandAsync(ctx, _services);

        if (!result.IsSuccess && interaction.Type != InteractionType.ApplicationCommandAutocomplete)
        {
            Console.WriteLine($"[CommandFailure] /{interaction}: {result.Error} — {result.ErrorReason}");
            try { await interaction.RespondAsync($"⚠️ {result.ErrorReason}", ephemeral: true); }
            catch (Exception ex) { Console.WriteLine($"[CommandFailure] fallback response also failed: {ex.Message}"); }
        }
    }

    private Task LogAsync(LogMessage log)
    {
        Console.WriteLine(log.ToString());
        return Task.CompletedTask;
    }
}
