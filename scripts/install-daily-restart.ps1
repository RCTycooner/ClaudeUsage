<#
.SYNOPSIS
    Registers a scheduled task that restarts ClaudeUsage once a day.

.DESCRIPTION
    The widget runs for days. Meanwhile Windows Update can service the .NET runtime it started from and remove
    that runtime's folder. After that, the first time the app needs an assembly it hadn't loaded yet, it fails;
    on 3 Nov this crashed the weekly insights mail. A daily restart keeps the process on the installed runtime.

    The task force-stops ClaudeUsage.exe and starts it again. It starts it through the 'ClaudeUsage' logon task
    when that exists (see install-startup-task.ps1), so it runs with that task's settings. Otherwise it runs
    "<InstallDir>\ClaudeUsage.exe --hidden" directly. If the widget wasn't running, the task starts it.
    The app writes its files atomically, so a forced stop doesn't corrupt them. Windows can leave a stale tray icon
    behind; it disappears when you move the mouse over it.

    A missed run (PC asleep) runs as soon as the PC is available again.

.EXAMPLE
    .\scripts\install-daily-restart.ps1
.EXAMPLE
    .\scripts\install-daily-restart.ps1 -At 03:30
.EXAMPLE
    .\scripts\install-daily-restart.ps1 -Uninstall
#>
[CmdletBinding()]
param(
    [switch]$Uninstall,
    [string]$At = '05:00',
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'ClaudeUsage\app')
)

$ErrorActionPreference = 'Stop'
$taskName = 'ClaudeUsage daily restart'
$logonTaskName = 'ClaudeUsage'
$exe = Join-Path $InstallDir 'ClaudeUsage.exe'

if ($Uninstall) {
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
    Write-Host "Removed scheduled task '$taskName'."
    return
}

if (-not (Test-Path $exe)) { throw "Not found: $exe (run install-startup-task.ps1 first, or pass -InstallDir)" }

# What the task runs. Passed as -EncodedCommand, so no script file has to stay at a fixed path.
$restart = @"
`$ErrorActionPreference = 'SilentlyContinue'
Get-Process -Name ClaudeUsage | Stop-Process -Force
Wait-Process -Name ClaudeUsage -Timeout 10
if (Get-ScheduledTask -TaskName '$logonTaskName') {
    # The logon task ignores a start while it still counts as running; wait until the stop has registered.
    for (`$i = 0; `$i -lt 20 -and (Get-ScheduledTask -TaskName '$logonTaskName').State -eq 'Running'; `$i++) {
        Start-Sleep -Milliseconds 500
    }
    Start-ScheduledTask -TaskName '$logonTaskName'
    Start-Sleep -Seconds 5
}
if (-not (Get-Process -Name ClaudeUsage)) {
    Start-Process -FilePath '$exe' -ArgumentList '--hidden' -WorkingDirectory '$InstallDir'
}
"@
$encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($restart))

# conhost --headless runs PowerShell without flashing a console window.
$user = "$env:USERDOMAIN\$env:USERNAME"
$action = New-ScheduledTaskAction -Execute 'conhost.exe' `
    -Argument "--headless powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand $encoded"
$trigger = New-ScheduledTaskTrigger -Daily -At $At
$principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited
$taskSettings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit (New-TimeSpan -Minutes 5) `
    -MultipleInstances IgnoreNew `
    -StartWhenAvailable

Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal `
    -Settings $taskSettings -Description 'Restarts the Claude usage tray widget once a day.' -Force | Out-Null

Write-Host "Registered scheduled task '$taskName' (daily at $At)."
Write-Host "Test it now: Start-ScheduledTask -TaskName '$taskName'"
