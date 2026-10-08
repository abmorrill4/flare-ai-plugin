# Dot-source this: . .\scripts\Find-Flare.ps1
# Provides Get-FlareInstall -> @{ Root; AppDir; Exe; PluginApiDll; Version }

. "$PSScriptRoot\Load-DotEnv.ps1"

function get-flareinstall {
    # FLARE_ROOT overrides discovery, e.g. a copy of the install on a user-writable drive (no admin needed).
    if ($env:FLARE_ROOT) { $candidates = @(Get-Item $env:FLARE_ROOT) } else { $candidates = @() }
    if (-not $candidates) { foreach ($base in @("${env:ProgramFiles}\MadCap Software", "${env:ProgramFiles(x86)}\MadCap Software")) {
        if (Test-Path $base) {
            $candidates += Get-ChildItem $base -Directory | Where-Object { $_.Name -like "MadCap Flare*" }
        }
    } }
    if (-not $candidates) { throw "No MadCap Flare install found under Program Files." }

    # newest version wins (folder names are like 'MadCap Flare 22')
    $root = $candidates | Sort-Object { [int]($_.Name -replace '[^\d]', '') } -Descending | Select-Object -First 1
    $appDir = Join-Path $root.FullName "Flare.app"
    $exe    = Join-Path $appDir "Flare.exe"
    $dll    = Get-ChildItem $appDir -Recurse -Filter "B3.PluginAPIKit.dll" -ErrorAction SilentlyContinue | Select-Object -First 1

    if (-not (Test-Path $exe)) { throw "Flare.exe not found at $exe" }
    if (-not $dll) { throw "B3.PluginAPIKit.dll not found under $appDir" }

    return @{
        Root         = $root.FullName
        AppDir       = $appDir
        Exe          = $exe
        PluginApiDll = $dll.FullName
        PluginsDir   = Join-Path $appDir "Plugins"
        Version      = ($root.Name -replace '[^\d]', '')
    }
}
