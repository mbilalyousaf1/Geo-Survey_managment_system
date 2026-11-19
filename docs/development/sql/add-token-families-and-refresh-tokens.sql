BEGIN TRANSACTION;
CREATE TABLE [identity].[TokenFamilies] (
    [Id] bigint NOT NULL IDENTITY,
    [PublicId] uniqueidentifier NOT NULL,
    [AccountId] int NOT NULL,
    [SecurityStamp] nvarchar(max) NOT NULL,
    [CreatedAtUtc] datetimeoffset(7) NOT NULL,
    [ExpiresAtUtc] datetimeoffset(7) NOT NULL,
    [RevokedAtUtc] datetimeoffset(7) NULL,
    CONSTRAINT [PK_TokenFamilies] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_TokenFamilies_Expiry] CHECK ([ExpiresAtUtc] > [CreatedAtUtc]),
    CONSTRAINT [CK_TokenFamilies_PublicId] CHECK ([PublicId] <> '00000000-0000-0000-0000-000000000000'),
    CONSTRAINT [CK_TokenFamilies_RevokedAt] CHECK ([RevokedAtUtc] IS NULL OR [RevokedAtUtc] >= [CreatedAtUtc]),
    CONSTRAINT [CK_TokenFamilies_SecurityStamp] CHECK (LEN([SecurityStamp]) > 0),
    CONSTRAINT [CK_TokenFamilies_Utc] CHECK (DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [ExpiresAtUtc]) = 0 AND ([RevokedAtUtc] IS NULL OR DATEPART(TZOFFSET, [RevokedAtUtc]) = 0)),
    CONSTRAINT [FK_TokenFamilies_Accounts_AccountId] FOREIGN KEY ([AccountId]) REFERENCES [identity].[Accounts] ([Id])
);

CREATE TABLE [identity].[RefreshTokens] (
    [Id] bigint NOT NULL IDENTITY,
    [TokenFamilyId] bigint NOT NULL,
    [TokenHash] char(64) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [CreatedAtUtc] datetimeoffset(7) NOT NULL,
    [ExpiresAtUtc] datetimeoffset(7) NOT NULL,
    [ConsumedAtUtc] datetimeoffset(7) NULL,
    CONSTRAINT [PK_RefreshTokens] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_RefreshTokens_ConsumedAt] CHECK ([ConsumedAtUtc] IS NULL OR [ConsumedAtUtc] >= [CreatedAtUtc]),
    CONSTRAINT [CK_RefreshTokens_Expiry] CHECK ([ExpiresAtUtc] > [CreatedAtUtc]),
    CONSTRAINT [CK_RefreshTokens_TokenHash] CHECK (LEN([TokenHash]) = 64 AND [TokenHash] COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9A-F]%'),
    CONSTRAINT [CK_RefreshTokens_Utc] CHECK (DATEPART(TZOFFSET, [CreatedAtUtc]) = 0 AND DATEPART(TZOFFSET, [ExpiresAtUtc]) = 0 AND ([ConsumedAtUtc] IS NULL OR DATEPART(TZOFFSET, [ConsumedAtUtc]) = 0)),
    CONSTRAINT [FK_RefreshTokens_TokenFamilies_TokenFamilyId] FOREIGN KEY ([TokenFamilyId]) REFERENCES [identity].[TokenFamilies] ([Id])
);

CREATE UNIQUE INDEX [IX_RefreshTokens_TokenFamilyId] ON [identity].[RefreshTokens] ([TokenFamilyId]) WHERE [ConsumedAtUtc] IS NULL;

CREATE UNIQUE INDEX [IX_RefreshTokens_TokenHash] ON [identity].[RefreshTokens] ([TokenHash]);

CREATE INDEX [IX_TokenFamilies_AccountId] ON [identity].[TokenFamilies] ([AccountId]);

CREATE UNIQUE INDEX [IX_TokenFamilies_PublicId] ON [identity].[TokenFamilies] ([PublicId]);

INSERT INTO [controlplane].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261118070921_AddTokenFamiliesAndRefreshTokens', N'10.0.9');

COMMIT;
GO

