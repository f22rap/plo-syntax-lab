$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.8 (64-bit Windows) is required.' }
Push-Location -LiteralPath $PSScriptRoot
try {
    New-Item -ItemType Directory -Force -Path test-results\equity | Out-Null
    & $compiler /nologo /target:exe /optimize+ /out:test-results\equity\EquityTests.exe /reference:System.dll /reference:System.Core.dll /reference:System.Xml.dll /reference:System.Runtime.Serialization.dll src\SyntaxMatcher.cs src\EquityEngine.cs src\EquityInput.cs src\EquityJob.cs src\EquityPlot.cs src\EquityArchive.cs src\ResearchData.cs src\ResearchRun.cs src\ResearchFiles.cs tests\equity\EquityEngineTests.cs tests\equity\EquityInputTests.cs tests\equity\EquityPlotTests.cs tests\equity\EquityArchiveTests.cs tests\equity\ResearchTests.cs tests\equity\ResearchRunTests.cs
    if ($LASTEXITCODE -ne 0) { throw 'Equity engine test compilation failed.' }
    & .\test-results\equity\EquityTests.exe
    if ($LASTEXITCODE -ne 0) { throw 'Equity engine tests failed.' }
} finally { Pop-Location }
