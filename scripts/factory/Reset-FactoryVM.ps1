<#
.SYNOPSIS Puts the factory VM back on its clean base every night, so leftover state, Windows Update and disk growth
can't pile up.
.DESCRIPTION Run elevated on the host. The base is one production checkpoint, "factory-base". Restoring it boots the
VM cold, so auto-logon starts the runner as usual. In order:
1. Waits up to -WaitMinutes for a running job.
2. Sets vm\maintenance, which keeps Watch-Factory away.
3. Copies C:\HouseStyle\factory-logs from the VM to vm\factory-logs on the host.
4. Shuts the guest down, restores the base and starts the VM.
5. Waits for the runner to come back online, then clears the flag.
-Refresh replaces the base with the VM as it is now, merging every other checkpoint into its disk. Run it after
updating the VM's tools, and about monthly so Windows updates and the runner version stay current. Logs go to
vm\reset-factory.log.
.EXAMPLE scripts\factory\Reset-FactoryVM.ps1 -Refresh    # once, to create the base
.EXAMPLE scripts\factory\Reset-FactoryVM.ps1 -Install    # registers the nightly 03:00 task
#>
param(
    [string]$Name = "HSFACTORY",
    [string]$Repo = "abmorrill4/house-style",
    [string]$Root = "E:\HouseStyle\vm",
    [int]$WaitMinutes = 60,
    [switch]$Refresh,
    [switch]$Install
)
$ErrorActionPreference = "Stop"
$base = "factory-base"
$log = Join-Path $Root "reset-factory.log"
$flag = Join-Path $Root "maintenance"
function Log($t) { "$(Get-Date -Format s) $t" | Tee-Object $log -Append | Write-Host }

if ($Install) {
    $action = New-ScheduledTaskAction -Execute conhost.exe -Argument "--headless powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
    $trigger = New-ScheduledTaskTrigger -Daily -At "03:00"
    # Interactive, like Watch-Factory: it needs the owner's gh sign-in. A night the owner is signed out is skipped.
    $principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Hours 2)
    Register-ScheduledTask "House Style factory reset" -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
    Write-Host "Registered 'House Style factory reset' (daily at 03:00). Log: $log"
    return
}

function RunnerState {
    $ErrorActionPreference = "Continue"
    $json = & gh.exe api "repos/$Repo/actions/runners" 2>$null
    $ok = -not $LASTEXITCODE
    $ErrorActionPreference = "Stop"
    if ($ok) { (($json | Out-String | ConvertFrom-Json).runners | Where-Object name -eq $Name) }
}

# Waits for an idle runner. If GitHub can't say (gh signed out, offline), it skips: a job might be running.
function Wait-Idle {
    $deadline = (Get-Date).AddMinutes($WaitMinutes)
    while ($true) {
        $r = RunnerState
        if (-not $r) { Log "Couldn't read the runner's state from GitHub; skipping."; return $false }
        if (-not $r.busy) { return $true }
        if ((Get-Date) -gt $deadline) { Log "Runner still busy after $WaitMinutes min; skipping."; return $false }
        Start-Sleep 60
    }
}
if (-not (Wait-Idle)) { return }

if ($Refresh) {
    New-Item $flag -ItemType File -Force | Out-Null
    try {
        Set-VM $Name -CheckpointType ProductionOnly
        Log "Refresh: merging every checkpoint into the disk."
        Get-VMSnapshot -VMName $Name | Remove-VMSnapshot
        while (Get-ChildItem $Root -Filter *.avhdx) { Start-Sleep 10 }
        Checkpoint-VM $Name -SnapshotName $base
        Log "Refresh: '$base' taken."
    }
    finally { Remove-Item $flag -ErrorAction SilentlyContinue }
    return
}

if (-not (Get-VMSnapshot -VMName $Name -Name $base -ErrorAction SilentlyContinue)) { throw "No '$base' checkpoint yet. Run with -Refresh once." }

New-Item $flag -ItemType File -Force | Out-Null
try {
    $cred = Import-Clixml (Join-Path $Root "factory-credential.xml")
    $s = $null
    try {
        $s = New-PSSession -VMName $Name -Credential $cred
        $dest = Join-Path $Root "factory-logs\$(Get-Date -Format yyyy-MM-dd)"   # one folder a night: nothing overwritten
        if (Invoke-Command -Session $s { Test-Path C:\HouseStyle\factory-logs }) {
            New-Item -ItemType Directory -Force $dest | Out-Null
            Copy-Item -FromSession $s C:\HouseStyle\factory-logs\* $dest -Recurse -Force
            Log "Copied factory logs to $dest."
        }
    }
    catch { Log "Couldn't copy the factory logs ($($_.Exception.Message)); resetting anyway." }
    finally { if ($s) { Remove-PSSession $s } }

    # A queued run can start the moment the last one ends: check once more, just before the VM goes off.
    if (-not (Wait-Idle)) { return }

    Stop-VM $Name -TurnOff -Force   # its state is being thrown away, so no shutdown that could hang
    Restore-VMSnapshot -VMName $Name -Name $base -Confirm:$false
    Start-VM $Name
    # Asked inside the VM: GitHub keeps showing the old runner session "online" for a minute or more after the reset.
    $up = { try { Invoke-Command -VMName $Name -Credential $cred -ErrorAction Stop { [bool](Get-Process Runner.Listener -ErrorAction SilentlyContinue) } } catch { $false } }
    $deadline = (Get-Date).AddMinutes(15)
    while (-not (& $up)) {
        if ((Get-Date) -gt $deadline) { Log "Reset done, but the runner isn't online after 15 min. Watch-Factory takes it from here."; return }
        Start-Sleep 15
    }
    Log "Reset to '$base'; runner running in the VM."
}
finally { Remove-Item $flag -ErrorAction SilentlyContinue }
