# Builds Release and zips a tester-ready plugin folder into dist\HouseStyle-<version>.zip
param([string]$Version = "0.1.0")
$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo

dotnet build src\FlareAI.Flare\FlareAI.Flare.csproj -c Release -p:Version=$Version
$stage = Join-Path $repo "dist\stage\FlareAI"
Remove-Item "$repo\dist\stage" -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $stage | Out-Null

Get-ChildItem "src\FlareAI.Flare\bin\Release\net48" -File | Where-Object { $_.Name -notlike "B3.*" -and $_.Extension -in ".dll", ".pdb", ".json" } | Copy-Item -Destination $stage
Copy-Item "styleguide" -Destination "$stage\StyleGuide" -Recurse -Force
dotnet build tools\FlareAI.Mcp\FlareAI.Mcp.csproj -c Release -p:Version=$Version
Copy-Item "tools\FlareAI.Mcp\bin\Release\net48\FlareAI.Mcp.exe", "tools\FlareAI.Mcp\bin\Release\net48\FlareAI.Mcp.exe.config" -Destination $stage -ErrorAction SilentlyContinue
if (Test-Path "src\FlareAI.Flare\Icons") { Copy-Item "src\FlareAI.Flare\Icons" -Destination $stage -Recurse }
# Zip root = Install.cmd, Install.ps1, README-INSTALL.md, FlareAI\ (no wrapper folder)
Copy-Item "docs\README-INSTALL.md" -Destination "$repo\dist\stage\README-INSTALL.md"
Copy-Item "scripts\release\Install.ps1", "scripts\release\Install.cmd" -Destination "$repo\dist\stage"
Copy-Item "LICENSE", "NOTICE.md" -Destination $stage

$zip = Join-Path $repo "dist\HouseStyle-$Version.zip"
Remove-Item $zip -Force -ErrorAction SilentlyContinue
# Build the zip by hand: Compress-Archive in Windows PowerShell 5.1 writes backslash entry names, which some unzip tools mishandle.
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$stageRoot = (Resolve-Path "$repo\dist\stage").Path.TrimEnd('\')
$za = [System.IO.Compression.ZipFile]::Open($zip, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    Get-ChildItem $stageRoot -Recurse -File | ForEach-Object {
        $entry = $_.FullName.Substring($stageRoot.Length + 1).Replace('\', '/')
        [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($za, $_.FullName, $entry, [System.IO.Compression.CompressionLevel]::Optimal)
    }
} finally { $za.Dispose() }
Write-Host "Packaged: $zip"
