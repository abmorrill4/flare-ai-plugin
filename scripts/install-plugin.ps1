# Builds Release and copies the plugin into Flare.app\Plugins\FlareAI. Run from an elevated shell.
# -NoBuild to copy what is already in bin\Release.
param([switch]$NoBuild, [string]$Configuration = "Release")
$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot\Find-Flare.ps1"

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin -and (Get-FlareInstall).Root -like "$env:ProgramFiles*") { throw "Plugins folder lives under Program Files — re-run as Administrator, or set FLARE_ROOT to a writable copy." }

if (Get-Process -Name "Flare" -ErrorAction SilentlyContinue) { throw "Flare is running. Close it first (Flare only scans Plugins at startup)." }

if (-not $NoBuild) { dotnet build "$repo\src\FlareAI.Flare\FlareAI.Flare.csproj" -c $Configuration }

$flare  = Get-FlareInstall
$target = Join-Path $flare.PluginsDir "FlareAI"
$src    = Join-Path $repo "src\FlareAI.Flare\bin\$Configuration\net48"
if (-not (Test-Path $src)) { throw "Build output not found at $src" }

New-Item -ItemType Directory -Force -Path $target | Out-Null
# Do NOT copy B3.PluginAPIKit.dll into the plugin folder — Flare already loads its own copy.
Get-ChildItem $src -File | Where-Object { $_.Name -ne "B3.PluginAPIKit.dll" } | Copy-Item -Destination $target -Force
# ship the docstyle MCP server (style guide + linter) + icons alongside
Remove-Item "$target\rules" -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path "$target\docstyle" | Out-Null
Remove-Item "$target\docstyle\*" -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item "$repo\mcp\docstyle\*" -Destination "$target\docstyle" -Recurse -Force -Exclude "__pycache__"
if (Test-Path "$repo\src\FlareAI.Flare\Icons") { Copy-Item "$repo\src\FlareAI.Flare\Icons" -Destination $target -Recurse -Force }

Write-Host "Installed to $target"
Get-ChildItem $target | Select-Object Name, Length | Format-Table -AutoSize
Write-Host "Now: start Flare > File > Options > Plugins > FlareAI > Enable > restart Flare."
