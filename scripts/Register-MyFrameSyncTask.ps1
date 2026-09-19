[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$SyncExecutable,
    [string]$TaskName = 'My Frame data sync',
    [ValidateSet('Hourly', 'Daily')]
    [string]$Frequency = 'Hourly',
    [switch]$Unregister
)

$ErrorActionPreference = 'Stop'
$resolvedExecutable = (Resolve-Path -LiteralPath $SyncExecutable).Path
if ([string]::IsNullOrWhiteSpace($TaskName) -or $TaskName.Length -gt 200) {
    throw 'TaskName must contain between 1 and 200 characters.'
}

if ($Unregister) {
    if ($PSCmdlet.ShouldProcess($TaskName, 'Unregister scheduled task')) {
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
        Write-Output "SYNC_TASK_UNREGISTERED=$TaskName"
    }
    exit 0
}

$arguments = '--all'
if ($WhatIfPreference) {
    Write-Output "SYNC_TASK_WOULD_REGISTER=$TaskName"
    Write-Output "SYNC_TASK_EXECUTABLE=$resolvedExecutable"
    Write-Output "SYNC_TASK_FREQUENCY=$Frequency"
    exit 0
}
$action = New-ScheduledTaskAction -Execute $resolvedExecutable -Argument $arguments -WorkingDirectory (Split-Path -Parent $resolvedExecutable)
$trigger = if ($Frequency -eq 'Daily') {
    New-ScheduledTaskTrigger -Daily -At (Get-Date).Date.AddMinutes(5)
} else {
    New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(5) `
        -RepetitionInterval (New-TimeSpan -Hours 1) `
        -RepetitionDuration (New-TimeSpan -Days 3650)
}
$principal = New-ScheduledTaskPrincipal -UserId ([System.Security.Principal.WindowsIdentity]::GetCurrent().Name) -LogonType Interactive -RunLevel LeastPrivilege
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -MultipleInstances IgnoreNew
$task = New-ScheduledTask -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description 'Synchronizes My Frame official Warframe data into the local SQLite store.'

if ($PSCmdlet.ShouldProcess($TaskName, "Register $Frequency MyFrame.Sync task")) {
    Register-ScheduledTask -TaskName $TaskName -InputObject $task -Force | Out-Null
    Write-Output "SYNC_TASK_REGISTERED=$TaskName"
    Write-Output "SYNC_TASK_EXECUTABLE=$resolvedExecutable"
    Write-Output "SYNC_TASK_FREQUENCY=$Frequency"
}
