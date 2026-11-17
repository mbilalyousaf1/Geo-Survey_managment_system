using System.Security.Cryptography;
using System.Text;
using GeoSurvey.Identity.Infrastructure.Mfa;

namespace GeoSurvey.Identity.Infrastructure.Tests;

public sealed class MfaVerificationServiceTests
{
    private sealed class RecordingStore : IMfaVerificationStore
    {
        public string? LastDigest { get; private set; }
        public Task<MfaFailureResult> RecordFailureAsync(Guid accountPublicId, MfaChallengePurpose purpose, string tokenHash, CancellationToken cancellationToken)
        {
            LastDigest = tokenHash;
            return Task.FromResult(MfaFailureResult.FailureRecorded);
        }
        public Task<bool> TryAcceptStepAsync(Guid accountPublicId, MfaChallengePurpose purpose, string tokenHash, long matchedStep, CancellationToken cancellationToken)
        {
            LastDigest = tokenHash;
            return Task.FromResult(true);
        }
    }

    [Fact]
    public async Task RawToken_DoesNotCrossPersistenceBoundary()
    {
        var store = new RecordingStore();
        var service = new MfaVerificationService(store);
        var token = new string('a', 43);
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(token)));
        Assert.Equal(MfaFailureResult.FailureRecorded, await service.RecordFailureAsync(Guid.NewGuid(), MfaChallengePurpose.Enrollment, token));
        Assert.Equal(expected, store.LastDigest);
        Assert.NotEqual(token, store.LastDigest);
        Assert.True(await service.TryAcceptStepAsync(Guid.NewGuid(), MfaChallengePurpose.Verification, token, 123));
        Assert.Equal(expected, store.LastDigest);
    }

    [Fact]
    public async Task InvalidInputs_DoNotReachPersistence()
    {
        var store = new RecordingStore();
        var service = new MfaVerificationService(store);
        Assert.Equal(MfaFailureResult.InvalidChallenge, await service.RecordFailureAsync(Guid.Empty, MfaChallengePurpose.Enrollment, new string('a', 43)));
        Assert.False(await service.TryAcceptStepAsync(Guid.NewGuid(), MfaChallengePurpose.Verification, "malformed", 123));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.RecordFailureAsync(Guid.NewGuid(), (MfaChallengePurpose)99, new string('a', 43)));
        Assert.Null(store.LastDigest);
    }
}
