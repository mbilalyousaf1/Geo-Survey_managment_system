using System.Net;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using GeoSurvey.Bootstrap;
using GeoSurvey.ControlPlane.Infrastructure;
using GeoSurvey.ControlPlane.Infrastructure.Bootstrap;
using GeoSurvey.Identity.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeoSurvey.LocalDemo;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "--help" or "-h")
            {
                Console.WriteLine("Local-only GeoSurvey demo utility: prepare ROOT HTTPS_PORT SQL_PORT [acceptance], initialize ROOT, bootstrap ROOT, seed-acceptance ROOT. Secrets are never printed.");
                return 0;
            }
            if (args[0] == "prepare" && args.Length is 4 or 5)
            {
                Prepare(args[1], int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture),
                    int.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture), args.Length == 5 && args[4] == "acceptance");
                return 0;
            }
            if (args.Length != 2)
                throw new ArgumentException("Unsupported local demo command.");
            var settings = Read(args[1]);
            switch (args[0])
            {
                case "initialize": await InitializeAsync(settings); return 0;
                case "bootstrap":
                    var previous = Environment.GetEnvironmentVariable(BootstrapCommand.ConnectionEnvironmentVariable);
                    try
                    {
                        Environment.SetEnvironmentVariable(BootstrapCommand.ConnectionEnvironmentVariable, BootstrapConnection(settings));
                        return await new BootstrapCommand(new SystemBootstrapTerminal()).RunAsync([]);
                    }
                    finally { Environment.SetEnvironmentVariable(BootstrapCommand.ConnectionEnvironmentVariable, previous); }
                case "seed-acceptance": await SeedAcceptanceAsync(settings); return 0;
                default: throw new ArgumentException("Unsupported local demo command.");
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Setup exceptions/SQL may contain supplied values; do not echo configuration or inner exceptions.
            Console.Error.WriteLine("Local demo setup failed. Verify the command, owned external directory, Docker SQL readiness, and required configuration. No secrets were printed.");
            return 1;
        }
    }

    private static void Prepare(string root, int httpsPort, int sqlPort, bool acceptance)
    {
        root = Path.GetFullPath(root);
        if (Directory.Exists(root) || File.Exists(root) || httpsPort is < 1024 or > 65535 || sqlPort is < 1024 or > 65535)
            throw new ArgumentException("A new demo directory and unprivileged ports are required.");
        var repository = FindRepository();
        var relative = Path.GetRelativePath(repository, root);
        if (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new ArgumentException("Demo credentials must live outside the repository.");
        Directory.CreateDirectory(root);
        RestrictDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "ring"));
        var settings = new DemoSettings
        {
            Root = root,
            DemoId = Guid.NewGuid().ToString("N"),
            HttpsPort = httpsPort,
            SqlPort = sqlPort,
            Acceptance = acceptance,
            SaPassword = RandomPassword(),
            RuntimePassword = RandomPassword(),
            JobsPassword = RandomPassword(),
            BootstrapPassword = RandomPassword(),
            ProtectionPassword = RandomPassword(),
            SigningPassword = RandomPassword(),
            HttpsPassword = RandomPassword()
        };
        GenerateCertificate(root, "protection", settings.ProtectionPassword, transport: false, signing: false);
        GenerateCertificate(root, "signing", settings.SigningPassword, transport: false, signing: true);
        GenerateCertificate(root, "https", settings.HttpsPassword, transport: true, signing: true);
        WritePrivate(Path.Combine(root, "demo-settings.json"), JsonSerializer.Serialize(settings, Json));
        var lines = new Dictionary<string, string>
        {
            ["DEMO_ROOT"] = root.Replace('\\', '/'),
            ["DEMO_HTTPS_PORT"] = httpsPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["DEMO_SQL_PORT"] = sqlPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["DEMO_SA_PASSWORD"] = settings.SaPassword,
            ["DEMO_API_CONNECTION"] = Connection("sql", "GeoSurvey_ControlPlane", "GeoSurveyRuntime", settings.RuntimePassword),
            ["DEMO_WORKER_CONNECTION"] = Connection("sql", "GeoSurvey_ControlPlane", "sa", settings.SaPassword),
            ["DEMO_TENANT_CONNECTION"] = Connection("sql", "master", "GeoSurveyRuntime", settings.RuntimePassword),
            ["DEMO_MANAGEMENT_CONNECTION"] = Connection("sql", "master", "sa", settings.SaPassword),
            ["DEMO_JOBS_CONNECTION"] = Connection("sql", "GeoSurvey_Jobs", "GeoSurveyJobs", settings.JobsPassword),
            ["DEMO_PROTECTION_PASSWORD"] = settings.ProtectionPassword,
            ["DEMO_SIGNING_PASSWORD"] = settings.SigningPassword,
            ["DEMO_HTTPS_PASSWORD"] = settings.HttpsPassword,
            ["DEMO_ISSUER"] = "https://localhost:" + httpsPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["DEMO_PROJECT"] = "geosurvey-phase1-" + settings.DemoId[..12]
        };
        WritePrivate(Path.Combine(root, "compose.env"), string.Join(Environment.NewLine, lines.Select(item => item.Key + "='" + item.Value.Replace("'", "\\'", StringComparison.Ordinal) + "'")) + Environment.NewLine);
        Console.WriteLine("Generated separate local HTTPS, JWT signing, and Data Protection certificates and private configuration outside source.");
    }

    private static async Task InitializeAsync(DemoSettings settings)
    {
        await WaitForSqlAsync(settings);
        await using (var services = new ServiceCollection().Configure<IdentityOptions>(IdentityPersistenceOptions.Configure).BuildServiceProvider())
        {
            var options = new DbContextOptionsBuilder<GlobalControlPlaneDbContext>()
                .UseGeoSurveyControlPlaneSqlServer(HostConnection(settings, "GeoSurvey_ControlPlane", "sa", settings.SaPassword), services)
                .UseLoggerFactory(NullLoggerFactory.Instance).Options;
            await using var context = new GlobalControlPlaneDbContext(options);
            await context.Database.MigrateAsync();
        }
        await using var master = new SqlConnection(HostConnection(settings, "master", "sa", settings.SaPassword));
        await master.OpenAsync();
        await ExecuteAsync(master, "IF DB_ID(N'GeoSurvey_Jobs') IS NULL CREATE DATABASE [GeoSurvey_Jobs]");
        await EnsureLoginAsync(master, "GeoSurveyRuntime", settings.RuntimePassword);
        await EnsureLoginAsync(master, "GeoSurveyJobs", settings.JobsPassword);
        await EnsureLoginAsync(master, "GeoSurveyBootstrapOperator", settings.BootstrapPassword);
        await using var global = new SqlConnection(HostConnection(settings, "GeoSurvey_ControlPlane", "sa", settings.SaPassword));
        await global.OpenAsync();
        await ExecuteAsync(global, await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "bootstrap-role.sql")));
        await ExecuteAsync(global, "IF USER_ID(N'GeoSurveyRuntime') IS NULL CREATE USER [GeoSurveyRuntime] FOR LOGIN [GeoSurveyRuntime]; IF USER_ID(N'GeoSurveyBootstrapOperator') IS NULL CREATE USER [GeoSurveyBootstrapOperator] FOR LOGIN [GeoSurveyBootstrapOperator]; ALTER ROLE [GeoSurveyBootstrap] ADD MEMBER [GeoSurveyBootstrapOperator];");
        await ExecuteAsync(global, "GRANT SELECT, INSERT, UPDATE ON SCHEMA::[identity] TO [GeoSurveyRuntime]; GRANT DELETE ON OBJECT::[identity].[PendingTotpEnrollments] TO [GeoSurveyRuntime]; GRANT SELECT, INSERT, UPDATE ON SCHEMA::[organization] TO [GeoSurveyRuntime]; GRANT SELECT ON SCHEMA::[controlplane] TO [GeoSurveyRuntime]; DENY INSERT, UPDATE, DELETE ON OBJECT::[controlplane].[BootstrapCompletion] TO [GeoSurveyRuntime];");
        // Explicit schema checks allow this utility to bootstrap only migrated capabilities, not invent tables.
        await ExecuteAsync(global, "IF SCHEMA_ID(N'tenancy') IS NOT NULL EXEC(N'GRANT SELECT, INSERT, UPDATE ON SCHEMA::[tenancy] TO [GeoSurveyRuntime]'); IF OBJECT_ID(N'audit.TenantLifecycleAudit', N'U') IS NOT NULL BEGIN GRANT SELECT, INSERT ON OBJECT::[audit].[TenantLifecycleAudit] TO [GeoSurveyRuntime]; DENY UPDATE, DELETE ON OBJECT::[audit].[TenantLifecycleAudit] TO [GeoSurveyRuntime]; END;");
        await using var jobs = new SqlConnection(HostConnection(settings, "GeoSurvey_Jobs", "sa", settings.SaPassword));
        await jobs.OpenAsync();
        await ExecuteAsync(jobs, "IF USER_ID(N'GeoSurveyJobs') IS NULL CREATE USER [GeoSurveyJobs] FOR LOGIN [GeoSurveyJobs]; ALTER ROLE [db_owner] ADD MEMBER [GeoSurveyJobs];");
        Console.WriteLine("Global migrations and separate runtime, bootstrap, and job-storage principals initialized. API runtime has no database-creation privileges.");
    }

    private static async Task SeedAcceptanceAsync(DemoSettings settings)
    {
        if (!settings.Acceptance)
            throw new InvalidOperationException("Synthetic seeding is restricted to an explicitly created acceptance environment.");
        var path = Path.Combine(settings.Root, "acceptance-credentials.json");
        if (File.Exists(path))
            throw new InvalidOperationException("Acceptance credentials already exist; refusing to overwrite.");
        var email = "phase1-" + settings.DemoId[..12] + "@example.test";
        var password = RandomPassword();
        await using var services = new ServiceCollection().Configure<IdentityOptions>(IdentityPersistenceOptions.Configure).BuildServiceProvider();
        var options = new DbContextOptionsBuilder<GlobalControlPlaneDbContext>()
            .UseGeoSurveyControlPlaneSqlServer(BootstrapConnection(settings), services).UseLoggerFactory(NullLoggerFactory.Instance).Options;
        var outcome = await new FirstPlatformAdministratorBootstrapper(options).CreateAsync(email, password);
        if (outcome != BootstrapCreationResult.Created)
            throw new InvalidOperationException("Synthetic acceptance administrator was not created.");
        WritePrivate(path, JsonSerializer.Serialize(new { email, password, baseUrl = "https://localhost:" + settings.HttpsPort }, Json));
        Console.WriteLine("Synthetic acceptance account created through the actual restricted bootstrap service. Its private credentials were not printed.");
    }

    private static async Task EnsureLoginAsync(SqlConnection connection, string login, string password)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "IF SUSER_ID(@login) IS NULL BEGIN DECLARE @sql nvarchar(max) = N'CREATE LOGIN ' + QUOTENAME(@login) + N' WITH PASSWORD = ' + QUOTENAME(@password, '''') + N', CHECK_POLICY = ON'; EXEC(@sql); END;";
        command.Parameters.AddWithValue("@login", login);
        command.Parameters.AddWithValue("@password", password);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteAsync(SqlConnection connection, string text)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = text;
        command.CommandTimeout = 90;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task WaitForSqlAsync(DemoSettings settings)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            try
            {
                await using var connection = new SqlConnection(HostConnection(settings, "master", "sa", settings.SaPassword));
                await connection.OpenAsync();
                return;
            }
            catch (SqlException) when (attempt < 59) { await Task.Delay(1000); }
        }
    }

    private static string Connection(string server, string database, string user, string password) => new SqlConnectionStringBuilder
    {
        DataSource = server,
        InitialCatalog = database,
        UserID = user,
        Password = password,
        Encrypt = true,
        TrustServerCertificate = true,
        Pooling = false,
        ConnectTimeout = 2
    }.ConnectionString;
    private static string HostConnection(DemoSettings settings, string database, string user, string password) => Connection("127.0.0.1," + settings.SqlPort, database, user, password);
    private static string BootstrapConnection(DemoSettings settings) => HostConnection(settings, "GeoSurvey_ControlPlane", "GeoSurveyBootstrapOperator", settings.BootstrapPassword);
    private static string RandomPassword() => "Gs!9" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

    private static void GenerateCertificate(string root, string name, string password, bool transport, bool signing)
    {
        using var rsa = RSA.Create(3072);
        var request = new CertificateRequest("CN=" + (transport ? "localhost" : "GeoSurvey local " + name), rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(signing ? X509KeyUsageFlags.DigitalSignature | (transport ? X509KeyUsageFlags.KeyEncipherment : 0) : X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        if (transport)
        {
            var names = new SubjectAlternativeNameBuilder();
            names.AddDnsName("localhost"); names.AddDnsName("api"); names.AddDnsName("web"); names.AddIpAddress(IPAddress.Loopback);
            request.CertificateExtensions.Add(names.Build());
        }
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddMonths(3));
        var path = Path.Combine(root, name + ".pfx");
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));
        RestrictFile(path);
        if (transport)
        {
            WritePrivate(Path.Combine(root, "https.crt"), certificate.ExportCertificatePem());
            WritePrivate(Path.Combine(root, "https.key"), rsa.ExportPkcs8PrivateKeyPem());
        }
    }

    private static DemoSettings Read(string root)
    {
        root = Path.GetFullPath(root);
        var settings = JsonSerializer.Deserialize<DemoSettings>(File.ReadAllText(Path.Combine(root, "demo-settings.json")), Json)
            ?? throw new InvalidOperationException("Demo configuration is missing.");
        if (!string.Equals(root, Path.GetFullPath(settings.Root), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            || !Guid.TryParseExact(settings.DemoId, "N", out _))
            throw new InvalidOperationException("Demo ownership configuration does not match.");
        return settings;
    }

    private static string FindRepository()
    {
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "GeoSurvey_Master_Architecture_and_Codex_Spec.md")))
                return directory.FullName;
        throw new InvalidOperationException("Run from the GeoSurvey repository.");
    }

    private static void WritePrivate(string path, string content)
    {
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(content);
        RestrictFile(path);
    }

    private static void RestrictDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            var owner = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Current operator identity is required.");
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(path).SetAccessControl(security);
        }
        else File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static void RestrictFile(string path)
    {
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }
}

internal sealed class DemoSettings
{
    public string Root { get; set; } = "";
    public string DemoId { get; set; } = "";
    public int HttpsPort { get; set; }
    public int SqlPort { get; set; }
    public bool Acceptance { get; set; }
    public string SaPassword { get; set; } = "";
    public string RuntimePassword { get; set; } = "";
    public string JobsPassword { get; set; } = "";
    public string BootstrapPassword { get; set; } = "";
    public string ProtectionPassword { get; set; } = "";
    public string SigningPassword { get; set; } = "";
    public string HttpsPassword { get; set; } = "";
}
