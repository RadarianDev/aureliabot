using System;
using System.Globalization;
using System.IO;
using System.Text;

public sealed class TimestampedWriter : TextWriter
{
    private readonly TextWriter _inner;
    private bool _atLineStart = true;

    public TimestampedWriter(TextWriter inner) => _inner = inner;

    public override Encoding Encoding => _inner.Encoding;

    public static void Install() => Console.SetOut(TextWriter.Synchronized(new TimestampedWriter(Console.Out)));

    public override void Write(char value)
    {
        if (_atLineStart && value != '\r')
        {
            _inner.Write(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss ", CultureInfo.InvariantCulture));
            _atLineStart = false;
        }
        _inner.Write(value);
        if (value == '\n') _atLineStart = true;
    }

    public override void Write(string? value)
    {
        if (value is null) return;
        foreach (var c in value) Write(c);
    }

    public override void Flush() => _inner.Flush();
}
