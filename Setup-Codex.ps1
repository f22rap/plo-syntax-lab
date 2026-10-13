param([string[]]$InputRoots = @(), [switch]$CheckOnly)
$ErrorActionPreference = 'Stop'
try {
    $node = Get-Command node -ErrorAction SilentlyContinue
    if (-not $node) { throw 'Node.js 20+ is required. Install Node.js and run this setup again.' }
    foreach ($file in @('dist\PloMcp.Native.exe','dist\PloResearch.Native.exe','mcp\server.mjs','node_modules\@modelcontextprotocol\client\package.json')) {
        if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot $file))) { throw "Package file missing: $file. Extract the entire ZIP first." }
    }
    if (-not $CheckOnly -and -not (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'mcp.local.json')) -and $InputRoots.Count -eq 0) {
        Add-Type -AssemblyName System.Windows.Forms
        $picker = New-Object System.Windows.Forms.FolderBrowserDialog
        $picker.Description = 'Select the folder containing your action and opponent CSV files.'
        $picker.ShowNewFolderButton = $false
        try {
            if ($picker.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { throw 'No CSV folder selected. Setup stopped.' }
            $InputRoots = @($picker.SelectedPath)
        } finally { $picker.Dispose() }
    }
    if (-not $CheckOnly) {
        & $node.Source (Join-Path $PSScriptRoot 'mcp\codex-setup.mjs') @InputRoots
        if ($LASTEXITCODE -ne 0) { throw 'Configuration preparation failed.' }
    }
    & $node.Source (Join-Path $PSScriptRoot 'mcp\check-connection.mjs') (Join-Path $PSScriptRoot 'mcp.local.json')
    if ($LASTEXITCODE -ne 0) { throw 'MCP connection check failed. Codex settings were not changed.' }
    if ($CheckOnly) { Write-Host 'MCP connection check passed.'; exit 0 }
    $codex = Get-Command codex -ErrorAction SilentlyContinue
    if ($codex) {
        $oldPreference = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        $existingText = & $codex.Source mcp get plo-syntax-lab-windows --json 2>$null
        $existingCode = $LASTEXITCODE
        $ErrorActionPreference = $oldPreference
        if ($existingCode -eq 0) {
            Write-Host 'This server name is already registered. Its settings have been preserved.'
            Write-Host 'Compare the existing command and args with codex-mcp.toml; update only this server in Codex settings if necessary.'
        } else {
            & $codex.Source mcp add plo-syntax-lab-windows -- $node.Source (Join-Path $PSScriptRoot 'mcp\server.mjs') --config (Join-Path $PSScriptRoot 'mcp.local.json')
            if ($LASTEXITCODE -ne 0) { throw 'Codex registration failed. Use the generated codex-mcp.toml or the MCP settings screen.' }
            & $codex.Source mcp get plo-syntax-lab-windows --json
            if ($LASTEXITCODE -ne 0) { throw 'Could not verify the Codex registration.' }
            Write-Host 'Registered with Codex. Restart its MCP connection, then check /mcp.'
        }
    } else {
        Write-Host 'Codex CLI was not found. In the desktop app, open Settings > MCP servers > Add server > STDIO.'
        Write-Host 'Use command and args from codex-mcp.toml, or merge that one table into your Codex config.toml.'
    }
    Write-Host 'For larger catalogs, apply startup_timeout_sec=60 and tool_timeout_sec=3600 from codex-mcp.toml.'
    Write-Host 'Open research-prompt.txt and send its prompt to Codex. Actual AI-host execution still needs this final check.'
} catch {
    Write-Error $_ -ErrorAction Continue
    exit 1
}
