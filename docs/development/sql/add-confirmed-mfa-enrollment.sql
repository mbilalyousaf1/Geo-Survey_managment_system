BEGIN TRANSACTION;
CREATE TABLE [identity].[MfaRecoveryCodes] (
    [AccountId] int NOT NULL,
    [CodeHash] char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [CreatedAtUtc] datetimeoffset(7) NOT NULL,
    [ConsumedAtUtc] datetimeoffset(7) NULL,
    CONSTRAINT [PK_MfaRecoveryCodes] PRIMARY KEY ([AccountId], [CodeHash]),
    CONSTRAINT [CK_MfaRecoveryCodes_ConsumedAt] CHECK ([ConsumedAtUtc] IS NULL OR [ConsumedAtUtc] >= [CreatedAtUtc]),
    CONSTRAINT [FK_MfaRecoveryCodes_Accounts_AccountId] FOREIGN KEY ([AccountId]) REFERENCES [identity].[Accounts] ([Id])
);

CREATE TABLE [identity].[TotpCredentials] (
    [AccountId] int NOT NULL,
    [ProtectedSecret] nvarchar(max) NOT NULL,
    [ConfirmedAtUtc] datetimeoffset(7) NOT NULL,
    CONSTRAINT [PK_TotpCredentials] PRIMARY KEY ([AccountId]),
    CONSTRAINT [FK_TotpCredentials_Accounts_AccountId] FOREIGN KEY ([AccountId]) REFERENCES [identity].[Accounts] ([Id])
);

INSERT INTO [controlplane].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261117174212_AddConfirmedMfaEnrollment', N'10.0.9');

COMMIT;
GO

