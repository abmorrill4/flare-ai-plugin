> **Shelved 2026-10-13:** House Style supports the current Flare version only; older versions cannot be tested without installs. Kept for reference.

# Gap 4: Support Flare 2020 (v16) through 2026 (v22) from one build

Status: plan, amended after the design gate. Author: planning agent, 2026-10-12.

## Gate amendments (2026-10-12)

Source: `docs/plans/ux/plan-critique.md`, section 1.4 (checklist) and section 5, Gap 4, items 1 to 8. Orchestrator decisions are marked (O).

1. **Plain words only.** Every user-facing line (Diagnostics, Settings, startup dialog, banner) uses capability names from section 3. API member names go to the log only. "Tool Strip", "capability", "untested" and "ContentChanged" do not appear in user text. (Item 1)
2. **Untested-version notice** is a dismissible banner in the review pane, shown once per Flare major, not a toast. Its text no longer over-promises: "You can undo changes with Ctrl+Z." (Item 2)
3. **Below the floor** there is no ribbon and no Diagnostics button, so a single one-time startup dialog says why the plugin is off. It is the only modal in this plan. "Says why in Diagnostics" is no longer the only channel. (Item 3)
4. **Disabled settings** carry the reason in the visible suffix and in the control's accessible description. Settings, Diagnostics and the startup dialog generate the wording from one list of capability names. The Editor toggle for margin markers shows the same reason. (Item 4 and critique 1.4, item 9)
5. **Probe layer with L1** (O). The probe layer lands in the same change as the ribbon redesign: the six-control ribbon and one shared command table that feeds the ribbon, the Tool Strip menu and the right-click submenu. `Plugin.Execute` and `EditorIntegration` are one refactor, not two. (Item 5)
6. **ApiSurface CI guard deferred.** The member-list guard and `docs/api/*.txt` wait until two real dumps exist. Regenerating `ref/` from the floor stays. See section 10. (Item 6)
7. **Installer trimmed.** Plural discovery, the running-process skip and the packaged `Install-HouseStyle.ps1` stay. `-Version`, `-List` and Pester tests move to section 10. (Item 7)
8. **Support claim only for passed versions** (O). A version is named "supported" only after it passes the smoke checklist (6.4, items 5 to 8 at minimum), stated as "tested on build X". At launch: current Flare plus the oldest install that passes. 2020 and 2021 stay "untested, expected to work" until then. Effort is 8 engineering days. (Item 8)
9. **Support table and text** in sections 0, 1, 7 and 9 are rewritten to match items 5 to 8 above. Cut items are listed in section 10 "Later".

Legend used throughout. **[V-web]** verified on the web (URL given). **[V-local]** verified on this machine (Flare 22 install, repo files).
**[Inferred]** reasoning or pattern-matching, not confirmed. **[Unknown]** needs a real older install to answer.

## 0. Bottom line

- **Ship one build with runtime capability probes.** No per-version builds. Keep a seam so a band-specific shim DLL can be added later if a signature break is ever found.
- **Compile against the oldest supported Flare's API surface (the "floor"), once a floor install exists.** Then every direct reference is guaranteed to exist everywhere, and anything newer is reached through probes. Until a floor install exists, compile against Flare 22 and check every member outside the probe layer against the harvest dumps. This is the main technique; the rest is plumbing.
- **Recommended minimum: Flare 2020 (v16), conditional.** It becomes the stated minimum only if the "harvest" step (section 6.1) shows the core edit path and a ribbon exist in a real 2020 install and the write checklist passes. **Fallback minimum: Flare 2021 (v17)**, then 2022 (v18). Until a version has passed the smoke checklist it is labelled "untested, expected to work", never "supported".
- **Effort: M, 8 engineering days** (harvest 1.5, probe layer 3, installer 1, docs 0.5, two version checklists 2), plus 3 to 5 days of VM and acquisition time. The probe layer lands with L1 (section 9).
- **Top risks:** (1) the tracked-change write path was only characterised on Flare 22 and has nasty Redo crashes there; older builds may differ in either direction; (2) a missing API member makes the *calling method* fail to JIT, so our current in-method `try/catch` is not enough; (3) shared `Newtonsoft.Json` versions inside Flare's process; (4) no old installs to test on.

## 1. Goal and user stories

Goal: a writer on any Flare from 2020 to current installs House Style once and gets every feature their version can host, with an honest, quiet statement of what their version lacks.

- **Writer on Flare 2021:** "I run the installer, it finds my Flare, I enable the plugin, and Check style works. If Check as I type isn't possible on my version, I'm not nagged about it; Diagnostics says what is missing."
- **Style-guide owner / docs lead with a mixed team** (some on 2022, some on 2025): the same style guide project and the same plugin zip work for everyone; findings and rule ids are identical across versions because the engine is in Core.
- **Build engineer:** the CLI/MCP program is Flare-independent already, so build pipelines are unaffected by Flare version. CI proves the plugin binary compiles against the reference stubs; behaviour on each Flare is proven by the smoke checklist.

## 2. What the competitor does and where we should be better

