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

## Keeping it running

```powershell
scripts\factory\Install-Runner.ps1 -Token (gh api -X POST repos/abmorrill4/house-style/actions/runners/registration-token --jq .token)
scripts\factory\Watch-Factory.ps1 -Install   # host task, every 5 minutes
```

- **Install-Runner** (elevated, on the host) installs the runner and registers it with `-Token`. It starts the runner
  at sign-in in the factory account's desktop, un-elevated, and restarts it within 30 seconds if it exits. It also
  turns off the screensaver and sleep. Without `-Token` it only refreshes those settings.
- **Test-FactoryReady** is the first step of every e2e and factory job. It fails in seconds, with the reason, if the
  runner has no desktop, the desktop is locked or disconnected, or C: is nearly full. It also closes any Flare left by
  an earlier run. A failure leaves `C:\HouseStyle\not-ready.txt` in the VM.
- **Watch-Factory** runs on the host while the owner is signed in. It starts the VM if it's off. It reboots the guest
  if the runner has been offline for 10 minutes, for example a VM stuck at the sign-in screen (seen 2026-10-23), or if
  `not-ready.txt` says the last job found the desktop unusable. That's a guest reboot through Hyper-V, never a hard
  reset, and at most one every 30 minutes. Create `E:\HouseStyle\vm\maintenance` to keep it away during checkpoint
  merges or rebuilds, and delete it afterwards. Its log is `E:\HouseStyle\vm\watch-factory.log`.

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

It takes one open report per run, in two steps. **Nothing is built from a report until the owner decides.**

1. **Triage** (any new report, `problem` or automatic `error`). An agent reads the report, tries to reproduce a bug
   with a scratch test, compares it with the open issues and themes, and writes a brief: what happened, the kind
   (bug, friction, feature, question, duplicate, noise), whether it reproduces, what it lines up with, 2 to 4 options
   with effort and risk, a recommendation and its impact on writers. The brief goes on the private report as a
   comment, labeled `triage:ready` or `triage:noise`. No branch, commit, PR or public issue. A failed triage gets
   `triage:failed` and is retried, three times at most.
2. **Fix** (only `decision:fix`, alone and added last). The owner decides with `decision:fix`, `decision:plan`,
   `decision:park` or `decision:decline`, by hand or through the `triage` skill. The fix agent gets the brief and the
   decision as its work order and carries it out; if it can't, the report goes back to the owner
   (`factory:needs-decision`), never `factory:done`. The factory refuses to set any `decision:*` label itself, and the
   feedback service only sets its fixed labels, so only the owner can approve a fix.

Guards against report text steering the agent:

- Only reports the feedback service filed (author `abmorrill4`, label `problem`).
- The report reaches the agent as a file, framed as untrusted data, never inside the prompt.
- No GitHub token while an agent runs or while agent-written code runs (the unit tests and e2e run with no secrets at
  all). The token is used only to push, open the PR and merge. The checkout keeps no credential.
- The agents sign in with the owner's subscription (`CLAUDE_CODE_OAUTH_TOKEN`, from `claude setup-token`) when that
  secret is set, else with `ANTHROPIC_API_KEY`.
- None of the run's git calls use hooks (`core.hooksPath` points at an empty folder), so a hook planted in `.git/hooks`
  never runs with the token.
- The agents get no web tools and only a short list of commands. That's a speed bump, so the real checks happen
  outside the agent:
  - the working tree must be clean afterwards;
  - prompts are read from `main` before the agent starts;
  - paths are checked without rename detection, so a move can't hide a deletion;
  - no mode changes or symlinks, and the diff has a size cap;
  - the e2e harness itself is off limits.
- The script reruns the unit tests and the e2e checks itself, rather than taking the agent's word.
- A second agent reviews the diff only, never the report.
- A fix merges only after every required check on that exact commit has passed.
- The public PR says only "Fix tester report n". What the tester wrote stays in the private reports repo.
- The e2e workflow runs on pushes to this repo's branches, never on `pull_request`. For a PR, GitHub runs the
  workflow file from the PR itself, so a fork could rewrite any guard in it.
- A fix that touches `src/FlareAI.Core/Update/`, `Settings/`, `Llm/` or `src/FlareAI.Flare/E2EHook.cs`.
  Nothing is pushed.
- The agents' output, the rerun tests' output and dry-run comments go to `C:\HouseStyle\factory-logs` on the VM, never
  the job log. This repo is public, so its job logs are too. A failed run logs only "report n is stuck", and the
  reason is on the private report. The report file is deleted before agent-written tests run.
- e2e runs on `factory/*` branches get no API key, so their `ai-*` scenarios are skipped.
- The e2e workflow has no concurrency group, because the single `flare` runner already queues its jobs. A group keeps
  only one waiting run and cancels the rest. The factory has its own group, so two factory runs never overlap.
- Each e2e scenario has a watchdog (`-ScenarioMinutes`, 12 by default, above the scenarios' own waits). A hung
  scenario leaves a screenshot and the open windows' titles, then Flare is killed. The watchdog stands down if its
  run has already died.
