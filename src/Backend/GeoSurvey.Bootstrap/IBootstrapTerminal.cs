namespace GeoSurvey.Bootstrap;

/// <summary>Terminal boundary: production reads intercepted keys; tests supply deterministic key events.</summary>
public interface IBootstrapTerminal
{
    bool IsInteractive { get; }
    ValueTask<ConsoleKeyInfo> ReadKeyAsync(CancellationToken cancellationToken);
    void Write(string text);
}
