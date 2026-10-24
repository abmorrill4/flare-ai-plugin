# Helpers for the factory's triage step: check the triage agent's brief, turn it into the comment on the private
# report, and check who approved a fix. Kept out of Invoke-Factory.ps1 so tests/factory can test them without a VM.

$Kinds = "bug", "friction", "feature", "question", "duplicate", "noise"

# What's wrong with a brief, as a list of reasons; empty when it is usable.
function Test-TriageBrief($b) {
    $problems = @()
    if (-not $b) { return @("The brief is empty.") }
    if ($Kinds -notcontains $b.kind) { $problems += "kind '$(Format-Field $b.kind 30)' isn't one of $($Kinds -join ', ')." }
    foreach ($f in "summary", "goal", "impact") { if (-not ("" + $b.$f).Trim()) { $problems += "$f is missing." } }
    if (-not $b.reproduced -or @("yes", "no", "not-tried") -notcontains $b.reproduced.result) { $problems += "reproduced.result must be yes, no or not-tried." }
    if (@($b.options | Where-Object { $_ }).Count -lt 2) { $problems += "There are fewer than two options." }
    if (-not $b.recommendation -or -not ("" + $b.recommendation.option).Trim()) { $problems += "There is no recommendation." }
    if (@("high", "medium", "low") -notcontains $b.confidence) { $problems += "confidence must be high, medium or low." }
    $problems
}

# Agent text for a comment: one line or a short paragraph, capped, with @mentions defused so nobody gets pinged, and
# no links, images or HTML, so a brief can't put a clickable or auto-loading URL in front of the owner.
function Format-Field($text, [int]$max = 600) {
    $t = ("" + $text) -replace '[\r\n]+', ' '
    $t = $t -replace '!\[[^\]]*\]\([^)]*\)', '' -replace '\[([^\]]*)\]\([^)]*\)', '$1' -replace '<[^>]*>', '' -replace '(?i)\b(https?|ftp|file)://\S+', '[link removed]'
    $t = $t.Trim()
    if ($t.Length -gt $max) { $t = $t.Substring(0, $max - 1) + "…" }
    $t -replace '@', ('@' + [char]0x200B)   # a zero-width space after @ stops the mention
}

function Format-TriageComment($b) {
    $lines = @(
        "**Triage:** $(Format-Field $b.kind 20) · confidence $(Format-Field $b.confidence 10)",
        "",
        "**What happened:** $(Format-Field $b.summary)",
        "**What they were trying to do:** $(Format-Field $b.goal 300)",
        "**Impact on writers:** $(Format-Field $b.impact 400)",
        "**Reproduced:** $(Format-Field $b.reproduced.result 10). $(Format-Field $b.reproduced.how 400)"
    )
    $hits = @($b.matches | Select-Object -First 6)
    if ($hits.Count) {
        $lines += "", "**Lines up with:**"
        foreach ($m in $hits) { $lines += "- $(Format-Field $m.ref 30) $(Format-Field $m.title 120) ($(Format-Field $m.relation 12)): $(Format-Field $m.why 200)" }
    }
    $lines += "", "**Options:**"
    foreach ($o in @($b.options | Where-Object { $_ } | Select-Object -First 4)) {
        $lines += "- $(Format-Field $o.option 200) · effort $(Format-Field $o.effort 4) · risk: $(Format-Field $o.risk 200) · for writers: $(Format-Field $o.for_writers 200)"
    }
    $lines += "", "**Recommendation:** $(Format-Field $b.recommendation.option 200). $(Format-Field $b.recommendation.why 400)"
    $questions = @($b.questions | Where-Object { $_ } | Select-Object -First 5)
    if ($questions.Count) {
        $lines += "", "**Questions for you:**"
        foreach ($q in $questions) { $lines += "- $(Format-Field $q 300)" }
    }
    $lines += "", "Decide with a label: ``decision:fix`` (bugs only), ``decision:plan``, ``decision:park`` or ``decision:decline``, or ask Claude to triage the reports."
    $lines -join "`n"
}

# The account that last added $label to the report, from the issue's events (oldest first, as the API returns them).
# $null when the label was never added. A label that was removed and added again counts from the last add.
function Get-LabelActor($events, [string]$label) {
    $last = @($events | Where-Object { $_.event -eq "labeled" -and $_.label.name -eq $label }) | Select-Object -Last 1
    if ($last) { $last.actor.login } else { $null }
}

function Get-Labels($r) { @($r.labels | ForEach-Object { $_.name }) }

# Which report this run works on, and in which step. Triage first (new reports, and failed triage up for a retry), then
# one fix the owner approved. Only reports the feedback service filed ($owner, label problem or error) are considered.
# Returns @{ Report; Mode } or $null.
function Select-Report($all, [string]$owner, [int]$issue = 0) {
    $ours = @($all | Where-Object {
        $l = Get-Labels $_
        $_.author.login -eq $owner -and ($l -contains "problem" -or $l -contains "error")
    } | Sort-Object number)
    if ($issue) { $ours = @($ours | Where-Object number -eq $issue) }
    foreach ($r in $ours) {
        $l = Get-Labels $r
        $new = -not ($l | Where-Object { $_ -like "decision:*" -or $_ -like "factory:*" -or ($_ -like "triage:*" -and $_ -ne "triage:failed") })
        if ($new) { return @{ Report = $r; Mode = "triage" } }
    }
    foreach ($r in $ours) {
        $l = Get-Labels $r
        $decisions = @($l | Where-Object { $_ -like "decision:*" })
        # decision:fix alone: a report with a second decision label waits for the owner and never blocks the queue.
        if ($decisions.Count -eq 1 -and $decisions[0] -eq "decision:fix" -and -not ($l | Where-Object { $_ -like "factory:*" })) { return @{ Report = $r; Mode = "fix" } }
    }
    $null
}

# Why a report may not be fixed yet, or $null when it may: exactly one decision label, decision:fix, and the last
# decision label added was decision:fix (so a later park or decline that someone forgot to clean up wins).
function Test-FixGate($labels, $events) {
    $decisions = @($labels | Where-Object { $_ -like "decision:*" })
    if ($decisions.Count -ne 1 -or $decisions[0] -ne "decision:fix") { return "it has decision labels $($decisions -join ', '), not decision:fix alone" }
    $last = @($events | Where-Object { $_.event -eq "labeled" -and $_.label.name -like "decision:*" }) | Select-Object -Last 1
    if (-not $last -or $last.label.name -ne "decision:fix") { return "the last decision label added was $(if ($last) { $last.label.name } else { 'none' }), not decision:fix" }
    $null
}

# Failed triage attempts since the owner last asked for a retry (a comment starting "**Retry triage**").
function Get-TriageFailures($comments) {
    $since = 0; $i = 0; $all = @($comments)
    foreach ($c in $all) { $i++; if (("" + $c.body).StartsWith("**Retry triage**")) { $since = $i } }
    @($all | Select-Object -Skip $since | Where-Object { ("" + $_.body).StartsWith("**Factory:** Triage failed") }).Count
}

# The factory writes only its own labels. decision:* is the owner's alone, so the factory can never approve a fix.
function Assert-FactoryLabel([string]$label) {
    if ($label -and $label -notlike "triage:*" -and $label -notlike "factory:*") { throw "The factory may not set the label '$label'." }
}

Export-ModuleMember -Function Test-TriageBrief, Format-TriageComment, Get-LabelActor, Format-Field, Select-Report, Test-FixGate, Assert-FactoryLabel, Get-TriageFailures, Get-Labels
