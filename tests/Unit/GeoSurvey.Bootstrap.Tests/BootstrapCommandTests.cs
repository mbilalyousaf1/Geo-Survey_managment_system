using GeoSurvey.ControlPlane.Infrastructure.Bootstrap;

namespace GeoSurvey.Bootstrap.Tests;

[CollectionDefinition("Bootstrap environment", DisableParallelization = true)]
public sealed class BootstrapEnvironmentCollection;

[Collection("Bootstrap environment")]
public sealed class BootstrapCommandTests
{
    private const string OfflineConnection = "Server=127.0.0.1,1;Database=GeoSurvey_ConsoleTests;Integrated Security=True;Encrypt=True";

    private static async Task WithConnection(string? connection, Func<Task> action)
    {
        var previous = Environment.GetEnvironmentVariable(BootstrapCommand.ConnectionEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(BootstrapCommand.ConnectionEnvironmentVariable, connection);
            await action();
        }
        finally { Environment.SetEnvironmentVariable(BootstrapCommand.ConnectionEnvironmentVariable, previous); }
    }

    [Fact]
    public async Task Help_DoesNotRequireConfigurationOrReadInput()
    {
        var terminal = new TestTerminal { IsInteractive = false };
        Assert.Equal(0, await new BootstrapCommand(terminal).RunAsync(["--help"]));
        Assert.Equal(0, terminal.Reads);
    }

    [Fact]
    public async Task Arguments_AreRejectedWithoutEchoingSecrets()
    {
        var terminal = new TestTerminal();
        Assert.Equal(2, await new BootstrapCommand(terminal).RunAsync(["--password", "do-not-echo-password"]));
        Assert.DoesNotContain("do-not-echo-password", terminal.Output.ToString());
        Assert.Equal(0, terminal.Reads);
    }

    [Fact]
    public async Task RedirectedTerminal_IsRejectedBeforeReading()
    {
        var terminal = new TestTerminal { IsInteractive = false };
        Assert.Equal(2, await new BootstrapCommand(terminal).RunAsync([]));
        Assert.Equal(0, terminal.Reads);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    [InlineData("bad-do-not-echo")]
    [InlineData("Server=local")]
    [InlineData("Server=local;Database=Test;Password=do-not-echo;Encrypt=invalid-value")]
    public async Task InvalidOperatorConfiguration_IsRedacted(string? connection)
    {
        await WithConnection(connection, async () =>
        {
            var terminal = new TestTerminal();
            Assert.Equal(2, await new BootstrapCommand(terminal).RunAsync([]));
            Assert.Equal(0, terminal.Reads);
            Assert.Contains(BootstrapCommand.ConnectionEnvironmentVariable, terminal.Output.ToString());
            Assert.DoesNotContain("do-not-echo", terminal.Output.ToString());
            Assert.DoesNotContain("invalid-value", terminal.Output.ToString());
        });
    }

    [Fact]
    public async Task ConfirmationMismatch_StopsBeforeCreationAndDoesNotEchoPasswords()
    {
        await WithConnection(OfflineConnection, async () =>
        {
            var terminal = new TestTerminal();
            terminal.AddLine("operator@example.test");
            terminal.AddLine("first synthetic passphrase");
            terminal.AddLine("second synthetic passphrase");
            Assert.Equal(2, await new BootstrapCommand(terminal).RunAsync([]));
            Assert.Contains("does not match", terminal.Output.ToString());
            Assert.DoesNotContain("first synthetic passphrase", terminal.Output.ToString());
            Assert.DoesNotContain("second synthetic passphrase", terminal.Output.ToString());
        });
    }

    [Fact]
    public async Task UnexpectedTerminalFailure_IsNotPrinted()
    {
        await WithConnection(OfflineConnection, async () =>
        {
            var terminal = new TestTerminal();
            Assert.Equal(1, await new BootstrapCommand(terminal).RunAsync([]));
            Assert.DoesNotContain("do-not-echo-terminal-error", terminal.Output.ToString());
        });
    }

    [Fact]
    public async Task Escape_ProducesCancellationExitCode()
    {
        await WithConnection(OfflineConnection, async () =>
        {
            var terminal = new TestTerminal();
            terminal.AddKey(ConsoleKey.Escape);
            Assert.Equal(130, await new BootstrapCommand(terminal).RunAsync([]));
        });
    }

    [Theory]
    [InlineData(BootstrapCreationResult.Created, 0)]
    [InlineData(BootstrapCreationResult.AlreadyCompleted, 3)]
    [InlineData(BootstrapCreationResult.AdministratorAlreadyExists, 4)]
    [InlineData(BootstrapCreationResult.AccountAlreadyExists, 5)]
    [InlineData(BootstrapCreationResult.LockUnavailable, 6)]
    [InlineData((BootstrapCreationResult)999, 1)]
    public void Results_HaveExplicitExitCodes(BootstrapCreationResult result, int code)
    {
        Assert.Equal(code, BootstrapCommand.Describe(result).ExitCode);
        if (result == BootstrapCreationResult.Created)
            Assert.Contains("MFA", BootstrapCommand.Describe(result).Message);
    }

    [Fact]
    public async Task PrecanceledCommand_DoesNotReadCredentials()
    {
        var terminal = new TestTerminal();
        Assert.Equal(130, await new BootstrapCommand(terminal).RunAsync([], new CancellationToken(true)));
        Assert.Equal(0, terminal.Reads);
    }
}
