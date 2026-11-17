BEGIN TRANSACTION;
ALTER TABLE [identity].[MfaChallenges] ADD CONSTRAINT [AK_MfaChallenges_Id_AccountId] UNIQUE ([Id], [AccountId]);

CREATE TABLE [identity].[PendingTotpEnrollments] (
    [AccountId] int NOT NULL,
    [ChallengeId] int NOT NULL,
    [ProtectedSecret] nvarchar(max) NOT NULL,
    [CreatedAtUtc] datetimeoffset(7) NOT NULL,
    CONSTRAINT [PK_PendingTotpEnrollments] PRIMARY KEY ([AccountId]),
    CONSTRAINT [FK_PendingTotpEnrollments_Accounts_AccountId] FOREIGN KEY ([AccountId]) REFERENCES [identity].[Accounts] ([Id]),
    CONSTRAINT [FK_PendingTotpEnrollments_MfaChallenges_ChallengeId_AccountId] FOREIGN KEY ([ChallengeId], [AccountId]) REFERENCES [identity].[MfaChallenges] ([Id], [AccountId])
);

CREATE UNIQUE INDEX [IX_PendingTotpEnrollments_ChallengeId] ON [identity].[PendingTotpEnrollments] ([ChallengeId]);

CREATE INDEX [IX_PendingTotpEnrollments_ChallengeId_AccountId] ON [identity].[PendingTotpEnrollments] ([ChallengeId], [AccountId]);

INSERT INTO [controlplane].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261117112151_AddPendingTotpEnrollments', N'10.0.9');

COMMIT;
GO

