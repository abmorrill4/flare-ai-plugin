# Serves a fake newer House Style release on this computer, to test automatic updates end to end (issue #101).
#   .\scripts\fake-update-server.ps1 -Version 9.9.0            # packages 9.9.0 and serves it on http://localhost:8765
#   then start Flare with HOUSESTYLE_UPDATE_URL=http://localhost:8765/repos/abmorrill4/house-style/releases/latest
# -Corrupt serves a zip that doesn't match checksums.txt, and -Unsigned leaves out checksums.txt.sig: either update must
# be refused. -NoPackage reuses dist\. Ctrl+C stops the server.
# Installs take only updates signed with the release key, so this signs with the owner's DPAPI-protected backup of it
# (%APPDATA%\HouseStyle-release\update-signing-key.dpapi): it works on the owner's account only. Elsewhere, use -Unsigned.
param(
    [string]$Version = "9.9.0",
    [int]$Port = 8765,
    [switch]$NoPackage,
    [switch]$Corrupt,
    [switch]$Unsigned
)
$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
if (-not $NoPackage) { & (Join-Path $PSScriptRoot "package.ps1") -Version $Version }
$zipName = "HouseStyle-$Version.zip"
$zip = Join-Path $repo "dist\$zipName"
if (-not (Test-Path $zip)) { throw "Missing $zip (run without -NoPackage)." }
$zipBytes = [IO.File]::ReadAllBytes($zip)
$sha = [Security.Cryptography.SHA256]::Create()
$hash = ([BitConverter]::ToString($sha.ComputeHash($zipBytes)) -replace '-', '').ToLowerInvariant()
if ($Corrupt) { $hash = ("0" * 64) }
$checksums = [Text.Encoding]::ASCII.GetBytes("$hash  $zipName`n")
$base = "http://localhost:$Port"
$assets = @(
    @{ name = $zipName; size = $zipBytes.Length; browser_download_url = "$base/download/$zipName" },
    @{ name = "checksums.txt"; size = $checksums.Length; browser_download_url = "$base/download/checksums.txt" })
$routes = @{
    "/download/$zipName" = @("application/zip", $zipBytes)
    "/download/checksums.txt" = @("text/plain", $checksums)
}
if (-not $Unsigned) {
    $keyFile = Join-Path $env:APPDATA "HouseStyle-release\update-signing-key.dpapi"
    if (-not (Test-Path $keyFile)) { throw "No release key backup at $keyFile. Run with -Unsigned to test the refusal instead." }
    Add-Type -AssemblyName System.Security
    $rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider -ArgumentList (New-Object System.Security.Cryptography.CspParameters -ArgumentList 24)
    try {
        $rsa.PersistKeyInCsp = $false
        $rsa.FromXmlString([Text.Encoding]::UTF8.GetString([Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($keyFile), $null, 'CurrentUser')))
        $sig = [Text.Encoding]::ASCII.GetBytes([Convert]::ToBase64String($rsa.SignData($checksums, "SHA256")))
    } finally { $rsa.Dispose() }
    $assets += @{ name = "checksums.txt.sig"; size = $sig.Length; browser_download_url = "$base/download/checksums.txt.sig" }
    $routes["/download/checksums.txt.sig"] = @("text/plain", $sig)
}
$latest = [Text.Encoding]::UTF8.GetBytes((@{
    tag_name = "v$Version"; name = "House Style $Version"; draft = $false; prerelease = $false
    html_url = "https://github.com/abmorrill4/house-style/releases"; body = "Fake release for testing."
    assets = $assets
} | ConvertTo-Json -Depth 5))
$routes["/repos/abmorrill4/house-style/releases/latest"] = @("application/json", $latest)

$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add("$base/")
$listener.Start()
Write-Host "Serving House Style $Version at $base (Ctrl+C to stop)" -ForegroundColor Cyan
Write-Host "Start Flare with:  `$env:HOUSESTYLE_UPDATE_URL = '$base/repos/abmorrill4/house-style/releases/latest'"
try {
    while ($listener.IsListening) {
        $ctx = $listener.GetContext()
        $path = $ctx.Request.Url.AbsolutePath
        $r = $routes[$path]
        Write-Host ("{0:HH:mm:ss}  {1} {2}" -f (Get-Date), $ctx.Request.HttpMethod, $path)
        if ($r) {
            $ctx.Response.ContentType = $r[0]
            $ctx.Response.ContentLength64 = $r[1].Length
            $ctx.Response.OutputStream.Write($r[1], 0, $r[1].Length)
        } else { $ctx.Response.StatusCode = 404 }
        $ctx.Response.Close()
    }
} finally { $listener.Stop() }
