<#
.SYNOPSIS The code factory: takes one open tester report. A new one is triaged into a brief for the owner; one the owner
marked decision:fix is fixed. Nothing is built from a report until the owner decides.
.DESCRIPTION Runs on the factory VM's runner. Safeguards against a report, or code an agent wrote, doing more than the fix:
- Only reports filed by the owner's feedback service (author abmorrill4, label "problem" or "error") in the private reports repo.
- Triage never commits, pushes or opens anything; its scratch tests are thrown away. Only the owner sets decision:*.
- The report reaches the agent as a file, framed as untrusted data, never inside the prompt.
- No GitHub token while an agent runs or while agent-written code (tests, e2e) runs; it comes back only to push,
  open the PR and merge. The checkout keeps no credential (persist-credentials: false); the push uses the token once.
- The agents get no web tools and a short command list; that's a speed bump, so the real checks are outside them:
  a clean working tree afterwards, prompts read from main before the agent starts, changed paths checked without
  rename detection (so a move can't hide a deletion), no mode changes or symlinks, a size cap, and the e2e harness
  itself is off limits.
- This script reruns the unit tests and the e2e checks; a second agent reviews the diff only, never the report.
- Merge only after every required check on the pushed commit has passed, pinned to that commit.
- The public PR says only "Fixes tester report n"; what the tester wrote stays in the private reports repo.
Needs: GH_TOKEN (repo and reports access), and CLAUDE_CODE_OAUTH_TOKEN (the owner's subscription, from
`claude setup-token`) or ANTHROPIC_API_KEY for the agents. Windows PowerShell 5.1 or PowerShell 7.
#>
param([int]$Issue, [switch]$DryRun)
$ErrorActionPreference = "Stop"
$repo = "abmorrill4/house-style"
$reports = "abmorrill4/house-style-reports"
$owner = "abmorrill4"
$tmp = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }
$here = Split-Path (Split-Path $PSScriptRoot)   # repo root
$allowed = '^(src|tests|styleguide/Company|docs/user-guide)/'
$denied = '\.csproj$|\.csproj\.user$|Directory\.Build\.|Directory\.Packages\.props$|nuget\.config$|packages\.lock\.json$|^tests/e2e/(Run-FlareE2E\.ps1|FlareE2E\.psm1)$|^tests/e2e/flaui/'
# Code that updates the plugin, holds settings and keys, or talks to AI providers: a fix here goes to the owner.
$ownerOnly = '^src/FlareAI\.Core/(Update|Settings|Llm)/|^src/FlareAI\.Flare/E2EHook\.cs$'
# The agents' output stays on the VM: this repository is public, so its job logs are too, and the agents read the report.
$logs = Join-Path $env:SystemDrive "HouseStyle\factory-logs"; New-Item -ItemType Directory -Force $logs | Out-Null
$token = $env:GH_TOKEN
$secrets = "GH_TOKEN", "GITHUB_TOKEN", "ANTHROPIC_API_KEY", "CLAUDE_CODE_OAUTH_TOKEN"
# The agents sign in with the subscription token when there is one; an API key in the environment would win over it.
$agentStrips = @("GH_TOKEN", "GITHUB_TOKEN") + $(if ($env:CLAUDE_CODE_OAUTH_TOKEN) { @("ANTHROPIC_API_KEY") } else { @() })
if (-not ($env:CLAUDE_CODE_OAUTH_TOKEN -or $env:ANTHROPIC_API_KEY)) { throw "No CLAUDE_CODE_OAUTH_TOKEN or ANTHROPIC_API_KEY for the agents." }

# No git hooks for any git call this run makes: an agent or its tests could plant one in .git/hooks, and the script's
# own git calls (fetch, switch, push) run with the GitHub token. Child processes inherit this too.
$noHooks = Join-Path $tmp "factory-no-hooks"; New-Item -ItemType Directory -Force $noHooks | Out-Null
$env:GIT_CONFIG_COUNT = "1"; $env:GIT_CONFIG_KEY_0 = "core.hooksPath"; $env:GIT_CONFIG_VALUE_0 = $noHooks

