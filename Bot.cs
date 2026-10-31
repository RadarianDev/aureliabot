using System.Runtime.InteropServices;
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
            .AddSingleton<GameFilesService>()
            .AddSingleton<ElectionPublisher>()
            .AddSingleton<AssemblyPublisher>()
            .AddSingleton<GovernmentPublisher>()
            .AddSingleton<CrisisPublisher>()
            .AddSingleton<ForcesPublisher>()
            .AddSingleton<ProjectPublisher>()
            .AddSingleton<ReminderService>()
            .AddSingleton<ServerCleanupService>()
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

        using var terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, Stop);
        using var interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, Stop);
        try { await Task.Delay(Timeout.Infinite, _stopping.Token); }
        catch (OperationCanceledException) { }
        await ShutdownAsync();
    }

    private int _backgroundStarted;
    private readonly CancellationTokenSource _stopping = new();

    private void Stop(PosixSignalContext context)
    {
        context.Cancel = true;
        _stopping.Cancel();
    }

    private async Task ShutdownAsync()
    {
        Console.WriteLine("[Shutdown] Stopping: waiting for any resolution in progress.");
        var settled = _services.GetRequiredService<ResolutionService>().ExclusiveAsync(() => Task.FromResult(true));
        if (await Task.WhenAny(settled, Task.Delay(TimeSpan.FromMinutes(2))) != settled)
            Console.WriteLine("[Shutdown] A resolution was still running after two minutes; stopping anyway.");
        try
        {
            await _client.StopAsync();
            await _client.LogoutAsync();
        }
        catch (Exception ex) { Console.WriteLine($"[Shutdown] Disconnect failed: {ex.Message}"); }
        Console.WriteLine("[Shutdown] Stopped.");
    }

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
            try { await turnTracker.RunAsync(_stopping.Token); }
            catch (Exception ex) when (!_stopping.IsCancellationRequested) { Console.WriteLine($"[TurnTracker] Loop stopped: {ex}"); }
        });
        turnTracker.RequestCurrent();
        _services.GetRequiredService<SetupChecklistService>().RequestRefresh();

        var gameFiles = _services.GetRequiredService<GameFilesService>();
        _ = Task.Run(async () =>
        {
            try
            {
                await gameFiles.ReportContentAsync();
                gameFiles.StartWatching();
            }
            catch (Exception ex) { Console.WriteLine($"[GameFiles] Startup load failed: {ex}"); }
        });

        _ = Task.Run(async () =>
        {
            try { await resolution.RunAsync(_stopping.Token); }
            catch (Exception ex) when (!_stopping.IsCancellationRequested) { Console.WriteLine($"[Resolution] Poller stopped: {ex}"); }
        });

        _ = Task.Run(async () =>
        {
            try { await _services.GetRequiredService<ReminderService>().RunAsync(_stopping.Token); }
            catch (Exception ex) when (!_stopping.IsCancellationRequested) { Console.WriteLine($"[Reminders] Loop stopped: {ex}"); }
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
                    Texts.Get("bot.userLeft", ("faction", Display.Faction(faction)), ("user", user.Username)));
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
                Texts.Get("bot.notWhitelisted"),
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
            try { await interaction.RespondAsync(Texts.Get("bot.failed", ("reason", result.ErrorReason)), ephemeral: true); }
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
            var name = string.Join(" ", path);
            var arguments = options.ToDictionary(o => o.Name, o => FormatOption(o.Value));
            _data.Log(LogKinds.Command, name, new CommandPayload(name, arguments).ToJson(), command.User.Id);
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
