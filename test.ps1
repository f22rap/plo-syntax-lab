param([switch]$Native)
$ErrorActionPreference = 'Stop'
Push-Location -LiteralPath $PSScriptRoot
try {
    foreach ($test in @('tests.cjs','symbolic-tests.cjs','straight-draw-tests.cjs','pocket-tests.cjs','defaults-test.cjs')) {
        node (Join-Path 'tests' $test)
        if ($LASTEXITCODE -ne 0) { throw ('Failed: ' + $test) }
    }
    if ($Native) {
        & .\build.ps1 -WithTests
        $process = Start-Process -FilePath (Join-Path $PSScriptRoot 'dist\FlopCommandApp.Tests.exe') -WorkingDirectory $PSScriptRoot -WindowStyle Hidden -Wait -PassThru
        if ($process.ExitCode -ne 0) { throw 'Native tests failed. See test-results\native\test-error.txt.' }
        Get-Content -LiteralPath 'test-results\native\modes-test-passed.txt'
    }
} finally { Pop-Location }
