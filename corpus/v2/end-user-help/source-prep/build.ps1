<#
.SYNOPSIS
  Rebuilds corpus/v2/end-user-help from the pinned upstream checkout. Deterministic: same checkout and same seed give the same bytes.
.DESCRIPTION
  stage.py        upstream user manual (reStructuredText) -> HTML topics (renames the product, drops outside links, placeholder images)
  corpuskit import -> Flare topics, block snippets, variables, xrefs, drop-downs
  enrich.py       conditions, text snippet, glossary terms, snippet link rebasing; after inject: togglers
  corpuskit scaffold / inject / contexts --update, then natural_labels.py, then validation.
  Needs: dotnet, uv (for Python with docutils and lxml), a checkout of the upstream repository (user_manual folder) at the commit in SOURCES.md.
.EXAMPLE
  git clone --depth 1 --filter=blob:none --sparse https://github.com/nextcloud/documentation.git $env:TEMP\corpus-src\nextcloud
  git -C $env:TEMP\corpus-src\nextcloud sparse-checkout set user_manual
  .\build.ps1 -Upstream $env:TEMP\corpus-src\nextcloud
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Upstream,
    [string]$Work = (Join-Path $env:TEMP "corpus-work\end-user-help"),
    [long]$Seed = 73001,
    [double]$Density = 0.09
)
$ErrorActionPreference = "Stop"
$prep = $PSScriptRoot
$proj = Split-Path -Parent $prep
$repo = (Resolve-Path (Join-Path $proj "..\..\..")).Path
$k = Join-Path $repo "tools\FlareAI.CorpusKit\bin\Release\net48\FlareAI.CorpusKit.exe"
$score = Join-Path $repo "tools\FlareAI.Scoring\bin\Release\net48\FlareAI.Scoring.exe"
if (-not (Test-Path $k)) { & dotnet build (Join-Path $repo "tools\FlareAI.CorpusKit\FlareAI.CorpusKit.csproj") -c Release --nologo -v q; if ($LASTEXITCODE) { throw "CorpusKit build failed" } }
if (-not (Test-Path $score)) { & dotnet build (Join-Path $repo "tools\FlareAI.Scoring") -c Release --nologo -v q; if ($LASTEXITCODE) { throw "Scoring build failed" } }
function Py { & uv run --no-project --with docutils --with lxml python -W ignore -I @args; if ($LASTEXITCODE) { throw "python failed: $args" } }
function Kit { & $k @args; if ($LASTEXITCODE) { throw "corpuskit $($args[0]) failed" } }

$stage = Join-Path $Work "stage"; $out = Join-Path $Work "proj"
foreach ($d in $stage, $out) { if (Test-Path $d) { Remove-Item -Recurse -Force $d } }
New-Item -ItemType Directory -Force $Work | Out-Null

Py (Join-Path $prep "stage.py") $Upstream $stage
Kit import --src $stage --out $out --name end-user-help --seed $Seed --snippet-min 3 --var "Acme Hub=General.ProductName:Acme Hub" --var "Acme Software=General.CompanyName:Acme Software"
Py (Join-Path $prep "enrich.py") $out content
$gl = "WebDAV=A protocol that lets a program read and write files on a server over HTTP.;CalDAV=A protocol for sharing calendars between a server and a calendar app.;CardDAV=A protocol for sharing contacts between a server and a contacts app.;TOTP=Time-based one-time password. A code that changes every 30 seconds and proves that you have your device.;E2EE=End-to-end encryption. Only the people in a conversation or folder can read its content.;end-to-end encryption=A way to protect data so that only the sender and the recipients can read it.;two-factor authentication=A sign-in check that needs a second proof of identity besides your password.;SSL=Secure sockets layer. The older name for the encryption that protects a connection to a server.;sync client=The program that keeps a folder on your computer in step with the server.;desktop client=The Acme Hub program for Windows, macOS and Linux that syncs files."
Kit scaffold --project $out --name end-user-help --conditions "Print,ScreenOnly" --glossary $gl

# Seeded defects: several passes so that one cheap kind (a period turned into "!") cannot crowd out the rest. The built-in catalogue is used:
# CorpusKit cannot read corpus/schema/defects.yaml (see SOURCES.md, "CorpusKit notes").
$kinds1 = "utilize,in_order_to,prior_to,is_able_to,please,simply,please_note,whitelist,blacklist,the_user,latin,will,we,claims_soon,claims_free,claims_guaranteed,marketing,passive,wordy,vague,ui_not_bold,undefined_abbr,phone,url"
Kit inject --project $out --seed $Seed --density $Density --project-id end-user-help --kinds $kinds1
Kit inject --project $out --seed ($Seed + 1) --density 0.01 --project-id end-user-help --kinds exclaim
Kit inject --project $out --seed ($Seed + 2) --density 0.03 --project-id end-user-help --kinds heading_skip
Kit inject --project $out --seed ($Seed + 3) --density 0.05 --project-id end-user-help --kinds alt_missing
Kit inject --project $out --seed ($Seed + 4) --density 0.02 --project-id end-user-help --kinds click_here
# Togglers go in after injection, on paragraphs that carry no label, so labelled XPaths do not move.
Py (Join-Path $prep "enrich.py") $out togglers
Kit contexts --project $out --update
$ctx = Join-Path $Work "contexts.jsonl"
Kit contexts --project $out --out $ctx
Py (Join-Path $prep "natural_labels.py") $out end-user-help $ctx
Kit contexts --project $out --update
Kit check --project $out
$v = Join-Path $Work "validate.txt"
& $score validate --labels (Join-Path $out "labels.jsonl") --kinds (Join-Path $repo "corpus\schema\defects.yaml") --root $out 2>&1 | Tee-Object $v
if ($LASTEXITCODE) {
    Py (Join-Path $prep "drop_unanchorable.py") (Join-Path $out "labels.jsonl") $v
    & $score validate --labels (Join-Path $out "labels.jsonl") --kinds (Join-Path $repo "corpus\schema\defects.yaml") --root $out
    if ($LASTEXITCODE) { throw "label validation failed" }
}

# Replace the generated parts of the project folder; the documents and this folder stay.
foreach ($g in "Content", "Project", "end-user-help.flprj", "labels.jsonl", "corpus-kit.json") {
    $p = Join-Path $proj $g
    if (Test-Path $p) { Remove-Item -Recurse -Force $p }
    Copy-Item -Recurse -Force (Join-Path $out $g) $p
}
Py (Join-Path $prep "project_json.py") $proj
Write-Host "Built $proj"
