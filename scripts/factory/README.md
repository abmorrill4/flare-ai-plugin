# The code factory

A Hyper-V VM on the owner's PC runs real Flare for the end-to-end checks (`tests/e2e`) and, as a GitHub Actions
runner, the e2e workflow on every pull request from this repository's own branches. It also runs the factory, which
works through tester reports from the private `house-style-reports` repo.

## Build or rebuild the VM (elevated PowerShell on the host)

```powershell
scripts\factory\New-FactoryVM.ps1 -Iso E:\HouseStyle\vm\win11-ltsc-eval.iso   # about 20 minutes; -Force rebuilds
scripts\factory\Install-FactoryTools.ps1                                      # Flare, Git, gh, .NET 8, Claude Code
```

- Windows 11 Enterprise LTSC evaluation, activated for 90 days. Rebuild from a newer evaluation ISO when it runs out.
- A local account `factory` signs in by itself, because Flare needs a desktop. Its password is in
  `E:\HouseStyle\vm\factory-credential.xml`, which only the owner's Windows account can read.
- Checkpoints: `windows-ready`, then `tools-ready`, then `e2e-ready` (suite verified). To reset, apply one:
  `Restore-VMSnapshot -VMName HSFACTORY -Name e2e-ready -Confirm:$false`.

## Things learned the hard way

- Flare's setup bundle installs nothing with `/quiet`, so the Visual C++ runtimes and Flare's MSI go in directly.
  The MSI then installs to `Program Files (x86)`.
- A fresh Flare asks for its UI language at start. `tests/e2e` turns that off with
  `HKCU\Software\MadCap Software\Flare\RunSelectLanguageUIOnStartup = False`.
- Run Flare and the runner **un-elevated**. An elevated Flare relaunches itself and drops its arguments and
  environment, so the plugin never sees `HOUSESTYLE_E2E_DIR`. The `factory` account can write Flare's Plugins folder
  instead.
- PowerShell Direct has no desktop. Anything that drives Flare runs in a task with an interactive logon for `factory`.

## The factory (`Invoke-Factory.ps1`)

It takes one open report per run. Guards against report text steering the agent:

- Only reports the feedback service filed (author `abmorrill4`, label `problem`).
- The report reaches the agent as a file, framed as untrusted data, never inside the prompt.
- The agents have no GitHub token and no web tools, and only a short list of commands.
- The agent can't push, open PRs or comment. The script does that, after checking the changed paths and diff size.
- The script reruns the unit tests and the e2e checks itself, rather than taking the agent's word.
- A second agent reviews the diff only, never the report. A fix merges only on its MERGE and green required checks.
- Features and questions go back to the owner as a comment and a `factory:needs-decision` label.
