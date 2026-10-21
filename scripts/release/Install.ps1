# House Style installer. Run by double-clicking Install.cmd (or: powershell -ExecutionPolicy Bypass -File .\Install.ps1).
# Copies the FlareAI folder next to this script into MadCap Flare's Plugins folder.
# Optional: -Flare <n> picks a Flare folder number (22 = Flare 2026, 21 = Flare 2025, ...).
param(
    [int]$Flare = 0,
    [switch]$NoPause,
    [switch]$Elevated
)
$ErrorActionPreference = "Stop"

function Wait-Exit([int]$code) {
    if (-not $NoPause) { Write-Host ""; Read-Host "Press Enter to close" | Out-Null }
    exit $code
}
function Fail([string]$msg) {
    Write-Host ""
    Write-Host "ERROR: $msg" -ForegroundColor Red
    Wait-Exit 1
}
function Get-FlareName([int]$n) {
    # MadCap folder number -> marketing name. 22 = 2026, 21 = 2025, 20 = 2024, 19 = 2023 ...
    if ($n -ge 17) { return "Flare " + (2004 + $n) }
    return "Flare (folder $n)"
}

try {
    $here = $PSScriptRoot
    if (-not $here) { $here = Split-Path -Parent $MyInvocation.MyCommand.Path }
    $src = Join-Path $here "FlareAI"

    Write-Host "House Style installer" -ForegroundColor Cyan
    if (-not (Test-Path (Join-Path $src "FlareAI.Flare.dll"))) {
        Fail ("Cannot find the FlareAI folder next to Install.ps1 (looked in $src).`n" +
              "Unzip the whole download first, then run Install.cmd from the unzipped folder, not from inside the zip.")
    }

    # 1. Unblock every file (Mark of the Web) so Windows lets Flare load the DLLs.
    $n = 0
    Get-ChildItem $here -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object {
        try { Unblock-File -LiteralPath $_.FullName -ErrorAction Stop; $n++ } catch { }
    }
    Write-Host "Unblocked $n files."

    # 2. Find installed Flare versions.
    $found = @()
    foreach ($base in @($env:ProgramFiles, ${env:ProgramFiles(x86)})) {
        if (-not $base) { continue }
        $mc = Join-Path $base "MadCap Software"
        if (-not (Test-Path $mc)) { continue }
        Get-ChildItem $mc -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "MadCap Flare*" } | ForEach-Object {
            $num = [int]("0" + ($_.Name -replace '[^\d]', ''))
            $app = Join-Path $_.FullName "Flare.app"
            if (Test-Path (Join-Path $app "Flare.exe")) {
                $found += New-Object psobject -Property @{ Number = $num; Name = (Get-FlareName $num); Folder = $_.FullName; App = $app }
            }
        }
    }
    if ($env:FLARE_ROOT -and (Test-Path (Join-Path $env:FLARE_ROOT "Flare.app\Flare.exe"))) {
        $num = [int]("0" + ((Split-Path $env:FLARE_ROOT -Leaf) -replace '[^\d]', ''))
        $found += New-Object psobject -Property @{ Number = $num; Name = (Get-FlareName $num); Folder = $env:FLARE_ROOT; App = (Join-Path $env:FLARE_ROOT "Flare.app") }
    }
    if (-not $found) { Fail "No MadCap Flare installation found under Program Files\MadCap Software." }
    $found = @($found | Sort-Object Number -Descending)

    Write-Host ""
    Write-Host "Installed Flare versions:"
    foreach ($f in $found) { Write-Host ("  {0,-12} folder: {1}" -f $f.Name, (Split-Path $f.Folder -Leaf)) }

    if ($Flare -gt 0) {
        $target = $found | Where-Object { $_.Number -eq $Flare } | Select-Object -First 1
        if (-not $target) { Fail "Flare folder number $Flare is not installed. Installed: $((($found | ForEach-Object { $_.Number }) -join ', '))." }
    } else {
        $target = $found[0]
        if ($found.Count -gt 1) { Write-Host "Picking the newest. Use -Flare <n> (for example -Flare $($found[1].Number)) to choose another." }
    }
    Write-Host ""
    Write-Host ("Target: {0} ({1})" -f $target.Name, $target.Folder) -ForegroundColor Cyan

    # 3. Refuse if Flare is running.
    $running = @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -eq "Flare" })
    if ($running.Count -gt 0) { Fail "Flare is running. Save your work, close Flare completely, then run Install.cmd again." }

    # 4. Self-elevate when the target is under Program Files.
    $dest = Join-Path $target.App "Plugins\FlareAI"
    $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    $underPF = ($target.App -like "$env:ProgramFiles*") -or (${env:ProgramFiles(x86)} -and ($target.App -like "${env:ProgramFiles(x86)}*"))
    if ($underPF -and -not $isAdmin) {
        if ($Elevated) { Fail "Administrator rights were not granted." }
        Write-Host "Administrator rights are needed to write to Program Files. Asking Windows for permission..."
        $argList = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -Flare $($target.Number) -Elevated"
        if ($NoPause) { $argList += " -NoPause" }
        try { Start-Process -FilePath "powershell.exe" -ArgumentList $argList -Verb RunAs -Wait } catch { Fail "Elevation was cancelled or failed: $($_.Exception.Message)" }
        Wait-Exit 0
    }

    # 5. Copy, replacing old files.
    if (Test-Path $dest) {
        Write-Host "Removing the previous install at $dest"
        Remove-Item $dest -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path (Split-Path $dest -Parent) | Out-Null
    Copy-Item $src -Destination $dest -Recurse -Force
    Get-ChildItem $dest -Recurse -File | ForEach-Object { try { Unblock-File -LiteralPath $_.FullName } catch { } }
    if (-not (Test-Path (Join-Path $dest "FlareAI.Flare.dll"))) { Fail "Copy finished but FlareAI.Flare.dll is missing from $dest." }

    Write-Host ""
    Write-Host "Installed to: $dest" -ForegroundColor Green
    Write-Host ""
    Write-Host "Next steps:"
    Write-Host "  1. Start $($target.Name)."
    Write-Host "  2. File > Options > Plugins > House Style > Enable."
    Write-Host "  3. Restart Flare. The House Style tab appears after the restart."
    Write-Host "  4. House Style > Settings: paste your Anthropic API key or gateway virtual key, then Save."
    Wait-Exit 0
} catch {
    Fail $_.Exception.Message
}
