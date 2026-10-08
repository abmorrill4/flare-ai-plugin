# One-time setup: git repo, Flare DLL, API dump, solution, first build.
$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
Set-Location $repo
. "$PSScriptRoot\Find-Flare.ps1"

# ---- git ----------------------------------------------------------------
if (-not (Test-Path ".git")) {
    git init -b main | Out-Null
    Write-Host "git: initialised"
} else { Write-Host "git: already a repo" }

# ---- Flare ---------------------------------------------------------------
$flare = Get-FlareInstall
Write-Host "Flare : $($flare.Root)  (version folder $($flare.Version))"
Write-Host "DLL   : $($flare.PluginApiDll)"
New-Item -ItemType Directory -Force -Path "lib" | Out-Null
Copy-Item $flare.PluginApiDll "lib\B3.PluginAPIKit.dll" -Force
# Flare internals used only by the docked sidebar (behind a fallback); compile-time reference, never shipped.
foreach ($d in "B3.AppKit", "B3.CoreKit", "B3.CommonInterfacesKit", "B3.CssKit", "B3.XmlKit") {
    Copy-Item (Join-Path $flare.AppDir "$d.dll") "lib\$d.dll" -Force
}
Write-Host "lib   : copied B3.PluginAPIKit.dll + B3 internals for the sidebar"

# Flare's own runtime — tells us what CLR the host actually runs on
$flareExe = [System.Reflection.AssemblyName]::GetAssemblyName($flare.Exe)
try {
    $rt = [System.Reflection.Assembly]::ReflectionOnlyLoadFrom($flare.Exe).ImageRuntimeVersion
    Write-Host "Flare.exe image runtime: $rt"
} catch { Write-Host "Flare.exe runtime: (could not read: $($_.Exception.Message))" }

# ---- API dump ------------------------------------------------------------
& "$PSScriptRoot\dump-api.ps1"

# ---- solution ------------------------------------------------------------
if (-not (Test-Path "FlareAI.sln")) {
    dotnet new sln -n FlareAI | Out-Null
    dotnet sln add src\FlareAI.Core\FlareAI.Core.csproj | Out-Null
    dotnet sln add src\FlareAI.Flare\FlareAI.Flare.csproj | Out-Null
    dotnet sln add tests\FlareAI.Core.Tests\FlareAI.Core.Tests.csproj | Out-Null
    Write-Host "sln   : created FlareAI.sln"
}

# ---- build ---------------------------------------------------------------
Write-Host ""
Write-Host "Restoring + building (first build will surface any mismatch with the real DLL)..."
dotnet build FlareAI.sln -c Debug
Write-Host ""
Write-Host "Next: review docs\api-dump.txt, then .\scripts\smoke-api.ps1 and dotnet test"
