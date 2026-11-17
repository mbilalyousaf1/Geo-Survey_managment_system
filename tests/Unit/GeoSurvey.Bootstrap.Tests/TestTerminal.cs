using System.Text;

namespace GeoSurvey.Bootstrap.Tests;

// Input/output test double only; the real creation service is not replaced with a fake success.
internal sealed class TestTerminal : IBootstrapTerminal
{
    private readonly Queue<ConsoleKeyInfo> _keys = new();
    public bool IsInteractive { get; init; } = true;
    public StringBuilder Output { get; } = new();
    public int Reads { get; private set; }

    public void AddText(string text)
    {
        foreach (var character in text)
            _keys.Enqueue(new ConsoleKeyInfo(character, (ConsoleKey)0, false, false, false));
    }

    public void AddKey(ConsoleKey key, char character = '\0', bool control = false) =>
        _keys.Enqueue(new ConsoleKeyInfo(character, key, false, false, control));

    public void AddLine(string text)
    {
        AddText(text);
        AddKey(ConsoleKey.Enter, '\r');
    }

    public ValueTask<ConsoleKeyInfo> ReadKeyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Reads++;
        return ValueTask.FromResult(_keys.Count > 0 ? _keys.Dequeue() : throw new IOException("do-not-echo-terminal-error"));
    }

    public void Write(string text) => Output.Append(text);
}
