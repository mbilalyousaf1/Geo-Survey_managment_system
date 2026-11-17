namespace GeoSurvey.Bootstrap;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += handler;
        try
        {
            return await new BootstrapCommand(new SystemBootstrapTerminal()).RunAsync(args, cancellation.Token);
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }
    }
}
