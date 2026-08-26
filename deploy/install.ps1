<#
.SYNOPSIS
    Installs Office Time Tracker for the current user.

.DESCRIPTION
    Per-user install into %LOCALAPPDATA%\Programs\TimeTracker. Deliberately not
    Program Files: that needs administrator rights, and a tool this size should not
    require IT involvement to trial. It also keeps the application beside the data it
    owns, which already lives under %LOCALAPPDATA%.

    Your timesheet database is NOT touched by install, upgrade or uninstall.

.PARAMETER RunAtStartup
    Add a Startup shortcut so the widget is there when you log in.

.PARAMETER Source
    Folder holding the published exes. Defaults to the dist folder beside this script.

.EXAMPLE
    .\install.ps1 -RunAtStartup
#>
[CmdletBinding()]
param(
    [switch] $RunAtStartup,
    [string] $Source = (Join-Path (Split-Path $PSScriptRoot -Parent) 'dist')
)

$ErrorActionPreference = 'Stop'

$appName    = 'Office Time Tracker'
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\TimeTracker'
$dataDir    = Join-Path $env:LOCALAPPDATA 'TimeTracker'
$startMenu  = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
$startup    = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Startup'

$appExe = Join-Path $Source 'app\TimeTracker.exe'
$mcpExe = Join-Path $Source 'mcp\TimeTracker.Mcp.exe'

foreach ($f in @($appExe, $mcpExe)) {
    if (-not (Test-Path $f)) {
        throw "Not found: $f`nRun deploy\publish.ps1 first."
    }
}

Write-Host "Installing $appName" -ForegroundColor Cyan
Write-Host "  to $installDir"

# Stop a running copy, otherwise the file copy fails with a lock nobody expects.
$running = Get-Process TimeTracker -ErrorAction SilentlyContinue
if ($running) {
    Write-Host '  stopping the running widget'
    $running | Stop-Process -Force
    Start-Sleep -Seconds 2
}

New-Item -ItemType Directory -Force -Path $installDir | Out-Null
Copy-Item $appExe -Destination $installDir -Force
Copy-Item $mcpExe -Destination $installDir -Force

$installedApp = Join-Path $installDir 'TimeTracker.exe'
$installedMcp = Join-Path $installDir 'TimeTracker.Mcp.exe'

function New-Shortcut {
    param([string] $Path, [string] $Target, [string] $Description)
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($Path)
    $link.TargetPath       = $Target
    $link.WorkingDirectory = Split-Path $Target -Parent
    $link.Description      = $Description
    $link.Save()
    [Runtime.InteropServices.Marshal]::ReleaseComObject($shell) | Out-Null
}

New-Shortcut -Path (Join-Path $startMenu "$appName.lnk") `
             -Target $installedApp -Description 'Track working hours and activities'
Write-Host '  Start Menu shortcut created'

$startupLink = Join-Path $startup "$appName.lnk"
if ($RunAtStartup) {
    New-Shortcut -Path $startupLink -Target $installedApp -Description 'Start with Windows'
    Write-Host '  will start with Windows'
} elseif (Test-Path $startupLink) {
    Remove-Item $startupLink -Force
    Write-Host '  removed from startup (pass -RunAtStartup to keep it)'
}

# Register the MCP server with Claude Desktop if it is present. Merged into the existing
# config rather than overwritten — clobbering someone's other MCP servers would be rude.
$claudeConfig = Join-Path $env:APPDATA 'Claude\claude_desktop_config.json'
if (Test-Path (Split-Path $claudeConfig -Parent)) {
    $config = if (Test-Path $claudeConfig) {
        Get-Content $claudeConfig -Raw | ConvertFrom-Json
    } else {
        [pscustomobject]@{}
    }

    if (-not $config.PSObject.Properties['mcpServers']) {
        $config | Add-Member -NotePropertyName mcpServers -NotePropertyValue ([pscustomobject]@{})
    }

    $entry = [pscustomobject]@{ command = $installedMcp; args = @() }
    if ($config.mcpServers.PSObject.Properties['timetracker']) {
        $config.mcpServers.timetracker = $entry
    } else {
        $config.mcpServers | Add-Member -NotePropertyName timetracker -NotePropertyValue $entry
    }

    $config | ConvertTo-Json -Depth 10 | Set-Content $claudeConfig -Encoding utf8
    Write-Host '  registered the MCP server with Claude Desktop (restart Claude to pick it up)'
} else {
    Write-Host '  Claude Desktop not found - MCP server installed but not registered'
    Write-Host "     point any MCP client at: $installedMcp"
}

Write-Host ''
Write-Host "Installed. Your data stays at $dataDir and was not touched." -ForegroundColor Green
Write-Host "Start it from the Start Menu, or run: $installedApp"
