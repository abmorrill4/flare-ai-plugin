<#
.SYNOPSIS
  Builds a Flare corpus project: import Markdown/HTML, scaffold the Flare project, inject seeded defects, compute contexts, check.
.DESCRIPTION
  Wraps tools\FlareAI.CorpusKit (dev tool, never shipped). Each step is one CorpusKit command, so a content package can run
  the steps one at a time when it wants to hand-edit between them (see -Steps).
.EXAMPLE
  .\scripts\make-corpus-v2.ps1 -Sample -Out $env:TEMP\sample-project
.EXAMPLE
  .\scripts\make-corpus-v2.ps1 -Source C:\src\docs -Out corpus\v2\api-docs -Name api-docs -Var "Kubernetes=General.ProductName:Acme Orbit" -Seed 20261021 -Density 0.10
#>
[CmdletBinding()]
param(
    [string]$Source,
    [Parameter(Mandatory = $true)][string]$Out,
    [string]$Name,
    [string[]]$Var = @(),
    [switch]$Sample,
    [long]$Seed = 1,
    [double]$Density = 0.12,
    [double]$AltDropRate = 0.0,
    [int]$SnippetMin = 3,
    [string]$Defects,
    [string]$Conditions = "Print,ScreenOnly",
    # Which steps to run, in order.
    [ValidateSet("import", "scaffold", "inject", "contexts", "check")][string[]]$Steps = @("import", "scaffold", "inject", "contexts", "check"),
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root "tools\FlareAI.CorpusKit\FlareAI.CorpusKit.csproj"
$exe = Join-Path $root ("tools\FlareAI.CorpusKit\bin\{0}\net48\FlareAI.CorpusKit.exe" -f $Configuration)

if ($Sample) {
    $Source = Join-Path $root "tools\FlareAI.CorpusKit\sample"
    if ($Var.Count -eq 0) { $Var = @("Zorbit=General.ProductName:Acme Orbit") }
    if (-not $Name) { $Name = "Sample" }
}
if (-not $Name) { $Name = Split-Path -Leaf $Out }

Write-Host "Building CorpusKit ($Configuration)..."
& dotnet build $proj -c $Configuration --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "CorpusKit build failed" }

function Invoke-Kit {
    param([string[]]$KitArgs)
    Write-Host ("> corpuskit " + ($KitArgs -join " "))
    & $exe @KitArgs
    if ($LASTEXITCODE -ne 0) { throw ("corpuskit {0} failed with exit code {1}" -f $KitArgs[0], $LASTEXITCODE) }
}

foreach ($step in $Steps) {
    switch ($step) {
        "import" {
            if (-not $Source) { throw "-Source is required for the import step (or use -Sample)" }
            $a = @("import", "--src", $Source, "--out", $Out, "--name", $Name, "--seed", $Seed, "--alt-drop-rate", $AltDropRate.ToString([Globalization.CultureInfo]::InvariantCulture), "--snippet-min", $SnippetMin)
            foreach ($v in $Var) { $a += @("--var", $v) }
            Invoke-Kit $a
        }
        "scaffold" { Invoke-Kit @("scaffold", "--project", $Out, "--name", $Name, "--conditions", $Conditions) }
        "inject" {
            $a = @("inject", "--project", $Out, "--seed", $Seed, "--density", $Density.ToString([Globalization.CultureInfo]::InvariantCulture), "--project-id", $Name)
            if ($Defects) { $a += @("--defects", $Defects) }
            Invoke-Kit $a
        }
        "contexts" { Invoke-Kit @("contexts", "--project", $Out, "--update") }
        "check" { Invoke-Kit @("check", "--project", $Out) }
    }
}
Write-Host "Done: $Out"
