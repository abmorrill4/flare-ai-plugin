# Launches Flare with ANTHROPIC_API_KEY from the repo's .env, so the plugin's env-var fallback
# works without storing a key in settings during development.
. "$PSScriptRoot\Find-Flare.ps1"
. "$PSScriptRoot\Load-DotEnv.ps1"
if (-not $env:ANTHROPIC_API_KEY) { throw "ANTHROPIC_API_KEY missing: put it in .env at the repo root." }
$flare = Get-FlareInstall
Write-Host "Launching $($flare.Exe) with ANTHROPIC_API_KEY from .env..."
& $flare.Exe
