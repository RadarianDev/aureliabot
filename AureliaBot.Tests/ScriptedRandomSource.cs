namespace AureliaBot.Tests;

internal sealed class ScriptedRandomSource : IRandomSource
{
    private readonly Queue<int> _rolls;
    private readonly Queue<double> _doubles;

    public ScriptedRandomSource(params int[] rolls) : this(rolls, []) { }

    public ScriptedRandomSource(IEnumerable<int> rolls, IEnumerable<double> doubles)
    {
        _rolls = new Queue<int>(rolls);
        _doubles = new Queue<double>(doubles);
    }

    public int RollDie(int faces) =>
        _rolls.Count > 0 ? _rolls.Dequeue() : throw new InvalidOperationException("ScriptedRandomSource ran out of scripted rolls.");

    public double NextDouble() =>
        _doubles.Count > 0 ? _doubles.Dequeue() : throw new InvalidOperationException("ScriptedRandomSource ran out of scripted doubles.");
}
