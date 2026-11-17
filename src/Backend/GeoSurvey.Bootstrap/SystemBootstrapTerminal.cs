namespace GeoSurvey.Bootstrap;

public sealed class SystemBootstrapTerminal : IBootstrapTerminal
{
    public bool IsInteractive => !Console.IsInputRedirected && !Console.IsOutputRedirected;

    public async ValueTask<ConsoleKeyInfo> ReadKeyAsync(CancellationToken cancellationToken)
    {
        while (!Console.KeyAvailable)
            await Task.Delay(25, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return Console.ReadKey(intercept: true);
    }

    public void Write(string text) => Console.Write(text);
}
