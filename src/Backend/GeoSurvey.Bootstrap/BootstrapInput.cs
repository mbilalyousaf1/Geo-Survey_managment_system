namespace GeoSurvey.Bootstrap;

public static class BootstrapInput
{
    public static bool PasswordsMatch(string password, string confirmation) => string.Equals(
        GeoSurvey.Identity.Infrastructure.BootstrapPassword.NormalizeAndValidateBase(password),
        GeoSurvey.Identity.Infrastructure.BootstrapPassword.NormalizeAndValidateBase(confirmation), StringComparison.Ordinal);

    public static async Task<string> ReadAsync(IBootstrapTerminal terminal, string prompt, bool masked, CancellationToken cancellationToken)
    {
        terminal.Write(prompt);
        var characters = new List<char>();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = await terminal.ReadKeyAsync(cancellationToken);
            if (key.Key == ConsoleKey.Escape || (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control)))
                throw new OperationCanceledException(cancellationToken);
            if (key.Key == ConsoleKey.Enter)
            {
                terminal.Write(Environment.NewLine);
                return new string(characters.ToArray());
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (characters.Count == 0)
                    continue;
                var remove = characters.Count >= 2 && char.IsLowSurrogate(characters[^1]) && char.IsHighSurrogate(characters[^2]) ? 2 : 1;
                characters.RemoveRange(characters.Count - remove, remove);
                for (var index = 0; index < remove; index++)
                    terminal.Write("\b \b");
                continue;
            }
            if (char.IsControl(key.KeyChar))
                continue;
            characters.Add(key.KeyChar);
            terminal.Write(masked ? "*" : key.KeyChar.ToString());
        }
    }
}
