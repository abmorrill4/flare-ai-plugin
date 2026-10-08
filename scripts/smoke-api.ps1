# Verifies the API key and model id from this machine without the plugin.
# Usage:  .\scripts\smoke-api.ps1            (reads ANTHROPIC_API_KEY from .env)
#         .\scripts\smoke-api.ps1 -Model claude-sonnet-5-5
param([string]$Model = "claude-sonnet-5-5")

. "$PSScriptRoot\Load-DotEnv.ps1"
if (-not $env:ANTHROPIC_API_KEY) { Write-Host "ANTHROPIC_API_KEY missing: put it in .env at the repo root."; exit 1 }

$body = @{
    model      = $Model
    max_tokens = 64
    messages   = @(@{ role = "user"; content = "Reply with the single word: ok" })
} | ConvertTo-Json -Depth 5

try {
    $r = Invoke-RestMethod -Method Post -Uri "https://api.anthropic.com/v1/messages" -Headers @{
        "x-api-key"         = $env:ANTHROPIC_API_KEY
        "anthropic-version" = "2023-06-01"
        "content-type"      = "application/json"
    } -Body $body
    Write-Host "OK  model=$($r.model)  stop=$($r.stop_reason)  text='$($r.content[0].text)'  in=$($r.usage.input_tokens) out=$($r.usage.output_tokens)"
} catch {
    Write-Host "FAILED: $($_.Exception.Message)"
    if ($_.ErrorDetails) { Write-Host $_.ErrorDetails.Message }
    exit 1
}
