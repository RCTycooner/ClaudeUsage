<#
.SYNOPSIS
    Installs ClaudeUsage to start at logon via Task Scheduler (instead of the HKCU Run key).

.DESCRIPTION
    - Publishes a Release build to %LOCALAPPDATA%\ClaudeUsage\app (a stable path, not a bin\ or worktree folder).
    - Removes the app's own HKCU Run entry and sets "startWithWindows": false in settings.json, so the app
      doesn't re-create the Run entry next time it starts.
    - Registers a per-user logon task that runs "ClaudeUsage.exe --hidden" after a short delay, with no
      execution time limit (the Task Scheduler default of 72h would kill the widget).

    A Windows service is not an option: services run in session 0 and can't show a window or tray icon.

.EXAMPLE
    .\scripts\install-startup-task.ps1
.EXAMPLE
    .\scripts\install-startup-task.ps1 -Uninstall
#>
[CmdletBinding()]
param(
    [switch]$Uninstall,
    [switch]$SkipPublish,
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'ClaudeUsage\app'),
    [int]$DelaySeconds = 30
)

$ErrorActionPreference = 'Stop'
$taskName = 'ClaudeUsage'
$exe = Join-Path $InstallDir 'ClaudeUsage.exe'
$settingsPath = Join-Path $env:LOCALAPPDATA 'ClaudeUsage\settings.json'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

function Stop-Widget {
    Get-Process ClaudeUsage -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

if ($Uninstall) {
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
    Write-Host "Removed scheduled task '$taskName'. Installed files left in $InstallDir."
    return
}

# 1. Publish to a stable location.
if (-not $SkipPublish) {
    $project = Join-Path $PSScriptRoot '..\src\ClaudeUsage\ClaudeUsage.csproj'
    Stop-Widget
    dotnet publish $project -c Release -o $InstallDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }
}
if (-not (Test-Path $exe)) { throw "Not found: $exe" }

# 2. Turn off the app's own Run-key startup so there's only one launcher.
Remove-ItemProperty -Path $runKey -Name 'ClaudeUsage' -ErrorAction SilentlyContinue
if (Test-Path $settingsPath) {
    $settings = Get-Content $settingsPath -Raw | ConvertFrom-Json
    if ($settings.PSObject.Properties['startWithWindows']) {
        $settings.startWithWindows = $false
    } else {
        $settings | Add-Member -NotePropertyName startWithWindows -NotePropertyValue $false
    }
    [IO.File]::WriteAllText($settingsPath, ($settings | ConvertTo-Json), (New-Object Text.UTF8Encoding $false))
}

# 3. Register the logon task for the current user.
$user = "$env:USERDOMAIN\$env:USERNAME"
$action = New-ScheduledTaskAction -Execute $exe -Argument '--hidden' -WorkingDirectory $InstallDir
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $user
$trigger.Delay = "PT${DelaySeconds}S"
$principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
$taskSettings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -MultipleInstances IgnoreNew `
    -StartWhenAvailable

Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal `
    -Settings $taskSettings -Description 'Starts the Claude usage tray widget at logon.' -Force | Out-Null

Write-Host "Registered scheduled task '$taskName' -> `"$exe`" --hidden (logon + ${DelaySeconds}s)."

# 4. Start it now so you don't have to log off to test.
Stop-Widget
Start-ScheduledTask -TaskName $taskName
Write-Host "Started. Check: Get-ScheduledTaskInfo -TaskName $taskName"