# Errors name the gh command only: its arguments can hold a comment body, and job logs are public.
function Gh { $o = & gh.exe @args; if ($LASTEXITCODE) { throw "gh $($args[0]) $($args[1]) failed" }; $o }
function Say($n, $text) {
    if (-not $DryRun) { Gh issue comment $n -R $reports --body "**Factory:** $text" | Out-Null; return }
    $text | Add-Content (Join-Path $logs "report-$n-comments.log"); Write-Host "[comment #$n] $($text.Length) chars, in factory-logs"
}
Import-Module (Join-Path $PSScriptRoot "Triage.psm1") -Force
function Label($n, $add, $remove) {
    Assert-FactoryLabel $add   # never decision:*: only the owner approves a fix
    if ($DryRun) { Write-Host "[label #$n] +$add -$remove"; return }
    $a = @("issue", "edit", $n, "-R", $reports, "--add-label", $add); if ($remove) { $a += @("--remove-label", $remove) }
    Gh @a | Out-Null
}

# Runs a block with the named environment variables removed, then puts them back.
function Without([string[]]$names, [scriptblock]$block) {
    $saved = @{}; foreach ($v in $names) { $saved[$v] = [Environment]::GetEnvironmentVariable($v); [Environment]::SetEnvironmentVariable($v, $null) }
    try { & $block } finally { foreach ($v in $names) { [Environment]::SetEnvironmentVariable($v, $saved[$v]) } }
}

# Claude Code without the GitHub token, without web tools, with only the commands the job needs. Its own sign-in
# (subscription token or API key) stays: it is the agent's credential. User settings only: the repository's .claude
# settings and hooks don't apply to the factory's agent. Claude's notes go to stderr, which Windows PowerShell would
# treat as a failure, so its exit code decides.
# The runner's PATH is fixed when it is registered and may not include Claude Code's own install folder.
$claude = @((Get-Command claude.exe -ErrorAction SilentlyContinue).Source, (Join-Path $env:USERPROFILE ".local\bin\claude.exe")) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $claude) { throw "Claude Code isn't installed for this account (no claude.exe on PATH or in ~\.local\bin)." }

function Agent([string]$prompt, $map, $tools, [string]$log) {
    foreach ($k in $map.Keys) { $prompt = $prompt.Replace("{{$k}}", $map[$k]) }
    $log = Join-Path $logs $log
    Without $agentStrips {
        $ErrorActionPreference = "Continue"
        $global:LASTEXITCODE = -1   # a claude that never starts must not look like success
        $prompt | & $claude -p --setting-sources user --permission-mode acceptEdits --allowedTools $tools --disallowedTools "WebFetch,WebSearch,Bash(gh:*),Bash(curl:*),Bash(git push:*)" --max-turns 200 *> $log
        $code = $LASTEXITCODE
        Write-Host "Agent output: $log on the factory VM"
        if ($code) { throw "claude exited $code" }
    }
}

function Assert-CleanTree($who) {
    $dirty = git status --porcelain
    if ($dirty) { throw "$who left changes outside its commits: $(@($dirty) -join '; ')" }
    Assert-GitConfig $who
}

# The checkout's own git config may hold only harmless keys. Code an agent wrote could add ones that run programs
# (core.fsmonitor, core.sshCommand, credential.helper, url.*.insteadOf, ...), and the script's git calls run with the
# token. The checkout persists between runs, so this is checked at the start too.
function Assert-GitConfig($who) {
    $ok = '^(core\.(repositoryformatversion|filemode|bare|logallrefupdates|symlinks|ignorecase|autocrlf|longpaths)|remote\.origin\.(url|fetch)|branch\..+\.(remote|merge)|user\.(name|email)|gc\.auto|extensions\.worktreeconfig|maintenance\.auto)='
    $bad = @(git config --local --list | Where-Object { $_ -notmatch $ok })
    if ($bad) { throw "$who left git settings the factory doesn't allow: $(($bad | ForEach-Object { ($_ -split '=')[0] }) -join ', ')" }
}

