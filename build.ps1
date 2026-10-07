param([switch]$WithTests)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.8 (64-bit Windows) is required.' }
if (-not (Get-Command node -ErrorAction SilentlyContinue)) { throw 'Install Node.js 20 or newer to build the embedded HTML.' }
Push-Location -LiteralPath $PSScriptRoot
try {
    node src/make-offline.cjs
    if ($LASTEXITCODE -ne 0) { throw 'HTML build failed.' }
    $references = @('System','System.Core','System.Xml','System.IO.Compression','System.IO.Compression.FileSystem','System.Drawing','System.Windows.Forms','System.Web.Extensions') | ForEach-Object { '/reference:' + $_ + '.dll' }
    $resources = @('/resource:src\compare-runtime.ps1,compare-runtime.ps1','/resource:src\SyntaxMatcher.cs,SyntaxMatcher.cs','/resource:dist\PLO-Syntax-Lab.html,Lab.html','/resource:dist\defaults.html,Defaults.html')
    $sources = @(Get-ChildItem -LiteralPath src -Filter '*.cs' | ForEach-Object { $_.FullName })
    & $compiler /nologo /target:winexe /optimize+ /out:dist\FlopCommandApp.exe /win32manifest:src\app.manifest @references @resources @sources
    if ($LASTEXITCODE -ne 0) { throw 'Application build failed.' }
    if ($WithTests) {
        $testSources = @(Get-ChildItem -LiteralPath tests -Filter '*.cs' | ForEach-Object { $_.FullName })
        & $compiler /nologo /target:winexe /optimize+ /main:FlopCommands.TestProgram /out:dist\FlopCommandApp.Tests.exe /win32manifest:src\app.manifest @references @resources @sources @testSources
        if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
    }
    Write-Output 'Built dist\FlopCommandApp.exe and dist\PLO-Syntax-Lab.html'
} finally { Pop-Location }
