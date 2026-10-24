<#
.SYNOPSIS Builds the shipped whats-new.md (or one version's release notes) from the hand-written file plus the per-PR
notes in changes/.
.DESCRIPTION Every PR that changes what writers see adds changes/<name>.md with one or two "- " bullets. A release's
notes are the changes/ files added between the previous v* tag and the release commit, so nothing ever edits a shared
section and nothing conflicts. Hand-written sections in src/FlareAI.Flare/whats-new.md stay; a generated version that
already has a hand-written section gets its bullets added to the end of it.
  Build-WhatsNew.ps1 -Version 0.3.5 -Out dist/stage/whats-new.md      # the file the plugin ships
  Build-WhatsNew.ps1 -Version 0.3.5 -NotesOnly                         # one version's notes, for the GitHub release
#>
param([Parameter(Mandatory)][string]$Version, [string]$Out, [switch]$NotesOnly, [string]$Repo = (Split-Path (Split-Path $PSScriptRoot)), [string]$Head = "HEAD")
$ErrorActionPreference = "Stop"

# Bullets from the changes/ files added in (from, to], in the order they were added.
function Get-Bullets([string]$from, [string]$to) {
    $range = if ($from) { "$from..$to" } else { $to }
    $files = @(git -C $Repo log --reverse --diff-filter=A --name-only --format= $range -- changes/ | Where-Object { $_ -like "changes/*.md" })
    $bullets = foreach ($f in $files) {
        $text = git -C $Repo show "${to}:$f" 2>$null
        if ($LASTEXITCODE) { continue }   # added and later removed: not shipped
        @($text | Where-Object { $_ -match '^\s*- |^  \S' })
    }
    @($bullets)
}

function Compare-Version($a, $b) { [version]($a -replace '-.*$', '') -ge [version]($b -replace '-.*$', '') }

# Every released version, newest first, plus the one being built.
$tags = @(git -C $Repo tag -l "v*" --sort=-v:refname | Where-Object { $_ -match '^v\d+\.\d+\.\d+$' })
$versions = @(@{ Version = $Version; To = $Head; From = ($tags | Where-Object { -not (Compare-Version ($_.Substring(1)) $Version) } | Select-Object -First 1) })
for ($i = 0; $i -lt $tags.Count; $i++) {
    if (Compare-Version ($tags[$i].Substring(1)) $Version) { continue }   # the version being built, or newer: not shipped yet
    $versions += @{ Version = $tags[$i].Substring(1); To = $tags[$i]; From = $(if ($i + 1 -lt $tags.Count) { $tags[$i + 1] } else { $null }) }
}
$generated = [ordered]@{}
foreach ($v in $versions) { $b = Get-Bullets $v.From $v.To; if ($b.Count) { $generated[$v.Version] = $b } }

if ($NotesOnly) {
    $hand = Join-Path $Repo "docs/release-notes/$Version.md"
    $lines = @()
    if (Test-Path $hand) { $lines += Get-Content $hand }
    if ($generated.Contains($Version)) { $lines += ""; $lines += $generated[$Version] }
    if (-not $lines.Count) { $lines = @("Fixes and improvements.") }
    $lines -join "`n"
    return
}

# Merge into the hand-written file: generated bullets join an existing "## version" section, or a new section goes in
# above the first hand-written one older than it.
$source = Get-Content (Join-Path $Repo "src/FlareAI.Flare/whats-new.md")
$heading = '^##\s+v?(\d+\.\d+\.\d+)\b'
$result = New-Object System.Collections.Generic.List[string]
$pending = [System.Collections.Generic.List[string]]@($generated.Keys)
$current = $null
function Flush-Section { if ($current -and $generated.Contains($current)) { $result.Add(""); $generated[$current] | ForEach-Object { $result.Add($_) } } }
foreach ($line in $source) {
    if ($line -match $heading) {
        Flush-Section
        $v = $Matches[1]
        foreach ($p in @($pending)) {
            if ($p -ne $v -and (Compare-Version $p $v)) { $result.Add("## $p"); $result.Add(""); $generated[$p] | ForEach-Object { $result.Add($_) }; $result.Add(""); [void]$pending.Remove($p) }
        }
        [void]$pending.Remove($v)
        $current = $v
    }
    $result.Add($line)
}
Flush-Section
foreach ($p in $pending) { $result.Add(""); $result.Add("## $p"); $result.Add(""); $generated[$p] | ForEach-Object { $result.Add($_) } }
$text = ($result -join "`n").TrimEnd() + "`n"
if ($Out) { [IO.File]::WriteAllText($Out, $text, (New-Object Text.UTF8Encoding($false))) } else { $text }
