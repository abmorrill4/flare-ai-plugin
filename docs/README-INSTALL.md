# Install House Style

House Style adds a **House Style** tab to MadCap Flare Desktop. Every edit it makes is a tracked change.

## Quick install (recommended)

1. **Unzip the whole download** to a normal folder such as your Desktop (right-click > Extract All). Do not run
   anything from inside the zip.
2. Close Flare.
3. Double-click **Install.cmd**. Say yes to the administrator prompt.
4. It unblocks the files, copies the plugin to `%LOCALAPPDATA%\HouseStyle`, links your Flare to it and prints what to
   do next. The administrator prompt is needed only this first time: updates install by themselves when you close
   Flare (House Style checks GitHub once a day; see the privacy page).
5. Start Flare > **File > Options > Plugins > House Style > Enable**, then **restart Flare again**.
6. **House Style > Settings**: paste your Anthropic API key, or the gateway virtual key you were given (set
   **Provider** to *Bifrost gateway* and enter the Base URL). Save.

If you have several Flare versions, the installer picks the newest. To pick another, open PowerShell in the unzipped
folder and run `.\Install.ps1 -Flare 21` (use the folder number from the table below).

## Which Flare is which

| Install folder | Flare version |
|---|---|
| `MadCap Flare 22` | Flare 2026 |
| `MadCap Flare 21` | Flare 2025 (including 2025 R2) |
| `MadCap Flare 20` | Flare 2024 |

House Style is tested on Flare 2026. The plugin loads per Flare version, so install it into the one you open.

## Manual install (if Install.cmd does not work)

A manual copy doesn't update itself: install each new version the same way.

1. **Unblock first.** Right-click the zip > **Properties** > tick **Unblock** > OK, then unzip. Already unzipped?
   Run this in PowerShell: `Get-ChildItem <unzipped folder> -Recurse | Unblock-File`. Windows silently stops Flare
   from loading blocked DLLs, so the plugin just never appears.
2. Close Flare.
3. Copy the **`FlareAI`** folder to
   `C:\Program Files\MadCap Software\MadCap Flare 22\Flare.app\Plugins\` (change `22` for your version).
4. **Check there is no wrapper folder.** The DLL must be at
   `...\Flare.app\Plugins\FlareAI\FlareAI.Flare.dll`. If you see `Plugins\HouseStyle-0.1.3\FlareAI\...`, your unzip
   tool added an extra folder: move the inner `FlareAI` folder up into `Plugins` and delete the wrapper.
5. Start Flare > **File > Options > Plugins > House Style > Enable**.
6. **Restart Flare.** Two restarts in total: one so Flare finds the plugin, one after enabling it.

## If House Style is not in the Plugins list

- Re-check step 4 (wrapper folder) and step 1 (unblock).
- Confirm you installed into the Flare version you are running (table above).
- Flare must be fully closed when you copy, and restarted afterwards.

Nothing else to install: the style guide, the linter and the LLM export run inside the plugin. More in
`TESTER-GUIDE.md` in the repository.