- **Mad Quality** states "Supports Flare 2020+" (from the planning brief, verified 2026-10-12 by the owner). It does not say what degrades on which version. I could not verify how it handles per-version differences. **[Unknown]**
- **Kaizen / AI Helper** are listed by MadCap as third-party plugins [V-web: https://www.madcapsoftware.com/downloads/plugins-api-redistributables.aspx]; the page does not give supported-version ranges.
- Where we should be better: (a) a claim backed by a published test matrix per version (what was tested, on what build), (b) graceful degradation with one honest line in Diagnostics instead of a crash or silent no-op, (c) an installer that handles every Flare on the machine, (d) a claim that never runs ahead of the checklist.

## 3. UX surface

Principle: version differences are invisible unless they matter, and when they matter they are told once, in one place, in plain words.

Display names used in every user-facing string (internal names in section 4.3):

| Feature (display name) | Needs |
|---|---|
| Check as I type | live check of edits |
| Check when saving | check on save |
| Margin markers | markers in the editor margin |
| Right-click items | House Style submenu in the editor right-click menu |
| Report before build | a warning before a build |
| Docked review pane | the findings pane docked in Flare (otherwise it floats) |
| Ribbon | the House Style ribbon tab (otherwise the menu and toolbar are used) |

| Where | What the writer sees |
|---|---|
| Ribbon (six controls), or the menu and toolbar where there is no ribbon | The same six controls on every version that has a ribbon. Commands that cannot work on this version are left out, not greyed out. Diagnostics lists them. |
| Right-click | One "House Style" submenu with the same commands as the ribbon, from the same command table (section 4.3). Entries whose feature is missing are left out. |
| Diagnostics (existing button) | **First line:** `Flare 2021 (17.0.xxxx) - House Style 0.1.0 - tested on build 17.0.xxxx`, or `... - not yet tested` for an untested version. Then **one line per unavailable feature**: `Not available in this version of Flare: Check as I type, margin markers, report before build.` If nothing is missing the line reads `All features available.` |
| Settings dialog | A setting whose feature is missing stays visible but disabled, with a short grey suffix: `Check as I type (needs Flare 2022 or later)`. The control's accessible description carries the same sentence. The Editor toggle for margin markers shows the same reason. The user guide describes the setting; a writer who cannot find it would think the plugin is broken. |
| Startup, below the floor (one time) | The only dialog in this plan, shown once per Flare install: `House Style needs Flare {floor} or later. This version of Flare is missing: {features}.` There is no ribbon and no Diagnostics button here, so this is the only place the reason can appear. The floor year comes from the harvest decision (section 6.1). |
| Banner in the review pane, untested version (e.g. a future Flare 2027) | Dismissible pane banner (IA 4.6), shown once per Flare major, never as a toast: `House Style hasn't been tested with this version of Flare yet. You can undo changes with Ctrl+Z.` Tested versions never see it. |
| User guide | One topic, `Supported Flare versions` (table: version, status, tested build, anything unavailable). The install topic links to it. Nothing else in the guide mentions versions. |

No new ribbon button, no new setting, no ribbon badge. The startup dialog and the banner are the only new messages. Strings above go through the glossary (Q7) and docstyle lint before release.

## 4. Design

### 4.1 What is actually known about the API across versions

**Verified on the web**
- A Plug-In API help page exists for Flare 2020 and its example path is `...\MadCap Flare16\Flare.app\Plugins` (v16 = 2020) [V-web: https://help.madcapsoftware.com/flare2020/Content/Flare/Plug-In-API/Plugin-API.htm]. The 2026 page uses `MadCap Flare22` [V-web: https://help.madcapsoftware.com/flare2026/Content/Flare/Plug-In-API/Plugin-API.htm]. Both say the API "only supports libraries targeted to .NET Framework 4.0" and both list the same interfaces including `IRibbon`, `IRibbonTab`, `IRibbonGroup`, `IRibbonComboBox`, `IRibbonMenu`.
- The 2020 pages document: `IRibbon.AddNewRibbonTab(string,string)` and `IsCollapsed` [V-web: .../flare2020/.../Interfaces/IRibbon.htm]; `INavContext` with `GetParentForm`, `CreateCustomToolBar`, `CreateToolStripMenu`, `GetRibbon` [.../INavContext.htm]; `IGroup` ribbon methods incl. `AddRibbonButton` [.../IRibbonGroup.htm]; `IToolStripMenuItem.AddMenuItem` and `AddSeparator` [.../IToolStripMenuItem.htm]; `IEditorContext` (`GetActiveDocument`, `GetDocuments`, `OpenDocument` x2, `GetMadCapSchema`, `DocumentSwitched`) [.../IEditorContext.htm]; `ISelection` incl. `ReplaceText`, `GetXmlNodeList`, `SetSelectionLength`, `TrackChange` [.../ISelection.htm]; `IDocument` incl. `ApplyStylesheet`, `RemoveStylesheet`, `GetUserPendingContextMenuItems`, `StartOperation`/`EndOperation`, `Select(string)`/`Select(XmlNode)`, `Save`, `Close`, `UpdateView`, and events `Saved`, `Saving`, `Closing`, `OnContentChanged` [.../IDocument.htm].
- **Caveat that matters:** the 2020 and 2026 pages are word-for-word identical, typos included (e.g. `LookupVariableValue` listed as `void`, `SavingsAs`). So the docs are copied forward and are **not evidence of what each binary contains**. They also disagree with the real Flare 22 binary: the docs list `OnContentChanged`, the binary has `ContentChanged` [V-local: `docs/api-dump.txt`]; the docs' `AddMenuItem(..., RibbonIconSize, ...)` differs from the binary's string-`imagePath` overloads [V-local]; `IHost.GetCompilerContext` and `ICompilerContext` (`CompileStart`, `CompileComplete`) exist in the binary but have **no documentation page in 2020 or 2026** (both URLs return 404) [V-web: .../flare2020/.../Interfaces/ICompilerContext.htm, .../flare2026/...]. Conclusion: the only trustworthy source is a reflection dump of each real install.
- Flare 2021 requires Windows 10 and Microsoft .NET Framework 4.8 [V-web: https://kb.madcapsoftware.com/knowledgebase/Content/Flare/Installation_-_Activation_-_Registration/IAR1033F_-_Flare_2021_System_Requirements.htm]. A search summary says the 2023 and 2024 pages also list 4.8 [V-web via search, 2023 page: https://kb.madcapsoftware.com/knowledgebase/Content/Flare/Installation_-_Activation_-_Registration/IAR1038F_-_Flare_2023_System_Requirements.htm]. I could not retrieve a Flare 2020 requirements page, so 2020's .NET Framework requirement is **[Unknown]** (likely 4.7.2 or 4.8).
- Flare 2020 shipped May 2020; 2020 r2 October 2020; 2020 r3 December 2020 replaced the embedded Internet Explorer browser with Chromium [V-web: https://www.componentsource.com/news/feature-releases/2020/05/06/madcap-flare]. This is why Flare 22 contains CefSharp [V-local]. Chromium does not change the plugin API, but internal UI classes (`B3.AppKit`) may differ between 2020 r1 and r3.
- Flare 2024 r2 release notes mention a bug fix for a crash with `.Select(node)` in the plugin API [V-web via search: https://help.madcapsoftware.com/flare2024r2/Content/Flare/Plug-In-API/Plugin-API.htm area; exact release-notes URL not retrieved]. We use `Select(string)` mostly; check whether `Select(XmlNode)` is on our path before trusting it on older builds.
- Trial availability: the public trial page is a 30-day trial of the current version only, no older version named [V-web: https://madcapsoftware.com/trials/flare-free-trial]. Older release download pages have existed (Flare 2018 r2 and v10.0) and the 2018 r2 page warns it installs over other 2018 builds [V-web via search: https://www.madcapsoftware.com/downloads/flare100-1dngc]. A reseller states a new licence entitles you to download older versions [V-web via search: componentsource.com licensing pages; secondary source, confirm with MadCap]. No public 2020/2021 trial found.
- Existing plugins (Mad Quality) advertise 2020+, so 2020 is a market-credible floor [brief].

**Verified locally (Flare 22)**
- Install folder is `C:\Program Files\MadCap Software\MadCap Flare 22` (with a space), exe `Flare.app\Flare.exe` version `22.0.9643.36696`, `supportedRuntime ... .NETFramework,Version=v4.8`, ships `Newtonsoft.Json` 13.0.3 and CefSharp. The MadCap docs write `MadCap Flare22` (no space), so the docs' folder example is not reliable either.
- `B3.PluginAPIKit` assembly identity `Version=22.0.0.0, PublicKeyToken=null` (unsigned) [V-local: `docs/api-dump.txt` header].

**Inferred**
- Major numbers: 2020=16, 2021=17, 2022=18, 2023=19, 2024=20, 2025=21, 2026=22 (consistent with the two doc examples and the brief). An "r2/r3" release shares its year's major number and folder. **[Inferred]**
- Folder names for older versions follow `MadCap Flare <major>` and the exe lives in `Flare.app`. **[Inferred]**; the discovery code must be tolerant (section 4.6).
- Several majors can sit side by side because the folder carries the major number. **[Inferred]**, but the 2018 r2 note suggests same-year builds replace each other, which fits.

### 4.2 The mechanism: why probes work and what exactly can fail

Because B3 assemblies are unsigned, the CLR binds `B3.PluginAPIKit` by simple name and accepts whatever version Flare loaded. So the same `FlareAI.Flare.dll` loads on every Flare. What can differ is the member surface. Failure modes, from worst to mild:

1. **Missing method/event/property:** `MissingMethodException`/`MissingMemberException` when the call executes, or `TypeLoadException`/`MissingMethodException` when the *enclosing method is JIT-compiled*. The repo already learned this for `B3.AppKit` (`PanelHost.cs` comments on `NoInlining`). Today `Plugin.Execute` and `EditorIntegration.Attach` put several risky calls (event subscriptions, `ApplyStylesheet`, `GetUserPendingContextMenuItems`) inside one method with an in-method `try/catch`; if JIT-time resolution fails, the catch never runs and the whole method (and plugin start) fails. **[Inferred]** that .NET Framework fails the whole method for unresolved member references; the safe design assumes it does either way.
2. **Changed signature** (parameter type, event delegate type, return type): same exceptions. Example risk: `SavedAs` is `FileSystemEventHandler` in 22; an older build could use `EventHandler`.
3. **Added member on an interface we implement** (`IPlugin`): `TypeLoadException` on a future Flare that adds a method we do not implement. Only fixable by shipping a new build; mitigate with an `IPlugin` shim (a second class in the same assembly) if it ever happens. **[Inferred]**
4. **Same signature, different behaviour:** the dangerous one. All of `docs/flare-api-notes.md` "Verified write behavior" (node replace keeps target, text-node replace throws, `ReplaceText` leaves `<SelectionMarker/>`, Redo crashes with variables) was measured on Flare 22 only. No probe can detect this; only tests can (section 6).

### 4.3 Compatibility strategy: one build + capability probes

**Compile against the floor, once a floor install exists.**
- `scripts/bootstrap.ps1` can copy a floor set (`lib\floor\B3.PluginAPIKit.dll`, `B3.AppKit.dll`) from the oldest verified install. The csproj references the floor copy when present; CI keeps using `ref\` stubs.
- Regenerate the `ref\` stubs from the floor DLL once a floor install exists, so a compile fails if someone calls a newer-only member directly (commitment kept; the CI member-list guard is deferred, section 10). Newer-only features go through the probe layer below, which is reflection or a probed delegate, so it compiles anywhere.
- Until a floor install exists, compile against the Flare 22 surface and check every member outside the probe layer against the harvest dumps.

**Probe layer (`FlareCaps`, Flare project only; Core stays Flare-free).**
- At `Execute()` run `FlareCaps.Detect(host)` once. It records: `Version` (from the exe's file version via `Process.GetCurrentProcess().MainModule.FileVersionInfo`, [V-local] gives 22.0.9643.36696), then booleans by reflecting over the *loaded* types: does `IDocument` have event `ContentChanged` (else `OnContentChanged`)? `Saved`, `Saving`, `Closing`? method `ApplyStylesheet`? `GetUserPendingContextMenuItems`? `IHost.GetCompilerContext` and `ICompilerContext.CompileStart`/`CompileComplete`? `INavContext.GetRibbon` returning non-null and `IRibbon.AddNewRibbonTab(string,string)` (else the 1-arg overload)? `INavContext.CreateToolStripMenu` and `CreateCustomToolBar`? `B3.AppKit.Editor.PaneDock` and `IPaneDock.InstallPane`?
- Internal feature names map to the display names in section 3: `LiveCheck` = Check as I type, `CheckOnSave` = Check when saving, `Markers` = Margin markers, `ContextMenu` = Right-click items, `ReportBeforeBuild` = Report before build, `DockedPane` = Docked review pane, `Ribbon` = Ribbon. User text is generated from the display names only. Nothing else in the code asks "which version is this".
- Probing is a pure function `Probe(Type documentType, Type hostType, ...)`, so unit tests feed hand-written fake types that lack members. No Flare needed.
- Every call that touches a possibly-missing member lives in its own tiny `[MethodImpl(NoInlining)]` method with its own `try/catch` (pattern already used by `FlarePane.Show`). If a probe says yes but the call still throws, the feature is switched off for the session and recorded in the Diagnostics list (and the log). Events that may be renamed are subscribed through `EventInfo.AddEventHandler` using a handler built with `Delegate.CreateDelegate` for the actual delegate type, so a changed delegate type is handled too.

**Shared command table (lands with L1, same change).**
- One table lists every House Style command once, with the feature it needs. The six-control ribbon, the Tool Strip menu and toolbar fallback, and the right-click "House Style" submenu are all built from this table. A command whose feature is missing is left out of all three surfaces at once, so the surfaces cannot disagree.
- The right-click submenu keeps the one-level, maximum-eight-entry shape from the critique (section 2.7); the table decides which entries exist.
- The ribbon's six controls are the same on every version where the ribbon API exists.

**Why not per-version builds.** (a) Binding ignores version, so one DLL loads everywhere; (b) the surface we touch is about 60 members (`ref/B3.PluginAPIKit/B3.PluginAPIKit.cs` is 141 lines, `ref/B3.AppKit` 140); (c) three or four DLLs multiply the packaging, signing (SignPath), testing and support matrix; (d) probes degrade correctly for versions we never tested, including future ones; a per-version build would simply not load. **Escape hatch:** keep Flare-facing code behind small internal seams (`IEditorHost`, `IRibbonHost`, `IPaneHost`); if a genuine signature break appears in one band, add `FlareAI.Flare.Compat<band>.dll`, loaded by reflection after detection. Not built now (section 10).

### 4.4 Capability matrix

Status key: **Y** present (verified live or by dump), **docs** listed in that year's help but binary unverified, **?** unknown until harvested, **fb** fallback behaviour. Only the Flare 22 column is binary-verified; the rest is the expectation we will test in section 6.1. Cells are filled by the harvest, not by this plan.

| Feature (display name) | Members relied on | Flare 22 | 2020-2021 (v16-17) | 2022-2023 (v18-19) | 2024-2025 (v20-21) | If missing |
|---|---|---|---|---|---|---|
| Core edit: Check style, Rewrite, Ask (tracked, one undo step) | `GetXmlDocument`, `StartOperation`/`EndOperation`, `Select(string)`, `ISelection.ReplaceText`/`TrackChange`/`GetXmlNodeList`, `GetSourceUrl`, `Save`, `Close` | Y (write behaviour live-tested) | docs; behaviour ? | ? | ? | **Hard requirement.** Below the floor, the one-time startup dialog (section 3) says why and the plugin stays off. |
| Ribbon | `INavContext.GetRibbon`, `IRibbon.AddNewRibbonTab`, `IRibbonTab.AddNewRibbonGroup`, `IRibbonGroup.AddRibbonButton`, `RibbonControlData` | Y | docs | ? | ? | Menu and toolbar only (already built). |
| Menu and toolbar | `CreateToolStripMenu`, `AddMenuItem(string imagePath)`, `CreateCustomToolBar`, `AddButton` | Y | docs (overload shape differs from docs, ?) | ? | ? | Ribbon only. If both fail: right-click items remain; plugin still works via those. |
| Right-click items | `IDocument.GetUserPendingContextMenuItems` | Y | docs | ? | ? | Items omitted; ribbon covers it. Diagnostics: `Right-click items`. |
| Check as I type | `IDocument.ContentChanged` (docs name `OnContentChanged`) | Y | docs (name ?) | ? | ? | Setting disabled; user runs Check style. |
| Check when saving | `IDocument.Saved` | Y | docs | ? | ? | Setting disabled. |
| Margin markers | `ApplyStylesheet`, `RemoveStylesheet`, `UpdateView` | Y | docs | ? | ? | No markers; pane list and Check style unchanged. |
| Report before build | `IHost.GetCompilerContext`, `ICompilerContext.CompileStart` (undocumented) | Y | ? (undocumented everywhere) | ? | ? | Setting disabled; "Check project" button covers it. |
| Docked review pane | internal `B3.AppKit.Editor.PaneDock`, `Pane`, `PaneView` | Y | ? (internal; Chromium swap in 2020 r3 may touch UI) | ? | ? | Floating window (already the fallback in `PanelHost`). |

Nothing in this table is a new feature; the table is the test target. Expectation **[Inferred]**: the first six rows exist on every version from 2020, the last two rows are where differences will show.

### 4.5 Member-surface guard in CI

Deferred to section 10. CI compiles against the `ref/` stubs and runs the unit tests (section 6.5). The rule still holds in review: a call to a member outside the probe layer needs a floor-version check from the harvest.

### 4.6 Installer: find every Flare, install to each

Today `scripts/Find-Flare.ps1` returns only the newest install and sorts by digits (`MadCap Flare 22`). `install-plugin.ps1` copies into that one.

- Add `Get-FlareInstalls` (plural) returning every install: scan `%ProgramFiles%` and `%ProgramFiles(x86)%` for `MadCap Software\MadCap Flare*` (name regex `^MadCap Flare\s*(?:20)?(\d{2})$`, tolerant of "MadCap Flare 22", "MadCap Flare22" and a possible "2020" spelling; **[Inferred]** names), require `Flare.app\Flare.exe`, read the exe's `FileVersionInfo` for the real major (do not trust the folder name). Also scan `Uninstall` registry keys whose DisplayName starts with `MadCap Flare` and InstallLocation points at a Flare.app parent, to catch non-default install drives. Exclude Flare Online/Central/Capture/Analyzer/Mimic by requiring `Flare.app\Flare.exe` **[Inferred]** that only Flare Desktop has this layout. Keep the existing `Get-FlareInstall` as "newest" for dev scripts; `FLARE_ROOT` still overrides.
- `install-plugin.ps1` installs to every detected install by default. For each target: refuse if that install's `Flare.exe` is running (match the process by path, not just name, so a running 2025 doesn't block installing to 2021), copy the same file set to `Flare.app\Plugins\FlareAI`, `Unblock-File`, then print one table: `Flare 2020 (16.0.x) - installed`, `Flare 2024 (20.0.x) - installed`, `Flare 22 - Flare is running, skipped`. Elevation check fails only if some target is under Program Files. Last line stays: enable in File > Options > Plugins and restart (the enable switch is per Flare install **[Inferred]**; the doc says enable per Flare). `-Version` and `-List` are in section 10.
- The release zip currently ships `README-INSTALL.md` only (`scripts/package.ps1`). Add `Install-HouseStyle.ps1` (self-contained version of the installer without the build step) to the zip so non-developers get the same behaviour, plus `-Uninstall`. Signing (SignPath) is unchanged: one signed `FlareAI.Flare.dll` for all versions is a benefit of the single-build decision.
- Per-user settings and the API key live outside the install (`PluginSettings.SettingsDirectory`), so a writer with two Flare versions shares one setup. **[Inferred]**; confirm in `PluginSettings`.

### 4.7 Telling users what is unavailable (no clutter)

Covered in section 3: one Diagnostics line, disabled-with-reason settings, the one-time startup dialog below the floor, one banner per untested major, and one guide topic. All user sentences are generated from the display names in section 3 (one source for Settings, Diagnostics, the dialog and the banner), so the wording stays consistent and testable. The detection result, with internal member names, is written to the existing Diagnostics log file (`Diagnostics.Log`) at startup so support can ask for it.

## 5. Files to add / change

Add
- `src/FlareAI.Flare/FlareCaps.cs`: detection, probes, feature flags, `Describe()` for Diagnostics. `src/FlareAI.Flare/FlareVersion.cs`: parse exe version, year name map (16->2020 ... 22->2026), tested list.
- `src/FlareAI.Flare/Commands/CommandTable.cs`: the one shared command table (section 4.3), with the feature each command needs.
- `src/FlareAI.Flare/Compat/SafeEvents.cs`: `Subscribe(object target, string[] names, Action<…>)` via `EventInfo`, `NoInlining` wrappers for stylesheet and context-menu calls.
- `docs/user-guide/Content/Supported-Flare-Versions.htm` and a TOC entry.

Change (one change with L1, section 9)
- `src/FlareAI.Flare/Plugin.cs`: `Execute` becomes a short sequence of independently guarded steps (ribbon, menu and toolbar, document-switched, compile hooks, editor integration), each in its own `NoInlining` method; `CreateRibbon` tries the 2-arg `AddNewRibbonTab` then the 1-arg; Diagnostics output starts with the version line; the startup dialog runs only below the floor.
- `src/FlareAI.Flare/EditorIntegration.cs`: route `GetUserPendingContextMenuItems`, `ContentChanged`/`Saved`/`Closing`, `ApplyStylesheet`/`RemoveStylesheet`/`UpdateView`, `CompileStart` through `FlareCaps`/`SafeEvents`; skip features whose capability is off. Shares the refactor with `Plugin.cs` (not a separate change).
- `src/FlareAI.Flare/UI/PanelHost.cs`: gate `FlarePane` on `FlareCaps.DockedPane` before JIT; fallback already exists.
- `src/FlareAI.Flare/UI/` review pane: a dismissible banner slot for the untested-version notice (shared with L2).
- `src/FlareAI.Flare/UI/SettingsForm.cs`: disabled state, visible suffix and accessible description per feature.
- `src/FlareAI.Flare/FlareAI.Flare.csproj`: floor reference (`lib\floor`) once a floor install exists; keep `ref\` fallback.
- `scripts/Find-Flare.ps1`, `scripts/install-plugin.ps1`, `scripts/bootstrap.ps1` (can also copy a floor set), `scripts/dump-api.ps1` (`-Out` parameter, default unchanged), `scripts/package.ps1` (include the installer).
- `docs/flare-api-notes.md` (add "Per-version differences" section, filled by harvest; add the warning that the docs are copy-forward and unreliable), `CLAUDE.md` (one line: compile against floor, route newer members through `FlareCaps`), `README.md` and `docs/TESTER-GUIDE.md` (supported versions and installer).

Tests (names and assertions)
- `FlareCapsTests.Detects_all_when_all_members_present` (fake types with every member) -> no missing features.
- `FlareCapsTests.Missing_ContentChanged_disables_Check_as_I_type_only` (fake doc without `ContentChanged` and without `OnContentChanged`).
- `FlareCapsTests.Renamed_OnContentChanged_is_accepted` (fake doc with only `OnContentChanged`) -> Check as I type on.
- `FlareCapsTests.No_CompileStart_disables_Report_before_build` and `No_GetCompilerContext_...`.
- `FlareCapsTests.Describe_lists_each_missing_feature_once_in_plain_words` (exact sentence from section 3).
- `FlareCapsTests.User_text_names_no_API_members` (display strings contain none of `ContentChanged`, `OnContentChanged`, `GetUserPendingContextMenuItems`, `CompileStart`, `Tool Strip`).
- `CommandTableTests.Missing_feature_removes_command_from_ribbon_menu_and_right_click_together`.
- `StartupDialogTests.Shown_only_below_the_floor` and `Shown_once_per_install`.
- `BannerTests.Untested_major_shows_banner_once_per_major`; tested majors never show it.
- `FlareVersionTests.Parses_22_0_9643_as_Flare_2026` and `Unknown_major_is_untested_but_enabled`.
- `FlareVersionTests.Year_map_16_to_2020_through_22_to_2026`.
- `SafeEventsTests.Falls_back_to_second_event_name` and `Swallows_and_records_throwing_target`.

## 6. Test strategy

### 6.1 Step 0, harvest (do this before writing code)
For each obtainable install (target 2020, 2021, 2022, 2024, 2025 in addition to 2026): run `scripts\dump-api.ps1` (needs Windows PowerShell 5.1) to produce a local member dump; record `Flare.exe` file version, `Flare.exe.config` `supportedRuntime`, whether `Newtonsoft.Json.dll` ships and its version, and whether `B3.AppKit.Editor.PaneDock` exists. Fill the matrix in 4.4 by hand from the dumps. Outcome decides the minimum version, the floor year used in the startup dialog, and the size of the probe layer. If an older version has no `ContentChanged`, no ribbon or no `TrackChange`, we find out in an hour instead of in the field.

### 6.2 Getting installs legitimately
- Public trial: current version only, 30 days [V-web: https://madcapsoftware.com/trials/flare-free-trial]. Fine for 2026 re-tests, not for 2020.
- Historic download pages and "a new licence lets you download older versions" are reported but not confirmed for 2020/2021 [V-web via search; secondary]. Action: **ask MadCap** (sales/partner contact) for previous-version downloads and evaluation or NFR licences for a third-party plugin vendor; MadCap lists third-party plugins itself [V-web: plugins-api-redistributables.aspx], so a vendor request is normal. Praeviderant should hold one commercial Flare licence regardless, which entitles older-version downloads per the reseller note. Do not try to re-arm expired trials.
- A tester community fallback: ask existing customers on 2020-2023 to run the harvest script (read-only, produces a text file) and the smoke checklist. Cheap, and finds real-world configurations.

### 6.3 VMs
One snapshot-able Windows 10 22H2 VM and one Windows 11 VM; Flare majors 16/17, 18, 20 and 22 installed side by side (each its own folder) or, if same-machine installs conflict, one VM per band. Take a snapshot after install so each test run starts clean. Include a Win10 build older than 1903 only if the 2020 requirements page shows 4.7.2 is acceptable to Flare: `FlareAI.Flare` targets net48 and will not load on a 4.7.2-only machine. If that matters, retarget `FlareAI.Flare` to net472 (it needs an older reference set; Core is net40 already). **[Unknown]** until the 2020 requirements are known.

### 6.4 Smoke checklist (per version, about 25 minutes; record build number, pass/fail, screenshot of Diagnostics)
1. Installer finds this Flare; installs; plugin listed in File > Options > Plugins; enable; restart; no startup error dialog.
2. Diagnostics: first line shows the right year/build and "tested" or the correct unavailable list. Compare with the matrix.
3. Ribbon mode: House Style tab and groups present, icons render, key tips work. Where the ribbon is unavailable: menu and toolbar present.
4. Settings opens; disabled-with-reason items match Diagnostics, and the accessible description is read by the screen reader.
5. Check style on a topic: findings list appears (docked pane, or floating window if no pane dock).
6. Apply one fix: tracked change appears; **Undo** restores; **Redo** applies without crash; Save; reopen: change still tracked; Reject works.
7. Apply many fixes in one operation: one Undo reverts all.
8. Word-level fix inside a paragraph containing a variable and an xref: no corruption, no `<SelectionMarker/>` in saved XML.
9. Rewrite selection and Ask (AI) produce a tracked replacement; failure path (bad key) shows the normal message.
10. Right-click menu shows the House Style submenu and its entries work.
11. Check as I type: type; after the pause the pane and markers refresh; no typing lag; no flicker.
12. Check when saving: toast; the "Show them" action.
13. Margin markers appear, clear after fixing, and the stylesheet temp files are gone after closing the topic.
14. Build a target: report-before-build toast and "Open the report".
15. Check project over a 200-topic project; no memory spike; many tabs not left open.
16. Close Flare: no crash on exit; reopen: plugin state remembered.
17. Newtonsoft check: with `ANTHROPIC_API_KEY` set, run Ask once (exercises JSON); look in Diagnostics log for assembly-load warnings.
18. Repeat items 5 to 8 with the project in the other UI mode and at 150% display scaling.

Items 6 to 8 are the "Verified write behavior" tests from `docs/flare-api-notes.md` and `docs/MANUAL-TESTS.md`, re-run per version. A version is named "supported" only if it passes items 5 to 8 on a recorded build. Any version that fails item 6 or 8 is **not** marked supported.

### 6.5 What CI covers
Compile against `ref/` stubs; unit tests for probes, command table, startup dialog and banner rules, version parsing, and the install script's discovery logic. CI cannot catch behavioural differences (section 4.2 mode 4) or UI issues; those are the manual checklist, run before each tagged release for every row of the support table, and after each new Flare release (Flare ships roughly twice a year: 2024, 2024 r2, 2025, 2025 r2, 2026 are listed in release trackers [V-web via search: componentsource releases]). The member-list guard is in section 10.

## 7. Minimum supported version recommendation

Support table at launch (the user-guide topic is generated from this). A version is "supported" only with a recorded passing build; everything else says "untested, expected to work".

| Flare | Status | Condition |
|---|---|---|
| 2026 (22) | Supported, tested on build 22.0.9643.36696 | Tested live now (smoke checklist items 5 to 8). |
| 2022-2025 (18-21) | Untested, expected to work | Becomes "supported, tested on build X" only after items 5 to 8 pass on that build. **[Inferred]** from docs. |
| 2020-2021 (16-17) | Untested, expected to work | Same rule. The floor candidates. |
| Older than the floor | Not supported | One-time startup dialog says why; the plugin stays off. |
| Newer than we tested | Works; one banner per Flare major | "Hasn't been tested with this version yet." |

Launch claim: current Flare plus the oldest install that passed the smoke checklist. Every other band stays "untested, expected to work" until it passes.

Recommendation: **the minimum is the oldest band that passes items 5 to 8 on a recorded build.** If the harvest shows the hard-requirement row present and the checklist passes on 2020 r3 or later, the minimum is 2020, stated as "2020 r3 or later" if r1/r2 behave differently. Otherwise 2021, then 2022. Reasoning: the 2020 help documents the full API we use, a competitor advertises 2020+, and the only Flare 2020 doubt is the quality of its internals around the December 2020 Chromium switch. 2020 is unrealistic only if the harvest finds the tracked-change write path unusable or a hard-requirement member missing, or if we cannot get an install to test. Claiming support we have not tested would be worse than a higher floor.

## 8. Risks, edge cases, open questions

1. **Write-path divergence (highest).** Redo crashes, `SelectionMarker` leftovers and node-vs-text-node replacement behaviour were found by experiment on Flare 22. Older builds may be better or worse. Mitigation: checklist items 6-8 per version; the tested list in code controls the untested banner; if a version needs a different write tactic, put it behind `IDocumentAdapter` (already the only write path) as a per-band adapter strategy, chosen by `FlareCaps`.
2. **JIT-time failures.** Current in-method `try/catch` does not protect against unresolved members. Mitigation: the `NoInlining` wrapper pattern everywhere in `FlareAI.Flare`, enforced by review and by the floor-version check in the harvest (the automated guard is in section 10).
3. **`Newtonsoft.Json` collision.** Flare 22 ships 13.0.3 with a binding-redirect in `Flare.exe.config` [V-local]; we ship 13.0.3. An older Flare may load an older Newtonsoft first, and our Core is compiled against 13.x. Whether our private copy loads beside it depends on how Flare loads plugin folders **[Unknown]**. Test item 17. If it fails, internalise Newtonsoft into `FlareAI.Core` (ILRepack, renamed namespace) rather than depend on Flare's copy. This is a harvest decision, not a feature.
4. **.NET Framework on 2020.** net48 plugin on a machine with only 4.7.2. Likely irrelevant on Windows 10 1903+ **[Inferred]**. Confirm from the 2020 requirements page (not found online). Fallback: net472.
5. **Docs are not the API.** Names and overloads differ between docs and binary (section 4.1). Never code from docs; code from the dumps. Add the warning to `docs/flare-api-notes.md`.
6. **Undocumented members we depend on:** `GetCompilerContext`, `CompileStart`, `B3.AppKit` pane classes. They may be removed in any release; they are already optional.
7. **Same-year r1/r2/r3 differences.** The matrix uses the major, but behaviour can differ by revision (2020 r3 Chromium). Record the exact build in every test report; treat "tested builds" as a list, not a range.
8. **Flare install layout drift.** Folder name spelling and per-user vs per-machine installs. The installer reads `Flare.exe` file version rather than trusting folder names, and has a registry scan; `FLARE_ROOT` stays as the manual override.
9. **Side-by-side installs sharing settings.** One settings directory means one API key for all versions (a convenience). If the guide project is on a network share, versions share it too (fine).
10. **Future Flare adds a member to `IPlugin`.** `TypeLoadException` at load. Cannot be probed. Mitigation: watch each Flare release; keep the smoke run on the newest version.
11. **Open questions for MadCap or customers:** which Flare 2020 build and .NET version; whether previous versions can be downloaded with a normal licence; whether MadCap would confirm the `ICompilerContext` events as stable; is there a way to detect Ribbon vs menu-only UI mode at runtime (`IRibbon.IsCollapsed` exists, not the same thing).

## 9. Effort and sequencing

Effort: **M, 8 engineering days** (harvest 1.5, probe layer 3, installer 1, docs 0.5, two version checklists 2), plus 3 to 5 days elapsed for VM runs and installs (mostly waiting on installs).

1. **Harvest (1.5 days + lead time on installs).** Dump scripts; fill the matrix; decide the floor and the floor year for the startup dialog; take the floor copy into `lib\floor` and regenerate `ref\` from it. Gate for everything else. Start now; installs are the long pole.
2. **Probe layer with L1 (3 days, one change).** `FlareCaps`, `SafeEvents`, the shared command table feeding the six-control ribbon, the menu and toolbar, and the right-click submenu; `Plugin.Execute` and `EditorIntegration` guards split as one refactor; Diagnostics line; Settings disabled state with accessible description; startup dialog below the floor; untested-version banner; tests. Do not land this separately from L1: both rewrite `Plugin.cs` and `EditorIntegration.cs`.
3. **Installer (1 day).** Plural discovery, running-process skip, packaged `Install-HouseStyle.ps1`.
4. **Checklist runs on VMs, fixes (2 days).** Two version checklists: the floor and the newest band that is available. Record build numbers. A version is named supported only after it passes items 5 to 8.
5. **Docs (0.5 days).** Supported-versions topic, install topic, README.

What ships first: steps 1 and 2 (even without older installs, the probe layer makes the plugin safe on any version and gives the Diagnostics line, the startup dialog and the banner). The support claim in the docs and marketing waits for step 4 per version.

## 10. Later (cut from v1, on request or when the trigger is met)

- **Member-surface CI guard.** `tools/FlareAI.ApiSurface` (Mono.Cecil), `docs/api/flare-<major>.txt` member lists, `docs/api/compat-optional.txt` allowlist, `scripts/Compare-Api.ps1`, and the CI step that fails when a call uses a member missing from a floor list. Trigger: two real dumps exist. Until then the rule in 4.5 is enforced in review.
- **Installer flags.** `install-plugin.ps1 -Version <major|all>` and `-List`.
- **Installer tests.** Pester `InstallScriptTests` with a temp tree of fake `MadCap Flare` folders.
- **Per-band shim DLL** (`FlareAI.Flare.Compat<band>.dll`). Not built now; the seam in section 4.3 stays.
- **Newtonsoft internalisation.** Stays a harvest decision (risk 3), not a feature, unless test item 17 fails.
