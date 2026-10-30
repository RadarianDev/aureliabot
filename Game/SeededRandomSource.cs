public interface IRandomSource
{
    int RollDie(int faces);
    double NextDouble();
}

public sealed class SeededRandomSource : IRandomSource
{
    private readonly Random _random;

    public SeededRandomSource(int seed)
    {
        Seed = seed;
        _random = new Random(seed);
    }

    public int Seed { get; }

    public static int NewSeed() => Random.Shared.Next();

    public int RollDie(int faces) => faces < 1 ? 0 : _random.Next(1, faces + 1);

    public double NextDouble() => _random.NextDouble();
}
