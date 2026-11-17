BEGIN TRANSACTION;
IF SCHEMA_ID(N'controlplane') IS NULL EXEC(N'CREATE SCHEMA [controlplane];');

CREATE TABLE [controlplane].[BootstrapCompletion] (
    [Id] int NOT NULL,
    [CompletedAtUtc] datetimeoffset(7) NOT NULL,
    CONSTRAINT [PK_BootstrapCompletion] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_BootstrapCompletion_Singleton] CHECK ([Id] = 1)
);

INSERT INTO [controlplane].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261117073638_AddBootstrapCompletion', N'10.0.9');

COMMIT;
GO