foreach ($l in "factory:working", "factory:done", "factory:needs-decision", "factory:stuck", "triage:ready", "triage:noise",
               "triage:failed", "decision:fix", "decision:plan", "decision:park", "decision:decline") {
    if (-not $DryRun) { & gh.exe label create $l -R $reports --force | Out-Null }
}

# ---- pick one report: triage what's new first, then fix what the owner approved ------------------------------------
# Nothing is fixed until the owner decides. Triage reads and reports. Only decision:fix leads to code, and only the
# owner can set it: the feedback service sets fixed labels only, and Label refuses decision:* here.
$all = Gh issue list -R $reports --state open --limit 200 --json "number,title,body,author,labels" | ConvertFrom-Json
$pick = Select-Report $all $owner $Issue
if (-not $pick) { Write-Host "Nothing to triage and no fix approved."; exit 0 }
$r = $pick.Report; $mode = $pick.Mode; $n = $r.number
$comments = @((Gh issue view $n -R $reports --json comments | ConvertFrom-Json).comments)
if ($mode -eq "triage") {
    $failures = @($comments | Where-Object { $_.body.StartsWith("**Factory:** Triage failed") }).Count
    if ($failures -ge 3) { Say $n "Triage failed three times. Over to the owner."; Label $n "factory:stuck" "triage:failed"; exit 0 }
}
if ($mode -eq "fix") {
    $events = Gh api "repos/$reports/issues/$n/events?per_page=100" --paginate | ConvertFrom-Json
    $why = Test-FixGate (Get-Labels $r) $events
    if ($why) { throw "Report #$n isn't cleared for a fix: $why." }
    Write-Host "decision:fix on #$n was added by $(Get-LabelActor $events 'decision:fix')"
}
Write-Host "Report #$n ($mode)"
Label $n "factory:working" $null

