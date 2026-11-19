[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$DemoRoot)
$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
$previousCredentials = $env:GEOSURVEY_ACCEPTANCE_CREDENTIALS
try {
    $resolvedRoot = (Resolve-Path -LiteralPath $DemoRoot).Path
    $settings = Get-Content -LiteralPath (Join-Path $resolvedRoot 'demo-settings.json') -Raw | ConvertFrom-Json
    if (-not $settings.acceptance -or [System.IO.Path]::GetFullPath($settings.root) -ne [System.IO.Path]::GetFullPath($resolvedRoot)) { throw 'Use an owned synthetic acceptance environment.' }
    $env:GEOSURVEY_ACCEPTANCE_CREDENTIALS = Join-Path $resolvedRoot 'acceptance-credentials.json'
    if (-not (Test-Path -LiteralPath $env:GEOSURVEY_ACCEPTANCE_CREDENTIALS)) { throw 'Start the acceptance demo first.' }
    Push-Location src/Frontend/geosurvey-web
    try {
        $npmExecutable = if ($env:OS -eq 'Windows_NT') { 'npm.cmd' } else { 'npm' }
        & $npmExecutable run test:e2e
        if ($LASTEXITCODE -ne 0) { throw 'Phase 1 browser acceptance failed. Inspect the sanitized browser summary.' }
    }
    finally { Pop-Location }
}
finally { $env:GEOSURVEY_ACCEPTANCE_CREDENTIALS = $previousCredentials; Pop-Location }
