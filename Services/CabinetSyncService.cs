using Discord.WebSocket;

public class CabinetSyncService
{
    private readonly DiscordSocketClient _client;
    private readonly DatabaseService _data;
    private readonly AlertService _alerts;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public CabinetSyncService(DiscordSocketClient client, DatabaseService data, AlertService alerts)
    {
        _client = client;
        _data = data;
        _alerts = alerts;
    }

    public async Task<IReadOnlyList<string>> SyncAsync()
    {
        await _lock.WaitAsync();
        try
        {
            return await SyncCoreAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<IReadOnlyList<string>> SyncCoreAsync()
    {
        var problems = new List<string>();
        if (_data.GetChannel(ChannelRole.CabinetRole) is not { } roleId)
        {
            problems.Add(Texts.Get("sync.cabinet.noRole"));
            return problems;
        }

        var guild = _client.Guilds.FirstOrDefault(g => g.GetRole(roleId) is not null);
        if (guild?.GetRole(roleId) is not { } role)
        {
            problems.Add(Texts.Get("sync.cabinet.roleGone", ("role", roleId)));
            await _alerts.PostAsync(problems[^1]);
            return problems;
        }

        if (!guild.HasAllMembers)
        {
            try { await guild.DownloadUsersAsync(); }
            catch (Exception ex) { Console.WriteLine($"[CabinetSync] Could not download members: {ex.Message}"); }
        }

        var holders = _data.GetSittingCabinet()?.Holders.ToHashSet() ?? [];

        foreach (var userId in holders)
        {
            if (guild.GetUser(userId) is not { } user || user.Roles.Any(r => r.Id == role.Id)) continue;
            await TryAsync(problems, Texts.Get("sync.cabinet.give", ("user", user.Username)), () => user.AddRoleAsync(role));
        }

        foreach (var user in guild.Users.Where(u => !holders.Contains(u.Id) && u.Roles.Any(r => r.Id == role.Id)).ToList())
            await TryAsync(problems, Texts.Get("sync.cabinet.take", ("user", user.Username)), () => user.RemoveRoleAsync(role));

        return problems;
    }

    private async Task TryAsync(List<string> problems, string what, Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            var problem = Texts.Get("sync.discordRefused", ("what", what), ("error", ex.Message));
            problems.Add(problem);
            await _alerts.PostAsync(problem);
        }
    }
}
