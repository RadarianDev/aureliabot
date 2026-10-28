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
            GatewayIntents = GatewayIntents.AllUnprivileged
        });

        _interactions = new InteractionService(_client.Rest);

        _services = new ServiceCollection()
            .AddSingleton(_config)
            .AddSingleton(_client)
            .AddSingleton(_interactions)
            .AddSingleton<DatabaseService>()
            .BuildServiceProvider();

        _data = _services.GetRequiredService<DatabaseService>();
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
        // Discord.Net awaits gateway event handlers one at a time on its single event-processing
        // loop — every DatabaseService call in this codebase is synchronous ADO.NET, so awaiting
        // HandleInteractionAsync directly here would let one command's DB work (a long
        // transaction, a slow query under load) block that loop for its full duration, queuing up
        // every other in-flight interaction behind it until Discord's 3s ack window lapses and they
        // fail with "Unknown interaction" (10062) — including commands with nothing to do with
        // whatever was actually slow. Task.Run detaches each interaction onto the thread pool
        // immediately so the loop is never blocked by command logic.
        _client.InteractionCreated += interaction =>
        {
            _ = Task.Run(async () =>
            {
                try { await HandleInteractionAsync(interaction); }
                catch (Exception ex)
                {
                    // HandleInteractionAsync's own try/catch only covers its fallback RespondAsync
                    // call — this outer one exists so a bug anywhere else in the dispatch path (e.g.
                    // ExecuteCommandAsync itself throwing instead of returning a failed IResult, or
                    // SocketInteractionContext construction) still reaches the console instead of
                    // becoming a silently-dropped unobserved Task exception, which is the default
                    // .NET behavior for a fire-and-forget Task.Run like this one that nobody ever
                    // awaits or observes. Without this, such a bug looks like the bot just hanging —
                    // no error anywhere, the interaction stuck on "Sending command..." forever.
                    Console.WriteLine($"[Unhandled] Interaction dispatch threw: {ex}");
                }
            });
            return Task.CompletedTask;
        };

        // Modules MUST be loaded before the client logs in. StartAsync() only kicks off the gateway
        // handshake — it doesn't wait for Ready — so if AddModulesAsync ran after it, there was a
        // race: a fast gateway connection could fire Ready (and, from ReadyAsync, call
        // RegisterCommandsToGuildAsync with deleteMissing defaulting to true) before _interactions
        // had any modules/commands registered in it, wiping every slash command already on the guild
        // and leaving the bot looking fully unresponsive until the next successful restart won that
        // race instead. Loading modules first removes the race entirely.
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
        // Guild-scoped registration when a GuildId is configured (near-instant propagation, good
        // for a single-server bot); otherwise fall back to slower global registration.
        if (_config.GuildId != 0)
            await _interactions.RegisterCommandsToGuildAsync(_config.GuildId);
        else
            await _interactions.RegisterCommandsGloballyAsync();

        Console.WriteLine($"Bot is ready! Logged in as {_client.CurrentUser.Username}");
    }

    // /whitelist itself is exempt from the check below — otherwise a server manager could lock
    // themselves out of ever un-restricting a channel again by whitelisting the wrong one (or none).
    private const string ChannelWhitelistCommandName = "whitelist";

    private async Task HandleInteractionAsync(SocketInteraction interaction)
    {
        // Only slash commands are gated — autocomplete must never be blocked (it must never even go
        // unanswered, see below) and component/modal interactions (buttons, select menus, modals)
        // stem from a message that was already posted from an allowed channel in the first place.
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

        // Surface failures (e.g. a denied precondition, or an unhandled exception in the command
        // itself) instead of leaving the interaction unacknowledged. This used to be scoped to
        // ApplicationCommand only, which meant a throwing button/select-menu handler got no
        // response at all — Discord shows that as "This
        // interaction failed" after its 3s ack window lapses, which reads to the user as a plain
        // timeout with no error message anywhere. Autocomplete must still never be replied to.
        if (!result.IsSuccess && interaction.Type != InteractionType.ApplicationCommandAutocomplete)
        {
            // Logged unconditionally, BEFORE attempting the fallback response below — if the
            // interaction's token already expired (e.g. it sat too long waiting for a thread pool
            // slot and blew Discord's 3s ack window before the command body ever ran), RespondAsync
            // here throws too and gets swallowed by its own catch, which previously left this failure
            // with literally no trace anywhere: not the original error, not the fact that reporting
            // it also failed. This line is now the one guaranteed place a command failure surfaces.
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
