using System.Security.Cryptography;
using System.Text;

namespace GeoSurvey.Identity.Infrastructure.Mfa;

/// <summary>Initial issuance only. Does not define recovery login, normalization, reset, or regeneration.</summary>
public static class MfaRecoveryCodes
{
    public const int BatchSize = 10;
    public const int CodeByteLength = 16;
    private const string DigestPurpose = "GeoSurvey.Identity.MfaRecoveryCode.v1";

    public static IReadOnlyList<string> GenerateBatch()
    {
        var codes = new string[BatchSize];
        Span<byte> bytes = stackalloc byte[CodeByteLength];
        try
        {
            for (var index = 0; index < codes.Length; index++)
            {
                RandomNumberGenerator.Fill(bytes);
                var hex = Convert.ToHexString(bytes);
                codes[index] = string.Create(39, hex, static (output, input) =>
                {
                    for (var group = 0; group < 8; group++)
                    {
                        input.AsSpan(group * 4, 4).CopyTo(output.Slice(group * 5, 4));
                        if (group < 7)
                            output[group * 5 + 4] = '-';
                    }
                });
            }
            return Array.AsReadOnly(codes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    /// <summary>Hashes an exactly formatted issued code with a versioned purpose and account binding.</summary>
    public static string ComputeIssuedCodeDigest(Guid accountPublicId, string issuedCode)
    {
        if (accountPublicId == Guid.Empty)
            throw new ArgumentException("An account public identifier is required.", nameof(accountPublicId));
        if (!IsIssuedFormat(issuedCode))
            throw new ArgumentException("A canonical issued recovery code is required.", nameof(issuedCode));
        var material = Encoding.ASCII.GetBytes(DigestPurpose + "\0" + accountPublicId.ToString("N") + "\0" + issuedCode.Replace("-", "", StringComparison.Ordinal));
        try
        {
            return Convert.ToHexString(SHA256.HashData(material));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(material);
        }
    }

    private static bool IsIssuedFormat(string? code)
    {
        if (code is not { Length: 39 })
            return false;
        for (var index = 0; index < code.Length; index++)
        {
            if (index % 5 == 4 ? code[index] != '-' : code[index] is not (>= '0' and <= '9' or >= 'A' and <= 'F'))
                return false;
        }
        return true;
    }
}
