<#
.SYNOPSIS Runs on the host every 5 minutes: starts the factory VM if it's off, and restarts it if its GitHub runner has
been offline for -OfflineMinutes (a VM stuck at the sign-in screen, a runner that exited).
.DESCRIPTION Leaves the VM alone while E:\HouseStyle\vm\maintenance exists (checkpoint merges, rebuilds), while it is
still booting, and for 30 minutes after its own last restart. Logs each action to watch-factory.log next to the VM.
Uses the owner's gh sign-in to read the runner's status. Elevated, because Hyper-V needs it.
.EXAMPLE scripts\factory\Watch-Factory.ps1 -Install   # registers the scheduled task (elevated PowerShell)
#>
param(
    [string]$Name = "HSFACTORY",
    [string]$Repo = "abmorrill4/house-style",
    [string]$Root = "E:\HouseStyle\vm",
    [int]$OfflineMinutes = 10,
    [switch]$Install
)
$ErrorActionPreference = "Stop"

if ($Install) {
    $action = New-ScheduledTaskAction -Execute powershell.exe -Argument "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$PSCommandPath`""
    $trigger = New-ScheduledTaskTrigger -Once -At (Get-Date) -RepetitionInterval (New-TimeSpan -Minutes 5)
    # Interactive: runs while the owner is signed in, with their gh sign-in (gh keeps it in Credential Manager, which a
    # task that runs signed out can't read).
    $principal = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes 4)
    Register-ScheduledTask "House Style factory watch" -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
    Write-Host "Registered 'House Style factory watch' (every 5 minutes). Log: $Root\watch-factory.log"
    return
}

$log = Join-Path $Root "watch-factory.log"
$statePath = Join-Path $Root "watch-factory.json"
function Log($t) { "$(Get-Date -Format s) $t" | Add-Content $log }
if (Test-Path (Join-Path $Root "maintenance")) { return }

$state = if (Test-Path $statePath) { Get-Content $statePath -Raw | ConvertFrom-Json } else { [pscustomobject]@{ offlineSince = $null; lastRestart = $null } }
function Save { $state | ConvertTo-Json | Set-Content $statePath }

$vm = Get-VM $Name
if ($vm.State -ne "Running") { Log "VM was $($vm.State); starting it."; Start-VM $Name; $state.lastRestart = (Get-Date).ToString("o"); Save; return }

# Filtered here, not in --jq: Windows PowerShell strips the quotes a jq string literal needs.
$ErrorActionPreference = "Continue"   # gh's stderr would otherwise throw
$runners = & gh.exe api "repos/$Repo/actions/runners" 2>$null
$ok = -not $LASTEXITCODE
$ErrorActionPreference = "Stop"
$status = if ($ok) { (($runners | Out-String | ConvertFrom-Json).runners | Where-Object name -eq $Name).status }
if (-not $status) { return }   # GitHub unreachable, gh signed out or runner unknown: no evidence either way
if ($status -eq "online") { if ($state.offlineSince) { Log "Runner back online."; $state.offlineSince = $null; Save }; return }

if (-not $state.offlineSince) { $state.offlineSince = (Get-Date).ToString("o"); Save; Log "Runner offline."; return }
$down = (Get-Date) - [datetime]$state.offlineSince
$sinceRestart = if ($state.lastRestart) { (Get-Date) - [datetime]$state.lastRestart } else { [timespan]::MaxValue }
if ($down.TotalMinutes -lt $OfflineMinutes -or $vm.Uptime.TotalMinutes -lt $OfflineMinutes -or $sinceRestart.TotalMinutes -lt 30) { return }

Log "Runner offline for $([int]$down.TotalMinutes) min; restarting the VM."
Restart-VM $Name -Force
$state.offlineSince = $null; $state.lastRestart = (Get-Date).ToString("o"); Save
