<#
.SYNOPSIS The code factory: takes one open tester report, and either fixes it (bug) or hands it to the owner (anything else).
.DESCRIPTION Runs on the factory VM's runner. Safeguards against a report, or code an agent wrote, doing more than the fix:
- Only reports filed by the owner's feedback service (author abmorrill4, label "problem") in the private reports repo.
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
$denied = '\.csproj$|Directory\.Build\.|nuget\.config$|packages\.lock\.json$|^tests/e2e/(Run-FlareE2E\.ps1|FlareE2E\.psm1)$'
$token = $env:GH_TOKEN
$secrets = "GH_TOKEN", "GITHUB_TOKEN", "ANTHROPIC_API_KEY", "CLAUDE_CODE_OAUTH_TOKEN"
# The agents sign in with the subscription token when there is one; an API key in the environment would win over it.
$agentStrips = @("GH_TOKEN", "GITHUB_TOKEN") + $(if ($env:CLAUDE_CODE_OAUTH_TOKEN) { @("ANTHROPIC_API_KEY") } else { @() })
if (-not ($env:CLAUDE_CODE_OAUTH_TOKEN -or $env:ANTHROPIC_API_KEY)) { throw "No CLAUDE_CODE_OAUTH_TOKEN or ANTHROPIC_API_KEY for the agents." }

# No git hooks for any git call this run makes: an agent or its tests could plant one in .git/hooks, and the script's
# own git calls (fetch, switch, push) run with the GitHub token. Child processes inherit this too.
$noHooks = Join-Path $tmp "factory-no-hooks"; New-Item -ItemType Directory -Force $noHooks | Out-Null
$env:GIT_CONFIG_COUNT = "1"; $env:GIT_CONFIG_KEY_0 = "core.hooksPath"; $env:GIT_CONFIG_VALUE_0 = $noHooks

