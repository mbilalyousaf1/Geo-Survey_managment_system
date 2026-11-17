using GeoSurvey.ControlPlane.Infrastructure;
using GeoSurvey.ControlPlane.Infrastructure.Bootstrap;
using GeoSurvey.Identity.Infrastructure;
using GeoSurvey.Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GeoSurvey.Bootstrap;

public sealed class BootstrapCommand(IBootstrapTerminal terminal)
{
    public const string ConnectionEnvironmentVariable = "GEOSURVEY_BOOTSTRAP_SQLSERVER";

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        try
        {
            if (args.Length == 1 && args[0] is "--help" or "-h")
            {
                terminal.Write($"GeoSurvey.Bootstrap: run interactively without arguments. Configure {ConnectionEnvironmentVariable} with the dedicated operator connection. No migrations, permission setup, credential reset, or MFA enrollment is performed.{Environment.NewLine}");
                return 0;
            }
            if (args.Length != 0)
                return Report(2, "Arguments are not supported. Enter the password only at the masked interactive prompt.");
            if (!terminal.IsInteractive)
                return Report(2, "An interactive input and output terminal is required. Redirected input/output is not accepted.");
            cancellationToken.ThrowIfCancellationRequested();
            var connection = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
            if (!IsValidConnection(connection))
                return Report(2, $"Configure {ConnectionEnvironmentVariable} with a valid dedicated SQL Server operator connection containing Server and Database.");

            terminal.Write($"First Platform Administrator bootstrap. This is not a password reset. MFA enrollment remains required.{Environment.NewLine}");
            var email = await BootstrapInput.ReadAsync(terminal, "Administrator email: ", false, cancellationToken);
            _ = AccountEmail.Create(email);
            var password = await BootstrapInput.ReadAsync(terminal, "Password: ", true, cancellationToken);
            var confirmation = await BootstrapInput.ReadAsync(terminal, "Confirm password: ", true, cancellationToken);
            var normalized = BootstrapPassword.NormalizeAndValidateBase(password);
            if (!BootstrapInput.PasswordsMatch(normalized, confirmation))
                return Report(2, "Password confirmation does not match. No creation attempt was made.");

            using var services = new ServiceCollection().Configure<IdentityOptions>(IdentityPersistenceOptions.Configure).BuildServiceProvider();
            var options = new DbContextOptionsBuilder<GlobalControlPlaneDbContext>()
                .UseGeoSurveyControlPlaneSqlServer(connection!, services).Options;
            var result = await new FirstPlatformAdministratorBootstrapper(options).CreateAsync(email, normalized, cancellationToken);
            var outcome = Describe(result);
            return Report(outcome.ExitCode, outcome.Message);
        }
        catch (OperationCanceledException)
        {
            return Report(130, "Bootstrap canceled. A retry will check permanent completion; no credential reset is available.");
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            return Report(130, "Bootstrap canceled. A retry will check permanent completion; no credential reset is available.");
        }
        catch (ArgumentException)
        {
            return Report(2, "Input is invalid or the password was rejected by the policy/blocklist. No credential reset is available.");
        }
        catch (Exception)
        {
            return Report(1, "Bootstrap could not complete. Check operator configuration, permissions, and database availability. Do not assume an interrupted commit failed; retries check permanent completion.");
        }
    }

    public static (int ExitCode, string Message) Describe(BootstrapCreationResult result) => result switch
    {
        BootstrapCreationResult.Created => (0, "Administrator created. MFA enrollment is required before administration; the enrollment workflow is separate from this tool."),
        BootstrapCreationResult.AlreadyCompleted => (3, "Bootstrap was previously completed. No account was replaced or reset."),
        BootstrapCreationResult.AdministratorAlreadyExists => (4, "A Platform Administrator already exists. No account was replaced or reset."),
        BootstrapCreationResult.AccountAlreadyExists => (5, "The Platform account already exists. It was not promoted or reset."),
        BootstrapCreationResult.LockUnavailable => (6, "Bootstrap lock unavailable. Retry later after checking other operator activity."),
        _ => (1, "Bootstrap returned an unrecognized outcome. Review operator configuration before retrying.")
    };

    private int Report(int code, string message)
    {
        terminal.Write(message + Environment.NewLine);
        return code;
    }

    private static bool IsValidConnection(string? connection)
    {
        if (string.IsNullOrWhiteSpace(connection))
            return false;
        try
        {
            var parsed = new SqlConnectionStringBuilder(connection);
            return !string.IsNullOrWhiteSpace(parsed.DataSource) && !string.IsNullOrWhiteSpace(parsed.InitialCatalog);
        }
        catch (ArgumentException) { return false; }
    }
}
