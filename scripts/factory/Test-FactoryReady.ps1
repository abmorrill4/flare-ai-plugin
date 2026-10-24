<#
.SYNOPSIS First step of every job on the factory VM: fails in seconds, with the reason, when the VM can't drive Flare.
.DESCRIPTION Without it, a locked desktop or a Flare left over from an earlier run shows up minutes later as a timeout
that looks like a test failure. Closes leftover Flare processes; everything else it only reports.
#>
$ErrorActionPreference = "Stop"
$problems = @()
$session = (Get-Process -Id $PID).SessionId
$mine = { param($name) @(Get-Process $name -ErrorAction SilentlyContinue | Where-Object SessionId -eq $session) }

if ($session -eq 0) { $problems += "The runner runs as a service (session 0), which has no desktop for Flare." }
elseif (-not (& $mine explorer).Count) { $problems += "The runner's session has no desktop (no explorer)." }
# LogonUI runs in a session while it is locked or at the sign-in screen.
if ((& $mine LogonUI).Count) { $problems += "The desktop is locked." }
$free = [math]::Round((Get-PSDrive C).Free / 1GB, 1)
if ($free -lt 10) { $problems += "Only $free GB free on C:." }

$stray = @(Get-Process Flare -ErrorAction SilentlyContinue)
if ($stray.Count) {
    Write-Host "Closing $($stray.Count) Flare process(es) left by an earlier run."
    $stray | Stop-Process -Force -PassThru | Wait-Process -Timeout 30
}

if ($problems) {
    foreach ($p in $problems) { Write-Host "::error::Factory VM not ready: $p" }
    exit 1
}
Write-Host "Factory VM ready: session $session, desktop unlocked, $free GB free."
exit 0