function Gh { $o = & gh.exe @args; if ($LASTEXITCODE) { throw "gh $($args -join ' ') failed" }; $o }
function Say($n, $text) { if (-not $DryRun) { Gh issue comment $n -R $reports --body "**Factory:** $text" | Out-Null } else { Write-Host "[comment #$n] $text" } }
function Label($n, $add, $remove) {
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

function Agent([string]$prompt, $map, $tools) {
    foreach ($k in $map.Keys) { $prompt = $prompt.Replace("{{$k}}", $map[$k]) }
    Without $agentStrips {
        $ErrorActionPreference = "Continue"
        $global:LASTEXITCODE = -1   # a claude that never starts must not look like success
        $prompt | & $claude -p --setting-sources user --permission-mode acceptEdits --allowedTools $tools --disallowedTools "WebFetch,WebSearch,Bash(gh:*),Bash(curl:*),Bash(git push:*)" --max-turns 200 2>&1 | Out-Host
        if ($LASTEXITCODE) { throw "claude exited $LASTEXITCODE" }
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

foreach ($l in "factory:working", "factory:done", "factory:needs-decision", "factory:stuck") {
    if (-not $DryRun) { & gh.exe label create $l -R $reports --force | Out-Null }
}

# ---- pick one report ------------------------------------------------------------------------------------------------
$all = Gh issue list -R $reports --label problem --state open --limit 100 --json "number,title,body,author,labels" | ConvertFrom-Json
$candidates = $all | Where-Object {
    $_.author.login -eq $owner -and -not ($_.labels.name | Where-Object { $_ -like "factory:*" })
} | Sort-Object number
if ($Issue) { $candidates = $all | Where-Object number -eq $Issue }
$r = @($candidates)[0]
if (-not $r) { Write-Host "No new reports."; exit 0 }
if ($r.author.login -ne $owner) { throw "Report #$($r.number) wasn't filed by the feedback service; not touching it." }
$n = $r.number
Write-Host "Report #$n"
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

    $tools = "Read,Grep,Glob,Edit,Write,Bash(git status:*),Bash(git diff:*),Bash(git log:*),Bash(git add:*),Bash(git commit:*)," +
             "Bash(dotnet build:*),Bash(dotnet test:*),Bash(powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/e2e/Run-FlareE2E.ps1:*)"
    Agent $fixPrompt @{ REPORT_PATH = $reportPath; RESULT_PATH = $resultPath } $tools
    Assert-CleanTree "The fixing agent"
    $result = Get-Content $resultPath -Raw | ConvertFrom-Json
    $summary = ("" + $result.summary).Trim()
    if ($summary.Length -gt 1500) { $summary = $summary.Substring(0, 1500) }
    $commits = @(git rev-list "$base..HEAD")

    if ($result.kind -ne "bug" -or -not $commits.Count) {
        $decide = $result.kind -in "feature", "question"
        Say $n ("This looks like a **$($result.kind)**. $summary" + $(if ($decide) { "`n`nWaiting for the owner to decide." } else { "" }))
        Label $n $(if ($decide) { "factory:needs-decision" } else { "factory:done" }) "factory:working"
        return
    }

    # ---- check what the agent changed (no rename detection: a move shows as a delete plus an add) ------------------
    $changes = @(git diff --no-renames --name-status "$base..HEAD")
    $bad = foreach ($c in $changes) { $path = ($c -split "`t")[-1]; if ($path -notmatch $allowed -or $path -match $denied) { $c } }
    if ($bad) { throw "The fix changed files the factory may not touch: $($bad -join ', ')" }
    $modes = @(git diff --no-renames --summary "$base..HEAD" | Where-Object { $_ -match 'mode change|create mode 120000|delete mode 120000' })
    if ($modes) { throw "The fix changes file modes or adds symlinks: $($modes -join '; ')" }
    $lines = (git diff --no-renames --numstat "$base..HEAD" | ForEach-Object { $p = $_ -split "`t"; [int]($p[0] -replace '-', '0') + [int]($p[1] -replace '-', '0') } | Measure-Object -Sum).Sum
    if ($lines -gt 800) { throw "The fix is $lines lines; too big for the factory to merge on its own." }

    # ---- test it again here, not on the agent's word; agent-written code runs with no secrets ----------------------
    Without $secrets {
        dotnet test FlareAI.sln -c Release | Out-Host
        if ($LASTEXITCODE) { throw "Unit tests fail on the fix." }
        powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\e2e\Run-FlareE2E.ps1 -Install -NoAi | Out-Host
        if ($LASTEXITCODE) { throw "End-to-end checks fail on the fix." }
    }
    git clean -xfdq -e bin -e obj   # build output is ignored; anything else the tests left is dropped
    Assert-CleanTree "The tests"

    # ---- second agent reviews the diff, never the report ------------------------------------------------------------
    $diffPath = Join-Path $tmp "fix-$n.diff"; git diff --no-renames "$base..HEAD" | Set-Content $diffPath -Encoding UTF8
    $summaryPath = Join-Path $tmp "summary-$n.txt"; Set-Content $summaryPath $summary -Encoding UTF8
    $reviewPath = Join-Path $tmp "review-$n.json"; Remove-Item $reviewPath -ErrorAction SilentlyContinue
    Agent $reviewPrompt @{ DIFF_PATH = $diffPath; SUMMARY_PATH = $summaryPath; RESULT_PATH = $reviewPath } "Read,Grep,Glob,Write"
    Assert-CleanTree "The reviewing agent"
    $review = Get-Content $reviewPath -Raw | ConvertFrom-Json
    if ($review.verdict -ne "MERGE") { throw "Review blocked the fix: $($review.reasons -join '; ')" }

    if ($DryRun) { Write-Host "Dry run: would push $branch, open a PR and merge it."; return }

    # ---- ship: the token is used here and only here -----------------------------------------------------------------
    $head = git rev-parse HEAD
    git -c "http.https://github.com/.extraheader=AUTHORIZATION: basic $([Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("x-access-token:$token")))" push -q -f origin $branch
    if ($LASTEXITCODE) { throw "Push failed." }
    $prBody = "Fixes tester report $n (private reports repo).`n`nThe factory's second agent reviewed the diff: MERGE. Unit tests and the end-to-end checks in Flare passed on the factory VM.`n`n🤖 Generated with [Claude Code](https://claude.com/claude-code)"
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
    Say $n "I couldn't finish this one: $($_.Exception.Message)"
    Label $n "factory:stuck" "factory:working"
    throw
}
finally { Pop-Location -ErrorAction SilentlyContinue }
