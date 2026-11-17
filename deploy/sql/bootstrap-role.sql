-- Operator-reviewed setup: connect to the intended migrated global database as its security administrator.
-- Creates no login/user, stores no credentials, and is never executed by API startup or bootstrap itself.
SET XACT_ABORT ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'identity.Accounts', N'U') IS NULL
    OR OBJECT_ID(N'identity.AccountClaims', N'U') IS NULL
    OR OBJECT_ID(N'controlplane.BootstrapCompletion', N'U') IS NULL
    THROW 51000, 'Apply the reviewed global migrations before configuring bootstrap permissions.', 1;

IF DATABASE_PRINCIPAL_ID(N'GeoSurveyBootstrap') IS NULL
    EXEC(N'CREATE ROLE [GeoSurveyBootstrap] AUTHORIZATION [dbo];');
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'GeoSurveyBootstrap' AND type = 'R')
    THROW 51001, 'The bootstrap principal name is already used by a non-role principal.', 1;

GRANT SELECT, INSERT ON OBJECT::[identity].[Accounts] TO [GeoSurveyBootstrap];
GRANT SELECT, INSERT ON OBJECT::[identity].[AccountClaims] TO [GeoSurveyBootstrap];
GRANT SELECT, INSERT ON OBJECT::[controlplane].[BootstrapCompletion] TO [GeoSurveyBootstrap];
DENY UPDATE, DELETE ON OBJECT::[controlplane].[BootstrapCompletion] TO [GeoSurveyBootstrap];
COMMIT;
