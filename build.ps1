param([switch]$WithTests)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.8 (64-bit Windows) is required.' }
if (-not (Get-Command node -ErrorAction SilentlyContinue)) { throw 'Install Node.js 20 or newer to build the embedded HTML.' }
Push-Location -LiteralPath $PSScriptRoot
try {
    node src/make-offline.cjs
    if ($LASTEXITCODE -ne 0) { throw 'HTML build failed.' }
    $references = @('System','System.Core','System.Xml','System.IO.Compression','System.IO.Compression.FileSystem','System.Drawing','System.Windows.Forms','System.Web.Extensions','System.Runtime.Serialization') | ForEach-Object { '/reference:' + $_ + '.dll' }
    $presetResource = '/resource:src\default-presets.json,PloResearch.DefaultPresets.json'
    $resources = @('/resource:src\compare-runtime.ps1,compare-runtime.ps1','/resource:src\SyntaxMatcher.cs,SyntaxMatcher.cs','/resource:dist\PLO-Syntax-Lab.html,Lab.html','/resource:dist\defaults.html,Defaults.html',$presetResource)
    $sources = @(Get-ChildItem -LiteralPath src -Filter '*.cs' | ForEach-Object { $_.FullName })
    & $compiler /nologo /target:winexe /optimize+ /out:dist\FlopCommandApp.exe /win32manifest:src\app.manifest @references @resources @sources
    if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
    $coreSources = @('src\Core.cs','src\CompareCommands.cs','src\Execution.cs','src\History.cs','src\LabSelection.cs','src\SyntaxMatcher.cs','mcp\NativeHost.cs')
    & $compiler /nologo /target:exe /optimize+ /out:dist\PloMcp.Native.exe @references @resources @coreSources
    if ($LASTEXITCODE -ne 0) { throw 'MCP native host build failed.' }
    $researchSources = @('src\EquityEngine.cs','src\EquityInput.cs','src\EquityJob.cs','src\EquityPlot.cs','src\EquityArchive.cs','src\SyntaxMatcher.cs','src\ResearchData.cs','src\ResearchRecipes.cs','src\ResearchGeneration.cs','src\ResearchRun.cs','src\ResearchFiles.cs','mcp\ResearchHost.cs')
    & $compiler /nologo /target:exe /optimize+ /out:dist\PloResearch.Native.exe $presetResource @references @researchSources
    if ($LASTEXITCODE -ne 0) { throw 'Research MCP host build failed.' }
    if ($WithTests) {
        & $compiler /nologo /target:exe /optimize+ /main:PloMcpResearchTests.Program /out:dist\PloResearch.Tests.exe $presetResource @references @researchSources tests\research\BridgeTests.cs
        if ($LASTEXITCODE -ne 0) { throw 'Research bridge test build failed.' }
        $testSources = @(Get-ChildItem -LiteralPath tests -Filter '*.cs' | ForEach-Object { $_.FullName })
        & $compiler /nologo /target:winexe /optimize+ /main:FlopCommands.TestProgram /out:dist\FlopCommandApp.Tests.exe /win32manifest:src\app.manifest @references @resources @sources @testSources
        if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
    }
    Write-Output 'Built dist\FlopCommandApp.exe and dist\PLO-Syntax-Lab.html'
} finally { Pop-Location }
