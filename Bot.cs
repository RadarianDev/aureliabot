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
            .AddSingleton<Confirmations>()
            .AddSingleton<SetupChecklistService>()
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
        _client.UserJoined += UserJoinedAsync;
        _client.UserLeft += UserLeftAsync;
        _client.ChannelCreated += _ => RefreshChecklist();
        _client.ChannelDestroyed += _ => RefreshChecklist();
        _client.ChannelUpdated += (_, _) => RefreshChecklist();
        _client.RoleCreated += _ => RefreshChecklist();
        _client.RoleDeleted += _ => RefreshChecklist();
        _client.RoleUpdated += (_, _) => RefreshChecklist();
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

    private int _backgroundStarted;

    private async Task ReadyAsync()
    {
        if (_config.GuildId != 0)
            await _interactions.RegisterCommandsToGuildAsync(_config.GuildId);
        else
            await _interactions.RegisterCommandsGloballyAsync();

        Console.WriteLine($"Bot is ready! Logged in as {_client.CurrentUser.Username}");

        if (Interlocked.Exchange(ref _backgroundStarted, 1) == 0)
            StartBackgroundWork();
    }

    private void StartBackgroundWork()
    {
        var turnTracker = _services.GetRequiredService<TurnTrackerService>();
        var resolution = _services.GetRequiredService<ResolutionService>();

        _ = Task.Run(async () =>
        {
            try { await turnTracker.RunAsync(CancellationToken.None); }
            catch (Exception ex) { Console.WriteLine($"[TurnTracker] Loop stopped: {ex}"); }
        });
        turnTracker.RequestCurrent();
        _services.GetRequiredService<SetupChecklistService>().RequestRefresh();

        _ = Task.Run(async () =>
        {
            try { await resolution.RunAsync(CancellationToken.None); }
            catch (Exception ex) { Console.WriteLine($"[Resolution] Poller stopped: {ex}"); }
        });

        _ = Task.Run(async () =>
        {
            try
            {
                await _services.GetRequiredService<PartySyncService>().SyncAllAsync();
                await _services.GetRequiredService<CabinetSyncService>().SyncAsync();
                await _services.GetRequiredService<Publisher>().UpdateDashboardAsync();
            }
            catch (Exception ex) { Console.WriteLine($"[Startup] Sync failed: {ex}"); }
        });
    }

    private Task RefreshChecklist()
    {
        _services.GetRequiredService<SetupChecklistService>().RequestRefresh();
        return Task.CompletedTask;
    }

    private Task UserJoinedAsync(SocketGuildUser user)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (_data.GetFaction(user.Id) is not { IsActive: true }) return;
                await _services.GetRequiredService<PartySyncService>().SyncMemberAsync(user.Id);
                await _services.GetRequiredService<CabinetSyncService>().SyncAsync();
            }
            catch (Exception ex) { Console.WriteLine($"[UserJoined] Sync for {user.Id} failed: {ex}"); }
        });
        return Task.CompletedTask;
    }

    private Task UserLeftAsync(SocketGuild guild, SocketUser user)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (_data.GetFaction(user.Id) is not { IsActive: true } faction) return;
                await _services.GetRequiredService<AlertService>().PostAsync(
                    $"{Display.Faction(faction)} ({user.Username}) left the server while their faction is active. " +
                    "Consider `/gm faction retire`.");
            }
            catch (Exception ex) { Console.WriteLine($"[UserLeft] Alert for {user.Id} failed: {ex}"); }
        });
        return Task.CompletedTask;
    }

    private static readonly HashSet<string> WhitelistExemptCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "whitelist", "gm", "content",
    };

    private async Task HandleInteractionAsync(SocketInteraction interaction)
    {
        if (interaction.Type == InteractionType.ApplicationCommand
            && interaction is SocketSlashCommand slashCommand
            && interaction.Channel is SocketGuildChannel guildChannel
            && !WhitelistExemptCommands.Contains(slashCommand.Data.Name)
            && !_data.IsChannelAllowed(guildChannel.Guild.Id, guildChannel.Id))
        {
            await interaction.RespondAsync(
                "This channel isn't whitelisted for bot commands. Ask a server manager to run `/whitelist add` here.",
                ephemeral: true);
            return;
        }

        if (interaction is SocketAutocompleteInteraction autocomplete
            && WhitelistExemptCommands.Contains(autocomplete.Data.CommandName)
            && autocomplete.Data.CommandName != "whitelist"
            && interaction.User is SocketGuildUser { GuildPermissions.ManageGuild: false })
        {
            await autocomplete.RespondAsync(Array.Empty<AutocompleteResult>());
            return;
        }

        var ctx = new SocketInteractionContext(_client, interaction);
        var result = await _interactions.ExecuteCommandAsync(ctx, _services);

        if (result.IsSuccess && interaction is SocketSlashCommand executed)
            LogCommand(executed);

        if (!result.IsSuccess && interaction.Type != InteractionType.ApplicationCommandAutocomplete)
        {
            Console.WriteLine($"[CommandFailure] /{interaction}: {result.Error} — {result.ErrorReason}");
            try { await interaction.RespondAsync($"⚠️ {result.ErrorReason}", ephemeral: true); }
            catch (Exception ex) { Console.WriteLine($"[CommandFailure] fallback response also failed: {ex.Message}"); }
        }
    }

    private void LogCommand(SocketSlashCommand command)
    {
        try
        {
            var path = new List<string> { command.Data.Name };
            IEnumerable<SocketSlashCommandDataOption> options = command.Data.Options;
            while (options.FirstOrDefault() is { Type: ApplicationCommandOptionType.SubCommand or ApplicationCommandOptionType.SubCommandGroup } sub)
            {
                path.Add(sub.Name);
                options = sub.Options;
            }
            var arguments = options.Select(o => $"{o.Name}:{FormatOption(o.Value)}");
            var payload = "/" + string.Join(" ", path.Concat(arguments));
            _data.Log(LogKinds.Command, string.Join(" ", path), payload, command.User.Id);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CommandLog] Could not log /{command.Data.Name}: {ex.Message}");
        }
    }

    private static string FormatOption(object? value) => value switch
    {
        IUser user => user.Id.ToString(),
        IChannel channel => channel.Id.ToString(),
        IRole role => role.Id.ToString(),
        IAttachment attachment => attachment.Filename,
        IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value?.ToString() ?? "",
    };

    private Task LogAsync(LogMessage log)
    {
        Console.WriteLine(log.ToString());
        return Task.CompletedTask;
    }
}
