<#
.SYNOPSIS Puts what the code factory needs into its VM: Flare (trial), Git, .NET SDK 8, GitHub CLI, Claude Code.
.DESCRIPTION Run elevated on the host after New-FactoryVM.ps1. Copies the Flare installer from this PC's Package Cache
into the VM over Hyper-V (no network share), installs everything silently over PowerShell Direct, turns the House Style
plugin on for the factory account, and checkpoints the VM as "tools-ready" (Reset-FactoryVM.ps1 goes back to it).
Downloads come only from the publishers: github.com releases (Git, gh), dot.net (dotnet-install), claude.ai (Claude Code).
#>
param(
    [string]$Name = "HSFACTORY",
    [string]$Root = "E:\HouseStyle\vm",
    [string]$FlareSetup = (Get-ChildItem "C:\ProgramData\Package Cache" -Recurse -Filter SetupMadCapFlare.exe -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName)
)
$ErrorActionPreference = "Stop"
$cred = Import-Clixml (Join-Path $Root "factory-credential.xml")
if (-not $FlareSetup) { throw "No SetupMadCapFlare.exe in the Package Cache. Pass -FlareSetup." }

# Flare's bundle and its MSI (the bundle looks for the MSI next to itself or in its cache).
$msi = Get-ChildItem "C:\ProgramData\Package Cache" -Recurse -Filter SetupMadCapFlare.msi -ErrorAction SilentlyContinue | Select-Object -First 1
foreach ($f in @($FlareSetup) + @($msi.FullName)) {
    if ($f) { Copy-VMFile -Name $Name -SourcePath $f -DestinationPath ("C:\Setup\" + (Split-Path $f -Leaf)) -CreateFullPath -FileSource Host -Force }
}

Invoke-Command -VMName $Name -Credential $cred -ScriptBlock {
    $ErrorActionPreference = "Stop"
    $ProgressPreference = "SilentlyContinue"
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    function Latest($repo, $pattern) {
        $rel = Invoke-RestMethod "https://api.github.com/repos/$repo/releases/latest" -Headers @{ "User-Agent" = "house-style-factory" }
        ($rel.assets | Where-Object name -match $pattern | Select-Object -First 1).browser_download_url
    }
    function Fetch($url, $file) { $p = "C:\Setup\$file"; Invoke-WebRequest $url -OutFile $p -UseBasicParsing; $p }

    # The runner and the e2e suite run PowerShell scripts; Windows blocks them by default.
    Set-ExecutionPolicy RemoteSigned -Scope LocalMachine -Force
    # The evaluation needs online activation for its 90 days; its automatic attempt at first sign-in can fail.
    cscript //nologo C:\Windows\System32\slmgr.vbs /ato | Out-Host

    # Each step skips what is already there, so a rerun only finishes what's missing.
    # The MSI installs to Program Files (x86) when run directly.
    $flareExe = "C:\Program Files (x86)\MadCap Software\MadCap Flare 22\Flare.app\Flare.exe"
    if (-not (Test-Path $flareExe)) {
        # Flare's bundle installs nothing with /quiet (it selects no features), so its prerequisites and MSI go in directly.
        Write-Host "Visual C++ runtimes..."
        foreach ($vc in @{ u = "https://aka.ms/highdpimfc2013x64enu"; f = "vc120.exe" }, @{ u = "https://aka.ms/vs/17/release/vc_redist.x64.exe"; f = "vc140.exe" }) {
            $p = Start-Process (Fetch $vc.u $vc.f) -ArgumentList "/install /quiet /norestart" -Wait -PassThru
            if ($p.ExitCode -notin 0, 1638, 3010) { throw "$($vc.f) exit code $($p.ExitCode)" }
        }
        Write-Host "Flare..."
        $p = Start-Process msiexec.exe -ArgumentList '/i "C:\Setup\SetupMadCapFlare.msi" ADDLOCAL=ProductFeature /qn /norestart /l*v C:\Setup\flare-msi.log' -Wait -PassThru
        if ($p.ExitCode -notin 0, 3010) { throw "Flare MSI exit code $($p.ExitCode) (C:\Setup\flare-msi.log in the VM)" }
        if (-not (Test-Path $flareExe)) { throw "Flare MSI finished but $flareExe isn't there." }
    }
    if (-not (Test-Path "C:\Program Files\Git\cmd\git.exe")) {
        Write-Host "Git..."
        $git = Fetch (Latest "git-for-windows/git" '^Git-[\d.]+-64-bit\.exe$') "git.exe"
        Start-Process $git -ArgumentList "/VERYSILENT /NORESTART /NOCANCEL /SP-" -Wait
    }
    if (-not (Test-Path "C:\Program Files\GitHub CLI\gh.exe")) {
        Write-Host "GitHub CLI..."
        $gh = Fetch (Latest "cli/cli" '_windows_amd64\.msi$') "gh.msi"
        Start-Process msiexec.exe -ArgumentList "/i `"$gh`" /qn /norestart" -Wait
    }
    if (-not (Test-Path "C:\Program Files\dotnet\dotnet.exe")) {
        Write-Host ".NET SDK 8..."
        $dn = Fetch "https://dot.net/v1/dotnet-install.ps1" "dotnet-install.ps1"
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $dn -Channel 8.0 -InstallDir "C:\Program Files\dotnet"
        if ($LASTEXITCODE) { throw "dotnet-install failed ($LASTEXITCODE)" }
    }
    $path = [Environment]::GetEnvironmentVariable("Path", "Machine")
    if ($path -notlike "*C:\Program Files\dotnet*") { [Environment]::SetEnvironmentVariable("Path", "$path;C:\Program Files\dotnet", "Machine") }
    if (-not (Test-Path "$env:USERPROFILE\.local\bin\claude.exe")) {
        Write-Host "Claude Code..."
        $cc = Fetch "https://claude.ai/install.ps1" "claude-install.ps1"
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $cc
    }

    # Tests and the runner run un-elevated (an elevated Flare relaunches itself and drops its arguments and environment),
    # so the factory account gets write access to the Plugins folder for install-plugin.ps1.
    icacls "C:\Program Files (x86)\MadCap Software\MadCap Flare 22\Flare.app\Plugins" /grant "factory:(OI)(CI)M" /T /Q | Out-Null
    # Flare loads House Style only when it is turned on for the user.
    $key = "HKCU:\Software\MadCap Software\Flare\Plugins\House Style"
    New-Item $key -Force | Out-Null
    New-ItemProperty $key -Name Activated -Value True -PropertyType String -Force | Out-Null
    # The scenarios write their runs here.
    New-Item -ItemType Directory -Force "C:\HouseStyle\e2e" | Out-Null
    Write-Host "Done."
}
Get-VMSnapshot -VMName $Name -Name "tools-ready" -ErrorAction SilentlyContinue | Remove-VMSnapshot
# Hyper-V sometimes makes the checkpoint but fails to name it: rename the newest one then.
try { Checkpoint-VM -Name $Name -SnapshotName "tools-ready" }
catch { Get-VMSnapshot -VMName $Name | Sort-Object CreationTime | Select-Object -Last 1 | Rename-VMSnapshot -NewName "tools-ready" }
Write-Host "Tools installed in $Name and checkpointed as tools-ready."
