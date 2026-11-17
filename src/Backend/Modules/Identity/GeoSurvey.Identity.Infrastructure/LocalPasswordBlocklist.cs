using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text;

namespace GeoSurvey.Identity.Infrastructure;

/// <summary>Immutable verified local corpus. Performs no network calls or account writes.</summary>
public sealed class LocalPasswordBlocklist
{
    public const string PackagedCorpusSha256 = "424A3E03A17DF0A2BC2B3CA749D81B04E79D59CB7AEEC8876A5A3F308D0CAF51";
    private const int MaximumCorpusBytes = 32 * 1024 * 1024;
    private const string ResourceName = "GeoSurvey.Identity.PasswordBlocklist.Corpus";
    private const string UnavailableMessage = "Verified password blocklist is unavailable. Bootstrap credential creation cannot continue.";
    private static readonly Lazy<LocalPasswordBlocklist> Packaged = new(LoadResource);
    private readonly FrozenSet<string> _passwords;

    public PasswordCorpusStatistics Statistics { get; }

    private LocalPasswordBlocklist(FrozenSet<string> passwords, PasswordCorpusStatistics statistics)
    {
        _passwords = passwords;
        Statistics = statistics;
    }

    /// <summary>Load/check once per process; a failed load never falls back to an empty corpus.</summary>
    public static LocalPasswordBlocklist LoadPackaged() => Packaged.Value;

    /// <summary>Load from the current stream position using an independently trusted expected digest.</summary>
    public static LocalPasswordBlocklist LoadVerified(Stream corpus, string expectedSha256)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        if (expectedSha256 is null || expectedSha256.Length != 64 || expectedSha256.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("A trusted SHA-256 digest is required.", nameof(expectedSha256));

        try
        {
            if (!corpus.CanRead)
                throw new InvalidDataException();
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = corpus.Read(chunk, 0, chunk.Length)) > 0)
            {
                if (buffer.Length + read > MaximumCorpusBytes)
                    throw new InvalidDataException();
                buffer.Write(chunk, 0, read);
            }
            var bytes = buffer.ToArray();
            if (bytes.Length == 0 || !Convert.ToHexString(SHA256.HashData(bytes)).Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException();

            var offset = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? Encoding.UTF8.Preamble.Length : 0;
            var text = new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset);
            using var reader = new StringReader(text);
            var passwords = new HashSet<string>(StringComparer.Ordinal);
            var total = 0;
            var empty = 0;
            var duplicates = 0;
            var normalizedEntries = 0;
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                total++;
                if (line.Length == 0)
                {
                    empty++;
                    continue; // Empty passwords always fail base validation; record the framing entry.
                }
                var normalized = line.Normalize(NormalizationForm.FormC);
                if (!string.Equals(line, normalized, StringComparison.Ordinal))
                    normalizedEntries++;
                if (!passwords.Add(normalized))
                    duplicates++;
            }
            if (passwords.Count == 0)
                throw new InvalidDataException();

            return new LocalPasswordBlocklist(passwords.ToFrozenSet(StringComparer.Ordinal),
                new PasswordCorpusStatistics(total, passwords.Count, duplicates, empty, normalizedEntries));
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or ObjectDisposedException or NotSupportedException)
        {
            // Do not attach source data, paths, parser errors, or password-bearing exceptions.
            throw new InvalidOperationException(UnavailableMessage);
        }
    }

    /// <summary>Accepted base rules plus exact local-corpus blocking; does not authorize account creation.</summary>
    public string NormalizeAndValidate(string password)
    {
        var normalized = BootstrapPassword.NormalizeAndValidateBase(password);
        if (_passwords.Contains(normalized))
            throw new ArgumentException("Choose a different password; this value is in the password blocklist.", nameof(password));
        return normalized;
    }

    private static LocalPasswordBlocklist LoadResource()
    {
        using var stream = typeof(LocalPasswordBlocklist).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(UnavailableMessage);
        return LoadVerified(stream, PackagedCorpusSha256);
    }
}

/// <summary>Corpus counts only; contains no password values.</summary>
public sealed record PasswordCorpusStatistics(int TotalEntries, int UniqueEntries, int DuplicateEntries, int EmptyEntries, int NormalizedEntries);
