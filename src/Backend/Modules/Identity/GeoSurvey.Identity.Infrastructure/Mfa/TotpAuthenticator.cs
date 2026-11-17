using System.Buffers.Binary;
using System.Security.Cryptography;

namespace GeoSurvey.Identity.Infrastructure.Mfa;

/// <summary>RFC 4226/6238 calculation only. Callers must enforce persistent attempt and replay controls.</summary>
public sealed class TotpAuthenticator(TimeProvider clock)
{
    public const int SecretByteLength = 20;
    public const int SecretCharacterLength = 32;
    public const int CodeLength = 6;
    public const int StepSeconds = 30;
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>Returns a new 160-bit secret as canonical uppercase, unpadded Base32.</summary>
    public static string GenerateSecret()
    {
        Span<byte> secret = stackalloc byte[SecretByteLength];
        Span<char> encoded = stackalloc char[SecretCharacterLength];
        try
        {
            RandomNumberGenerator.Fill(secret);
            var buffer = 0;
            var bits = 0;
            var position = 0;
            foreach (var value in secret)
            {
                buffer = (buffer << 8) | value;
                bits += 8;
                while (bits >= 5)
                {
                    bits -= 5;
                    encoded[position++] = Alphabet[(buffer >> bits) & 31];
                }
                buffer &= (1 << bits) - 1;
            }
            return new string(encoded);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
            encoded.Clear();
        }
    }

    /// <summary>
    /// Returns the latest matching step within current +/- one, or null for invalid input/no match.
    /// A match is not authentication. Atomically enforce step reuse and challenge consumption in the caller.
    /// </summary>
    public long? Verify(string? secret, string? code)
    {
        if (code is not { Length: CodeLength } || code.Any(character => character is < '0' or > '9'))
            return null;
        var seconds = clock.GetUtcNow().ToUnixTimeSeconds();
        if (seconds < 0)
            return null;
        Span<byte> key = stackalloc byte[SecretByteLength];
        Span<byte> counter = stackalloc byte[8];
        Span<byte> digest = stackalloc byte[20];
        Span<byte> supplied = stackalloc byte[CodeLength];
        Span<byte> expected = stackalloc byte[CodeLength];
        try
        {
            if (!TryDecodeSecret(secret, key))
                return null;
            for (var index = 0; index < CodeLength; index++)
                supplied[index] = (byte)code[index];
            var currentStep = seconds / StepSeconds;
            long? matchedStep = null;
            for (var offset = -1; offset <= 1; offset++)
            {
                var candidate = currentStep + offset;
                if (candidate < 0)
                    continue;
                BinaryPrimitives.WriteUInt64BigEndian(counter, (ulong)candidate);
                HMACSHA1.HashData(key, counter, digest);
                var truncationOffset = digest[^1] & 15;
                var value = (BinaryPrimitives.ReadUInt32BigEndian(digest.Slice(truncationOffset, 4)) & 0x7fffffffu) % 1_000_000;
                for (var index = CodeLength - 1; index >= 0; index--)
                {
                    expected[index] = (byte)('0' + value % 10);
                    value /= 10;
                }
                // Evaluate the whole permitted window; each comparison has a fixed six-byte length.
                if (CryptographicOperations.FixedTimeEquals(expected, supplied))
                    matchedStep = candidate;
            }
            return matchedStep;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(counter);
            CryptographicOperations.ZeroMemory(digest);
            CryptographicOperations.ZeroMemory(supplied);
            CryptographicOperations.ZeroMemory(expected);
        }
    }

    private static bool TryDecodeSecret(string? secret, Span<byte> bytes)
    {
        if (secret is not { Length: SecretCharacterLength })
            return false;
        var buffer = 0;
        var bits = 0;
        var position = 0;
        foreach (var character in secret)
        {
            var value = Alphabet.IndexOf(character);
            if (value < 0)
                return false;
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                bytes[position++] = (byte)(buffer >> bits);
                buffer &= (1 << bits) - 1;
            }
        }
        return true;
    }
}
