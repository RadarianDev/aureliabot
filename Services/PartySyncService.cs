using Discord.WebSocket;

public class PartySyncService
{
    private readonly DiscordSocketClient _client;
    private readonly DatabaseService _data;
    private readonly AlertService _alerts;

    public PartySyncService(DiscordSocketClient client, DatabaseService data, AlertService alerts)
    {
        _client = client;
        _data = data;
        _alerts = alerts;
    }

    public Task<IReadOnlyList<string>> SyncAsync(long partyId) => throw new NotImplementedException();

    public Task<IReadOnlyList<string>> SyncAllAsync() => throw new NotImplementedException();

    public Task SyncMemberAsync(ulong userId) => throw new NotImplementedException();

    public Task ArchiveAsync(Party deleted) => throw new NotImplementedException();

    public Task HandleDissolutionAsync(Party dissolved) => throw new NotImplementedException();
}
