<#
.SYNOPSIS
  Rebuilds corpus/v2/api-docs from the pinned upstream checkout. Deterministic: same checkout and same seed give the same bytes.
.DESCRIPTION
  stage.py        upstream API docs and OpenAPI spec -> Markdown (renames the product, drops outside links, writes endpoint pages)
  corpuskit import -> Flare topics, snippets, variables, xrefs
  enrich.py       conditions (Cloud / OnPrem), snippet link rebasing, target expressions
  corpuskit scaffold / inject / contexts --update, then natural_labels.py, then validation.
  Needs: dotnet, uv (for Python with pyyaml and lxml), a checkout of the upstream repository at the commit in SOURCES.md.
.EXAMPLE
  git clone https://github.com/zulip/zulip.git $env:TEMP\corpus-src\zulip; git -C $env:TEMP\corpus-src\zulip checkout 4bae1c54f50c6ef523d69f084b47aed1fcc57c4e
  .\build.ps1 -Upstream $env:TEMP\corpus-src\zulip
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Upstream,
    [string]$Work = (Join-Path $env:TEMP "corpus-work\api-docs"),
    [long]$Seed = 72001,
    [double]$Density = 0.08
)
$ErrorActionPreference = "Stop"
$prep = $PSScriptRoot
$proj = Split-Path -Parent $prep
$repo = (Resolve-Path (Join-Path $proj "..\..\..")).Path
$k = Join-Path $repo "tools\FlareAI.CorpusKit\bin\Release\net48\FlareAI.CorpusKit.exe"
$score = Join-Path $repo "tools\FlareAI.Scoring\bin\Release\net48\FlareAI.Scoring.exe"
if (-not (Test-Path $k)) { & dotnet build (Join-Path $repo "tools\FlareAI.CorpusKit\FlareAI.CorpusKit.csproj") -c Release --nologo -v q; if ($LASTEXITCODE) { throw "CorpusKit build failed" } }
if (-not (Test-Path $score)) { & dotnet build (Join-Path $repo "tools\FlareAI.Scoring") -c Release --nologo -v q; if ($LASTEXITCODE) { throw "Scoring build failed" } }
function Py { & uv run --no-project --with pyyaml --with lxml python -W ignore -I @args; if ($LASTEXITCODE) { throw "python failed: $args" } }
function Kit { & $k @args; if ($LASTEXITCODE) { throw "corpuskit $($args[0]) failed" } }

$stage = Join-Path $Work "stage"; $out = Join-Path $Work "proj"
foreach ($d in $stage, $out) { if (Test-Path $d) { Remove-Item -Recurse -Force $d } }
New-Item -ItemType Directory -Force $Work | Out-Null

Py (Join-Path $prep "stage.py") $Upstream $stage
Kit import --src $stage --out $out --name api-docs --seed $Seed --snippet-min 3 --var "Acme Relay=General.ProductName:Acme Relay" --var "Acme Software=General.CompanyName:Acme Software" --var "APIVERSIONTOKEN=API.Version:1"
Py (Join-Path $prep "enrich.py") $out content
$gl = "API=Application programming interface. A set of calls that one program makes to another.;REST=Representational state transfer. A style of web API that uses standard HTTP methods.;Narrow=A filter that limits a message view to one conversation or search.;Event queue=A server-side queue that holds events for a client until the client fetches them.;Bot=A non-human account that sends and receives messages through the API.;Feature level=An integer that the server increases each time its API changes."
Kit scaffold --project $out --name api-docs --conditions "Cloud,OnPrem" --glossary $gl
Py (Join-Path $prep "enrich.py") $out targets

# Seeded defects: several passes so that one cheap kind (a period turned into "!") cannot crowd out the rest. The built-in catalogue is used:
# CorpusKit cannot read corpus/schema/defects.yaml (see SOURCES.md, "CorpusKit notes").
$kinds1 = "utilize,in_order_to,prior_to,is_able_to,please,simply,please_note,whitelist,blacklist,the_user,latin,will,we,claims_soon,claims_free,claims_guaranteed,marketing,passive,wordy,vague,ui_not_bold,undefined_abbr,click_here,phone,url"
Kit inject --project $out --seed $Seed --density $Density --project-id api-docs --kinds $kinds1
Kit inject --project $out --seed ($Seed + 1) --density 0.006 --project-id api-docs --kinds exclaim
Kit inject --project $out --seed ($Seed + 2) --density 0.02 --project-id api-docs --kinds heading_skip
Kit contexts --project $out --update
$ctx = Join-Path $Work "contexts.jsonl"
Kit contexts --project $out --out $ctx
Py (Join-Path $prep "natural_labels.py") $out api-docs $ctx
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
foreach ($g in "Content", "Project", "api-docs.flprj", "labels.jsonl", "corpus-kit.json") {
    $p = Join-Path $proj $g
    if (Test-Path $p) { Remove-Item -Recurse -Force $p }
    Copy-Item -Recurse -Force (Join-Path $out $g) $p
}
Py (Join-Path $prep "project_json.py") $proj
Write-Host "Built $proj"
