namespace GeoSurvey.Identity.Infrastructure.Mfa;

/// <summary>Protects recoverable authenticator secrets for one server-selected account.</summary>
public interface ITotpSecretProtector
{
    string Protect(Guid accountPublicId, string secret);
    string Unprotect(Guid accountPublicId, string protectedSecret);
}
