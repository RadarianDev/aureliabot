public class ReminderService
{
    private readonly DatabaseService _data;
    private readonly FactionChannelService _factionChannels;

    public ReminderService(DatabaseService data, FactionChannelService factionChannels)
    {
        _data = data;
        _factionChannels = factionChannels;
    }

    public Task RunAsync(CancellationToken cancellation) => Task.CompletedTask;
}
