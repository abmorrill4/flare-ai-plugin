# Builds Release and zips a tester-ready plugin folder into dist\FlareAI-<version>.zip
param([string]$Version = "0.1.0")
$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo

dotnet build src\FlareAI.Flare\FlareAI.Flare.csproj -c Release
$stage = Join-Path $repo "dist\stage\FlareAI"
Remove-Item "$repo\dist\stage" -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $stage | Out-Null

Get-ChildItem "src\FlareAI.Flare\bin\Release\net48" -File | Where-Object { $_.Name -ne "B3.PluginAPIKit.dll" -and $_.Extension -in ".dll", ".pdb", ".json" } | Copy-Item -Destination $stage
Copy-Item "styleguide" -Destination "$stage\StyleGuide" -Recurse -Force
if (Test-Path "src\FlareAI.Flare\Icons") { Copy-Item "src\FlareAI.Flare\Icons" -Destination $stage -Recurse }
Copy-Item "docs\TESTER-GUIDE.md" -Destination "$repo\dist\stage\README-INSTALL.md" -ErrorAction SilentlyContinue

$zip = Join-Path $repo "dist\FlareAI-$Version.zip"
Remove-Item $zip -Force -ErrorAction SilentlyContinue
Compress-Archive -Path "$repo\dist\stage\*" -DestinationPath $zip
Write-Host "Packaged: $zip"
