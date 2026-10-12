# House Style: user-facing surface inventory

Repo: `E:\flare-ai-plugin` (branch main, HEAD 8ccadc3). Plugin version string `0.1.0` (`Plugin.cs:27`).
Compiled 2026-10-12 for UX review. Read-only pass: no source was changed.

## Method and limits

- Read in full: `src/FlareAI.Flare/Plugin.cs`, `EditorIntegration.cs`, every file in `src/FlareAI.Flare/UI/` (AskForm, AssistForm, BuildGuideForm, DiffView, FindingsPanel, GuideReportForm, PanelHost, ResultForm, RuleSettingForm, SettingsForm, Toast, TuneGuideForm), `src/FlareAI.Core/Settings/PluginSettings.cs`.
- Skimmed: `docs/user-guide/Content/*.htm` (13 pages, tags stripped).
- Not read (only referenced from the files above): `ActionRunner`, `MarkerCss`, `Diagnostics`, `LlmClientFactory`, `Routing`, `Team`, `ProjectRules`, `StructureCheck`, `ReviewCache`, `FeedbackLog`, `Dismissals`, `GuideOverrides`, `StyleEngine`, `docstyle` server. Anything that depends on them is marked **(unverified)**.
- Line numbers are `file:line` from the working tree as read. `Plugin.cs` is `src/FlareAI.Flare/Plugin.cs`, and so on; UI files are under `src/FlareAI.Flare/UI/`.
- Strings are verbatim. Curly quotes and ellipsis characters are kept as written in the source.

---

## 1. Ribbon and Tool Strip

### 1.1 Ribbon tab

Tab label `"House Style"`, key tip `Q` (`Plugin.cs:77`). Plugin list description (`Plugin.cs:29`): `"AI editing assistant: check style, rewrite, ask. Edits arrive as tracked changes."`. Plugin name `"House Style"` (`Plugin.cs:30`), author `"Praeviderant Inc."` (`Plugin.cs:28`).

Large icon = `Icons\<name>.png`, small icon = `Icons\<name>-16.png`. A missing icon leaves the button without an image; no error is shown (`Plugin.cs:138-144`).

| Group (label) | Button label | Key tip | Icon | What it does | Source |
|---|---|---|---|---|---|
| Assist | Check style | C | check | Opens the review pane (modeless) for the topic. Whole topic, or only paragraphs containing the selection. Lint + structure at once; AI review follows. | `Plugin.cs:79`, `:148`, `:434` |
| Assist | Rewrite selection | R | rewrite | Modal result window. AI rewrites wording of the selected paragraphs (whole topic if nothing selected, `:423-428`); Apply writes tracked changes. | `Plugin.cs:80`, `:149`, `:437` |
| Assist | Ask… | A | ask | Asks for an instruction (AskForm), then runs the same modal result window with it. | `Plugin.cs:81`, `:151-158` |
| Assist | Check project | P | project | Lints every topic and snippet in the project (no AI) into the review pane, grouped by file. Calls `NewRunner` first, so it returns early when `NewRunner` yields null, which happens without a usable key (`Plugin.cs:172-173`, see section 8, item 37). | `Plugin.cs:82`, `:161-184` |
| Assist | Topic assists | I | assist | Modal window with drafted meta description, index keywords, synonyms, micro content, glossary candidates. | `Plugin.cs:83`, `:215-230` |
| Plugin | Settings | S | settings | Opens SettingsForm; on OK, reloads team/guide state. | `Plugin.cs:86`, `:186-189` |
| Plugin | Validate style guide | V | validate | Modal report on the layered style guide (tables tested by examples; optional AI check). | `Plugin.cs:87`, `:345-362` |
| Plugin | Tune style guide | T | tune | Modal owner report of team review decisions. | `Plugin.cs:88`, `:326-342` |
| Plugin | Build style guide | B | build | Modal window proposing rules from the project and from pasted notes. | `Plugin.cs:89`, `:306-323` |
| Plugin | Diagnostics | D | diag | Message box with the session's selection, routing and AI usage; also written to the log. Placed in the **Plugin** group even though it is listed after the Output group (`Plugin.cs:93`). | `Plugin.cs:93`, `:373-417` |
| Output | Export for LLMs | L | llm | Folder picker, then exports Markdown + llms.txt for the built HTML; Yes/No message box offers to open the folder. | `Plugin.cs:92`, `:250-272` |

Counts: 3 groups, 11 buttons (5 + 5 + 1).

### 1.2 Tool Strip mode (menu and toolbar)

Used only when Flare runs in Tool Strip UI mode, where the ribbon tab is not visible (`Plugin.cs:97-99`). Failures here are logged only (`Plugin.cs:119`).

Menu `"House Style"`, key tip `Q` (`Plugin.cs:104`):
1. Check style (check-16) `Plugin.cs:105`
2. Rewrite selection (rewrite-16) `Plugin.cs:106`
3. Ask… (ask-16) `Plugin.cs:107`
4. Check project (project-16) `Plugin.cs:108`
5. separator `Plugin.cs:109`
6. Settings (settings-16) `Plugin.cs:110`
7. Validate style guide (validate-16) `Plugin.cs:111`
8. Diagnostics (diag-16) `Plugin.cs:112`

Not in the menu: Topic assists, Tune style guide, Build style guide, Export for LLMs.

Toolbar `"House Style"` (`Plugin.cs:114-117`), 16 px icons:
- `"Check style"`
- `"Rewrite"` (menu says "Rewrite selection")
- `"Ask…"`

### 1.3 Icons

All 11 ribbon icon pairs and all menu/toolbar 16 px icons referenced in code exist in `src/FlareAI.Flare/Icons/`: ask, assist, build, check, diag, llm, project, rewrite, settings, tune, validate (each with and without `-16`).

---

## 2. Editor integration: right-click, margin markers, toasts

Source: `src/FlareAI.Flare/EditorIntegration.cs`. Each feature has its own setting. The class header says none of them blocks typing, saving or building (`EditorIntegration.cs:24`).

### 2.1 Right-click menu

Added to each topic's context menu once, after a separator (`EditorIntegration.cs:76-85`; duplicate check at `:80`):
- `House Style: Check style` (`:82`)
- `House Style: Rewrite selection` (`:83`)
- `House Style: Ask…` (`:84`)

No key tips and no icons. Same handlers as the ribbon.

### 2.2 Margin markers

- A paragraph with open proposals gets a margin bar and tint, applied as a view stylesheet (`EditorIntegration.cs:168-202`). Not saved into the topic.
- Source of markers: the review pane's findings for the open topic (lint + AI) if the pane shows that topic, otherwise the last live lint (`:177-180`).
- Colors come from `MarkerCss` **(unverified)**. Docs (`editor.htm`) say red = errors, amber = warnings, blue = suggestions.
- Setting: `ShowMarkers` (default on).
- Temp stylesheets: `%TEMP%\FlareAI-markers\*.css` (`:34`), cleared at start (`:51`).

### 2.3 Live check (as you type)

- Debounce timer 1200 ms after the last keystroke (`EditorIntegration.cs:72`, `:89-94`).
- Lint only, no AI (`:132-145`). Refreshes the pane (if it shows this topic) and the markers.
- Setting: `LiveCheck` (default on).

### 2.4 Check on save: toast

Text (`EditorIntegration.cs:159-160`):
- `N style issue(s) in <file>` + ` (E error(s))` when any error, then `. Saved anyway.`
- Link text: `Show them` (opens Check style, `:160`).
- Shown only if the count is greater than 0 (`:157`).

Setting: `CheckOnSave` (default on).

### 2.5 Report before build: toast

Runs in the background when a build starts (`EditorIntegration.cs:212-239`).
- No issues: `Building. No style issues in the project's N topics and snippets.` (no link, `:230`).
- Issues: `Building. N style issue(s) in M file(s)` + `, E error(s)` if any + `.`, link `Open the report` (runs Check project) (`:233-234`).

Setting: `ReportBeforeBuild` (default on).

### 2.6 Export after build: silent

After a build, if `LlmExportAfterBuild` is on, the newest HTML output is exported in a background thread (`Plugin.cs:235-247`). On success the only trace is a log line (`:244`). No toast, no message. If no fresh output is found, only a log line is written (`:243`).

### 2.7 Pane follows the active topic

