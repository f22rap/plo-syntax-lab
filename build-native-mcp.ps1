param([switch]$WithTests)
$ErrorActionPreference = 'Stop'
$ploCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $ploCompiler)) { throw '.NET Framework 4.8 (64-bit Windows) is required.' }
Push-Location -LiteralPath $PSScriptRoot
try {
    New-Item -ItemType Directory -Path dist -Force | Out-Null
    $ploReferences = @('System','System.Core','System.Xml','System.Xml.Linq','System.Runtime.Serialization','System.IO.Compression','System.IO.Compression.FileSystem','System.Drawing','System.Windows.Forms') | ForEach-Object { '/reference:' + $_ + '.dll' }
    $ploSources = @(Get-ChildItem -LiteralPath mcp\native -Filter '*.cs' | ForEach-Object { $_.FullName })
    $ploSources += @('src/EquityEngine.cs','src/EquityInput.cs','src/EquityJob.cs','src/EquityPlot.cs','src/EquityArchive.cs','src/SyntaxMatcher.cs','src/ResearchData.cs','src/ResearchRun.cs','src/ResearchFiles.cs','mcp/ResearchHost.cs')
    $ploResource = '/resource:mcp/native/ToolDefinitions.json,PloNativeMcp.ToolDefinitions.json'
    & $ploCompiler /nologo /langversion:5 /target:winexe /optimize+ /define:WINDOWS_GUI /main:PloNativeMcp.Program /out:dist\PloMcp.Connect.exe /win32manifest:src\app.manifest $ploResource @ploReferences @ploSources
    if ($LASTEXITCODE -ne 0) { throw 'Native MCP connection EXE build failed.' }
    if ($WithTests) {
        & $ploCompiler /nologo /langversion:5 /target:exe /optimize+ /define:WINDOWS_GUI /main:PloNativeMcpTests.Program /out:dist\PloMcp.Connect.Tests.exe $ploResource @ploReferences @ploSources tests\native-mcp\Tests.cs
        if ($LASTEXITCODE -ne 0) { throw 'Native MCP tests build failed.' }
        & .\dist\PloMcp.Connect.Tests.exe
        if ($LASTEXITCODE -ne 0) { throw 'Native MCP tests failed.' }
    }
    Write-Output 'Built dist\PloMcp.Connect.exe (Node.js is not required to build or run this preview).'
} finally { Pop-Location }