try {
    # The report as data: comments stripped, length capped, inside <report> tags the prompt tells the agent not to obey.
    $body = ($r.body -replace '(?s)<!--.*?-->', '') -replace '(?i)</?report>', '[report tag]'
    if ($body.Length -gt 8000) { $body = $body.Substring(0, 8000) + "`n[cut]" }
    $title = ("" + $r.title) -replace '(?i)</?report>', '[report tag]' -replace '[\r\n]', ' '
    $reportPath = Join-Path $tmp "report-$n.md"
    Set-Content $reportPath ("<report>`n# " + $title + "`n`n" + $body + "`n</report>") -Encoding UTF8
    $resultPath = Join-Path $tmp "fix-$n.json"; Remove-Item $resultPath -ErrorAction SilentlyContinue

    Push-Location $here
    git fetch -q origin main
    $branch = "factory/report-$n"
    git switch -q -C $branch origin/main
    git clean -xfdq
    Assert-GitConfig "An earlier run"
    $base = git rev-parse HEAD
    # Prompts from main, read before any agent runs, so an agent can't rewrite the reviewer's instructions.
    $fixPrompt = (git show "${base}:.github/factory/fix-prompt.md") -join "`n"
    $reviewPrompt = (git show "${base}:.github/factory/review-prompt.md") -join "`n"
    $triagePrompt = (git show "${base}:.github/factory/triage-prompt.md") -join "`n"

    if ($mode -eq "triage") {
        # ---- triage: read, try to reproduce, compare with the roadmap, write a brief. Never a branch, PR or issue. --
        # Context for matching, gathered now while the script has the token (the agent gets none).
        $contextPath = Join-Path $tmp "context-$n.md"
        $open = Gh issue list -R $repo --state open --limit 300 --json "number,title,labels" | ConvertFrom-Json
        $ctx = @("# Open issues and themes in $repo", "")
        foreach ($i in $open) { $ctx += "- #$($i.number) $($i.title) [$(($i.labels.name) -join ', ')]" }
        $ctx += "", "# Other reports' triage state", ""
        foreach ($o in $all | Where-Object number -ne $n) { $ctx += "- report #$($o.number): $((@($o.labels.name | Where-Object { $_ -like 'triage:*' -or $_ -like 'decision:*' })) -join ', ')" }
        Set-Content $contextPath ($ctx -join "`n") -Encoding UTF8
        $briefPath = Join-Path $tmp "triage-$n.json"; Remove-Item $briefPath -ErrorAction SilentlyContinue
        $triageTools = "Read,Grep,Glob,Edit,Write,Bash(git status:*),Bash(git diff:*),Bash(git log:*),Bash(dotnet build:*),Bash(dotnet test:*)"
        Agent $triagePrompt @{ REPORT_PATH = $reportPath; CONTEXT_PATH = $contextPath; RESULT_PATH = $briefPath } $triageTools "report-$n-triage.log"
        # Scratch tests are thrown away: triage never leaves code behind, and may not commit.
        if (@(git rev-list "$base..HEAD").Count) { throw "The triage agent made a commit; triage may not change code." }
        git reset -q --hard $base
        git clean -xfdq -e bin -e obj
        Assert-CleanTree "The triage agent"
        Remove-Item $reportPath, $contextPath -ErrorAction SilentlyContinue
        $brief = Get-Content $briefPath -Raw -ErrorAction SilentlyContinue | ConvertFrom-Json
        $problems = @(Test-TriageBrief $brief)
        if ($problems.Count) { throw "The triage brief isn't usable: $($problems -join ' ')" }
        Say $n (Format-TriageComment $brief)
        Label $n $(if ($brief.kind -eq "noise") { "triage:noise" } else { "triage:ready" }) "factory:working"
        return
    }

    $tools = "Read,Grep,Glob,Edit,Write,Bash(git status:*),Bash(git diff:*),Bash(git log:*),Bash(git add:*),Bash(git commit:*)," +
             "Bash(dotnet build:*),Bash(dotnet test:*),Bash(powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/e2e/Run-FlareE2E.ps1:*)"
    # The work order: the triage brief the owner decided on, and the owner's decision comment. Both quote nothing from the
    # report, but they came from an agent that read it, so they go in as data too.
    $brief = @($comments | Where-Object { $_.body.StartsWith("**Factory:** **Triage:**") }) | Select-Object -Last 1
    $decision = @($comments | Where-Object { $_.body.StartsWith("**Decision:**") }) | Select-Object -Last 1
    $orderPath = Join-Path $tmp "order-$n.md"
    Set-Content $orderPath ("<workorder>`n" + $(if ($brief) { $brief.body } else { "(no triage brief)" }) + "`n`n" + $(if ($decision) { $decision.body } else { "(no decision comment)" }) + "`n</workorder>") -Encoding UTF8
    Agent $fixPrompt @{ REPORT_PATH = $reportPath; ORDER_PATH = $orderPath; RESULT_PATH = $resultPath } $tools "report-$n-fix.log"
    Remove-Item $orderPath -ErrorAction SilentlyContinue
    Assert-CleanTree "The fixing agent"
    $result = Get-Content $resultPath -Raw | ConvertFrom-Json
    $summary = ("" + $result.summary).Trim()
    if ($summary.Length -gt 1500) { $summary = $summary.Substring(0, 1500) }
    $commits = @(git rev-list "$base..HEAD")

    if ($result.kind -ne "fixed" -or -not $commits.Count) {
        # The owner approved a fix and it didn't happen: back to the owner with the reason, never "done".
        Say $n "I couldn't fix this as approved. $summary`n`nOver to the owner."
        Label $n "factory:needs-decision" "factory:working"
        return
    }

    # ---- check what the agent changed (no rename detection: a move shows as a delete plus an add) ------------------
    $changes = @(git diff --no-renames --name-status "$base..HEAD")
    $bad = foreach ($c in $changes) { $path = ($c -split "`t")[-1]; if ($path -notmatch $allowed -or $path -match $denied) { $c } }
    if ($bad) { throw "The fix changed files the factory may not touch: $($bad -join ', ')" }
    $mine = foreach ($c in $changes) { $path = ($c -split "`t")[-1]; if ($path -match $ownerOnly) { $path } }
    if ($mine) {
        Say $n "The fix touches code the owner reviews (updates, settings, AI providers): $($mine -join ', '). The branch stays on the factory VM for the owner; nothing was pushed."
        Label $n "factory:needs-decision" "factory:working"
        return
    }
    $modes = @(git diff --no-renames --summary "$base..HEAD" | Where-Object { $_ -match 'mode change|create mode 120000|delete mode 120000' })
    if ($modes) { throw "The fix changes file modes or adds symlinks: $($modes -join '; ')" }
    # Binary files count as no lines, so they'd get past the cap; a fix has no reason to add one (the harness loads DLLs).
    $binary = @(git diff --no-renames --numstat "$base..HEAD" | Where-Object { $_ -match '^-\t-\t' } | ForEach-Object { ($_ -split "`t")[-1] })
    if ($binary) { throw "The fix adds or changes binary files: $($binary -join ', ')" }
    $lines = (git diff --no-renames --numstat "$base..HEAD" | ForEach-Object { $p = $_ -split "`t"; [int]($p[0] -replace '-', '0') + [int]($p[1] -replace '-', '0') } | Measure-Object -Sum).Sum
    if ($lines -gt 800) { throw "The fix is $lines lines; too big for the factory to merge on its own." }

    # ---- test it again here, not on the agent's word; agent-written code runs with no secrets ----------------------
    # The agent's tests could print the report or anything else readable: no report on disk, output to factory-logs.
    Remove-Item $reportPath
    Without $secrets {
        $ErrorActionPreference = "Continue"   # with Stop, redirected stderr from a native command throws; exit codes decide
        dotnet test FlareAI.sln -c Release *> (Join-Path $logs "report-$n-unit.log")
        if ($LASTEXITCODE) { throw "Unit tests fail on the fix." }
        $e2eOut = Join-Path $tmp "e2e-$n"
        Remove-Item $e2eOut -Recurse -Force -ErrorAction SilentlyContinue   # never read an earlier run's results
        powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\e2e\Run-FlareE2E.ps1 -Install -NoAi -OutDir $e2eOut *> (Join-Path $logs "report-$n-e2e.log")
        if ($LASTEXITCODE) {
            # In parentheses: Windows PowerShell's ConvertFrom-Json sends a whole array down the pipeline as one object.
            $failed = @((Get-Content (Join-Path $e2eOut "results.json") -Raw -ErrorAction SilentlyContinue | ConvertFrom-Json) | Where-Object { -not $_.passed })
            if ($failed.Count -and -not ($failed | Where-Object kind -ne "vm")) { throw "Flare failed to start or stopped responding during the end-to-end checks, twice. That points at the factory VM rather than the fix; rerun the report once the VM is healthy." }
            if (-not (Test-Path (Join-Path $e2eOut "results.json"))) { throw "The end-to-end run stopped before any scenario ran (setup, install or the FlaUI restore). See report-$n-e2e.log on the factory VM." }
            throw "End-to-end checks fail on the fix."
        }
        $retried = @((Get-Content (Join-Path $e2eOut "results.json") -Raw | ConvertFrom-Json) | Where-Object { $_.passed -and $_.firstTry } | ForEach-Object scenario)
    }
    git clean -xfdq -e bin -e obj   # build output is ignored; anything else the tests left is dropped
    Assert-CleanTree "The tests"

    # ---- second agent reviews the diff, never the report ------------------------------------------------------------
    $diffPath = Join-Path $tmp "fix-$n.diff"; git diff --no-renames "$base..HEAD" | Set-Content $diffPath -Encoding UTF8
    $summaryPath = Join-Path $tmp "summary-$n.txt"; Set-Content $summaryPath $summary -Encoding UTF8
    $reviewPath = Join-Path $tmp "review-$n.json"; Remove-Item $reviewPath -ErrorAction SilentlyContinue
    Agent $reviewPrompt @{ DIFF_PATH = $diffPath; SUMMARY_PATH = $summaryPath; RESULT_PATH = $reviewPath } "Read,Grep,Glob,Write" "report-$n-review.log"
    Assert-CleanTree "The reviewing agent"
    $review = Get-Content $reviewPath -Raw | ConvertFrom-Json
    if ($review.verdict -ne "MERGE") { throw "Review blocked the fix: $($review.reasons -join '; ')" }

    if ($DryRun) { Write-Host "Dry run: would push $branch, open a PR and merge it."; return }

    # ---- ship: the token is used here and only here -----------------------------------------------------------------
    $head = git rev-parse HEAD
    git -c "http.https://github.com/.extraheader=AUTHORIZATION: basic $([Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("x-access-token:$token")))" push -q -f origin $branch
    if ($LASTEXITCODE) { throw "Push failed." }
    $prBody = "Fixes tester report $n (private reports repo).`n`nThe factory's second agent reviewed the diff: MERGE. Unit tests and the end-to-end checks in Flare passed on the factory VM.$(if ($retried) { " These passed only on a retry after a VM failure, so look for a flaky start-up: $($retried -join ', ')." })`n`n🤖 Generated with [Claude Code](https://claude.com/claude-code)"
    $url = Gh pr create -R $repo --base main --head $branch --title "Fix tester report $n" --body $prBody
    $pr = ($url -split '/')[-1]

    # Required checks: wait until they exist and none is pending; every one must pass.
    $required = @((Gh api "repos/$repo/branches/main/protection/required_status_checks" | ConvertFrom-Json).contexts | Where-Object { $_ })
    if (-not $required.Count) { throw "main has no required checks configured; the factory won't merge without CI." }
    $deadline = (Get-Date).AddMinutes(60)
    do {
        Start-Sleep 30
        $runs = @((Gh api "repos/$repo/commits/$head/check-runs?per_page=100" | ConvertFrom-Json).check_runs)
        $state = foreach ($c in $required) {
            $run = $runs | Where-Object name -eq $c | Sort-Object started_at | Select-Object -Last 1
            if (-not $run -or $run.status -ne "completed") { "pending" } elseif ($run.conclusion -eq "success") { "pass" } else { "fail:$c" }
        }
    } until (-not ($state -contains "pending") -or (Get-Date) -gt $deadline)
    if ($state -contains "pending") { throw "Required checks didn't finish in an hour on $url." }
    if ($state | Where-Object { $_ -like "fail:*" }) { throw "Required checks failed on ${url}: $(($state | Where-Object { $_ -like 'fail:*' }) -join ', ')" }

    Gh pr merge $pr -R $repo --merge --delete-branch --match-head-commit $head | Out-Null
    Say $n "Fixed in $url. It ships in the next release.`n`n$summary"
    Label $n "factory:done" "factory:working"
    Gh issue close $n -R $reports | Out-Null
}
catch {
    if ($mode -eq "triage") {
        # A failed triage stays visible and is retried; the third failure goes to the owner (see the pick step).
        try { Say $n "Triage failed (attempt $($failures + 1) of 3): $($_.Exception.Message)"; Label $n "triage:failed" "factory:working" }
        catch { Write-Host "Couldn't mark report #$n as failed on GitHub." }
        throw "Triage of report #$n failed. The reason is on the report, in the private reports repo."
    }
    try { Say $n "I couldn't finish this one: $($_.Exception.Message)"; Label $n "factory:stuck" "factory:working" }
    catch { Write-Host "Couldn't mark report #$n as stuck on GitHub." }
    throw "Report #$n is stuck. The reason is on the report, in the private reports repo."   # not the message: it can quote the report
}
finally { Pop-Location -ErrorAction SilentlyContinue }
