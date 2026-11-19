[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$DemoRoot, [switch]$RemoveOwnedVolumes)
$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    $resolvedRoot = (Resolve-Path -LiteralPath $DemoRoot).Path
    $settings = Get-Content -LiteralPath (Join-Path $resolvedRoot 'demo-settings.json') -Raw | ConvertFrom-Json
    if ([System.IO.Path]::GetFullPath($settings.root) -ne [System.IO.Path]::GetFullPath($resolvedRoot) -or $settings.demoId -notmatch '^[a-f0-9]{32}$') { throw 'Demo ownership configuration does not match.' }
    $arguments = @('compose', '--env-file', (Join-Path $resolvedRoot 'compose.env'), '-f', 'docker-compose.phase1.yml', 'down')
    if ($RemoveOwnedVolumes) { $arguments += '--volumes' }
    & docker @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Demo stop failed.' }
    Write-Output 'Only this configured Compose project was stopped. Private configuration/certificates were preserved.'
}
finally { Pop-Location }
