[CmdletBinding()]
param(
    [string]$DemoRoot,
    [int]$HttpsPort = 5443,
    [int]$SqlPort = 14333,
    [switch]$Acceptance,
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    if (-not $DemoRoot) { $DemoRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('GeoSurvey-Phase1-' + [Guid]::NewGuid().ToString('N')) }
    $DemoRoot = [System.IO.Path]::GetFullPath($DemoRoot)
    $settingsPath = Join-Path $DemoRoot 'demo-settings.json'
    if (-not (Test-Path -LiteralPath $settingsPath)) {
        $prepareArguments = @('src/Backend/GeoSurvey.LocalDemo/bin/Release/net10.0/GeoSurvey.LocalDemo.dll', 'prepare', $DemoRoot, $HttpsPort, $SqlPort)
        if ($Acceptance) { $prepareArguments += 'acceptance' }
        & dotnet @prepareArguments
        if ($LASTEXITCODE -ne 0) { throw 'Private demo configuration generation failed.' }
    }
    $envFile = Join-Path $DemoRoot 'compose.env'
    if ($env:OS -ne 'Windows_NT' -and -not ((Get-Content -LiteralPath $envFile) -match '^DEMO_UID=')) {
        $demoUserId = & id -u
        if ($LASTEXITCODE -ne 0 -or $demoUserId -notmatch '^\d+$') { throw 'Cannot resolve the local demo file owner.' }
        Add-Content -LiteralPath $envFile -Value "DEMO_UID='$demoUserId'"
    }
    & docker compose --env-file $envFile -f docker-compose.phase1.yml up --detach sql
    if ($LASTEXITCODE -ne 0) { throw 'Demo SQL startup failed.' }
    & dotnet src/Backend/GeoSurvey.LocalDemo/bin/Release/net10.0/GeoSurvey.LocalDemo.dll initialize $DemoRoot
    if ($LASTEXITCODE -ne 0) { throw 'Demo database initialization failed.' }
    $upArguments = @('compose', '--env-file', $envFile, '-f', 'docker-compose.phase1.yml', 'up', '--detach')
    if (-not $NoBuild) { $upArguments += '--build' }
    $upArguments += @('api', 'worker', 'web')
    & docker @upArguments
    if ($LASTEXITCODE -ne 0) { throw 'Demo application startup failed.' }
    $ready = $false
    for ($attempt = 0; $attempt -lt 45; $attempt++) {
        $health = & docker compose --env-file $envFile -f docker-compose.phase1.yml exec -T web curl --fail --silent --max-time 5 --cacert /run/geosurvey/https.crt https://localhost:8443/health/live 2>$null
        if ($LASTEXITCODE -eq 0 -and $health -eq 'Healthy') { $ready = $true; break }
        Start-Sleep -Seconds 2
    }
    if (-not $ready) { throw 'Demo HTTPS/API readiness timed out. Inspect the owned stack status before running acceptance.' }
    if ($Acceptance -and -not (Test-Path -LiteralPath (Join-Path $DemoRoot 'acceptance-credentials.json'))) {
        & dotnet src/Backend/GeoSurvey.LocalDemo/bin/Release/net10.0/GeoSurvey.LocalDemo.dll seed-acceptance $DemoRoot
        if ($LASTEXITCODE -ne 0) { throw 'Synthetic acceptance bootstrap failed.' }
    }
    $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
    Write-Output "Demo URL: https://localhost:$($settings.httpsPort)"
    Write-Output "Private demo directory: $DemoRoot"
    if (-not $Acceptance) { Write-Output 'Next run: dotnet src/Backend/GeoSurvey.LocalDemo/bin/Release/net10.0/GeoSurvey.LocalDemo.dll bootstrap <private-demo-directory>' }
    Write-Output 'The generated local HTTPS certificate is for this demo only; no system trust store was changed.'
}
finally { Pop-Location }
