param([ValidateRange(1,20)][int]$Repeats = 3)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.8 (64-bit Windows) is required.' }
Push-Location -LiteralPath $PSScriptRoot
try {
    New-Item -ItemType Directory -Force -Path test-results\equity | Out-Null
    & $compiler /nologo /target:exe /platform:x64 /optimize+ /out:test-results\equity\EquityBenchmark.exe /reference:System.dll /reference:System.Core.dll src\SyntaxMatcher.cs src\EquityEngine.cs src\EquityInput.cs src\EquityJob.cs src\EquityPlot.cs tests\equity\EquityBenchmark.cs
    if ($LASTEXITCODE -ne 0) { throw 'Equity benchmark compilation failed.' }
    & .\test-results\equity\EquityBenchmark.exe $Repeats | Tee-Object -FilePath test-results\equity\performance.csv
    if ($LASTEXITCODE -ne 0) { throw 'Equity benchmark failed.' }
} finally { Pop-Location }
