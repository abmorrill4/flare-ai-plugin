<#
.SYNOPSIS Runs the end-to-end scenarios in real Flare and writes results.json; the exit code is the number of failures.
.DESCRIPTION Each scenario (scenarios\*.ps1) gets a fresh Flare on a fresh copy of the fixture project, with the
plugin's settings isolated in the run folder. A failure saves Flare's state (JSON) and a screenshot next to results.json.
Closes any running Flare: don't run it while you have unsaved work in Flare.
A watchdog ends a scenario that runs past -ScenarioMinutes: it saves a screenshot and the open windows' titles, then
kills Flare, which also frees a UI Automation call stuck on an unresponsive window.
.EXAMPLE powershell.exe -File tests\e2e\Run-FlareE2E.ps1 -Install
.EXAMPLE powershell.exe -File tests\e2e\Run-FlareE2E.ps1 -Scenario reopen-issues
#>
param(
    [string[]]$Scenario,                      # names without .ps1; default all
    [switch]$Install,                         # build Release and install into Flare first
    [switch]$NoAi,                            # skip scenarios that call the AI (no key, or to save cost)
    [string]$Fixture = (Join-Path $PSScriptRoot "..\..\corpus\DigitalBanking"),
    [string]$OutDir,
    [int]$ScenarioMinutes = 12                # a scenario still running after this is hung (its own waits add up to ~11)
)
$ErrorActionPreference = "Stop"
if ($PSVersionTable.PSEdition -ne "Desktop") { throw "Run with Windows PowerShell 5.1 (powershell.exe): UI Automation needs .NET Framework." }
Import-Module (Join-Path $PSScriptRoot "FlareE2E.psm1") -Force
if (-not $OutDir) { $OutDir = Join-Path (Get-E2ERoot) ("results-" + (Get-Date -Format "yyyyMMdd-HHmmss")) }
$repo = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$Fixture = Resolve-Path $Fixture

if ($Install) {
    Get-Process Flare -ErrorAction SilentlyContinue | Stop-Process -Force -PassThru | Wait-Process -Timeout 30
    & (Join-Path $repo "scripts\install-plugin.ps1")
    if ($LASTEXITCODE) { throw "install-plugin.ps1 failed" }
}

$files = Get-ChildItem (Join-Path $PSScriptRoot "scenarios") -Filter *.ps1 | Where-Object { -not $Scenario -or $Scenario -contains $_.BaseName }
if ($NoAi) { $files = $files | Where-Object { $_.BaseName -notlike "ai-*" } }
if (-not @($files).Count) { throw "No scenarios to run (check -Scenario names)." }   # an empty run must not look like a pass
$out = $OutDir
New-Item -ItemType Directory -Force $out | Out-Null
$results = @()
# Runs in its own process, so it fires even while this one is blocked inside a UI Automation call. It uses no UI
# Automation for the same reason: process window titles only.
$watchdog = {
    param($parent, $seconds, $png, $txt)
    # Waits on this run, not just the clock: if the run dies without its finally, the watchdog leaves a later Flare alone.
    Wait-Process -Id $parent -Timeout $seconds -ErrorAction SilentlyContinue
    if (-not (Get-Process -Id $parent -ErrorAction SilentlyContinue)) { return }
    Get-Process | Where-Object MainWindowTitle | ForEach-Object { "$($_.ProcessName) $($_.Id) responding=$($_.Responding) '$($_.MainWindowTitle)'" } | Set-Content $txt
    try {
        Add-Type -AssemblyName System.Windows.Forms, System.Drawing
        $b = [System.Windows.Forms.SystemInformation]::VirtualScreen
        $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
        [System.Drawing.Graphics]::FromImage($bmp).CopyFromScreen($b.Left, $b.Top, 0, 0, $bmp.Size)
        $bmp.Save($png)
    } catch { }
    Get-Process Flare -ErrorAction SilentlyContinue | Stop-Process -Force
}
foreach ($f in $files) {
    $start = Get-Date
    Write-Host "== $($f.BaseName)"
    $hung = Join-Path $out "$($f.BaseName)-hung.txt"
    $q = { param($p) "'" + ($p -replace "'", "''") + "'" }
    $cmd = "& {$watchdog} $PID $($ScenarioMinutes * 60) $(& $q (Join-Path $out "$($f.BaseName)-hung.png")) $(& $q $hung)"
    $dog = Start-Process powershell.exe -WindowStyle Hidden -PassThru -ArgumentList "-NoProfile", "-EncodedCommand", ([Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($cmd)))
    $r = [ordered]@{ scenario = $f.BaseName; passed = $false; seconds = 0; error = $null; version = $null; artifacts = @() }
    try {
        & $f.FullName -Fixture $Fixture
        $r.passed = $true
        Write-Host "   PASS" -ForegroundColor Green
    } catch {
        $r.error = $_.Exception.Message
        if (Test-Path $hung) { $r.error = "Hung for $ScenarioMinutes min, so the watchdog killed Flare: $($r.error)"; $r.artifacts += "$($f.BaseName)-hung.txt", "$($f.BaseName)-hung.png" }
        Write-Host "   $($r.error)" -ForegroundColor Red
        try { Get-E2EState | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $out "$($f.BaseName)-state.json"); $r.artifacts += "$($f.BaseName)-state.json" } catch { }
        try {
            Add-Type -AssemblyName System.Windows.Forms, System.Drawing
            $b = [System.Windows.Forms.SystemInformation]::VirtualScreen
            $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
            [System.Drawing.Graphics]::FromImage($bmp).CopyFromScreen($b.Left, $b.Top, 0, 0, $bmp.Size)
            $bmp.Save((Join-Path $out "$($f.BaseName).png")); $r.artifacts += "$($f.BaseName).png"
        } catch { }
    } finally {
        Stop-Process -Id $dog.Id -Force -ErrorAction SilentlyContinue
        try { $r.version = (Get-Module FlareE2E).Invoke({ $script:Run.Version }) } catch { }
        Stop-FlareE2E
        $r.seconds = [int]((Get-Date) - $start).TotalSeconds
    }
    $results += [pscustomobject]$r
}
$results | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $out "results.json")
$failed = @($results | Where-Object { -not $_.passed }).Count
Write-Host "$($results.Count - $failed) passed, $failed failed. $out\results.json"
exit $failed
