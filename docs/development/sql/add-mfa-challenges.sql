BEGIN TRANSACTION;
CREATE TABLE [identity].[MfaChallenges] (
    [Id] int NOT NULL IDENTITY,
    [AccountId] int NOT NULL,
    [Purpose] int NOT NULL,
    [TokenHash] char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [SecurityStamp] nvarchar(max) NOT NULL,
    [CreatedAtUtc] datetimeoffset(7) NOT NULL,
    [ExpiresAtUtc] datetimeoffset(7) NOT NULL,
    [ConsumedAtUtc] datetimeoffset(7) NULL,
    [RevokedAtUtc] datetimeoffset(7) NULL,
    CONSTRAINT [PK_MfaChallenges] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_MfaChallenges_Expiry] CHECK ([ExpiresAtUtc] > [CreatedAtUtc]),
    CONSTRAINT [CK_MfaChallenges_Purpose] CHECK ([Purpose] IN (1, 2)),
    CONSTRAINT [FK_MfaChallenges_Accounts_AccountId] FOREIGN KEY ([AccountId]) REFERENCES [identity].[Accounts] ([Id])
);

CREATE UNIQUE INDEX [IX_MfaChallenges_AccountId_Purpose] ON [identity].[MfaChallenges] ([AccountId], [Purpose]) WHERE [ConsumedAtUtc] IS NULL AND [RevokedAtUtc] IS NULL;

CREATE UNIQUE INDEX [IX_MfaChallenges_TokenHash] ON [identity].[MfaChallenges] ([TokenHash]);

INSERT INTO [controlplane].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261117102009_AddMfaChallenges', N'10.0.9');

COMMIT;
GO

