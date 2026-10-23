# Builds Release and copies the plugin into Flare.app\Plugins\FlareAI. Needs write access to the Plugins folder (admin, or Modify granted once).
# -NoBuild to copy what is already in bin\Release.
param([switch]$NoBuild, [string]$Configuration = "Release")
$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot\Find-Flare.ps1"

# No admin needed when the Plugins folder is writable (grant your account Modify on it once); otherwise say so.
$probe = Join-Path (Get-FlareInstall).PluginsDir "FlareAI\.write-test"
try { New-Item -ItemType Directory -Force -Path (Split-Path $probe) | Out-Null; [IO.File]::WriteAllText($probe, ""); Remove-Item $probe }
catch { throw "Can't write to $((Get-FlareInstall).PluginsDir). Re-run as Administrator, or give your account Modify on that folder." }

if (Get-Process -Name "Flare" -ErrorAction SilentlyContinue) { throw "Flare is running. Close it first (Flare only scans Plugins at startup)." }

if (-not $NoBuild) { dotnet build "$repo\src\FlareAI.Flare\FlareAI.Flare.csproj" -c $Configuration }

$flare  = Get-FlareInstall
$target = Join-Path $flare.PluginsDir "FlareAI"
$src    = Join-Path $repo "src\FlareAI.Flare\bin\$Configuration\net48"
if (-not (Test-Path $src)) { throw "Build output not found at $src" }

New-Item -ItemType Directory -Force -Path $target | Out-Null
# Do NOT copy B3.PluginAPIKit.dll into the plugin folder: Flare already loads its own copy.
Get-ChildItem $src -File | Where-Object { $_.Name -ne "B3.PluginAPIKit.dll" } | Copy-Item -Destination $target -Force
# Ship the bundled style guide (a Flare project: Google rules + starter company rules) and icons alongside.
# Remove what older versions shipped (the Python docstyle helper).
Remove-Item "$target\rules", "$target\docstyle", "$target\StyleGuide" -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item "$repo\styleguide" -Destination "$target\StyleGuide" -Recurse -Force
# The same engine as a command-line/MCP program (LLM export in madbuild post-build events; style tools for AI agents).
$mcp = Join-Path $repo "tools\FlareAI.Mcp\bin\$Configuration\net48\FlareAI.Mcp.exe"
if (Test-Path $mcp) { Copy-Item $mcp, "$mcp.config" -Destination $target -Force -ErrorAction SilentlyContinue }
if (Test-Path "$repo\src\FlareAI.Flare\Icons") { Copy-Item "$repo\src\FlareAI.Flare\Icons" -Destination $target -Recurse -Force }
# The practice project for the guided first lint.
Remove-Item "$target\Samples" -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item "$repo\src\FlareAI.Flare\Samples" -Destination "$target\Samples" -Recurse -Force

# Mark of the Web: Windows silently blocks .NET assemblies that came from a downloaded zip, and Flare then just
# doesn't list the plugin. Clear it on everything we installed.
Get-ChildItem $target -Recurse -File | Unblock-File
Write-Host "Installed to $target"
Get-ChildItem $target | Select-Object Name, Length | Format-Table -AutoSize
Write-Host "Now: start Flare > File > Options > Plugins > House Style > Enable > restart Flare."
