using System.Security.Cryptography;
using GeoSurvey.Identity.Infrastructure.Mfa;
using Microsoft.AspNetCore.DataProtection;

namespace GeoSurvey.Identity.Infrastructure.Tests;

public sealed class PendingTotpEnrollmentServiceTests
{
    private sealed class RecordingStore : IPendingTotpEnrollmentStore
    {
        public string? Ciphertext { get; private set; }
        public string? Digest { get; private set; }
        public Task<bool> TryStoreAsync(Guid accountPublicId, string enrollmentTokenHash, string protectedSecret, CancellationToken cancellationToken)
        {
            Ciphertext = protectedSecret;
            Digest = enrollmentTokenHash;
            return Task.FromResult(true);
        }

        public Task<string?> ReadProtectedSecretAsync(Guid accountPublicId, string enrollmentTokenHash, DateTimeOffset now, CancellationToken cancellationToken) =>
            Task.FromResult<string?>("not-valid-ciphertext");
    }

    [Fact]
    public async Task ServiceBoundary_PassesOnlyCiphertextAndTokenDigestToPersistence()
    {
        var store = new RecordingStore();
        var protector = new DataProtectionTotpSecretProtector(new EphemeralDataProtectionProvider());
        var service = new PendingTotpEnrollmentService(store, protector, TimeProvider.System);
        var account = Guid.NewGuid();
        var token = new string('a', 43);
        Assert.True(await service.TryStoreAsync(account, token, "synthetic-secret"));
        Assert.NotNull(store.Ciphertext);
        Assert.DoesNotContain("synthetic-secret", store.Ciphertext);
        Assert.Equal("synthetic-secret", protector.Unprotect(account, store.Ciphertext));
        Assert.NotEqual(token, store.Digest);
        Assert.Equal(64, store.Digest!.Length);
    }

    [Fact]
    public async Task UnreadableStoredCiphertext_FailsClosed()
    {
        var service = new PendingTotpEnrollmentService(new RecordingStore(),
            new DataProtectionTotpSecretProtector(new EphemeralDataProtectionProvider()), TimeProvider.System);
        var error = await Assert.ThrowsAsync<CryptographicException>(() => service.ReadSecretAsync(Guid.NewGuid(), new string('a', 43)));
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("not-valid-ciphertext", error.Message);
    }

    [Fact]
    public async Task CancelledWrite_DoesNotReachPersistence()
    {
        var store = new RecordingStore();
        var service = new PendingTotpEnrollmentService(store,
            new DataProtectionTotpSecretProtector(new EphemeralDataProtectionProvider()), TimeProvider.System);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.TryStoreAsync(Guid.NewGuid(), new string('a', 43), "synthetic-secret", cancellation.Token));
        Assert.Null(store.Ciphertext);
    }
}
