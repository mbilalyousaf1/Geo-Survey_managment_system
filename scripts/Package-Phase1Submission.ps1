[CmdletBinding()]
param([string]$PrivateDemoRoot)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    $repository = (Get-Location).Path
    $outputDirectory = Join-Path $repository 'artifacts/phase-1'
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
    $paths = @(& git -c core.quotepath=false ls-files --cached --others --exclude-standard) | Sort-Object -Unique
    if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate submission source.' }
    $secretValues = @()
    if ($PrivateDemoRoot) {
        $settings = Get-Content -LiteralPath (Join-Path $PrivateDemoRoot 'demo-settings.json') -Raw | ConvertFrom-Json
        $secretValues += $settings.PSObject.Properties | Where-Object { $_.Name -match 'Password$' } | ForEach-Object { [string]$_.Value }
        $credentialsPath = Join-Path $PrivateDemoRoot 'acceptance-credentials.json'
        if (Test-Path -LiteralPath $credentialsPath) {
            $credentials = Get-Content -LiteralPath $credentialsPath -Raw | ConvertFrom-Json
            $secretValues += @([string]$credentials.password, [string]$credentials.totpSecret)
        }
    }
    $selected = @()
    foreach ($relative in $paths) {
        if ($relative -match '(^|/)(\.git|node_modules|bin|obj|dist|\.angular|TestResults|test-results|playwright-report|artifacts)(/|$)' -or $relative -match '\.(pfx|p12|pem|key|zip)$' -or $relative -match '(^|/)\.env($|\.)' -or $relative -match '(secrets|demo-settings|acceptance-credentials)\.json$') { continue }
        $absolute = [System.IO.Path]::GetFullPath((Join-Path $repository $relative))
        if (-not $absolute.StartsWith($repository + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Submission path leaves the workspace.' }
        if (-not (Test-Path -LiteralPath $absolute -PathType Leaf)) { continue }
        if ((Get-Item -LiteralPath $absolute).Length -lt 20000000) {
            $contents = [System.IO.File]::ReadAllText($absolute)
            foreach ($secret in $secretValues) {
                if ($secret.Length -ge 16 -and $contents.Contains($secret)) { throw "Private generated secret detected in submission file: $relative" }
            }
        }
        $selected += [PSCustomObject]@{ path = $relative; sha256 = (Get-FileHash -LiteralPath $absolute -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
    $manifestPath = Join-Path $outputDirectory 'source-manifest.json'
    [System.IO.File]::WriteAllText($manifestPath, (ConvertTo-Json -InputObject @($selected) -Depth 4), (New-Object System.Text.UTF8Encoding($false)))
    $archivePath = Join-Path $outputDirectory ('GeoSurvey-Phase1-Submission-' + (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss') + '.zip')
    $archive = [System.IO.Compression.ZipFile]::Open($archivePath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($item in $selected) { [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $repository $item.path), $item.path.Replace('\','/'), [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null }
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $manifestPath, 'submission-source-manifest.json') | Out-Null
    }
    finally { $archive.Dispose() }
    Write-Output "Created secret-scanned submission archive with $($selected.Count) source/documentation files: $archivePath"
}
finally { Pop-Location }
