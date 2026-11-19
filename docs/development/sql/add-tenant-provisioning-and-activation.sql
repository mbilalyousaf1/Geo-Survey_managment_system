BEGIN TRANSACTION;
IF SCHEMA_ID(N'tenancy') IS NULL EXEC(N'CREATE SCHEMA [tenancy];');

IF SCHEMA_ID(N'audit') IS NULL EXEC(N'CREATE SCHEMA [audit];');

CREATE TABLE [tenancy].[AdministratorInvitations] (
    [Id] bigint NOT NULL IDENTITY,
    [PublicId] uniqueidentifier NOT NULL,
    [OrganizationId] int NOT NULL,
    [AccountId] int NOT NULL,
    [TokenHash] binary(32) NOT NULL,
    [CreatedAtUtc] datetimeoffset(7) NOT NULL,
    [ExpiresAtUtc] datetimeoffset(7) NOT NULL,
    [ConsumedAtUtc] datetimeoffset(7) NULL,
    [RevokedAtUtc] datetimeoffset(7) NULL,
    CONSTRAINT [PK_AdministratorInvitations] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_AdministratorInvitations_Expiry] CHECK ([ExpiresAtUtc] > [CreatedAtUtc]),
    CONSTRAINT [CK_AdministratorInvitations_Outcome] CHECK ([ConsumedAtUtc] IS NULL OR [RevokedAtUtc] IS NULL),
    CONSTRAINT [FK_AdministratorInvitations_Accounts_AccountId] FOREIGN KEY ([AccountId]) REFERENCES [identity].[Accounts] ([Id]),
    CONSTRAINT [FK_AdministratorInvitations_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [organization].[Organizations] ([Id])
);

CREATE TABLE [tenancy].[ProvisioningOutbox] (
    [Id] bigint NOT NULL IDENTITY,
    [PublicId] uniqueidentifier NOT NULL,
    [OrganizationId] int NOT NULL,
    [OrganizationPublicId] uniqueidentifier NOT NULL,
    [CreatedAtUtc] datetimeoffset(7) NOT NULL,
    [DispatchedAtUtc] datetimeoffset(7) NULL,
    [JobId] nvarchar(100) NULL,
    CONSTRAINT [PK_ProvisioningOutbox] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ProvisioningOutbox_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [organization].[Organizations] ([Id])
);

CREATE TABLE [audit].[TenantLifecycleAudit] (
    [Id] bigint NOT NULL IDENTITY,
    [PublicId] uniqueidentifier NOT NULL,
    [OrganizationPublicId] uniqueidentifier NOT NULL,
    [ActorPublicId] uniqueidentifier NULL,
    [EventType] nvarchar(80) NOT NULL,
    [Outcome] nvarchar(40) NOT NULL,
    [OccurredAtUtc] datetimeoffset(7) NOT NULL,
    CONSTRAINT [PK_TenantLifecycleAudit] PRIMARY KEY ([Id])
);

CREATE TABLE [tenancy].[TenantRegistry] (
    [OrganizationId] int NOT NULL,
    [RegionKey] nvarchar(50) NOT NULL,
    [DatabaseName] nvarchar(128) NOT NULL,
    [State] nvarchar(32) NOT NULL,
    [InitialAdministratorAccountId] int NOT NULL,
    [ProvisioningAttempts] int NOT NULL,
    [SchemaVersion] int NOT NULL,
    [FailureCode] nvarchar(80) NULL,
    [CreatedAtUtc] datetimeoffset(7) NOT NULL,
    [UpdatedAtUtc] datetimeoffset(7) NOT NULL,
    [DatabaseHealthyAtUtc] datetimeoffset(7) NULL,
    [RowVersion] rowversion NOT NULL,
    CONSTRAINT [PK_TenantRegistry] PRIMARY KEY ([OrganizationId]),
    CONSTRAINT [CK_TenantRegistry_Attempts] CHECK ([ProvisioningAttempts] >= 0),
    CONSTRAINT [CK_TenantRegistry_Ready] CHECK ([State] NOT IN (N'Ready',N'AwaitingAdministrator') OR ([SchemaVersion] = 1 AND [DatabaseHealthyAtUtc] IS NOT NULL)),
    CONSTRAINT [CK_TenantRegistry_State] CHECK ([State] IN (N'Pending',N'Provisioning',N'AwaitingAdministrator',N'Ready',N'Failed',N'Suspended')),
    CONSTRAINT [FK_TenantRegistry_Accounts_InitialAdministratorAccountId] FOREIGN KEY ([InitialAdministratorAccountId]) REFERENCES [identity].[Accounts] ([Id]),
    CONSTRAINT [FK_TenantRegistry_Organizations_OrganizationId] FOREIGN KEY ([OrganizationId]) REFERENCES [organization].[Organizations] ([Id])
);

CREATE UNIQUE INDEX [IX_AdministratorInvitations_AccountId] ON [tenancy].[AdministratorInvitations] ([AccountId]) WHERE [ConsumedAtUtc] IS NULL AND [RevokedAtUtc] IS NULL;

CREATE INDEX [IX_AdministratorInvitations_OrganizationId] ON [tenancy].[AdministratorInvitations] ([OrganizationId]);

CREATE UNIQUE INDEX [IX_AdministratorInvitations_PublicId] ON [tenancy].[AdministratorInvitations] ([PublicId]);

CREATE UNIQUE INDEX [IX_AdministratorInvitations_TokenHash] ON [tenancy].[AdministratorInvitations] ([TokenHash]);

CREATE INDEX [IX_ProvisioningOutbox_DispatchedAtUtc] ON [tenancy].[ProvisioningOutbox] ([DispatchedAtUtc]);

CREATE INDEX [IX_ProvisioningOutbox_OrganizationId] ON [tenancy].[ProvisioningOutbox] ([OrganizationId]);

CREATE UNIQUE INDEX [IX_ProvisioningOutbox_PublicId] ON [tenancy].[ProvisioningOutbox] ([PublicId]);

CREATE INDEX [IX_TenantLifecycleAudit_OrganizationPublicId_OccurredAtUtc] ON [audit].[TenantLifecycleAudit] ([OrganizationPublicId], [OccurredAtUtc]);

CREATE UNIQUE INDEX [IX_TenantLifecycleAudit_PublicId] ON [audit].[TenantLifecycleAudit] ([PublicId]);

CREATE UNIQUE INDEX [IX_TenantRegistry_DatabaseName] ON [tenancy].[TenantRegistry] ([DatabaseName]);

CREATE INDEX [IX_TenantRegistry_InitialAdministratorAccountId] ON [tenancy].[TenantRegistry] ([InitialAdministratorAccountId]);

INSERT INTO [controlplane].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261118092155_AddTenantProvisioningAndActivation', N'10.0.9');

COMMIT;
GO

