<#
.SYNOPSIS
    Removes Office Time Tracker for the current user.

.DESCRIPTION
    Removes the application, its shortcuts and its Claude Desktop registration.

    Your timesheet database is KEPT by default. It is the only copy of your hours and
    nothing else holds it — deleting it silently during an uninstall would be
    unrecoverable. Pass -DeleteMyData if you genuinely want it gone.

.PARAMETER DeleteMyData
    Also delete the encrypted database and its key. Irreversible.

.EXAMPLE
    .\uninstall.ps1
    .\uninstall.ps1 -DeleteMyData
#>
[CmdletBinding()]
param([switch] $DeleteMyData)

$ErrorActionPreference = 'Stop'

$appName    = 'Office Time Tracker'
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\TimeTracker'
$dataDir    = Join-Path $env:LOCALAPPDATA 'TimeTracker'
$startMenu  = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\$appName.lnk"
$startup    = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Startup\$appName.lnk"

Write-Host "Removing $appName" -ForegroundColor Cyan

Get-Process TimeTracker -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host '  stopping the running widget'
    $_ | Stop-Process -Force
}
Start-Sleep -Seconds 2

foreach ($path in @($startMenu, $startup)) {
    if (Test-Path $path) { Remove-Item $path -Force; Write-Host "  removed $(Split-Path $path -Leaf)" }
}

if (Test-Path $installDir) {
    Remove-Item $installDir -Recurse -Force
    Write-Host '  removed the application'
}

$claudeConfig = Join-Path $env:APPDATA 'Claude\claude_desktop_config.json'
if (Test-Path $claudeConfig) {
    $config = Get-Content $claudeConfig -Raw | ConvertFrom-Json
    if ($config.PSObject.Properties['mcpServers'] -and
        $config.mcpServers.PSObject.Properties['timetracker']) {
        $config.mcpServers.PSObject.Properties.Remove('timetracker')
        $config | ConvertTo-Json -Depth 10 | Set-Content $claudeConfig -Encoding utf8
        Write-Host '  unregistered the MCP server from Claude Desktop'
    }
}

if ($DeleteMyData) {
    if (Test-Path $dataDir) {
        Remove-Item $dataDir -Recurse -Force
        Write-Host '  DELETED your timesheet database' -ForegroundColor Yellow
    }
} elseif (Test-Path $dataDir) {
    Write-Host ''
    Write-Host "Your timesheet data is still at $dataDir" -ForegroundColor Green
    Write-Host 'Reinstalling will pick up exactly where you left off.'
    Write-Host 'To remove it too, re-run with -DeleteMyData.'
}

Write-Host ''
Write-Host 'Done.' -ForegroundColor Green
