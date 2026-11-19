using System.Reflection;
using GeoSurvey.Identity.Infrastructure.SignIn;

namespace GeoSurvey.Identity.Infrastructure.Tests;

public sealed class PlatformMfaVerificationResultTests
{
    [Fact]
    public void Result_ContainsOnlyProvisionalPublicSubject_AndPrintsNoSubjectData()
    {
        var account = Guid.NewGuid();
        var result = PlatformMfaVerificationResult.Verified(account);
        Assert.True(result.IsVerified);
        Assert.Equal(account, result.AccountPublicId);
        Assert.DoesNotContain(account.ToString(), result.ToString()!);
        Assert.Equal(new[] { "AccountPublicId", "IsVerified" }, typeof(PlatformMfaVerificationResult)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public).Select(property => property.Name).Order());
        Assert.All(typeof(PlatformMfaVerificationResult).GetProperties(BindingFlags.Instance | BindingFlags.Public), property => Assert.False(property.CanWrite));
        Assert.False(PlatformMfaVerificationResult.Rejected.IsVerified);
        Assert.Null(PlatformMfaVerificationResult.Rejected.AccountPublicId);
        Assert.Throws<ArgumentException>(() => PlatformMfaVerificationResult.Verified(Guid.Empty));
    }

    [Fact]
    public void VerificationContract_AcceptsNoClientSubjectSecretMatchedStepOrSuccessFlag()
    {
        var parameters = typeof(IPlatformMfaVerifier).GetMethod(nameof(IPlatformMfaVerifier.VerifyAsync))!.GetParameters();
        Assert.Equal(new[] { typeof(string), typeof(string), typeof(CancellationToken) }, parameters.Select(parameter => parameter.ParameterType));
    }
}
