BEGIN TRANSACTION;
CREATE TABLE [identity].[MfaVerificationStates] (
    [AccountId] int NOT NULL,
    [FailedAttemptCount] int NOT NULL,
    [FailureWindowStartedAtUtc] datetimeoffset(7) NULL,
    [BlockedUntilUtc] datetimeoffset(7) NULL,
    [LastAcceptedStep] bigint NULL,
    CONSTRAINT [PK_MfaVerificationStates] PRIMARY KEY ([AccountId]),
    CONSTRAINT [CK_MfaVerificationStates_Block] CHECK ([BlockedUntilUtc] IS NULL OR [FailedAttemptCount] = 5),
    CONSTRAINT [CK_MfaVerificationStates_Count] CHECK ([FailedAttemptCount] BETWEEN 0 AND 5),
    CONSTRAINT [CK_MfaVerificationStates_Step] CHECK ([LastAcceptedStep] IS NULL OR [LastAcceptedStep] >= 0),
    CONSTRAINT [CK_MfaVerificationStates_Window] CHECK (([FailedAttemptCount] = 0 AND [FailureWindowStartedAtUtc] IS NULL) OR ([FailedAttemptCount] > 0 AND [FailureWindowStartedAtUtc] IS NOT NULL)),
    CONSTRAINT [FK_MfaVerificationStates_Accounts_AccountId] FOREIGN KEY ([AccountId]) REFERENCES [identity].[Accounts] ([Id])
);

INSERT INTO [controlplane].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261117115011_AddMfaVerificationStates', N'10.0.9');

COMMIT;
GO

