public class ResolutionService
{
    private readonly DatabaseService _data;
    private readonly ConstantsStore _constants;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ResolutionService(DatabaseService data, ConstantsStore constants)
    {
        _data = data;
        _constants = constants;
    }

    public async Task<ResolutionReport> ResolveNowAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var seed = _data.GetGameState().RngSeed ?? SeededRandomSource.NewSeed();
            return _data.Resolve(_constants.Current, new SeededRandomSource(seed), seed, DateTimeOffset.UtcNow);
        }
        finally
        {
            _gate.Release();
        }
    }
}