When the review pane is open and the author switches topic (`Plugin.cs:527-545`):
- The pane retargets to the new topic. Lint runs; AI results appear only if cached (`aiOnStart` false, `:542`, `FindingsPanel.cs:74-75`).
- Skipped for snippets (`.flsnp`, `:538`) and style-guide topics (`\Resources\StyleGuide\`, `:540`).
- Calls `NewRunner`, which can show an error and open Settings (see section 4.2 and section 8, item 16).

---

## 3. Review pane (FindingsPanel) and its host

### 3.1 Host (`UI/PanelHost.cs`)

- Docked pane first, via Flare's internal pane API (`FlarePane`, `PanelHost.cs:61-116`). Pane name `FlareAI.Findings` (`:82`). Pane title = the panel's title.
- If docking fails for any reason, a floating window is used (`PanelHost.cs:42-57`):
  - Title = panel title; size 720 x 460; minimum 320 x 260.
  - Placed at the bottom-right of the working area, 24 px from each edge (`:50`).
  - Not in the taskbar. Escape closes it (`:54`).
- Closing from Flare's own pane ✕ uninstalls the pane; it is reinstalled next time (`PanelHost.cs:78-79`).
- Only one review panel at a time (`PanelHost.cs:21`). Opening another replaces the current one (`:25`).

Titles (`FindingsPanel.cs:59`, `:82`):
- Topic: `House Style — <file name>`
- Project: `House Style — project <project folder name> (N files)`

### 3.2 Layout, top to bottom (`FindingsPanel.cs:20-160`)

- Control minimum size 260 x 320; padding 6 (`:84-85`).
- **Status label**: top, fixed height 36 (`:87`). Initial text `Linting…`.
- **Progress bar**: top, 5 px, marquee while working (`:88`, `:197`).
- **Triage row** (flow, wraps): `Accept all mechanical (N)` and `Propose all as tracked changes (N)` (`:89-92`).
- **List** (fill): two columns, `Proposal` (62% of width) and `Rule` (rest). Header not clickable. Single selection. Checkboxes off (`:93-96`).
- **Card** (split panel below the list; the card keeps at least 250 px height, `:130-135`):
  - Head label: bold, 34 px high (`:103`).
  - "What changes" box: read-only rich text, top, 64 px high (`:104-105`).
  - "Why" box: read-only rich text, fills the rest (`:106-107`).
  - Action row, bottom, wraps: Accept, Reject ▾, Edit…, Refine…, Re-analyze, Guide ↗ (`:108-114`).
- **Bottom bar** (flow, wraps): Re-check, Turn off rule…, Close (`:137-140`).

### 3.3 Status line texts (`FindingsPanel.cs`)

Set at the top label. Several texts are appended together.

- `Linting…` (start, `:197`).
- `Linting N files…` (project, `:271`); progress `Linting <done>/<total> files…` (`:273`).
- `N issue(s) in M of K files (linter only). Accept opens the file.` (project done, `:285`).
- `Project check failed: <message>` (`:278`).
- `Reviewing with AI — X of Y paragraphs… (you can work on these now)` (AI running, `:246`).
- `Linter only — click Re-check for the AI review.` (no AI ran, `:258`).
- `No open proposals.` (`:259`, `:407`).
- `AI review failed: <message>` (`:256`).
- Lint note when the style linter is unavailable: `Style linter unavailable (<message>) — showing AI review only.` (`:208`).
- Team note when the guide is behind: `team.Status` text (`:262`).
- Summary, built by `Summary()` (`:404-417`):
  `N proposal(s)` + ` in S sections` when more than one section + `: X mechanical · Y wording · Z need a rewrite` + ` · W to fix by hand` if any + `.`
- After Accept all mechanical, if snippet fixes exist: `  N more in snippets: select one to apply it there.` (`:581`).
- After Apply: `Applied N as tracked change(s)` + `; M skipped because the text changed.` or `.` + ` Ctrl+Z undoes them.` (`:621`).
- After Propose all: `Wrote N tracked change(s) — review them in the Review tab. Proposals that need a rewrite or a manual fix are still listed.` (`:594`).
- After Reject: `Rejected (<reason>); it stays hidden for this text.` (`:641`).
- Accept on a stale proposal: `That text changed since the review. Re-analyze the paragraph.` (`:506`).
- Accept on a variable proposal: `The text is selected: use Insert ▸ Variable ▸ <variable> (Flare replaces the selection).` (`:510`).
- Accept on a manual proposal: `Fix it in the editor (<suggestion>), then Re-analyze.` (`:518`).
- Re-analyze: `Re-analyze works on the topic's paragraphs.` (`:699`), `That paragraph is gone. Re-check the topic.` (`:701`), `Re-analyzing the paragraph…` (`:703`), `Paragraph re-analyzed: N proposal(s).` (`:724`), `Re-analyze failed: <message>` (`:716`).
- Refine: `Refining…` (`:670`), `Refine failed: <message>` (`:677`), `Refined. Accept, edit, or refine again.` (`:690`).
- Rewrite paragraph from a snippet proposal: `Open the snippet and use Rewrite selection there.` (`:731`); block gone: `That text is no longer in the topic. Re-analyze or Re-check.` (`:733`).
- Ignore rule: `Select a proposal whose rule you want to turn off.` (`:751`); `Sent to the style guide owner` + ` (set a team feedback folder in Settings so they receive it).` or `.` (`:759`); `Rule set to <off|suggestion|warning|error>` + ` for the team (in <file>; commit it so everyone gets it).` or ` for this project.` (`:781`); `Could not change the rule: <message>` (`:784`).
- Open a snippet that won't open: `Could not open <path>` (`:844`). Stale link: `That text is no longer in the topic (it was edited). Re-analyze to refresh.` (`:847`).
- Alt text: `That image already has alt text (or is gone). Re-analyze.` (`:533`); `Looking at <file>…` (`:543`); `Alt text added as a tracked change.` (`:562`); `Could not apply: <message>` (`:565`); `Alt text failed: <message>` (`:550`); image load error plus `Write the alt text in the image's properties.` (`:539`).
- Busy text: `Busy` shows its own text, for example `Refining…` (`:820-825`).

### 3.4 List items

- Text: `ProposalText` (`:371-381`):
  - Variable: `"<excerpt>" → variable <name>`
  - Restructure: `"<excerpt, 48 chars>" — rewrite`
  - Manual: `<rule part after the dot>: "<excerpt, 36 chars>"`
  - Other: `delete "<excerpt>"` when no replacement, else `"<excerpt, 30>" → "<replacement, 30>"`
  - Prefix `✓ ` when accepted, `⚠ ` when stale (text changed).
- Second column: `LayerLabel` (`:392-398`): `AI · <Layer>: <section>` for guide-tagged AI rules; `AI` for plain AI; `<Layer> · <section>` for table rules; otherwise `<Layer> · <rule name>`.
- Groups: by section, `(top of topic)` when none (`:362`). Project mode: by file, shown relative to `Content` (`:361`, `:385`). In topic mode, a snippet group label gets `  — used in N files` when used in more than one file (`:366`).
- Tooltip: `<LayerLabel> · <rule>` + newline + suggestion (`:327`).
- Style: accepted = strikeout and grey; stale = italic and grey; error severity = dark red (Firebrick); otherwise normal (`:891-897`).
- Sorting: reading order by `Order` (`:319-321`); project mode sorts by file path.

### 3.5 Card

Shown for the selected proposal (`ShowCard`, `:426-468`). With none selected:
- `Select a proposal to see what changes and why.` when findings exist, else blank (`:432`).

Head (`:439-440`): `<LayerLabel>  ·  <KindLabel>` + `  ·  accepted` / `  ·  text changed — Re-analyze` + newline + location line:
- `In snippet <name>` + ` — changes N file(s) that use it` for snippets.

KindLabel (`:488-498`): `rule fix` (mechanical), `AI wording` (wording), `use the variable` (variable), `fix in the editor` (manual), `needs a rewrite` (anything else).

"What changes" box: words before the problem (grey, prefixed `…`), then the change: deleted text struck through in dark red, added text underlined in green. With no replacement, the excerpt is shown on a yellow highlight (`:443-451`).

"Why" box (`:454-466`):
- Suggestion text.
- Rule line: `Google · <section>: <rule text>`, `This project · …`, or `Company · …`, in italics.
- Example: `✗ <bad>` and `✓ <good>` (`:461-462`).
- `General editing judgment (not tied to a guide rule).` for AI findings without a rule (`:464`).
- Grey line `Rule id <id>` (`:465`) — the only place a rule id is shown to writers.

### 3.6 Per-card and triage buttons (label, enabled-when, action)

- **Accept** (`:109`): label changes by proposal type (`:904`):
  - `Suggest alt text…` for a missing-alt-text finding on a topic.
  - `Rewrite…` for a restructure proposal.
  - `Go to` for variable or manual proposals.
  - otherwise `Accept`.
  - Enabled when the proposal is open (not accepted, not stale). Action: apply as a tracked change, then move to the next (`:502-526`). Enter, double-click, and the tooltip `Apply this proposal as a tracked change and go to the next one (Enter or double-click).` (`:143`).
- **Reject ▾** (`:110`): opens the menu below. Enabled when the proposal is not accepted. Tooltip: `Not applying it: say why, so the guide and the AI can be tuned (Delete).` (`:144`).
- **Edit…** (`:111`): enabled when open and not variable or manual. Opens `TextPrompt` titled `House Style — Edit proposal` with label `Replace "<excerpt, 80>" with:`, prefilled with the replacement. Unchanged text closes silently (`:649`). Otherwise applies the edited text. Tooltip `Change the proposed text yourself, then apply it.` (`:145`).
- **Refine…** (`:112`): enabled when open and not reviewing. Opens `TextPrompt` titled `House Style — Refine proposal` (multiline) with label `How should the AI change this proposal? (for example: keep the term SSO; make it shorter; keep the passive voice here)`. Asks the AI for this one proposal. If the result equals the original text, it is rejected automatically with reason `fine here (refined: …)` (`:680-686`). Tooltip `Ask the AI again for this one proposal, with your instruction ("keep SSO", "shorter").` (`:146`).
- **Re-analyze** (`:113`): enabled when the selected proposal is in the topic (not a snippet), not reviewing, and not project mode. Re-lints and re-reviews the paragraph, replacing its proposals (`:696-727`). Tooltip `Review this paragraph again (after you edited it).` (`:147`).
- **Guide ↗** (`:114`): enabled when the proposal has a link. `file:` links to a `.htm`/`.html` topic open in Flare; `https://` links open the browser; other links are ignored silently (`:873-889`, `:887`). Failure text `Could not open <url>`.
- **Accept all mechanical (N)** (`:90`, text set at `:911`): enabled when N > 0 (topic or project). Applies every exact rule fix as one tracked change per file. Confirmation only when more than 3 files (see section 5, item 3a). Tooltip `Apply every exact rule fix in this topic as one tracked change (one Ctrl+Z undoes it).` (`:149`).
- **Propose all as tracked changes (N)** (`:91`, text `:914`): enabled when N > 0 and not in project mode. Writes all exact rule fixes and AI wording into the topic (`:586-596`). Always confirms (section 5, item 3b). Tooltip `Write every proposal that has exact text (rule fixes and AI wording) into the topic as tracked changes, then review them with Flare's own Accept/Reject (Review tab) or in a review package.` (`:148`).
- **Re-check** (`:138`): disabled while reviewing. Starts a fresh lint and AI review (`_aiOnStart = true`, `:138`). No tooltip.
- **Turn off rule…** (`:139`): enabled when a proposal is selected. Tooltip `Turn the selected rule off for this whole project (written to the project's StyleGuide folder, shared with your team).` (`:150`).
  - Writers who are not owners: asks for a reason in `TextPrompt` `House Style — Propose turning off a rule` and sends it as feedback (`:752-761`).
  - Owners (or no team): `RuleSettingForm` (section 4.8).
- **Close** (`:140`): raises CloseRequested; the host hides the pane or closes the window.

Keyboard (`FindingsPanel.cs:185-189`): Enter = Accept; Delete = opens the Reject ▾ menu.

### 3.7 Reject ▾ menu (`:115-120`)

1. `Wrong — the proposal is incorrect` → Reject with reason `wrong`
2. `Fine here — correct in this context` → reason `fine here`
3. `Rule doesn't apply to this text` → reason `rule doesn't apply`
4. separator
5. `Skip for now` → moves to the next proposal without recording anything (`Next()`).

Reject hides the proposal for this text (stored in `dismissed.json`, `FindingsPanel.cs:36`, `:635`) and records feedback (`:636`). Next proposal is selected after it.

### 3.8 Empty and edge states

- Nothing found, AI on: `No open proposals.` on the status line; card head is blank (`:259`, `:432`).
- Nothing found, AI not run (cache empty, not requested): the status line shows `Linter only — click Re-check for the AI review.` (`:258`), not `No open proposals.`.
- Proposals exist but none selected: card head `Select a proposal to see what changes and why.`. The first open proposal is selected automatically after a check (`:263`).
- Project check with zero findings: the status line shows `0 issue(s) in 0 of N files (linter only). Accept opens the file.` (`:285`) — text from the code path; no special empty message.
- Pane while the AI runs: Re-check is disabled and Refine is disabled (`:907`, `:916`).

### 3.9 Sub-dialogs opened from the pane

- `TextPrompt` (section 4.9): Edit, Refine, alt-text, and the "Propose turning off a rule" prompt.
- `RuleSettingForm` (section 4.8): Turn off rule (owners).
- `ResultForm` titled `House Style — Rewrite paragraph` (`:737`), modal, used for restructure proposals (section 4.6).
- Alt text: `TextPrompt` titled `House Style — Alt text` (caption built by `:926`). Label: `The AI thinks <file> is decorative. Type alt text if it isn't (screen readers will read it):` when no suggestion, otherwise `Alt text for <file> (edit before applying):`. Prefilled with the suggestion (`:552-554`).

---

## 4. Dialogs, forms and other surfaces

Forms use `Font = SystemFonts.MessageBoxFont` unless noted. Modal = `ShowDialog`.

### 4.1 AskForm (`UI/AskForm.cs`) — modal

- Title `House Style — Ask` (`:15`). Fixed dialog, no min/max. ClientSize 520 x 200. Centered on parent (`:16`).
- Label (`:22`, 496 x 32): `What should I do with the selected text? (whole topic if nothing is selected)`
- Text box (`:23`): multiline, accepts Enter, vertical scrollbar, 496 x 96, no default text, no placeholder.
- Button `Run` (`:24`, 75 wide, at 352,156): DialogResult OK. Empty input: the dialog stays open and the text box is focused, with no message (`:26`). Also the default (Accept) button.
- Button `Cancel` (`:25`): DialogResult Cancel; Escape.
- No tooltips.

### 4.2 SettingsForm (`UI/SettingsForm.cs`) — modal

- Title `House Style — Settings` (`:23`). Fixed dialog, ClientSize 520 x 692 (`:27`). Centered on parent.
- Controls in order (label text and type; defaults from PluginSettings):
  1. **Provider** (`:31-34`): dropdown, options `Anthropic (direct)`, `Bifrost gateway`. Default from `Provider` (Anthropic). Changing it toggles MCP include tools (see below).
  2. **Base URL** (`:36`): text box, default `https://api.anthropic.com`. On a non-Bifrost provider it is reset to that URL if empty or if it contains `/v1` (`:93-94`), silently.
  3. **Model (standard)** (`:37`): text box, default `claude-sonnet-5-5`. No hint text.
  4. **Fast / deep models** (`:38-41`): two text boxes side by side (fast at left 140 width 180; deep at left 328 width 180), both blank by default. Hint (grey, 368 x 30): `Empty = claude-haiku-5-5 / claude-opus-5-5. A style guide's FlareAI\models.json overrides these for the team.`
  5. **API / virtual key** (`:42`): password-masked text box. Never shown back. Empty on Save keeps the stored key (`:124`).
  6. Key status (grey, `:43`), depends on state (`:97-102`):
     - `A key is stored (protected). Blank keeps it.`
     - `Using ANTHROPIC_API_KEY from the environment.`
     - `No key configured.`
  7. Link **Clear stored key** (`:44-45`, at left 430): clears the stored key in memory and clears the field. Nothing is saved until Save; Cancel discards it. No confirmation.
  8. **MCP include tools** (`:47`): text box, default from `McpIncludeTools`. Enabled only for Bifrost (`:92`).
  9. **Style guide project** (`:48`): multiline, 40 px high, vertical scrollbar. Text shows layers one per line (`;` converted to newlines). Hint (grey, `:50`): `Folder of your team's style guide (a Flare project). Empty = the guide bundled with the plugin.`
  10. Checkbox **Also use the open project's variables, glossary and Resources\StyleGuide** (`:51`), default on.
  11. **Team feedback folder** (`:53`): text box, default empty. Hint (grey, 368 x 30, `:54`): `Shared folder (synced or network) where each writer's review decisions are pooled. Empty = personal only.`
  12. **Timeout (s)** (`:55`): text box 60 wide, default `90`. Validated on Save: must be a number >= 10 (`:107`).
  13. Checkbox `After each HTML build, add LLM-ready Markdown and llms.txt to the output` (`:56`), default off.
  14. Checkbox `Mark paragraphs with proposals in the editor` (`:58`), default on.
  15. Checkbox `Check as I type (style rules only, no AI)` (`:60`), default on.
  16. Checkbox `After saving, tell me how many style issues the topic has` (`:62`), default on.
  17. Checkbox `When a build starts, report the project's style issues` (`:64`), default on.
  18. Checkbox `Write diagnostics log` (`:66`), default on.
- Buttons: **Save** (`:69`, 75 wide at left 352, bottom). Accept button. Runs validation, writes all settings, closes with OK (`:104-135`). Save failure: message box (section 5). **Cancel** (`:70`, left 433), Cancel button.
- No tooltips.
- Label column width 124 (`:81`).

### 4.3 AssistForm (`UI/AssistForm.cs`) — modal, "Topic assists"

- Title `House Style — Topic assists: <file name>` (`:25`). ClientSize 760 x 620, min 520 x 360 (`:27-28`). Centered on parent.
- Status (top, 40 px, `:30`): `Reading the topic…`, then on success `Drafted from this topic only. Review each, then copy it into Flare (Topic Properties, Index, Synonyms, Micro Content, Glossary).` (`:47`). On error `Failed: <message>` (`:45`).
- Sections (scrollable column; each section is a title label, a text box and a Copy button):
  1. `Meta description  (Topic Properties ▸ Description)`. Copy text = the description.
  2. `Index keywords  (Insert ▸ Index Keyword; one term per line)`. Display: one per line. Copy text: **semicolon-separated** (`:49`).
  3. `Search synonyms  (Synonyms file ▸ Groups)`. Display: one group per line, terms comma-separated. Copy = the displayed text.
  4. `Micro content  (question/answer pairs for search results and chatbots)`. Display: `Q: …` and `A: …` pairs. Copy = the displayed text.
  5. `Glossary candidates  (terms used but not defined)`. Display: `term — definition`. Copy = the displayed text.
- Empty sections are omitted (`:59`).
- Each Copy button: text `Copy`, changes to `Copied` after use (`:64`).
- Section text boxes are editable (`ReadOnly = false`, `:62`); edits are not saved anywhere.
- Bottom: **Close** (`:34`), Cancel button. Also Escape.

### 4.4 BuildGuideForm (`UI/BuildGuideForm.cs`) — modal, "Build style guide"

- Title `House Style — Build style guide` (`:40`). ClientSize 940 x 640, min 640 x 420 (`:42-43`). Centered on parent.
- Head label (top, 46 px, `:46`, text at `:86-87`): `Proposes rules from what your team already writes and from your notes. Check the ones to keep, then add them to the guide.` For non-owners, a second line: `Read-only: only the style guide owners (<owners>) can add rules.`
- Progress bar (5 px, marquee, hidden until work starts, `:47`).
- Tools row (`:48-53`):
  - **Mine this project** (`:49`): enabled when a topic is open (project path) and the style engine exists. Scans every topic and snippet. Head text while running: `Reading every topic and snippet in the project…`. Result: `N topic(s) and snippet(s) read: N proposed rule(s) the guide doesn't cover yet.` + ` Where the team was split, pick the preferred form.`, or ` Your content is consistent with the guide.` when zero (`:105-106`).
  - **Add notes or an old guide…** (`:50`): always enabled. Opens the notes dialog (section 4.4a).
- Proposal list (`:55-60`): check boxes, single selection, details view. Columns: `Proposed rule` (430), `Becomes` (110), `Evidence` (330). Groups: `From your content`, `From your notes`.
  - `Becomes`: `Avoid/Use row` for table rows, `Prose rule` otherwise (`:165`).
  - `Evidence`: `“<form>” N×` items joined by ` · `, or the note quote in curly quotes (`:167-171`).
- Detail area (bottom, 200 px, `:62-71`): read-only rich text (diff view), plus a `Preferred form:` label and a dropdown (260 wide). The dropdown shows only when a candidate has more than one form (`:177-185`).
  - Detail for a table row: `Adds a table row  ·  Avoid: <x>  ·  Use: <y or (flag only)>  ·  <severity>` then `Why:` and `Example (must break the rule):` lines.
  - Detail for a prose rule: `Adds a prose rule: <text>` with `Bad:` and `Good:` lines when present.
- Bottom bar (`:73-77`), right-to-left:
  - **Close** (Cancel, `:74`).
  - **Add checked rules to the guide** (`:75`). Text becomes `Add N checked rule(s) to the guide` (`:216`). Enabled only for owners, when a guide root exists, and when at least one item is checked (`:215`). Writes the checked rules to `From-our-content.htm` in `Content\Resources\StyleGuide` (or the guide root), title `From our content` (`:223-227`). Success text: `Added N rule(s) to <path>. Review the topic in Flare, run Validate style guide (it tests every new row against its example), then commit the guide.` Opens the new topic in Flare (`:230`). Failure: message box (section 5).
- No tooltips.

#### 4.4a Notes dialog (`NotesDialog`, `BuildGuideForm.cs:246-276`) — modal

- Title `House Style — Notes for the style guide`. ClientSize 680 x 460, min 480 x 300.
- Label (top, 44 px): `Paste or dictate (Win+H) anything: how the team writes, words you never use, an old style guide, or the prompt you used with another AI tool. Or load a .txt, .md or .htm file.`
- Multiline text box, fill, vertical scrollbar, word wrap.
- Bottom bar: **Load file…** (opens a file dialog, filter `Text, Markdown, HTML` + `All files`; HTML tags are stripped on load, `:265-266`), **Find rules** (OK, `:256`), **Cancel** (`:257`). No default button.

### 4.5 GuideReportForm (`UI/GuideReportForm.cs`) — modal, "Validate style guide"

- Title `House Style — Validate style guide` (`:33`). ClientSize 820 x 560, min 520 x 320.
- Status (top, 34 px, `:39`). Initial `Testing the guide's rule tables…`.
- Progress bar: marquee; becomes a percentage bar during the AI check (`:112`).
- Report box: read-only, multiline, vertical scrollbar, word wrap, monospace 9 pt.
- Validation runs automatically on open (`:53`). For owners, or when there is no team, it also assigns permanent rule ids to new rules (`:62-64`) **(unverified whether this writes files; `AssignKeys` is in Core)**.
- Status after validation (`:77-78`): `Gave N new rule(s) permanent ids.  ` (if any) + `Rule tables pass.` or `N error(s) to fix.` + `  N prose rule(s) have Bad/Good examples you can check against the AI.`
- Report content (`:86-101`):
  - `Layers (later wins): <layer> → <layer>`
  - `N table rule(s), N tested by their Example. N prose rule(s), N with Bad/Good examples.`
  - `N error(s), N warning(s).`
  - `ERRORS — these rules don't work as written:` and/or `WARNINGS:`, each issue as `<file>, <location> [<layer>]` and the message.
  - When no issues: `Every rule-table row is triggered by its Example and fixed by its replacement.`
- Bottom bar (right-to-left, `:42-47`):
  - **Close** (Cancel, `:43`).
  - **Check prose rules with AI…** (`:44`). Disabled until validation finishes; enabled only when prose rules with examples exist (`:79`). Confirmation (`:107`, caption `House Style`, OK/Cancel, Question icon): `Check N prose rule(s) against the AI reviewer?` + two newlines + `This makes M model call(s).` Runs the AI on each example. Progress status `Asking the AI… <done>/<total>`. Result appended to the report: `AI CHECK — does the reviewer apply the prose rules?`, then per rule `✓ <id>  <text>` or `✗ <id>  <text>` with notes `Bad example caught` / `Bad example MISSED` / `Good example clean` / `Good example wrongly flagged`. Closing summary: `N of M prose rule(s) applied as written. For a missed rule: sharpen its wording, add a clearer Bad example, or make it a table row (deterministic).` Status: `AI applies N of M prose rule(s) as written.` Failure: `AI check failed: <message>`.
- Validation failure: `Could not validate: <message>` (`:71`).
- No tooltips.

### 4.6 ResultForm (`UI/ResultForm.cs`) — modal, used by Rewrite selection, Ask, Rewrite paragraph, Draft style guide change

- Title `House Style — <title>` (`:58`). Titles seen: `Rewrite selection`, `Ask`, `Rewrite paragraph` (`FindingsPanel.cs:737`), `Draft style guide change` (`TuneGuideForm.cs:164`). ClientSize 760 x 520, min 500 x 300. Centered on parent.
- Status label (top, `:64`): initially `Contacting model…`.
- Progress (marquee, `:65`).
- Output text box (monospace, read-only, `:66`), shown only when **Show XHTML** is checked.
- Diff box (read-only rich text, `:67-70`): shows the proposal as text with word changes marked. Default view when a proposal exists.
- Checkbox **Show XHTML** (`:71`), hidden until a proposal exists.
- Button **Restructure instead…** (`:76`), hidden by default. Appears when the AI says the change needs a structural edit. Asks again for validated XHTML.
- Button **Apply as tracked changes** (`:73`, 160 wide, at left 540). Disabled until the proposal validates. Applies the change; on success closes with OK. Failure: message box (section 5).
- Button **Cancel** (`:74-75`, 60 wide, at left 688). Changes to `Close` in some end states (`:112`, `:150`, `:157`). Escape. Closing while busy cancels the worker; nothing is written afterwards (`:93`, `:105`).
- Status texts (`:117-148`):
  - Error: `Failed.`; output shows the error message (`:110-111`).
  - Valid, no linter issues left: `Proposed edit passes the style linter.`; otherwise `Proposed edit — N linter issue(s) remain (listed below).`
  - Plus warnings and notes; then `Applies as N word-level tracked change(s).` or `Applies as a paragraph-level tracked change (structure changes).`
  - Variable warning: `Note: Flare 22 can undo this edit but not redo it (Ctrl+Y) because the text contains a variable.`
  - Needs restructure: `… The AI says this also needs a structural change[: note] — apply the wording now, or Restructure instead.`
  - Restructure note only: `Note from the AI: <note>`.
  - No wording changes: `No wording changes; the AI says this needs a structural change…`, or `No wording changes. Note from the AI: …`, or `Already meets the style guide — nothing to change.` Apply disabled.
  - Rejected by validator: `Rejected — the model's output would damage the topic structure. Nothing was changed.` Output shows the validation result and `--- raw response ---`.
  - Restructure in progress: `Asking for a structural edit…`.
- Tooltips: none.

### 4.7 TuneGuideForm (`UI/TuneGuideForm.cs`) — modal, "Tune style guide"

- Title `House Style — Tune style guide` (`:47`). ClientSize 900 x 600, min 600 x 400.
- Head label (top, 54 px, `:53`). Initial `Reading the team's review decisions…`. Result (`:115-127`): `N decision(s) from N writer(s) (<date> – <date>). Source: <feedback folder>.` (or `your own decisions only — set a team feedback folder in Settings to pool everyone's`), then newline, `Nothing needs attention yet. ` or `N item(s) to look at. `, then the role line: `No owners file: anyone can change the guide.` / `You are a style guide owner.` / `Read-only: the owners are <names>.` Then the sync status if any.
- List (`:54-57`): details view, single selection. Columns `Item` (420), `Accepted` (70), `Rejected` (70), `Writers` (200). Groups (`:34-41`):
  - `Writers disagree — decide, reword, or scope the rule`
  - `Often rejected — reword, demote to prose, or drop`
  - `Repeated hand edits — the replacement is probably wrong`
  - `Requests to turn a rule off`
  - `Working well`
- Detail box (bottom, 170 px, read-only rich text, `:58-59`): title (bold), `Rule <id> (<layer> · <section>): <text>` (italic) when known, evidence lines `• …`, and advice (italic). Advice texts (`:200-210`):
  - Disputed: `Most splits are about scope: the rule is right for some content and wrong for other. Scope it (a project layer or a separate section), or reword it to say when it applies.`
  - Review: `Read the rejected excerpts: if the rule is wrong there, narrow its wording or make it a prose rule with an example of the exception.`
  - Edit: `Writers agree on a better replacement: update the rule's Use column (or add an Avoid/Use row), then run Validate style guide.`
  - Ignore: `Decide for the team: turn the rule off (Google overrides table), scope it, or answer the writers.`
- Bottom bar (`:60-70`, left to right):
  - **Close** (Cancel, `:61`).
  - **Refresh** (`:62`): reloads decisions.
  - **Open rule in the guide** (`:63`): enabled when the rule has a link. `file:` links open the topic and close this form; `https://` links open the browser (`:220-235`). Failure: `Could not open <link>: <message>`.
  - **Draft a change with AI…** (`:64`): enabled when the rule is in a Flare topic (`file:` link), the item is not `working`, and the user is an owner (`:144`). Opens the rule's topic, finds the rule, and runs a modal result window to draft the change as tracked changes (`:150-171`). Messages: `Could not open <link>`, `Could not find this rule in <file>. Open it and edit the rule there.` Success head text: `<applied summary> In <file>: review, save and commit the guide, then run Validate style guide.`
  - **Customize AI prompts…** (`:65`): enabled for owners only. Creates `FlareAI\prompts\*` in the guide with built-in templates (missing files only), writes `README.txt` (`:189-193`), opens the folder in Explorer. Head text: `Copied N prompt template(s) to <dir>. Edit, then commit with the guide.` or `Prompt templates are in <dir>. Edit, then commit with the guide.` Silent no-op when there is no guide root (`:176`).
- Tooltips (`:72-73`):
  - Draft: `The AI rewrites this rule in the guide from the team's evidence; you review the change and apply it as tracked changes (owners only).`
  - Prompts: `Copy the AI's instructions into the guide (FlareAI\prompts) so the team can tune them, versioned with the rules (owners only).`
- Status when `Read` fails: `Could not read feedback: <message>` (`:98`).

### 4.8 RuleSettingForm (`UI/RuleSettingForm.cs`) — modal, "Turn off / change rule"

- Title `House Style — Turn off / change rule` (`:22`). Fixed dialog, ClientSize 520 x 270.
- Rule text label (48 px, auto-ellipsis): `described` text from the pane, e.g. `Google · <section>: <rule text>` or `"<rule>" (AI judgment)` (`FindingsPanel.cs:765`).
- Radio **For the whole team (writes it into the style guide)** (`:26`). Enabled and checked only when the team guide is writable.
- Radio **For this project only** (`:27`). Checked when the team option is not available.
- Label `Set the rule to:` plus dropdown (`:29-32`), read-only, options `off`, `suggestion`, `warning`, `error`. Default `off`.
- Label `Why (the team sees this in the guide):` plus text box (`:34-35`).
- Buttons: **Apply** (`:37`, OK, default) and **Cancel** (`:38`). No tooltips.
- Team change writes to the guide's override file (`GuideOverrides.Set`, `FindingsPanel.cs:772`); project change writes to the project's overrides (`:773`). Both **(unverified: Core)**.

### 4.9 TextPrompt (`FindingsPanel.cs:921-937`) — modal, shared helper

- Title `House Style — <title>`. Fixed dialog, no min/max. ClientSize 520 x 190 (multiline) or 150 (single).
- Label (34 px) plus text box (50 px from top; 84 px high when multiline, 24 px when single). OK (75 wide) and Cancel (75 wide) at the bottom right.
- Used for: `Edit proposal`, `Refine proposal`, `Alt text`, `Propose turning off a rule`.

### 4.10 Toast (`UI/Toast.cs`) — non-modal notice

- Borderless, never takes focus, auto-hides after 7 seconds (`:38`), dismissed on any click (`:41`).
- Dark background (45,45,48), white text, width 240 minimum, label max width 340 (`:25-28`).
- Optional link (`LinkLabel`, blue) runs the action and closes the toast (`:32-33`, `:42`).
- Placed at the bottom-right of the Flare window, 24 px from the right edge and 48 px from the bottom (`Toast.cs:53`).
- A new toast replaces the one showing (`:50`). A replaced toast may drop its link.
- Three call sites (section 5).

### 4.11 Other surfaces

- **Reject ▾ menu**: `ContextMenuStrip` with five entries (section 3.7).
- **Folder picker** for Export for LLMs (`Plugin.cs:257`): description `Built HTML5 or Clean XHTML output to export (Markdown per topic + llms.txt are added to it):`, initial folder = newest output or empty.
- **Open file dialog** inside the notes dialog (`BuildGuideForm.cs:261`): filter `Text, Markdown, HTML|*.txt;*.md;*.htm;*.html|All files|*.*`.
- **Tooltips** exist only in FindingsPanel (`:143-150`, section 3.6) and TuneGuideForm (`:72-73`, section 4.7).
- **Diagnostics** is a message box, not a form (section 5, item 24).

---

## 5. MessageBox catalogue (29 call sites)

Caption and icon as written. `—` is an em dash. "Buttons" is the MessageBoxButtons value; default OK when omitted.

| # | File:line | Text (verbatim) | Caption | Buttons | Icon | When |
|---|---|---|---|---|---|---|
| 1 | Plugin.cs:57 | `House Style failed to start: <message>` | `House Style` | OK | Error | Execute() threw at start-up |
| 2 | Plugin.cs:169 | `Open any topic of the project first (Check project scans that project's Content folder).` | `House Style` | OK | Information | Check project with no topic open or no project files |
| 3 | Plugin.cs:182 | `<message>` | `House Style — Check project` | OK | Error | Check project threw |
| 4 | Plugin.cs:220 | `Open a topic in the XML Editor first.` | `House Style` | OK | Information | Topic assists with no editable topic |
| 5 | Plugin.cs:228 | `<message>` | `House Style — Topic assists` | OK | Error | Topic assists threw |
| 6 | Plugin.cs:264 | `<summary>` + two newlines + `Open the folder?` (summary: `N topic(s) exported as Markdown to <dir>, with llms.txt and llms-full.txt in <output>.`) | `House Style — Export for LLMs` | Yes/No | Information | After a successful export |
| 7 | Plugin.cs:270 | `<message>` | `House Style — Export for LLMs` | OK | Error | Export threw |
| 8 | Plugin.cs:321 | `<message>` | `House Style — Build style guide` | OK | Error | Build guide threw |
| 9 | Plugin.cs:340 | `<message>` | `House Style — Tune style guide` | OK | Error | Tune threw |
| 10 | Plugin.cs:351 | `The style linter is turned off in Settings.` | `House Style` | OK | Information | Validate style guide when the engine is null. **Unreachable**, see section 8, item 9 |
| 11 | Plugin.cs:360 | `<message>` | `House Style — Validate style guide` | OK | Error | Validate threw |
| 12 | Plugin.cs:368 | `<LLM error message>` | `House Style` | OK | Warning | Building a runner without a topic and no usable key (Validate/Build) |
| 13 | Plugin.cs:410 | `<diagnostics text>` + `(written to <log path>)` | `FlareAI diagnostics` | OK | none | Diagnostics button |
| 14 | Plugin.cs:415 | `<exception text>` | `House Style diagnostics error` | OK | none | Diagnostics threw |
| 15 | Plugin.cs:426 | `Open a topic in the XML Editor first.` | `House Style` | OK | Information | Check style / Rewrite / Ask with no editable topic |
| 16 | Plugin.cs:443 | `<message>` | `House Style — <Check style|Rewrite selection|Ask>` | OK | Error | RunAction threw |
| 17 | Plugin.cs:463 | `<LLM error message>` | `House Style` | OK | Warning | Creating a runner with no usable key; then Settings opens (`:464`) |
| 18 | UI/SettingsForm.cs:107 | `Timeout must be a number ≥ 10.` | none | OK | none | Save with a bad timeout |
| 19 | UI/SettingsForm.cs:133 | `Could not save settings: <message>` | `House Style` | OK | Error | Settings file write failed |
| 20 | UI/ResultForm.cs:172 | `Could not apply the change: <message>` | `House Style` | OK | Error | Apply threw |
| 21 | UI/GuideReportForm.cs:107 | `Check N prose rule(s) against the AI reviewer?` + two newlines + `This makes M model call(s).` | `House Style` | OK/Cancel | Question | Before the paid AI check of prose rules |
| 22 | UI/BuildGuideForm.cs:232 | `Could not write <topic path>: <message>` | `House Style` | OK | none | Writing the guide topic failed |
| 23 | UI/TuneGuideForm.cs:158 | `Could not open <link>` | `House Style` | OK | none | Draft: rule topic could not be opened |
| 24 | UI/TuneGuideForm.cs:160 | `Could not find this rule in <file>. Open it and edit the rule there.` | `House Style` | OK | none | Draft: rule text not found in the topic |
| 25 | UI/TuneGuideForm.cs:170 | `<message>` | `House Style — Draft change` | OK | none | Draft threw |
| 26 | UI/TuneGuideForm.cs:197 | `<message>` | `House Style — Customize AI prompts` | OK | none | Prompt copy threw |
| 27 | UI/TuneGuideForm.cs:234 | `Could not open <link>: <message>` | `House Style` | OK | none | Open rule threw |
| 28 | UI/FindingsPanel.cs:575 | `Apply <N> fixes in <M> files?` + `Each file gets the fixes as tracked changes. Files that aren't open now are saved and closed after their fixes (the changes stay tracked: review them with Review > Accept / Reject). Files you have open are left open and unsaved.` | `House Style` | OK/Cancel | Question | Accept all mechanical across more than 3 files |
| 29 | UI/FindingsPanel.cs:590 | `Write <N> proposal(s) into the topic as tracked changes?` + `Review them in Flare's Review tab (Accept / Reject change), or send the topic out in a review package. One Ctrl+Z removes them all.` | `House Style — Propose all` | OK/Cancel | Question | Propose all as tracked changes |

Notes:
- Item 13 uses `FlareAI diagnostics` as caption (old product name).
- Item 18 passes no owner window, so it is not centred on the Settings form.
- Items 22-27 use `MessageBox.Show(this, …)`. Their captions are `House Style` or `House Style — Draft change` / `House Style — Customize AI prompts`, and none of them has an icon.

### 5a. Toasts (not message boxes), 3 call sites

- `EditorIntegration.cs:159-160`: check on save: `N style issue(s) in <file>` [+ ` (E error(s))`] + `. Saved anyway.` Link `Show them`.
- `EditorIntegration.cs:230`: `Building. No style issues in the project's N topics and snippets.` (no link).
- `EditorIntegration.cs:233-234`: `Building. N style issue(s) in M file(s)` [+ `, E error(s)`] + `.` Link `Open the report`.

---

## 6. Settings

Storage: `%APPDATA%\FlareAI\settings.json` (`PluginSettings.cs:137-142`). Written only on Save (`SettingsForm.cs:127`). A corrupt file falls back to defaults (`PluginSettings.cs:154`).
The API key is DPAPI-protected for the current Windows user (`PluginSettings.cs:107-115`), with entropy `FlareAI.v1`.

| Setting (property) | Default | Where edited | What it changes |
|---|---|---|---|
| Provider (`Provider`) | Anthropic (direct) (`:94`) | Settings > Provider (`SettingsForm.cs:31`) | Backend: Anthropic direct or Bifrost gateway. Enables MCP include tools only for Bifrost. |
| Base URL (`BaseUrl`) | `https://api.anthropic.com` (`:22`, `:95`) | Settings > Base URL | Endpoint for the LLM client. Silently reset when it contains `/v1` on the direct provider (`SettingsForm.cs:94`). No hint text. |
| Model, standard (`Model`) | `claude-sonnet-5-5` (`:23`, `:96`) | Settings > Model (standard) | Standard-tier model: reviews, rewrite, Ask, alt text, rules from notes. Empty means the built-in default (`PluginSettings.cs:63`). |
| Fast model (`FastModel`) | empty (built-in) (`:54`) | Settings > Fast / deep models (left) | Fast tier: Refine, topic assists. Hint: `claude-haiku-5-5` (per `SettingsForm.cs:41`, `models.htm`). A team file overrides it. |
| Deep model (`DeepModel`) | empty (built-in) (`:56`) | Settings > Fast / deep models (right) | Deep tier: owner edits to the guide. Hint: `claude-opus-5-5`. A team file overrides it. |
| API key (`ProtectedApiKey`) | none (`:32`) | Settings > API / virtual key | Stored key, DPAPI-protected. Blank on Save keeps it. Clear stored key link. Fallback: env var below. |
| Environment key (`ANTHROPIC_API_KEY`) | (not set) (`:24`) | Environment, not in the UI | Used when no stored key. Status shown at `SettingsForm.cs:100`. |
| MCP include tools (`McpIncludeTools`) | empty (`:34`) | Settings > MCP include tools | Bifrost only: patterns for `x-bf-mcp-include-tools`. Disabled on Anthropic direct. |
| Max tokens (`MaxTokens`) | 4096 (`:97`) | **No UI** | Token limit per model call. |
| Timeout (`TimeoutSeconds`) | 90 (`:98`) | Settings > Timeout (s) | Model call timeout. Must be >= 10 (`SettingsForm.cs:107`). |
| Parallelism (`Parallelism`) | 0 (`:58`), code uses 6 (`Plugin.cs:370`, `:471`) | **No UI** | Concurrent model calls per check. |
| Style guide project (`StyleLayers`) | empty (`:100`) | Settings > Style guide project (multiline) | Guide folder(s), one per line (`;` also works, `:79`). Empty = bundled guide (`:81`). Last entry wins for the team file. |
| Use project layer (`UseProjectLayer`) | on (`:101`) | Settings checkbox `Also use the open project's variables, glossary and Resources\StyleGuide` | Adds the open project's variables and glossary as the last rule layer. |
| Team feedback folder (`FeedbackFolder`) | empty = personal only (`:70`) | Settings > Team feedback folder | Shared folder where each writer's decisions are pooled, read by Tune style guide. |
| Export after build (`LlmExportAfterBuild`) | off (not set in ctor, `:44`) | Settings checkbox `After each HTML build, add LLM-ready Markdown and llms.txt to the output` | Exports Markdown + llms.txt after each HTML build (`Plugin.cs:240`). Silent except for the log. |
| Show markers (`ShowMarkers`) | on (`:102`) | Settings checkbox `Mark paragraphs with proposals in the editor` | Margin bar and tint for paragraphs with open proposals (`EditorIntegration.cs:176`). |
| Live check (`LiveCheck`) | on (`:102`) | Settings checkbox `Check as I type (style rules only, no AI)` | Lint 1.2 s after typing stops (`EditorIntegration.cs:92`). |
| Check on save (`CheckOnSave`) | on (`:102`) | Settings checkbox `After saving, tell me how many style issues the topic has` | Toast after save (`EditorIntegration.cs:152`). |
| Report before build (`ReportBeforeBuild`) | on (`:102`) | Settings checkbox `When a build starts, report the project's style issues` | Toast when a build starts (`EditorIntegration.cs:217`). |
| Write diagnostics (`WriteDiagnostics`) | on (`:99`) | Settings checkbox `Write diagnostics log` | Log file to `%APPDATA%\FlareAI\diag.log` (per `troubleshooting.htm`); sets `Diagnostics.Enabled` (`Plugin.cs:41`). |

Other persisted files (user-visible state, not settings): `%APPDATA%\FlareAI\feedback.jsonl` (`Plugin.cs:200`, personal review decisions), `%APPDATA%\FlareAI\reviews.json` (`Plugin.cs:497`, up to 300 cached AI reviews), `%APPDATA%\FlareAI\dismissed.json` (`FindingsPanel.cs:36`, rejected proposals). The team-level files `FlareAI/owners.txt`, `layer.txt`, `models.json`, `prompts/` live in the style guide project (`docs/user-guide/Content/team.htm`); their parsing is in Core **(unverified)**.

---

## 7. Counts

- Ribbon: 1 tab, 3 groups, **11 buttons**.
- Tool Strip: 1 menu (7 items + 1 separator, covering 7 of the 11 actions), 1 toolbar (3 buttons). Entry points that duplicate ribbon actions: 10.
- Right-click: 3 items. Entry points duplicating ribbon actions: 3.
- Toast links: 2 (`Show them`, `Open the report`).
- **Distinct user-triggerable actions** (labels counted once; Cancel, Close, OK and Yes/No counted as generic):
  - Ribbon/menu: 11.
  - Review pane controls: 15 (Accept, Reject ▾, Wrong, Fine here, Rule doesn't apply, Skip for now, Edit…, Refine…, Re-analyze, Guide ↗, Accept all mechanical, Propose all as tracked changes, Re-check, Turn off rule…, Close). The Accept button also has three label variants (`Go to`, `Rewrite…`, `Suggest alt text…`).
  - Pane keyboard shortcuts: Enter (Accept), Delete (Reject menu), double-click (Accept).
  - Dialog actions: Run (Ask); Copy (Topic assists, one per section, 5 instances) ; Mine this project, Add notes or an old guide…, preferred-form dropdown, Add checked rule(s) to the guide (Build guide); Find rules, Load file… (Notes); Check prose rules with AI… (Validate); Apply as tracked changes, Restructure instead…, Show XHTML (Result); Refresh, Open rule in the guide, Draft a change with AI…, Customize AI prompts… (Tune); Apply (Turn off rule); Save, Clear stored key (Settings).
  - Total labelled dialog and pane actions: about **46** (11 ribbon + 15 pane + 20 in dialogs), plus generic Cancel/Close/OK.
- **Settings**: 19 properties (`PluginSettings.cs:26-70`); 17 editable in the Settings form (provider, base URL, model, fast, deep, API key, MCP tools, style guide project, project-layer checkbox, feedback folder, timeout, export checkbox, markers, live check, check on save, report before build, diagnostics); 2 with no UI (`MaxTokens`, `Parallelism`). Plus one env var (`ANTHROPIC_API_KEY`).
- **Dialogs and surfaces**:
  - Modal forms: 10 types: AskForm, AssistForm, BuildGuideForm, NotesDialog (static), GuideReportForm, ResultForm, TuneGuideForm, RuleSettingForm, SettingsForm, TextPrompt (static).
  - Modeless: FindingsPanel (docked pane or floating window), Toast.
  - OS common dialogs: FolderBrowserDialog, OpenFileDialog.
  - Popup menu: Reject ▾.
  - Message boxes: 29 call sites (section 5). Four ask a question: items 6 (Yes/No, open folder), 21, 28 and 29 (OK/Cancel confirmations).
  - Toasts: 3 call sites.
- **Pane-specific**: 4 TextPrompt uses, 1 RuleSettingForm, 1 ResultForm (restructure).
- **Tooltips**: 10 `SetToolTip` calls. FindingsPanel has 8 (`FindingsPanel.cs:143-150`); TuneGuideForm has 2 (`TuneGuideForm.cs:72-73`). No other form has tooltips.

---

## 8. Facts a UX reviewer should look at

Stated as observed. No design opinion.

1. **Four names for one category.** "mechanical" (`FindingsPanel.cs:413`, `:416`, `:911`), "rule fix" (`:492`), "exact" (`:149`, `:913`, `:594`), and "exact rule fix" (`:149`). The docs use "exact fix" (`check-style.htm`).

2. **Accept and Apply for the same operation.** Pane button: `Accept` (`FindingsPanel.cs:109`). Result window: `Apply as tracked changes` (`ResultForm.cs:73`). Both create tracked changes.

3. **Three words for "run it again."** `Re-check` (`FindingsPanel.cs:138`, whole topic with AI), `Re-analyze` (`:113`, one paragraph), `Refresh` (`TuneGuideForm.cs:62`, reloads decisions).

4. **Delete key: docs and code differ.** `check-style.htm` says "Reject, or Delete … Hides the proposal". Code: Delete opens the Reject ▾ menu (`FindingsPanel.cs:188`). The tooltip also says "(Delete)" (`:144`).

5. **Turn off rule: three labels.** Button `Turn off rule…` (`FindingsPanel.cs:139`), dialog title `Turn off / change rule` (`RuleSettingForm.cs:22`), request prompt title `Propose turning off a rule` (`FindingsPanel.cs:756`). `team.htm` says `Ignore rule…`, which does not appear in the UI.

6. **Export: five names.** Ribbon/menu `Export for LLMs` (`Plugin.cs:92`); caption `House Style — Export for LLMs` (`:264`); doc page title `Export for AI assistants` (`llm-export.htm`); settings checkbox `After each HTML build, add LLM-ready Markdown and llms.txt to the output` (`SettingsForm.cs:56`); docs setting name `Export for LLMs after build` (`settings.htm`).

7. **Old product name in UI.** Diagnostics caption `FlareAI diagnostics` (`Plugin.cs:410`), while other captions say `House Style` and the diagnostics error box says `House Style diagnostics error` (`:415`). Plugin folder and docs still say `FlareAI`.

8. **Caption and icon are not consistent.** Caption formats: `House Style`, `House Style — <action>`, `FlareAI diagnostics`, and none (`SettingsForm.cs:107`). Icons: Error, Information, Warning, Question, and none, varying across message boxes with the same kind of failure (for example `BuildGuideForm.cs:232`, `TuneGuideForm.cs:158` have no icon; `Plugin.cs:182` has Error).

9. **Dead branch names a setting that does not exist.** `Plugin.cs:351` says `The style linter is turned off in Settings.`. `StyleService` never returns null (`Plugin.cs:485-494`), so the branch is unreachable. `LintRunner` has the same unreachable null check (`:522`). `PluginSettings` has no linter toggle.

10. **Modal blocking.** Ask, Rewrite selection, Topic assists, Validate, Build, Tune and Settings are modal (`ShowDialog`) (`Plugin.cs:223, 316, 335, 355, 188, 438`). Only Check style, Check project and the auto-follow pane are modeless. The AI result for Rewrite and Ask blocks the editor until closed (`ResultForm`, `Plugin.cs:437-438`). The pane's Rewrite paragraph (`FindingsPanel.cs:739`) and alt-text prompt are modal too.

11. **Same button, different action.** The Accept button text changes to `Go to`, `Rewrite…`, or `Suggest alt text…` (`FindingsPanel.cs:904`). The first two only move the cursor or open a window; `Suggest alt text…` opens an AI call and a prompt before anything is written (`:514`).

12. **Skip versus Reject.** `Skip for now` (`:120`) moves on and records nothing. `Wrong`, `Fine here`, `Rule doesn't apply` record a reason and hide the proposal for this text (`:116-118`, `:631-642`).

13. **Silent no-ops.**
    - Ask with empty text keeps the dialog open with no message (`AskForm.cs:26`).
    - Edit with unchanged text closes without a message (`FindingsPanel.cs:649`).
    - Customize AI prompts does nothing when no guide root exists (`TuneGuideForm.cs:176`).
    - Guide ↗ ignores links other than `file:` and `https://` (`FindingsPanel.cs:887`).
    - Accept on a stale proposal changes only the status line (`:506`).

14. **Clear stored key is not final until Save.** `SettingsForm.cs:45` clears only the in-memory settings object; nothing is written until Save (`:127`). Cancel discards it. There is no confirmation.

15. **Base URL changed without a message.** `SettingsForm.cs:93-94` replaces the Base URL with the Anthropic default when it contains `/v1` and the provider is not Bifrost.

16. **Settings opens on a missing key, including on topic switch.** `NewRunner` shows the LLM error (`Plugin.cs:463`) and then opens Settings (`:464`). It also runs when the pane follows a topic switch (`:541`). Whether `LlmClientFactory.Create` throws without a key is in Core **(unverified)**.

17. **Model default text.** The `Fast / deep` hint names `claude-haiku-5-5` and `claude-opus-5-5` (`SettingsForm.cs:41`). The standard model field defaults to `claude-sonnet-5-5` (`PluginSettings.cs:23`) with no hint. `models.htm` gives the same defaults.

18. **Field label versus docs.** Settings label `API / virtual key` (`SettingsForm.cs:42`); `settings.htm` says `API key`.

19. **Pane placement comments disagree.** `PanelHost.cs:10` says "a pane on the right". The `FlarePane` comment at `PanelHost.cs:83` says "the left panes by default". Actual placement is not verifiable from source.

20. **Status label may clip.** The status label is fixed at 36 px high (`FindingsPanel.cs:87`), but `ProgressBar`, Summary, AI progress, lint note and team status are concatenated onto it (`:246-262`). Long text can be cut off.

21. **Duplicate paths to the same action.**
    - Check style: ribbon, Tool Strip menu, toolbar, right-click, and the save toast link (`EditorIntegration.cs:160`).
    - Rewrite selection: ribbon, menu, toolbar, right-click.
    - Ask: ribbon, menu, toolbar, right-click.
    - Check project: ribbon, menu, and the build toast link (`:234`).
    - Settings: ribbon, menu, and auto-open on a missing key (`Plugin.cs:464`).
    - Validate style guide and Diagnostics: ribbon and menu.

22. **Tool Strip mode is partial.** Its menu omits Topic assists, Tune style guide, Build style guide and Export for LLMs (`Plugin.cs:104-112`). Its toolbar label `Rewrite` differs from the menu's `Rewrite selection` (`:116`, `:106`).

23. **Key tips.** Mnemonics are `C R A P I S V T B L D` (`Plugin.cs:79-93`). `I` is Topic assists and `L` is Export for LLMs; neither letter appears in the label.

24. **Toast replacement can drop an action.** A new toast closes the current one (`Toast.cs:50`), so the `Open the report` or `Show them` link disappears with it. The toast also closes after 7 s (`:38`) or on any click (`:41`).

25. **Terms: proposal, issue, finding.** Pane: `proposal(s)` (`FindingsPanel.cs:415`). Toasts and project status: `style issue(s)` (`EditorIntegration.cs:159`, `:233`; `FindingsPanel.cs:285`). Code and class names: `FindingsPanel`, `StyleFinding`. The pane's own doc comment says "A review as discrete pieces of work" (`:13-14`).

26. **Jargon in user-visible text.**
    - "layer": `Layers (later wins)` (`GuideReportForm.cs:86`); pane labels `AI · Google: <section>` (`FindingsPanel.cs:394`).
    - "lint" and "linter": `Linting…` (`:197`), `linter only` (`:285`), `Proposed edit passes the style linter.` (`ResultForm.cs:121`).
    - "rule id": `Rule id <id>` (`FindingsPanel.cs:465`).
    - "XHTML": `Show XHTML` (`ResultForm.cs:71`).
    - "structure"/"structural": `Applies as a paragraph-level tracked change (structure changes).` (`ResultForm.cs:123`).
    - "permanent ids": `Gave N new rule(s) permanent ids.` (`GuideReportForm.cs:77`).
    - "Avoid/Use row", "prose rule", "table rule": `BuildGuideForm.cs:165`, `:198-201`.
    - "word-level tracked change(s)": `ResultForm.cs:123`.
    - "snippet" in the pane: `In snippet <name>` (`FindingsPanel.cs:436`).
    - "Flare 22 … Ctrl+Y": `ResultForm.cs:125`.

27. **Project check does not mention AI.** The triage row and status say `(linter only)` (`FindingsPanel.cs:285`). The AI review only runs from the topic (`check-project.htm`).

28. **Export folder description omits llms-full.txt.** The picker says `llms.txt` (`Plugin.cs:257`), while the summary says `llms.txt and llms-full.txt` (`:278`). `llms-full.txt` appears nowhere else in the UI.

29. **Tune view can be empty without explanation.** When there are no decisions, the head says `0 decision(s) from 0 writer(s). Source: …` and `Nothing needs attention yet.` (`TuneGuideForm.cs:115-127`). No empty-state text is shown in the list itself.

30. **Build guide: non-owners see an enabled list and a disabled button.** The list and tools stay usable; only `Add checked rules to the guide` is disabled, and the reason appears only in the head text (`BuildGuideForm.cs:86-87`, `:215`).

31. **Assist copy formats differ from display.** Index keywords display one per line but Copy gives semicolons (`AssistForm.cs:49`, heading says "one term per line"). Section text boxes are editable, though nothing is saved from them.

32. **Diagnostics dialog has no icon and shows raw state.** It lists the doc path, selection text, selection nodes and the routing (`Plugin.cs:378-410`), and the box text ends with a log path. It is not a styled window.

33. **Confirmation thresholds differ.** Accept all mechanical confirms only for more than 3 files (`FindingsPanel.cs:575`). Propose all always confirms (`:590`). Accept all mechanical in one topic has no confirmation.

34. **Chosen wording used as jargon on tooltips.** The Propose all tooltip says "review package" (`FindingsPanel.cs:148`); its confirmation says "Review tab" (`:590`). "review package" is not used anywhere else in the UI.

35. **Ask label scope.** `What should I do with the selected text? (whole topic if nothing is selected)` (`AskForm.cs:22`). The pane scope is set once per check and stays on re-check (`FindingsPanel.cs:80-81`), which the label does not mention.

36. **Plugin description and Topic assists.** The plugin description says "Edits arrive as tracked changes." (`Plugin.cs:29`). Topic assists writes nothing into the topic (`assists.htm`: "The plugin writes nothing into the topic"), which is consistent with the description but is a separate path that only offers copy buttons (`AssistForm.cs:64`).

37. **Check style and Check project may need a key even for lint.** `RunAction` (`Plugin.cs:432`) and `OnCheckProject` (`:172-173`) call `NewRunner`. If `LlmClientFactory.Create` throws without a key (Core, **unverified**), `NewRunner` shows the LLM error (`:463`), opens Settings (`:464`) and returns null, so the pane does not open. Live check, check on save and report before build use a lint-only runner with no key (`LintRunner`, `Plugin.cs:524`). Whether `LlmClientFactory.Create` throws without a key is in Core **(unverified)**. The docs say the project check "costs nothing" (`check-project.htm`), which is the case for the lint itself.

38. **Reject on the last open proposal.** `Reject` records the reason, hides the proposal, then calls `Summary()` (`FindingsPanel.cs:641`). When no open proposal remains, the status reads `No open proposals.  Rejected (<reason>); it stays hidden for this text.`. The card clears (`:640`, `:817`).

39. **Diagnostics log path is shown in the message box.** `(written to <path>)` (`Plugin.cs:410`). The path is in `%APPDATA%\FlareAI\diag.log` per `troubleshooting.htm`; `Diagnostics.LogPath` is in Core **(unverified)**.

40. **Layout size ranges.** Pane minimum 260 x 320 (`FindingsPanel.cs:85`). Floating fallback 720 x 460 with minimum 320 x 260 (`PanelHost.cs:47-48`). Dialogs have minimums from 480 x 300 (Notes) to 640 x 420 (Build guide). No dialog specifies a maximum size.
