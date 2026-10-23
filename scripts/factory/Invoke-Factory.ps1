<#
.SYNOPSIS The code factory: takes one open tester report, and either fixes it (bug) or hands it to the owner (anything else).
.DESCRIPTION Runs on the factory VM's runner (.github/workflows/factory.yml). Safeguards against a report steering the agent:
- Only reports filed by the owner's feedback service (author abmorrill4, label "problem") in the private reports repo.
- The report reaches the agent as a file, framed as untrusted data, never inside the prompt.
- The agents run without the GitHub token, with no web tools, and with a short list of allowed commands.
- The agent can't push, open PRs or comment; this script does, after checking which files changed.
- The fix is tested again here (unit tests and real Flare), then reviewed by a second agent that never sees the
  report, only the diff and the first agent's summary. Merge only on MERGE and green required checks.
Needs: GH_TOKEN (repo and reports access), ANTHROPIC_API_KEY. Windows PowerShell 5.1 or PowerShell 7.
#>
param([int]$Issue, [switch]$DryRun)
$ErrorActionPreference = "Stop"
$repo = "abmorrill4/house-style"
$reports = "abmorrill4/house-style-reports"
$owner = "abmorrill4"
$tmp = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }
$here = Split-Path (Split-Path $PSScriptRoot)   # repo root
$allowed = '^(src|tests|styleguide/Company|docs/user-guide)/'
$denied = '\.csproj$|Directory\.Build\.|nuget\.config$|packages\.lock\.json$'

function Gh { $o = & gh @args; if ($LASTEXITCODE) { throw "gh $($args -join ' ') failed" }; $o }
function Say($n, $text) { if (-not $DryRun) { Gh issue comment $n -R $reports --body "**Factory:** $text" | Out-Null } else { Write-Host "[comment #$n] $text" } }
function Label($n, $add, $remove) {
    if ($DryRun) { Write-Host "[label #$n] +$add -$remove"; return }
    $a = @("issue", "edit", $n, "-R", $reports, "--add-label", $add); if ($remove) { $a += @("--remove-label", $remove) }
    Gh @a | Out-Null
}

# Claude Code with no GitHub token, no web tools, and only the commands the job needs.
function Agent($promptFile, $map, $tools) {
    $prompt = Get-Content $promptFile -Raw
    foreach ($k in $map.Keys) { $prompt = $prompt.Replace("{{$k}}", $map[$k]) }
    $saved = $env:GH_TOKEN; $env:GH_TOKEN = $null; $savedG = $env:GITHUB_TOKEN; $env:GITHUB_TOKEN = $null
    try {
        $prompt | & claude -p --permission-mode acceptEdits --allowedTools $tools --disallowedTools "WebFetch,WebSearch,Bash(gh:*),Bash(curl:*),Bash(git push:*)" --max-turns 200 | Out-Host
        if ($LASTEXITCODE) { throw "claude exited $LASTEXITCODE" }
    } finally { $env:GH_TOKEN = $saved; $env:GITHUB_TOKEN = $savedG }
}

foreach ($l in "factory:working", "factory:done", "factory:needs-decision", "factory:stuck") {
    if (-not $DryRun) { & gh label create $l -R $reports --force | Out-Null }
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
Write-Host "Report #${n}: $($r.title)"
Label $n "factory:working" $null

try {
    # The report as data: comments stripped, length capped, inside <report> tags the prompt tells the agent not to obey.
    $body = ($r.body -replace '(?s)<!--.*?-->', '')
    if ($body.Length -gt 8000) { $body = $body.Substring(0, 8000) + "`n[cut]" }
    $reportPath = Join-Path $tmp "report-$n.md"
    Set-Content $reportPath ("<report>`n# " + $r.title + "`n`n" + $body.Replace("</report>", "</ report>") + "`n</report>") -Encoding UTF8
    $resultPath = Join-Path $tmp "fix-$n.json"; Remove-Item $resultPath -ErrorAction SilentlyContinue

    Push-Location $here
    git fetch -q origin main
    $branch = "factory/report-$n"
    git switch -q -C $branch origin/main
    $base = git rev-parse HEAD

    $tools = "Read,Grep,Glob,Edit,Write,Bash(git status:*),Bash(git diff:*),Bash(git log:*),Bash(git add:*),Bash(git commit:*)," +
             "Bash(dotnet build:*),Bash(dotnet test:*),Bash(powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/e2e/Run-FlareE2E.ps1:*)"
    Agent (Join-Path $here ".github\factory\fix-prompt.md") @{ REPORT_PATH = $reportPath; RESULT_PATH = $resultPath } $tools
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

    # ---- check what the agent changed ------------------------------------------------------------------------------
    $files = @(git diff --name-only "$base..HEAD")
    $bad = $files | Where-Object { $_ -notmatch $allowed -or $_ -match $denied }
    if ($bad) { throw "The fix changed files the factory may not touch: $($bad -join ', ')" }
    $lines = (git diff --numstat "$base..HEAD" | ForEach-Object { $p = $_ -split "`t"; [int]$p[0] + [int]$p[1] } | Measure-Object -Sum).Sum
    if ($lines -gt 800) { throw "The fix is $lines lines; too big for the factory to merge on its own." }

    # ---- test it again here, not on the agent's word ---------------------------------------------------------------
    dotnet test FlareAI.sln -c Release | Out-Host
    if ($LASTEXITCODE) { throw "Unit tests fail on the fix." }
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\e2e\Run-FlareE2E.ps1 -Install | Out-Host
    if ($LASTEXITCODE) { throw "End-to-end checks fail on the fix." }

    # ---- second agent reviews the diff, never the report ------------------------------------------------------------
    $diffPath = Join-Path $tmp "fix-$n.diff"; git diff "$base..HEAD" | Set-Content $diffPath -Encoding UTF8
    $summaryPath = Join-Path $tmp "summary-$n.txt"; Set-Content $summaryPath $summary -Encoding UTF8
    $reviewPath = Join-Path $tmp "review-$n.json"; Remove-Item $reviewPath -ErrorAction SilentlyContinue
    Agent (Join-Path $here ".github\factory\review-prompt.md") @{ DIFF_PATH = $diffPath; SUMMARY_PATH = $summaryPath; RESULT_PATH = $reviewPath } "Read,Grep,Glob"
    $review = Get-Content $reviewPath -Raw | ConvertFrom-Json
    if ($review.verdict -ne "MERGE") { throw "Review blocked the fix: $($review.reasons -join '; ')" }

    if ($DryRun) { Write-Host "Dry run: would push $branch, open a PR and merge it."; return }

    # ---- ship -------------------------------------------------------------------------------------------------------
    git push -q -f origin $branch
    $title = if ($result.title) { "" + $result.title } else { "Fix tester report " + $n }
    $prBody = "Fixes a tester report (house-style-reports #$n).`n`n$summary`n`nReviewed by the factory's second agent: MERGE. Unit tests and the end-to-end checks in Flare passed on the factory VM.`n`n🤖 Generated with [Claude Code](https://claude.com/claude-code)"
    $url = Gh pr create -R $repo --base main --head $branch --title $title --body $prBody
    $pr = ($url -split '/')[-1]
    & gh pr checks $pr -R $repo --watch --required --interval 30 | Out-Null
    Gh pr merge $pr -R $repo --merge --delete-branch | Out-Null
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
