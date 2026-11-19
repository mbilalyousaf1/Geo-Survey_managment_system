using System.Security.Cryptography;
using System.Text;

namespace GeoSurvey.Identity.Infrastructure.Authentication;

public static class OpaqueAuthenticationToken
{
    public static bool IsWellFormed(string? token) => token is { Length: 43 }
        && token.All(character => character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_');

    public static string Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        try { return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public static string Digest(string token)
    {
        var bytes = Encoding.ASCII.GetBytes(token);
        Span<byte> hash = stackalloc byte[32];
        try { SHA256.HashData(bytes, hash); return Convert.ToHexString(hash); }
        finally { CryptographicOperations.ZeroMemory(bytes); CryptographicOperations.ZeroMemory(hash); }
    }

    /// <summary>The issued eight-group hexadecimal format permits surrounding whitespace and lowercase input only.</summary>
    public static string? NormalizeRecoveryCode(string? input)
    {
        if (input is null || input.Length > 100)
            return null;
        var code = input.Trim().ToUpperInvariant();
        if (code.Length != 39)
            return null;
        for (var index = 0; index < code.Length; index++)
            if (index % 5 == 4 ? code[index] != '-' : code[index] is not (>= '0' and <= '9' or >= 'A' and <= 'F'))
                return null;
        return code;
    }
}
