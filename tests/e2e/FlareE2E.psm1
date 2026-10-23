# Drives real Flare for end-to-end checks. Flare's own UI (trial dialog, ribbon) through UI Automation; the issues pane
# and House Style's dialogs through the plugin's E2E hook (src/FlareAI.Flare/E2EHook.cs), which Flare turns on when it
# starts with HOUSESTYLE_E2E_DIR set. Windows PowerShell 5.1 (UIAutomationClient ships with .NET Framework).
Set-StrictMode -Version 2
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$script:A = [System.Windows.Automation.AutomationElement]
$script:Scope = [System.Windows.Automation.TreeScope]
$script:Run = $null

function New-Cond($prop, $value) { New-Object System.Windows.Automation.PropertyCondition($prop, $value) }
function New-And($a, $b) { New-Object System.Windows.Automation.AndCondition($a, $b) }

<# Where runs go: E:\HouseStyle\e2e on the dev PC, C:\HouseStyle\e2e in the factory VM (no E: there). #>
function Get-E2ERoot { if (Test-Path "E:\") { "E:\HouseStyle\e2e" } else { "$env:SystemDrive\HouseStyle\e2e" } }

function Wait-Until([scriptblock]$Test, [int]$Seconds = 60, [string]$What = "condition") {
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $deadline) {
        $r = & $Test
        if ($r) { return $r }
        Start-Sleep -Milliseconds 300
    }
    throw "Timed out after ${Seconds}s waiting for $What"
}

function Get-FlareWindow([string]$Name) {
    $byPid = New-Cond $A::ProcessIdProperty $script:Run.Process.Id
    if ($Name) { return $A::RootElement.FindFirst($Scope::Children, (New-And $byPid (New-Cond $A::NameProperty $Name))) }
    # Flare has several top-level windows (floating editors, toasts): the main one is "<project> - MadCap Flare …".
    return @($A::RootElement.FindAll($Scope::Children, $byPid) | Where-Object { $_.Current.Name -like "* - MadCap Flare*" })[0]
}

<#
.SYNOPSIS Starts Flare on a fresh copy of a fixture project, with the plugin isolated in its own settings folder.
.PARAMETER Fixture  A Flare project folder (copied; the original is never touched).
.PARAMETER Ai       Turn AI review on (needs ANTHROPIC_API_KEY, from the environment or the repo's .env).
.PARAMETER Force    Close a Flare that is already running (its unsaved work is lost).
#>
function Start-FlareE2E {
    param([Parameter(Mandatory)][string]$Fixture, [switch]$Ai, [switch]$Force, [string]$RunRoot = (Get-E2ERoot))
    $running = Get-Process Flare -ErrorAction SilentlyContinue
    if ($running) {
        if (-not $Force) { throw "Flare is running. Close it, or pass -Force to close it (unsaved work is lost)." }
        $running | Stop-Process -Force -PassThru | Wait-Process -Timeout 30
    }
    # Program Files on the dev PC; Program Files (x86) where Flare's MSI was installed directly (the factory VM).
    $flare = @("$env:ProgramFiles", "${env:ProgramFiles(x86)}") | ForEach-Object { Join-Path $_ "MadCap Software\MadCap Flare 22\Flare.app\Flare.exe" } | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $flare) { throw "Flare 22 isn't installed." }
    $dir = Join-Path $RunRoot (Get-Date -Format "yyyyMMdd-HHmmss")
    $hook = Join-Path $dir "hook"
    $appdata = Join-Path $hook "appdata"
    New-Item -ItemType Directory -Force $appdata | Out-Null
    Copy-Item $Fixture (Join-Path $dir "project") -Recurse
    $flprj = Get-ChildItem (Join-Path $dir "project") -Filter *.flprj | Select-Object -First 1

    # Setup already done and the tour already offered, so no first-run dialogs; AI per -Ai.
    @{ AiOff = (-not $Ai); WriteDiagnostics = $true; LiveCheck = $false } | ConvertTo-Json | Set-Content (Join-Path $appdata "settings.json")
    @{ FirstStart = (Get-Date).ToUniversalTime().ToString("o"); TourOffered = $true; ChecklistHidden = $true; ProjectLinted = $true } | ConvertTo-Json | Set-Content (Join-Path $appdata "onboarding.json")
    # Flare loads a plugin only when it is turned on for this user.
    $key = "HKCU:\Software\MadCap Software\Flare\Plugins\House Style"
    New-Item $key -Force | Out-Null
    New-ItemProperty $key -Name Activated -Value True -PropertyType String -Force | Out-Null
    # A fresh install asks for its UI language at start until this is set (its OK button can't be automated).
    New-ItemProperty "HKCU:\Software\MadCap Software\Flare" -Name RunSelectLanguageUIOnStartup -Value False -PropertyType String -Force | Out-Null

    if ($Ai -and -not $env:ANTHROPIC_API_KEY) {
        # The repo's .env: this checkout's, else the main checkout's (worktrees don't have one).
        $repo = Split-Path (Split-Path $PSScriptRoot)
        $main = Split-Path (git -C $repo rev-parse --path-format=absolute --git-common-dir)
        foreach ($f in (Join-Path $repo ".env"), (Join-Path $main ".env")) {
            if (-not (Test-Path $f)) { continue }
            foreach ($line in Get-Content $f) { if ($line -match '^\s*ANTHROPIC_API_KEY\s*=\s*"?(.*?)"?\s*$') { $env:ANTHROPIC_API_KEY = $Matches[1] } }
            if ($env:ANTHROPIC_API_KEY) { break }
        }
        if (-not $env:ANTHROPIC_API_KEY) { throw "-Ai needs ANTHROPIC_API_KEY (environment or .env)." }
    }
    $env:HOUSESTYLE_E2E_DIR = $hook
    try { $proc = Start-Process $flare -ArgumentList "`"$($flprj.FullName)`"" -PassThru }
    finally { Remove-Item Env:HOUSESTYLE_E2E_DIR }
    $script:Run = [pscustomobject]@{ Dir = $dir; Hook = $hook; Project = (Join-Path $dir "project"); Process = $proc; Next = 1 }

    # Trial copies show the licensing wizard at every start: continue in trial mode.
    $readyFile = Join-Path $hook "ready.json"
    $wiz = Wait-Until { $w = Get-FlareWindow "Licensing Wizard"; if ($w) { $w } elseif (Test-Path $readyFile) { "licensed" } } 120 "the licensing wizard or House Style to start"
    if ($wiz -ne "licensed") {
        $finish = $wiz.FindFirst($Scope::Descendants, (New-Cond $A::AutomationIdProperty "mFinish"))
        $finish.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    }
    $ready = Wait-Until { Test-Path (Join-Path $hook "ready.json") } 120 "House Style to start in Flare (is the plugin installed and turned on?)"
    $script:Run | Add-Member Version ((Get-Content (Join-Path $hook "ready.json") | ConvertFrom-Json).version)
    Wait-Until { $w = Get-FlareWindow; $w -and $w.Current.Name -like "$($flprj.Name)*" } 120 "the project to open" | Out-Null
    return $script:Run
}

function Stop-FlareE2E {
    if ($script:Run -and -not $script:Run.Process.HasExited) { $script:Run.Process | Stop-Process -Force -PassThru | Wait-Process -Timeout 30 }
}

<#
.SYNOPSIS Sends one command to the plugin's E2E hook and returns its answer (ok, value, error, state).
#>
function Send-E2E {
    param([Parameter(Mandatory)][string]$Verb, [string]$Arg, [hashtable]$Extra = @{}, [switch]$NoWait, [int]$Seconds = 60)
    $id = $script:Run.Next; $script:Run.Next++
    $cmd = @{ verb = $Verb; arg = $Arg } + $Extra
    $path = Join-Path $script:Run.Hook "cmd-$id.json"
    ($cmd | ConvertTo-Json -Compress) | Set-Content "$path.tmp"
    Move-Item "$path.tmp" $path
    if ($NoWait) { return }
    $res = Join-Path $script:Run.Hook "res-$id.json"
    Wait-Until { Test-Path $res } $Seconds "an answer to $Verb $Arg" | Out-Null
    $r = Get-Content $res -Raw | ConvertFrom-Json
    if (-not $r.ok) { throw "$Verb $Arg failed: $($r.error)" }
    return $r
}

function Get-E2EState { (Send-E2E state).state }

<# Waits until the pane is open and done linting (status no longer says Linting/Reviewing). #>
function Wait-PaneDone([int]$Seconds = 180) {
    Wait-Until {
        $s = Get-E2EState
        if ($s.pane -and -not $s.pane.panel.reviewing -and $s.pane.panel.status -and $s.pane.panel.status -notmatch '^(Linting|Reviewing)') { $s }
    } $Seconds "the issues pane to finish"
}

<# Clicks a House Style ribbon button by its label, through UI Automation (the writer's path). #>
function Invoke-Ribbon([Parameter(Mandatory)][string]$Label) {
    # UI Automation can miss the tab for a moment (a tooltip opening, the ribbon relaying out): look again until it shows.
    $tabCond = New-And (New-Cond $A::NameProperty "House Style") (New-Cond $A::ControlTypeProperty ([System.Windows.Automation.ControlType]::TabItem))
    $tab = Wait-Until { $w = Get-FlareWindow; if ($w) { $w.FindFirst($Scope::Descendants, $tabCond) } } 15 "the House Style ribbon tab"
    $tab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $b = Wait-Until { $tab.FindFirst($Scope::Descendants, (New-And (New-Cond $A::NameProperty $Label) (New-Cond $A::ControlTypeProperty ([System.Windows.Automation.ControlType]::Button)))) } 10 "ribbon button $Label"
    $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}

<# True while the issues pane is on screen (its title, through UI Automation): what the writer sees, not the plugin's state. #>
function Test-PaneOnScreen {
    $w = Get-FlareWindow
    $titles = $w.FindAll($Scope::Descendants, (New-And (New-Cond $A::FrameworkIdProperty "WPF") (New-Cond $A::ControlTypeProperty ([System.Windows.Automation.ControlType]::Text))))
    return [bool]@($titles | Where-Object { $_.Current.Name -like "House Style Issues*" -and -not $_.Current.IsOffscreen }).Count
}

function Assert-E2E([bool]$Condition, [string]$Message) { if (-not $Condition) { throw "FAIL: $Message" } }

Export-ModuleMember -Function Get-E2ERoot, Start-FlareE2E, Stop-FlareE2E, Send-E2E, Get-E2EState, Wait-PaneDone, Invoke-Ribbon, Test-PaneOnScreen, Assert-E2E, Wait-Until
