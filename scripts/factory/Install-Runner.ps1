<#
.SYNOPSIS Sets up the factory VM's GitHub Actions runner: installs it, registers it (with -Token), and starts it at
sign-in in the factory account's desktop session, restarting it if it exits.
.DESCRIPTION Run elevated on the host after Install-FactoryTools.ps1. The runner can't be a service: a service has no
desktop, and Flare needs one. Safe to re-run: without -Token it only refreshes the start-up task and session settings.
.EXAMPLE scripts\factory\Install-Runner.ps1 -Token (gh api -X POST repos/abmorrill4/house-style/actions/runners/registration-token --jq .token)
#>
param(
    [string]$Name = "HSFACTORY",
    [string]$Root = "E:\HouseStyle\vm",
    [string]$Repo = "abmorrill4/house-style",
    [string]$Token   # one-hour registration token; only needed to register
)
$ErrorActionPreference = "Stop"
$cred = Import-Clixml (Join-Path $Root "factory-credential.xml")

Invoke-Command -VMName $Name -Credential $cred -ArgumentList $Repo, $Token, $Name -ScriptBlock {
    param($repo, $token, $name)
    $ErrorActionPreference = "Stop"
    $ProgressPreference = "SilentlyContinue"
    $dir = "C:\actions-runner"
    if (-not (Test-Path "$dir\run.cmd")) {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $rel = Invoke-RestMethod "https://api.github.com/repos/actions/runner/releases/latest" -Headers @{ "User-Agent" = "house-style-factory" }
        $zip = "C:\Setup\actions-runner.zip"
        Invoke-WebRequest ($rel.assets | Where-Object name -match '^actions-runner-win-x64-[\d.]+\.zip$' | Select-Object -First 1).browser_download_url -OutFile $zip -UseBasicParsing
        Expand-Archive $zip $dir -Force
    }
    if ($token) {
        & "$dir\config.cmd" --unattended --replace --url "https://github.com/$repo" --token $token --name $name --labels "flare" --work _work
        if ($LASTEXITCODE) { throw "config.cmd failed" }
    }

    # Started at sign-in, in the desktop session, un-elevated (an elevated Flare relaunches itself and drops its
    # environment). The loop restarts run.cmd whenever it exits: Task Scheduler's own restart setting only covers a task
    # that fails to start, not one that exits.
    $loop = "while (`$true) { & '$dir\run.cmd'; Start-Sleep 30 }"
    $action = New-ScheduledTaskAction -Execute powershell.exe -Argument "-NoProfile -WindowStyle Hidden -Command `"$loop`"" -WorkingDirectory $dir
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User "$env:COMPUTERNAME\factory"
    $principal = New-ScheduledTaskPrincipal -UserId "$env:COMPUTERNAME\factory" -LogonType Interactive -RunLevel Limited
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew `
        -ExecutionTimeLimit ([TimeSpan]::Zero)
    Register-ScheduledTask "GitHub runner" -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
    if (-not (Get-Process Runner.Listener -ErrorAction SilentlyContinue)) { Start-ScheduledTask "GitHub runner" }

    # A screensaver or lock would take the desktop away from Flare in the middle of a run.
    $u = New-Object System.Security.Principal.NTAccount("factory")
    $sid = $u.Translate([System.Security.Principal.SecurityIdentifier]).Value
    $hive = "Registry::HKEY_USERS\$sid\Control Panel\Desktop"
    if (Test-Path $hive) { Set-ItemProperty $hive ScreenSaveActive 0; Set-ItemProperty $hive ScreenSaverIsSecure 0 }
    else { Write-Warning "The factory account's settings aren't loaded (not signed in): screensaver not changed." }
    powercfg /change monitor-timeout-ac 0; powercfg /change standby-timeout-ac 0
    "Runner task registered. Registered: $([bool](Test-Path "$dir\.runner"))"
}
