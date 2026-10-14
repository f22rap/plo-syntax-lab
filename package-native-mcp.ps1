param([Parameter(Mandatory = $true)][string]$SourceCommit)
$ErrorActionPreference = 'Stop'
if ($SourceCommit -notmatch '^[a-f0-9]{40}$') { throw 'A full source commit SHA is required.' }
Push-Location -LiteralPath $PSScriptRoot
try {
    $ploPackageName = 'PLO-Syntax-Lab-NoNode'
    $ploPackageRoot = Join-Path $PSScriptRoot ('dist\' + $ploPackageName)
    # Build in a fresh directory; never include local configuration, CSVs or keys.
    if (Test-Path -LiteralPath $ploPackageRoot) { throw 'Package directory already exists. Use a clean build checkout.' }
    $ploFiles = [ordered]@{
        'dist/PloMcp.Connect.exe' = 'PloMcp.Connect.exe'
        'dist/FlopCommandApp.exe' = 'FlopCommandApp.exe'
        'dist/PLO-Syntax-Lab.html' = 'PLO-Syntax-Lab.html'
        'docs/native-mcp-install.txt' = 'README.txt'
        'docs/native-mcp-preview.md' = 'docs/native-mcp-preview.md'
        'docs/research-setup.md' = 'docs/research-setup.md'
        'docs/research-run.md' = 'docs/research-run.md'
        'docs/directory-import.md' = 'docs/directory-import.md'
        'docs/usage.txt' = 'docs/usage.txt'
    }
    foreach ($ploSource in $ploFiles.Keys) {
        if (-not (Test-Path -LiteralPath $ploSource -PathType Leaf)) { throw ('Missing package input: ' + $ploSource) }
    }
    New-Item -ItemType Directory -Path $ploPackageRoot | Out-Null
    $ploHashes = [ordered]@{}
    foreach ($ploSource in $ploFiles.Keys) {
        $ploRelative = $ploFiles[$ploSource]
        $ploTarget = Join-Path $ploPackageRoot $ploRelative
        New-Item -ItemType Directory -Path (Split-Path -Parent $ploTarget) -Force | Out-Null
        Copy-Item -LiteralPath $ploSource -Destination $ploTarget
        $ploHashes[$ploRelative] = (Get-FileHash -LiteralPath $ploTarget -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    $ploBuild = [ordered]@{
        source_commit = $SourceCommit
        native_version = '0.2.0-preview.1'
        tool_count = 17
        node_required = $false
        tunnel_client_included = $false
        files = $ploHashes
    }
    $ploBuild | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $ploPackageRoot 'build-info.json') -Encoding UTF8
    $ploZip = Join-Path $PSScriptRoot ('dist\' + $ploPackageName + '.zip')
    if (Test-Path -LiteralPath $ploZip) { throw 'Package ZIP already exists. Use a clean build checkout.' }
    Compress-Archive -Path (Join-Path $ploPackageRoot '*') -DestinationPath $ploZip
    $ploZipHash = (Get-FileHash -LiteralPath $ploZip -Algorithm SHA256).Hash.ToLowerInvariant()
    ($ploZipHash + '  ' + $ploPackageName + '.zip') | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'dist\NONODE-SHA256SUMS.txt') -Encoding ASCII
    Write-Output ('Packaged ' + $ploZip + ' (17 tools; Node.js not included or required).')
} finally { Pop-Location }
