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

    public async Task<bool> DealAsync(ulong factionId)
    {
        if (!_data.GetGameState().IsPreElection)
            return _data.DealAgenda(factionId, _constants.Config, Random.Shared) is { } hand && await SendAsync(hand);
        return _data.DealFutureAgenda(factionId, _constants.Config, Random.Shared) is { } future
            && await SendAsync(factionId, AgendaWording.Future(future.Agenda, _constants.Config.Agendas.Reward, future.HasCurrent));
    }

    public async Task<(List<ulong> Dealt, List<ulong> Unsent)> DealMissingAsync()
    {
        var unsent = new List<ulong>();
        if (_data.GetGameState().IsPreElection)
        {
            var futures = _data.DealMissingFutureAgendas(_constants.Config, Random.Shared);
            foreach (var (factionId, future) in futures)
                if (!await SendAsync(factionId, AgendaWording.Future(future.Agenda, _constants.Config.Agendas.Reward, future.HasCurrent)))
                    unsent.Add(factionId);
            return ([.. futures.Keys], unsent);
        }
        var dealt = _data.DealMissingAgendas(_constants.Config, Random.Shared);
        foreach (var hand in dealt)
            if (!await SendAsync(hand)) unsent.Add(hand.FactionId);
        return ([.. dealt.Select(h => h.FactionId)], unsent);
    }

    public async Task<(AgendaHand? Hand, bool Sent)> RedealAsync(ulong factionId)
    {
        var hand = _data.RedealAgenda(factionId, _constants.Config, Random.Shared);
        return (hand, hand is not null && await SendAsync(hand));
    }

    public async Task<(AssignAgendaResult Result, bool Sent)> AssignAsync(ulong factionId, string agendaId, bool now)
    {
        var result = _data.AssignAgenda(factionId, agendaId, _constants.Config, now);
        var config = _constants.Config.Agendas;
        var sent = result switch
        {
            { Dealt: { } hand } => await SendAsync(hand),
            { Future: { } future } when config.Find(future) is { } agenda => await SendAsync(factionId, AgendaWording.Future(agenda, config.Reward, _data.GetActiveAgenda(factionId) is not null)),
            _ => false,
        };
        return (result, sent);
    }

    private async Task<bool> SendAsync(AgendaHand hand)
    {
        var config = _constants.Config.Agendas;
        return config.Find(hand.AgendaId) is { } agenda && await SendAsync(hand.FactionId, AgendaWording.Dealt(agenda, config.Reward));
    }

    private async Task<bool> SendAsync(ulong factionId, string message)
    {
        try
        {
            return await _factionChannels.SendAsync(factionId, message);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AgendaService] Sending the agenda to {factionId} failed: {ex.Message}");
            return false;
        }
    }
}
