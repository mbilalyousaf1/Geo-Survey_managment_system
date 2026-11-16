# Requires Docker with Linux containers. Run after restore/build.
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$containerName = 'geosurvey-tests-' + [Guid]::NewGuid().ToString('N')
$previousPassword = $env:MSSQL_SA_PASSWORD
$previousConnection = $env:GEOSURVEY_TEST_SQLSERVER
$testExitCode = 1
$containerStarted = $false
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    & docker info --format '{{.OSType}}'
    if ($LASTEXITCODE -ne 0) { throw 'Docker Linux engine must be running.' }

    # Pass the password by environment variable name; never include its value in arguments/output.
    $passwordBytes = New-Object byte[] 24
    $random = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $random.GetBytes($passwordBytes) } finally { $random.Dispose() }
    $env:MSSQL_SA_PASSWORD = 'Gs!9' + [BitConverter]::ToString($passwordBytes).Replace('-', '')
    & docker run --detach --rm --name $containerName --publish '127.0.0.1::1433' --env ACCEPT_EULA=Y --env MSSQL_PID=Developer --env MSSQL_SA_PASSWORD mcr.microsoft.com/mssql/server:2022-latest
    if ($LASTEXITCODE -ne 0) { throw 'Could not start isolated test SQL Server.' }
    $containerStarted = $true
    $binding = & docker port $containerName 1433/tcp
    if ($LASTEXITCODE -ne 0 -or $binding -notmatch '^127\.0\.0\.1:(\d+)$') { throw 'Could not resolve isolated SQL Server port.' }
    $env:GEOSURVEY_TEST_SQLSERVER = "Server=127.0.0.1,$($Matches[1]);User ID=sa;Password=$env:MSSQL_SA_PASSWORD;Encrypt=True;TrustServerCertificate=True;Pooling=False"
    & dotnet test GeoSurvey.sln --configuration Release --no-build --no-restore
    $testExitCode = $LASTEXITCODE
}
finally {
    if ($containerStarted) { & docker rm --force $containerName | Out-Null }
    $env:MSSQL_SA_PASSWORD = $previousPassword
    $env:GEOSURVEY_TEST_SQLSERVER = $previousConnection
    Pop-Location
}
exit $testExitCode
