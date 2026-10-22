<#
.SYNOPSIS
  The Bench gate: lint-only bench over the corpus, compared with the committed baseline (tests/baselines/bench-lint.json).
  On pull requests it also enforces the label rules of docs/testing/BENCH.md.

.DESCRIPTION
  Offline, no key. Fails (exit 1) on: a new false positive on a clean/exempt label, fewer lint true positives, a layer's
  precision or recall down more than 0.5 point, a rule with support 10+ down more than 5 points, more wrong-rule findings,
  lower label coverage. Prints per-rule and per-context deltas either way.

  Pull request checks (GITHUB_EVENT_NAME=pull_request; PR_LABELS = comma separated label names, PR_BODY = description):
    - the baseline file changed        -> label 'baseline-change' and every changed rule row named in the PR body
    - labels and rule/engine files changed together -> label 'label-fix'

  Regenerate the baseline after an intended change:  FlareAI.Bench.exe --no-ai --update-baseline
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [switch]$SkipPolicy
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
Set-Location $repo
$bench = Join-Path $repo "tools\FlareAI.Bench\bin\$Configuration\net48\FlareAI.Bench.exe"
if (-not (Test-Path $bench)) { dotnet build tools/FlareAI.Bench -c $Configuration --nologo -v q; if ($LASTEXITCODE) { exit $LASTEXITCODE } }

$failed = $false
$watch = [Diagnostics.Stopwatch]::StartNew()
& $bench --no-ai --check --parallel 4 --out (Join-Path $repo 'bench-out\gate')
if ($LASTEXITCODE -ne 0) { $failed = $true }
Write-Host ("Bench gate took {0:0.0} s" -f $watch.Elapsed.TotalSeconds)

if (-not $SkipPolicy -and $env:GITHUB_EVENT_NAME -eq 'pull_request' -and $env:GITHUB_BASE_REF) {
    $base = "origin/$($env:GITHUB_BASE_REF)"
    $labels = @(($env:PR_LABELS -split ',') | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    $changed = @(git diff --name-only "$base...HEAD")
    # Only baselines that already exist on the base branch count as changes; adding a new one needs no label.
    $baselines = @($changed | Where-Object { $_ -like 'tests/baselines/bench-*.json' } | Where-Object { git cat-file -e "${base}:$_" 2>$null; $LASTEXITCODE -eq 0 })
    $labelFiles = @($changed | Where-Object { $_ -like 'corpus/*labels.jsonl' })
    $engine = @($changed | Where-Object { $_ -like 'styleguide/*' -or $_ -like 'src/FlareAI.Core/Style/*' -or $_ -like 'tools/FlareAI.GuideCompiler/*' })

    if ($labelFiles.Count -gt 0 -and $engine.Count -gt 0 -and $labels -notcontains 'label-fix') {
        Write-Host "::error::This PR changes labels ($($labelFiles -join ', ')) and rule or engine files ($($engine.Count) files). Split it, or add the 'label-fix' label and list each changed label with its reason in the body."
        $failed = $true
    }
    if ($baselines.Count -gt 0) {
        if ($labels -notcontains 'baseline-change') {
            Write-Host "::error::This PR changes a bench baseline ($($baselines -join ', ')); it needs the 'baseline-change' label."
            $failed = $true
        }
        foreach ($b in $baselines) {
            $old = Join-Path $env:RUNNER_TEMP 'old-baseline.json'
            if (-not $env:RUNNER_TEMP) { $old = Join-Path $repo 'bench-out\old-baseline.json' }
            New-Item -ItemType Directory -Force (Split-Path $old) | Out-Null
            git show "${base}:$b" 2>$null | Set-Content -Encoding utf8 $old
            if (-not (Test-Path $old) -or (Get-Item $old).Length -eq 0) { continue }   # nothing to compare
            $rules = @(& $bench baseline-diff $old (Join-Path $repo $b))
            foreach ($r in $rules) {
                if (-not ($env:PR_BODY -and $env:PR_BODY.Contains($r))) {
                    Write-Host "::error::$b changed the row for rule '$r'; the PR body needs a line about it."
                    $failed = $true
                }
            }
        }
    }
}

if ($failed) { exit 1 }
