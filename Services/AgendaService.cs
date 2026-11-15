public sealed class AgendaService
{
    private readonly DatabaseService _data;
    private readonly ConstantsStore _constants;
    private readonly FactionChannelService _factionChannels;

    public AgendaService(DatabaseService data, ConstantsStore constants, FactionChannelService factionChannels)
    {
        _data = data;
        _constants = constants;
        _factionChannels = factionChannels;
    }

    public async Task<bool> DealAsync(ulong factionId) =>
        _data.DealAgenda(factionId, _constants.Config, Random.Shared) is { } hand && await SendAsync(hand);

    public async Task<(List<AgendaHand> Dealt, List<ulong> Unsent)> DealMissingAsync()
    {
        var dealt = _data.DealMissingAgendas(_constants.Config, Random.Shared);
        var unsent = new List<ulong>();
        foreach (var hand in dealt)
            if (!await SendAsync(hand)) unsent.Add(hand.FactionId);
        return (dealt, unsent);
    }

    public async Task<(AgendaHand? Hand, bool Sent)> RedealAsync(ulong factionId)
    {
        var hand = _data.RedealAgenda(factionId, _constants.Config, Random.Shared);
        return (hand, hand is not null && await SendAsync(hand));
    }

    public async Task<(AssignAgendaResult Result, bool Sent)> AssignAsync(ulong factionId, string agendaId, bool now)
    {
        var result = _data.AssignAgenda(factionId, agendaId, _constants.Config, now);
        return (result, result.Dealt is { } hand && await SendAsync(hand));
    }

    private async Task<bool> SendAsync(AgendaHand hand)
    {
        var config = _constants.Config.Agendas;
        if (config.Find(hand.AgendaId) is not { } agenda) return false;
        try
        {
            return await _factionChannels.SendAsync(hand.FactionId, AgendaWording.Dealt(agenda, config.Reward));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AgendaService] Sending the agenda to {hand.FactionId} failed: {ex.Message}");
            return false;
        }
    }
}
