namespace GeoSurvey.Bootstrap.Tests;

public sealed class BootstrapInputTests
{
    [Fact]
    public async Task MaskedInput_NeverWritesPasswordCharacters()
    {
        var terminal = new TestTerminal();
        const string password = "synthetic secret passphrase";
        terminal.AddLine(password);
        Assert.Equal(password, await BootstrapInput.ReadAsync(terminal, "Password: ", true, default));
        Assert.Equal("Password: " + new string('*', password.Length) + Environment.NewLine, terminal.Output.ToString());
        Assert.DoesNotContain(password, terminal.Output.ToString());
    }

    [Fact]
    public async Task Backspace_RemovesSupplementaryCodePointWithoutLeavingInvalidUnicode()
    {
        var terminal = new TestTerminal();
        terminal.AddKey(ConsoleKey.Backspace);
        terminal.AddText("prefix\U0001f680");
        terminal.AddKey(ConsoleKey.Backspace);
        terminal.AddLine(" suffix");
        Assert.Equal("prefix suffix", await BootstrapInput.ReadAsync(terminal, "", true, default));
        Assert.DoesNotContain("prefix", terminal.Output.ToString());
    }

    [Theory]
    [InlineData(ConsoleKey.Escape, false)]
    [InlineData(ConsoleKey.C, true)]
    public async Task EscapeOrControlC_CancelsInput(ConsoleKey key, bool control)
    {
        var terminal = new TestTerminal();
        terminal.AddKey(key, '\0', control);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BootstrapInput.ReadAsync(terminal, "Password: ", true, default));
    }

    [Fact]
    public async Task Cancellation_IsCheckedBeforeReadingKeys()
    {
        var terminal = new TestTerminal();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BootstrapInput.ReadAsync(terminal, "", true, new CancellationToken(true)));
        Assert.Equal(0, terminal.Reads);
    }

    [Fact]
    public void Confirmation_UsesNfcAndPreservesCaseAndWhitespace()
    {
        Assert.True(BootstrapInput.PasswordsMatch("example passphrase e\u0301", "example passphrase \u00e9"));
        Assert.False(BootstrapInput.PasswordsMatch("example passphrase", "Example passphrase"));
        Assert.False(BootstrapInput.PasswordsMatch(" example passphrase ", "example passphrase"));
    }
}
