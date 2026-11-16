IF OBJECT_ID(N'[controlplane].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'controlplane') IS NULL EXEC(N'CREATE SCHEMA [controlplane];');
    CREATE TABLE [controlplane].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF SCHEMA_ID(N'identity') IS NULL EXEC(N'CREATE SCHEMA [identity];');

IF SCHEMA_ID(N'organization') IS NULL EXEC(N'CREATE SCHEMA [organization];');

CREATE TABLE [organization].[Organizations] (
    [Id] int NOT NULL IDENTITY,
    [PublicId] uniqueidentifier NOT NULL,
    [Code] nvarchar(50) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [DisplayName] nvarchar(200) NOT NULL,
    [CreatedAtUtc] datetimeoffset(7) NOT NULL,
    CONSTRAINT [PK_Organizations] PRIMARY KEY ([Id])
);

CREATE TABLE [identity].[Accounts] (
    [Id] int NOT NULL IDENTITY,
    [PublicId] uniqueidentifier NOT NULL,
    [AccountScope] int NOT NULL,
    [OrganizationId] int NULL,
    [UserName] nvarchar(32) NOT NULL,
    [NormalizedUserName] nvarchar(32) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [Email] nvarchar(254) NOT NULL,
    [NormalizedEmail] nvarchar(254) COLLATE Latin1_General_100_BIN2 NOT NULL,
    [EmailConfirmed] bit NOT NULL,
    [PasswordHash] nvarchar(max) NULL,
    [SecurityStamp] nvarchar(max) NULL,
    [ConcurrencyStamp] nvarchar(max) NULL,
    [PhoneNumber] nvarchar(256) NULL,
    [PhoneNumberConfirmed] bit NOT NULL,
    [TwoFactorEnabled] bit NOT NULL,
    [LockoutEnd] datetimeoffset NULL,
    [LockoutEnabled] bit NOT NULL,
    [AccessFailedCount] int NOT NULL,
    CONSTRAINT [PK_Accounts] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Accounts_ScopeOrganization] CHECK (([AccountScope] = 1 AND [OrganizationId] IS NULL) OR ([AccountScope] = 2 AND [OrganizationId] IS NOT NULL AND [OrganizationId] > 0)),
    CONSTRAINT [FK_Accounts_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [organization].[Organizations] ([Id])
);

CREATE TABLE [identity].[AccountClaims] (
    [Id] int NOT NULL IDENTITY,
    [UserId] int NOT NULL,
    [ClaimType] nvarchar(max) NULL,
    [ClaimValue] nvarchar(max) NULL,
    CONSTRAINT [PK_AccountClaims] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AccountClaims_Accounts_UserId] FOREIGN KEY ([UserId]) REFERENCES [identity].[Accounts] ([Id])
);

CREATE TABLE [identity].[AccountLogins] (
    [LoginProvider] nvarchar(128) NOT NULL,
    [ProviderKey] nvarchar(128) NOT NULL,
    [ProviderDisplayName] nvarchar(max) NULL,
    [UserId] int NOT NULL,
    CONSTRAINT [PK_AccountLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
    CONSTRAINT [FK_AccountLogins_Accounts_UserId] FOREIGN KEY ([UserId]) REFERENCES [identity].[Accounts] ([Id])
);

CREATE TABLE [identity].[AccountTokens] (
    [UserId] int NOT NULL,
    [LoginProvider] nvarchar(128) NOT NULL,
    [Name] nvarchar(128) NOT NULL,
    [Value] nvarchar(max) NULL,
    CONSTRAINT [PK_AccountTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
    CONSTRAINT [FK_AccountTokens_Accounts_UserId] FOREIGN KEY ([UserId]) REFERENCES [identity].[Accounts] ([Id])
);

CREATE INDEX [IX_AccountClaims_UserId] ON [identity].[AccountClaims] ([UserId]);

CREATE INDEX [IX_AccountLogins_UserId] ON [identity].[AccountLogins] ([UserId]);

CREATE UNIQUE INDEX [UX_Accounts_NormalizedUserName] ON [identity].[Accounts] ([NormalizedUserName]);

CREATE UNIQUE INDEX [UX_Accounts_OrganizationEmail] ON [identity].[Accounts] ([OrganizationId], [NormalizedEmail]) WHERE [AccountScope] = 2;

CREATE UNIQUE INDEX [UX_Accounts_PlatformEmail] ON [identity].[Accounts] ([NormalizedEmail]) WHERE [AccountScope] = 1;

CREATE UNIQUE INDEX [UX_Accounts_PublicId] ON [identity].[Accounts] ([PublicId]);

CREATE UNIQUE INDEX [IX_Organizations_Code] ON [organization].[Organizations] ([Code]);

CREATE UNIQUE INDEX [IX_Organizations_PublicId] ON [organization].[Organizations] ([PublicId]);

INSERT INTO [controlplane].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261116114419_InitialGlobalControlPlane', N'10.0.9');

COMMIT;
GO

