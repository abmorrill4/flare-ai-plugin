# House Style: deliberate UX review

Scope: ribbon, review pane, right-click, toasts, dialogs, settings, first run. Evidence: the 12 Flare 2026 screenshots, `inventory.md` (referred to as INV), the user guide, and the code where I confirmed behavior (marked "code-verified"). Findings use ids F-01 and up. No source was changed.

Orchestrator observations, verified:
- Settings checkbox cut off: **confirmed** (screenshot 06 shows "...variables, glossary and"; code `SettingsForm.cs:51` gives the text 368 px at 9 pt and no wrap). The "Clear stored key" link is also cut ("Clear stored", `:44`, width 90). The MCP include tools field is invisible, not just disabled (06).
- Diagnostics caption "FlareAI": **confirmed** (screenshot 11, `Plugin.cs:410`). The box also prints a raw `FlareAI\diag.log` path and raw XML selection dump.
- Nine buttons on the card: **confirmed** (screenshot 03 shows 9 controls in three wrapped rows: Rewrite…, Reject ▾, Edit…, Refine…, Re-analyze, Guide ↗, Re-check, Turn off rule…, Close). Plus 2 triage buttons above the list = 11 command buttons in a 185 px pane.
- Validate shows a raw monospace log: **confirmed** (08). It is a 1,000-line wall of "Overrides ... The company rule wins." warnings; in the shot the first screenful is entirely 13 warnings that are by-design overrides.
- Tune lists raw ids: **confirmed** (09: "Asked to turn off: company:plain-language/2", "Working well: Google.Contractions"; also shows an email address as the "Writers" column).
- Check project replaces the per-topic pane and markers vanish: **confirmed**. Screenshot 07 vs 02: the editor lost its amber margin bars and the card is blank. Cause (code-verified, `EditorIntegration.cs:179-180`): when the pane is a project report, markers fall back to `state.Lint`, which holds only the last live lint, so the topic you are looking at loses its tint although the list shows its issues.

Honest scorecard: the plugin's engine interactions are strong (tracked changes in one undo step, stale-text detection, cached reviews, explain-the-rule card, reject with a reason). The surface around it grew by accretion: three naming systems, no accessibility metadata at all (zero `AccessibleName` in `src/`, code-verified), and modal dialogs for what should be panes.

---

## 1. Executive summary

1. **The ribbon is a menu of internals, not tasks.** Eleven buttons, five of them (Validate, Tune, Build, Diagnostics, Settings) used by one person once a month, shown to every writer all day. A writer's daily path is two buttons (Check style, Check project). Cut the ribbon to about six controls, and group the guide-owner tools behind one "Style guide" menu.
2. **First run is broken for the free path.** With no API key, Check style and Check project show a warning that says "Open AI > Settings" (there is no "AI" tab) and then refuse to open, although the linter needs no key (code-verified, `Plugin.cs:460-465`). It also re-fires on every topic switch while the pane is open. The product's best demo, free instant lint, is behind a key wall.
3. **The review card carries nine buttons; a card should carry two.** Accept and Reject are the job. Edit, Refine, Re-analyze, Guide, Turn off rule belong in an overflow and in the right-click menu on the list row; Re-check and Close belong in the pane toolbar. Add the keyboard model people expect from a triage list (A, R, J, K, E).
4. **Three vocabularies and many jargon words leak.** "proposal / issue / finding", "mechanical / exact / rule fix", "Re-check / Re-analyze / Refresh", "Accept / Apply", "layer", "lint", "rule id", "XHTML". One glossary (section 5) removes most of it, and costs one day.
5. **Two states fall through the cracks: scale and accessibility.** Project mode (183 issues) uses the same 185 px pane with no filter, no counts by rule, no per-file Accept, no card, and loses markers; and the whole UI has no AccessibleName, no tab order, no screen-reader announcements for progress. Fix both before adding any feature, because every planned feature (rule types, readability, CLI) lands in these two surfaces.

---

## 2. Information architecture

### 2.1 Principles for placement

- **Ribbon** = what a writer starts on purpose, at least weekly: check, ask the AI, settings. Max 6 controls, max 3 groups.
- **Pane** = work in progress (triage, project results).
- **Right-click** = actions on the thing under the cursor (selection or list row).
- **One "More" menu** = rare, safe, no judgment needed (export, diagnostics, user guide, about).
- **Settings** = decisions you make once. Put Diagnostics and the guide link here as buttons, not on the ribbon.
- **Owner tools** = one split button "Style guide", opened by the person who owns rules.

### 2.2 Proposed ribbon

Flare convention: large icon buttons, short labels, groups named by task, dropdowns for related variants (Flare's own Review and Insert tabs do this). Proposed:

```
 File  Home  Insert ... Review  Tools  ...  [ House Style ]
 ┌─ Check ─────────────────┬─ Write ──────────┬─ Guide ─────────┬─ House Style ───────────┐
 │ [Check ]  [Check   ]    │ [ Ask AI  ▾ ]    │ [ Style guide ▾]│ [Settings] [ More ▾ ]   │
 │ [topic ]  [project ]    │  Rewrite         │  Validate       │                         │
 │                         │  Ask…            │  Review feedback│  More:                  │
 │                         │  Topic details…  │  Add rules      │   Export for LLMs…      │
 │                         │                  │  Open guide     │   Diagnostics…          │
 │                         │                  │                 │   User guide            │
 └─────────────────────────┴──────────────────┴─────────────────┴─────────────────────────┘
```

Six controls (4 large, 1 small, 1 menu) instead of eleven. Key tips: C topic, P project, A ask, G guide, S settings, M more (letters match the labels, fixing INV 8.23 where I and L do not).

Why Ask AI is a dropdown, not three buttons: Rewrite selection and Ask both take the same selection and open the same result window; the writer's mental model is "do something with this text." Make the main click open Ask (with a "Rewrite" preset chip in the dialog) and the menu offer the presets.

Why "Guide ▾" rather than hiding: owners need discoverability; writers glance at it once. If the plugin can detect that the user is not an owner (`Team` role is already read), set the group's label to "Guide (owners)" tooltip and keep Validate available read-only. Do not hide: hidden buttons break training and screenshots.

### 2.3 Before / after for every action

| Current action | Today | Proposed home | Notes |
|---|---|---|---|
| Check style | Ribbon, right-click, toolbar, menu, save toast | Ribbon "Check topic" (renamed), right-click, toast | Keep; rename for clarity ("style" is vague, scope is a topic) |
| Rewrite selection | Ribbon, right-click, toolbar, menu | Ask AI ▾ menu; right-click | Selection-bound, so right-click is its natural home |
| Ask… | Ribbon, right-click, toolbar, menu | Ask AI (main click); right-click | Add presets to the dialog (Rewrite, Shorten, Simplify, Make consistent) |
| Check project | Ribbon, menu, build toast | Ribbon "Check project" | Keep. Also the primary toast link |
| Topic assists | Ribbon (modal) | Ask AI ▾ > "Topic details…"; right-click topic tab/Explorer | Rename: "assists" means nothing. It drafts description, keywords, synonyms, Q&A |
| Settings | Ribbon, auto-open on missing key | Ribbon (small). Missing key opens an inline pane banner instead | Settings is a one-time act |
| Validate style guide | Ribbon | Style guide ▾ | Owners. Opens a results pane, not a log (F-12) |
| Tune style guide | Ribbon | Style guide ▾ > "Review feedback" | Rename: what it shows is feedback on rules |
| Build style guide | Ribbon | Style guide ▾ > "Add rules from content…" | Rename: it mines rules, it does not build a guide |
| Diagnostics | Ribbon, message box | Settings > "Troubleshooting" and More ▾ | Support tool; show a window with a Copy button |
| Export for LLMs | Ribbon (Output group) | More ▾ | One use per release; plus the build setting. Align name with docs (one name: "Export for AI assistants") |
| Settings (toggles: markers, live check, save toast, build toast) | 5 flat checkboxes | Settings "Editor" section, plus a pane header menu with the three most-toggled | Writers who find it noisy turn it off in context |
| Accept / Reject | Pane card | Pane card (only primary controls) | |
| Edit… / Refine… | Pane card | Card overflow ⋯ and keyboard E / F | |
| Re-analyze | Pane card | Automatic on edit; overflow "Re-analyze paragraph" | Should rarely be needed (F-09) |
| Guide ↗ | Pane card | A link inside the rule line of the card | It is a link, not a command |
| Re-check | Pane bottom bar | Pane toolbar (⟳ icon) | |
| Turn off rule… | Pane bottom bar | Card overflow, right-click on a list row, and "Ask owners to change this rule" for writers | Word for non-owners: "Suggest changing this rule" |
| Close | Pane bottom bar | Remove; Flare's own pane ✕ and Escape already close | Redundant, 1 of 9 buttons |
| Accept all mechanical | Triage row | Pane toolbar split button "Fix all N exact fixes ▾" | |
| Propose all as tracked changes | Triage row | Same split menu: "Write all as tracked changes for review" | |
| Reject reasons (Wrong / Fine here / Doesn't apply / Skip) | Reject ▾ menu | Keep, as the Reject split. Skip becomes J | Skip is navigation, not a rejection |
| Tool Strip menu and toolbar | 7 of 11 actions, different labels | Same six controls, same labels | Single source of truth for labels |
| Copy buttons (Topic details) | 5 Copy buttons | Keep, add "Copy all" | |
| Result window actions (Apply, Restructure, Show XHTML) | Modal | Keep Apply; "Show XHTML" becomes "Show markup" in an overflow | Writers do not know XHTML |

---

## 3. Findings

Severity: critical = blocks a core job or loses trust; major = frequent friction or accessibility barrier; minor = occasional; polish = craft.

| ID | Sev | Heuristic | Where | Problem | Evidence | Recommendation |
|---|---|---|---|---|---|---|
| F-01 | critical | Error prevention; help users recover; first-run | `Plugin.cs:460-466`, `:172-173`, `:541` | No key = Check style and Check project refuse to open, although lint is free. Message tells users to "Open AI > Settings" (no such tab), then Settings opens modally. Also fires when the pane follows a topic switch. | Code-verified: `AnthropicClient` ctor throws on empty key (`AnthropicClient.cs:29`); `NewRunner` shows MessageBox then `OnSettings()` | Build the runner in two parts: lint always works; AI is optional. Open the pane with lint results and a banner: "Style checks are on. Add your Anthropic key to turn on AI review. [Add key…] [Not now]". Never show a modal on topic switch. Fix the copy: "Settings" lives on the House Style tab. |
| F-02 | major | Recognition vs recall; minimalism | Ribbon (01), `Plugin.cs:79-93` | 11 buttons, 5 of which are owner or support tools; two groups called "Assist" and "Plugin" are not task names; Diagnostics sits in "Plugin" but is declared later than Output | Screenshot 01: all 11 same size, same weight | Adopt the layout in 2.2: 6 controls, groups named Check / Write / Guide. |
| F-03 | major | Aesthetic and minimalist design | Pane card (03) | 9 buttons across 3 wrapped rows in a 185 px pane; the row wraps unpredictably (`Re-analyze` and `Guide ↗` land on row 2, `Close` last). Primary action not visually distinct. | 03: Rewrite…, Reject ▾, Edit…, Refine… / Re-analyze, Guide ↗ / Re-check, Turn off rule…, Close | 2 primary buttons, ⋯ overflow, toolbar for pane-level commands. See section 4. |
| F-04 | major | Consistency and standards; accessibility | whole `src/FlareAI.Flare/UI` | No `AccessibleName`/`AccessibleDescription` anywhere; no explicit tab order; progress is not announced; color-only signals (red severity text, strikethrough, tint) | Code-verified: grep for "Accessible" returns 0 in `src` | Set AccessibleName on every control (list: "Proposals"; Accept: "Accept proposal"), AccessibleRole on custom cards, `AccessibleDescription` = the summary line; announce status changes via a hidden live label; add text/icon for severity (E / W / i). Add a TabIndex pass and tab through each dialog as a test. |
| F-05 | major | Visibility of system status | Project check (07) | Status says "183 issue(s) in 120 of 252 files (linter only). Accept opens the file." Not scannable; no per-rule counts; list cut at about 12 rows in a 185 px pane; "Propose all (0)" disabled with no reason | 07 | Summary strip plus Filters (section 4.5). Disable with a reason tooltip: "Select a topic to write tracked changes." |
| F-06 | major | Consistency; visibility | `EditorIntegration.cs:179-180` | Check project replaces the topic pane and the editor loses its margin markers, though the open topic has issues (the list shows `accounts\about.htm` issues) | 02 (markers) vs 07 (none) | Markers should come from the union: use the pane's findings for the open topic in either mode. Project mode must not own the single pane: open it as a second tab in the same pane host (Topic / Project) so switching back does not lose the topic. |
| F-07 | major | Match between system and real world | Strings: INV 8.1, 8.25 | "mechanical", "exact", "rule fix", "proposal", "issue", "finding" mean the same or overlapping things | Tooltips say "exact rule fix", button says "mechanical", card says "rule fix", docs say "exact fix" | One glossary (section 5.1). Writer-facing nouns: **suggestion** (any item), **fix** (exact, one click), **rewrite** (AI), **issue** only in counts of what is wrong. |
| F-08 | major | Help users recover; user control | Reject (04) | Reject carries 3 reasons plus Skip. Skip is navigation, hides inside "Reject". Writers who reject 20 items must open a menu 20 times; Delete opens a menu rather than rejecting (docs say Delete rejects, INV 8.4) | 04; `FindingsPanel.cs:188` | R rejects with the last-used reason (default "Fine here"); Shift+R opens the reasons; J/K/Down/Up navigate and replace Skip. Show an Undo link for 8 seconds after a reject ("Hidden. Undo"). |
| F-09 | major | Visibility; efficiency | Re-analyze (`FindingsPanel.cs:696`) | The writer must remember to click Re-analyze after editing a paragraph, else the item shows ⚠ stale. | INV 3.4: stale = italic and grey | Re-run lint on the edited paragraph automatically (live check already runs in 1.2 s) and keep the manual command in overflow. |
| F-10 | major | Error prevention | Settings (06) | Checkbox label cut off; "Clear stored" link truncated; MCP include tools label shown with no visible field; fast/deep model inputs have no labels (two empty boxes); 17 fields in one 692 px column | 06; `SettingsForm.cs:44,51` | Group into sections with headers: Connection (key, model), Editor (4 toggles), Team (guide, feedback), Advanced (provider, base URL, fast/deep, MCP, timeout, diagnostics, export after build). Use AutoSize, wrap, and a TableLayoutPanel; add a "Test connection" button. |
| F-11 | major | Error prevention; visibility | Settings, model fields (`SettingsForm.cs:37-41`) | Model name typed by hand; wrong string fails later at the first AI call. | 06: no validation, no list | Offer a dropdown of known models plus "Custom…"; "Test connection" returns the model and token cost. |
| F-12 | major | Aesthetic; recognition vs recall | Validate (08) | A monospace text log with 13 by-design warnings first ("Overrides google:tone/20 ...: The company rule wins.") and the actual result ("629 prose rule(s) have Bad/Good examples") in a header row | 08 | Summary card first (green check: "All 475 table rules pass their examples"), then a grouped, collapsible list: Errors (must fix) / Overrides (informational, collapsed) / Duplicates (suggest removing). Each row has "Open rule". |
| F-13 | major | Match between system and real world; privacy | Tune (09) | Raw ids ("company:plain-language/2", "Google.Contractions") and email addresses in the Writers column; "Working well" fills the list with 12+ rows of non-actionable data, pushing the one actionable row to the top only by group order | 09 | Show the rule's human title ("Avoid 'in order to'") first, id in a smaller grey column. Collapse "Working well" by default. Show initials or first name; the email is personal data on a shared screen. |
| F-14 | major | Visibility; recognition | Check project (07) | Card area is blank (grey box) in project mode with no instruction, and Accept is disabled; clicking an item does not open the file, but status text says "Accept opens the file" (an Accept button that is also navigation) | 07; INV 3.3, 8.11 | Double-click or Enter opens the topic; card shows "Open topic to fix" with the first match highlighted. Replace "Accept opens the file" with a real "Open" button. |
| F-15 | major | Flexibility and efficiency | Pane (03) | No keyboard model other than Enter and Delete; no J/K; no accelerators on buttons; Escape does not close the pane | `FindingsPanel.cs:185-189` | Section 4.4. |
| F-16 | major | Flexibility; efficiency | Pane list | In a 7-item topic, fine. At 183 items there is no filter, no sort, no group-by-rule, no "this rule only", no "mark file done" | 07 | Section 4.5. |
| F-17 | major | Consistency with Flare | Right-click (05) | Three new entries ("House Style: Check style / Rewrite selection / Ask…") placed with plain text, no icons, no submenu; Flare uses submenus for families (Insert, Synonyms) | 05 | One submenu "House Style ▸" with Check, Rewrite, Ask, and (context-sensitive) "Add 'X' to the glossary", "Why was this flagged?" when the cursor is on a marked paragraph. |
| F-18 | major | Consistency; recognition | Margin markers (02) | Marker colors rely on tint and a thin bar; one hover target; no tooltip or "Show in pane" action; color-only (amber for warning) | 02 | Add a right-click item "Show suggestion in House Style pane"; use a distinct bar pattern per severity (solid, dashed) to be color-blind safe. |
| F-19 | minor | Consistency | Diagnostics (11) | Caption "FlareAI diagnostics"; no icon; dump of raw selection XML; "(written to C:\Users\...\Roaming\FlareAI\diag.log)" | 11; `Plugin.cs:410` | Window titled "House Style — Diagnostics" with sections (Environment, Model routing, Last errors), Copy and Open log buttons. Redact the file path of the user profile. |
| F-20 | minor | Consistency | MessageBoxes (INV 5) | 29 boxes, 5 caption styles, inconsistent icons (some none); errors raw (`<message>` from exceptions) | INV 5 items 3, 5, 7, 8, 9, 11, 22-27 | One `Notify.Error(what, why, fix)` helper; 3 icons only: Error, Warning, Information. No raw exception text; offer "Copy details". |
| F-21 | minor | Help and documentation | Docs vs code (INV 8.4, 8.5) | Docs say "Delete rejects"; code opens a menu. Docs say "Ignore rule…"; UI says "Turn off rule…". | `check-style.htm:21`, `team.htm` | Fix with section 4.4 and the glossary. |
| F-22 | minor | Error prevention | AskForm (`AskForm.cs:26`) | Empty prompt: dialog stays, no message. Label only mentions the selection scope, not that nothing is selected means the whole topic AND that the pane keeps the previous scope | INV 8.13, 8.35 | Disable Run until text is entered, with a placeholder: "For example: make this shorter and keep the product name". Show the scope: "Applies to: 3 selected paragraphs". Add 4 preset chips. |
| F-23 | minor | Visibility of system status | Check on save toast (`Toast.cs:38`) | Auto-hides in 7 s; replaced by the next toast (a build toast kills the "Show them" link); any click dismisses | INV 8.24 | Queue toasts; keep the link live for 15 s; add a status-bar badge ("4 style issues") that persists. |
| F-24 | minor | Consistency | Build guide (10) | Empty list shown with only two small buttons; no empty state instructions; the confirmation button "Add checked rules to the guide" is disabled for non-owners with no reason beyond the head text | 10; INV 8.30 | Empty state: "Find rules your team already follows. [Mine this project] reads every topic (about a minute, no AI). [Add notes or an old guide…]". Disabled button tooltip: "Only guide owners can add rules. Owners: ...". |
| F-25 | minor | Match between system and real world | Topic assists (12) | Name is vague; Copy buttons are 70 px tall boxes stretching as tall as the text area; synonyms are lines of CSV; five targets in five Flare places; edits to the boxes are not saved | 12 | Rename "Topic details". Tab per item, or collapse. Button text "Copy for Index". Add "Open Topic Properties" deep links where Flare's API supports it. |
| F-26 | minor | Consistency | Words: Accept vs Apply; Re-check / Re-analyze / Refresh | See glossary | INV 8.2, 8.3 | Section 5.1. |
| F-27 | minor | Help and documentation | No in-product help | The only discoverability for the rules is Guide ↗ and the Why text; no "?" and no link to the user guide from the ribbon | Ribbon (01) | More ▾ > User guide; first-run panel (4.6). |
| F-28 | minor | Visibility of system status | Pane status line | Fixed 36 px label with concatenated text; two-line wrap clips in 185 px | INV 8.20; 03 | Split into a headline (count) and a transient line; allow wrap; use the live region. |
| F-29 | minor | Aesthetic | Pane width | The docked pane is 185 px in the default Flare layout; list columns "Rule" cut to "Company · Plain…" | 02, 03 | Collapse columns: show the rule as a muted second line under the item text at narrow widths; default the pane to 320 px. |
| F-30 | minor | Match | "Layer" jargon in the list column ("AI · Google: choose", "Company · Plain …") | Writers do not know "layers" | 03 | Use source chips: Google, Our rules, AI. Section name as tooltip. |
| F-31 | minor | Match | Card kind labels: "rule fix", "AI wording", "use the variable", "fix in the editor", "needs a rewrite" | Five labels in one position; "needs a rewrite" and the button "Rewrite…" differ in verb | 03 | Three types, each with one verb: **Fix** (exact), **Reword** (AI wording), **Needs you** (manual). |
| F-32 | minor | Error prevention | Clear stored key (`SettingsForm.cs:44`) | Clears immediately in the form with no confirmation, final only on Save | INV 8.14 | Rename "Remove key", confirm in place ("Key removed. Save to apply." with an Undo link). |
| F-33 | minor | Error prevention | Base URL silently changed (`SettingsForm.cs:93`) | Value rewritten without notice | INV 8.15 | Validate inline: "This URL ends in /v1. We use https://api.anthropic.com. Fix it?" |
| F-34 | minor | Consistency | Tool Strip menu vs ribbon labels ("Rewrite" vs "Rewrite selection") and 7 of 11 actions | | INV 8.22 | Generate both from the same table. |
| F-35 | polish | Aesthetic | Hint text | Hints are grey 8 pt at low contrast; lines wrap at 368 px | 06 | Use 9 pt, meet WCAG AA (4.5:1). |
| F-36 | polish | Aesthetic | Pane list | Severity only via dark red text; accepted items struck through; stale italic grey, three signals in one list | INV 3.4 | Leading glyph column: ● error, ▲ warning, ○ suggestion; accepted check mark; stale warning triangle with tooltip. |
| F-37 | polish | Consistency | Settings label "API / virtual key" vs docs "API key" | | INV 8.18 | "Anthropic API key" (direct) and "Gateway key" (Bifrost), switching with provider. |
| F-38 | polish | Consistency | Key tips C R A P I S V T B L D | I and L not mnemonic | INV 8.23 | New tips (2.2). |
| F-39 | polish | Help and documentation | Dead branch `Plugin.cs:351` "style linter is turned off in Settings" | Names a nonexistent setting | INV 8.9 | Delete. |
| F-40 | polish | Aesthetic | Result window "Show XHTML", "Restructure instead…", "Flare 22 can undo this edit but not redo it (Ctrl+Y)" | Insider wording | INV 8.26 | "Show markup", "Rewrite the structure…", "You can undo this with Ctrl+Z but not redo it (a Flare limit with variables)." |

What is genuinely good (keep, and defend in future designs):
- Writes as tracked changes in one undo step, with stale-text protection. This is the trust story.
- The card's three-part anatomy: what changes (diff), why, and the rule with a bad/good example.
- Cached reviews and AI progress that appears while the writer already works ("you can work on these now").
- Reject records a reason and feeds the guide owner. Few tools close this loop.
- Confirmation for batch writes counts the files and says undo is available.
- Check on save never blocks save ("Saved anyway").
- Settings keeps the key DPAPI-protected and never echoes it.

---

## 4. Review pane redesign

### 4.1 Layout principles

- Width-responsive: at 320 px or more show list and card; at 185 px show a single column (list, with the card replaced by a one-line detail under the selected row).
- Two tabs in the pane header: **This topic** and **Project** (F-06). Closing one never closes the other. Selecting a project row opens that topic and switches to the topic tab.
- Primary = Accept, Reject. Everything else in a single "⋯" menu.
- The pane toolbar owns pane-level commands (re-check, fix all, filter, settings).

### 4.2 Card anatomy

```
┌ House Style ─────────────────────────── [⟳] [Fix all (4) ▾] [⚙] ┐
│ [This topic 7]  [Project 183]                                     │
│ 5 fixes · 1 reword · 1 needs you      Filter: [All ▾] [AI ▾]      │
│ ┌───────────────────────────────────────────────────────────────┐ │
│ │ ▲ "utilize" → "use"                              Our rules   │ │
│ │ ▲ "customer" → "member"                          Our rules   │ │
│ │ ○ "this page" → "this document"                  Google      │ │
│ │ ● delete "Please "                               Our rules   │ │
│ │ ◆ "choose which of these features…" — reword     AI          │ │
│ └───────────────────────────────────────────────────────────────┘ │
│ ──────────────────────────────────────────────────────────────── │
│ FIX · Our rules · Plain words                                     │
│ Use the …  ̶u̶t̶i̶l̶i̶z̶e̶  use  customer list.                           │
│ Why: Use "use", not "utilize". Shorter and plainer.               │
│ Example  ✗ utilize the API      ✓ use the API                     │
│ Rule: Our rules › Plain words ›  ↗ Open the rule                  │
│                                                                   │
│  [ Accept  A ]   [ Reject  R ▾ ]                    [ ⋯ More ]    │
└───────────────────────────────────────────────────────────────────┘
```

- Severity glyph in a leading column: ● error, ▲ warning, ○ suggestion, ◆ AI reword (F-36).
- Source chip: **Google**, **Our rules**, **AI** (F-30). Section name goes in the card, not the list.
- "Rule: ...  ↗ Open the rule" replaces the Guide ↗ button. No rule id on the card; "Copy rule id" is in ⋯ for owners.
- Three kinds, each with one verb on the primary button: **Fix** (button Accept), **Reword** (button Accept, and opens a preview when it needs the model), **Needs you** (button "Go to"; a single sentence says what to do).

Primary button text by kind (replaces INV 8.11's four label variants with a rule: the primary button always names what it will do):

| Kind | Primary | Result |
|---|---|---|
| Fix | Accept | Tracked change, next |
| Reword | Accept | Tracked change, next |
| Needs a rewrite (structural) | Rewrite… | Opens the result preview |
| Variable | Use variable | Selects text and says what to press (see F-copy below) |
| Manual | Go to | Moves the cursor |
| Missing alt text | Suggest alt text… | AI draft, then edit |

### 4.3 Overflow ("⋯ More") and right-click on a list row

```
 Edit replacement…         E
 Ask the AI to change it…  F        (was Refine…)
 Check this paragraph again          (was Re-analyze)
 ─────────────────────────
 Turn off this rule…                 (owner)   /  Suggest changing this rule…  (writer)
 Show this rule in the style guide
 Copy rule id                         (owners)
```

Same entries on the list row context menu (so pointer users never need the card for them).

### 4.4 Keyboard model

| Key | Action | Notes |
|---|---|---|
| J or Down | Next suggestion | Skips accepted |
| K or Up | Previous | |
| A or Enter | Accept | Moves to next |
| R | Reject with last-used reason (default "Fine here") | Shows an Undo link |
| Shift+R | Reject with a reason menu | Replaces Delete menu |
| Delete | Same as R | Matches the docs |
| E | Edit replacement | |
| F | Refine with AI | |
| O | Open the rule in the guide | |
| / | Focus filter | |
| Ctrl+Z | Undoes the last accept (Flare) | Tell the user in the status line |
| Escape | Close the pane when the list has focus | |
| F6 | Move focus between pane and editor | Standard Windows |

Every key has its letter underlined or shown in the button caption. Provide a "?" in the pane header that lists them (a 2-column popup). Type-ahead in the list must not collide with these: disable list type-ahead (`ListView` search) or require Ctrl.

Screen readers: list rows announce "Warning. Replace 'utilize' with 'use'. Our rules, Plain words. 3 of 7." The status line is a live region ("Accepted. 6 suggestions left."). Progress for AI review announces at 25 % steps, not per paragraph.

### 4.5 Bulk flows (the 100+ issue case)

Job: a writer handles 183 issues across 120 files.

```
┌ House Style — Project ─────────────────────────── [⟳] [⚙] ┐
│ 183 issues in 120 of 252 files   Fixable now: 29            │
│ By rule ▾          Top rules                    ▸ Fix all   │
│  Google · this page → this document   41    [Review] [Fix]  │
│  Our rules · utilize → use            23    [Review] [Fix]  │
│  Our rules · Filler words             18    [Review] [Fix]  │
│  Structure · Table header missing     11    [Review]        │
│  … 14 more                                                  │
│ ─ Files ──────────────────────────────────────────────────  │
│ [Sort: Most issues ▾]  [Hide done]  [Only fixable]          │
│  accounts\about.htm                               4   ▸     │
│  admin\change-a-feature-setting.htm               2   ▸     │
│ ──────────────────────────────────────────────────────────  │
│ Selected file: accounts\about.htm   [Open]  [Fix 3 in file] │
└────────────────────────────────────────────────────────────┘
```

Rules for bulk:
1. Default grouping is **by rule** (one decision resolves many items) with a toggle to group by file. This is how a writer clears a backlog, and how a lead sees what the team gets wrong.
2. "Fix" on a rule row applies all exact fixes of that rule project-wide, in the same "N files, tracked changes, one Ctrl+Z per file" style the existing confirmation uses, but the confirmation shows a **preview of 3 examples** and the count. Default no confirmation for 1 file, one confirmation for more (not "more than 3 files" magic number; INV 8.33).
3. Progress with a Cancel button, and a result summary with "Open the N changed files", "Undo all (per file)" is not possible through Flare, so say so honestly: "Review in the Review tab".
4. Persistent "Done" per file: a file with zero open issues is marked and hidden by the default filter.
5. Export: "Copy summary" and "Save report (.csv/.html)" in the toolbar; the lead's pre-release check (job 4) needs a shareable artifact, and the CLI plan should write the same format.
6. The Project tab keeps its state when the writer opens a topic (F-06).

### 4.6 Empty, loading and error states

First run, no key:
```
┌ House Style ────────────────────────────────┐
│ Style checks are ready.                     │
│ AI review is off until you add a key.       │
│                                             │
│ [ Check this topic ]     (works now)        │
│ [ Add Anthropic key… ]   Why a key?         │
└─────────────────────────────────────────────┘
```
Style checks run without a key; the banner stays at the top of the pane (dismissible) rather than a modal.

No guide configured: the bundled Google guide is used; a one-line note: "Using the Google developer style guide. [Add your team's rules…]" with the link going to Settings > Team (set once), and in Style guide ▾ > "Add rules from content…".

Loading (lint is instant, AI is progressive):
```
 Style checks: done.  7 suggestions
 AI review: 3 of 8 paragraphs…   [Stop]
 (You can work on these now.)
```

Nothing found:
```
 ✓ No style issues in this topic.
   AI review: nothing to change.            [ Check again ]
```
Distinguish "linter only" (AI not run): "Style checks found nothing. Run AI review for wording. [Run AI review]" (INV 3.8 currently shows no clear empty state in project mode, the `0 issue(s) in 0 of N files` text).

All accepted:
```
 ✓ All 7 handled. 5 accepted, 2 hidden.
   Review the tracked changes in the Review tab.  [ Open Review tab ]
```

Error (AI call failed):
```
 AI review stopped: the key was rejected (HTTP 401).
 Style checks still work.   [ Fix key… ]  [ Try again ]  [ Details ]
```
Errors keep partial results, say what still works, and offer exactly one next step. Never a modal for an error that happens in the background (live check, topic switch, build).

Stale item: the row dims with "Text changed" and the card offers "Check this paragraph again" as primary (not a silent no-op, INV 8.13).

### 4.7 Settings, redesigned (grouped, one screen)

```
 House Style — Settings                                  
 ┌ Connection ─────────────────────────────────────────┐
 │ Anthropic API key  [••••••••••••]   ✓ Stored        │
 │ Model              [claude-sonnet-5-5 ▾]  [Test]    │
 ├ Editor ─────────────────────────────────────────────┤
 │ ☑ Mark paragraphs that have suggestions             │
 │ ☑ Check as I type (style rules only)                │
 │ ☑ After saving, show how many issues remain         │
 │ ☑ When a build starts, report issues                │
 ├ Team ───────────────────────────────────────────────┤
 │ Style guide    [folder…]  [Browse]                  │
 │ ☑ Also use this project's variables and glossary    │
 │ Feedback folder [folder…]                           │
 ├ ▸ Advanced (provider, URL, models, timeout, export) ┤
 │ [Troubleshooting…]                    [Save] [Cancel]│
 └─────────────────────────────────────────────────────┘
```
17 controls become 9 visible; the other 8 are one click away and rarely needed. "Test" calls the model and reports success or the readable error.

---

## 5. Design principles for every future feature

### 5.1 Glossary (one name per thing; use everywhere, including docs)

| Concept | Use | Do not use |
|---|---|---|
| An item in the pane | **suggestion** (count: "7 suggestions") | proposal, finding, issue (issue only for the project-level count "183 issues", pick one in the next pass: prefer "suggestions" everywhere) |
| Exact deterministic change | **fix** | mechanical, exact, rule fix |
| AI wording change | **reword** | AI wording |
| Needs the writer | **needs you** | manual, fix in the editor |
| Apply to the topic | **Accept** (and "Write as tracked changes" for batch) | Apply, Propose all |
| Run again | **Check again** (topic), **Check this paragraph again** | Re-check, Re-analyze, Refresh |
| Hide it | **Reject** (with a reason) | Skip (use J) |
| Rule sources | **Google**, **Our rules**, **AI** | layer |
| Turn a rule off | **Turn off this rule** / **Suggest changing this rule** | Ignore rule, Propose turning off |
| Guide tools | **Style guide** | Plugin |

### 5.2 Checklist the gap plans must pass

A feature plan is not accepted until every box can be ticked and written down in its "UX surface" section.

1. **Home.** It names exactly one home: pane, right-click, existing dialog, Settings, guide tables, or CLI. A new ribbon button needs a written reason a menu entry cannot work and approval from the owner. Budget: the ribbon never exceeds 6 controls.
2. **Discovery at the moment of need.** Say what the writer is doing when they should find it (for example: right-click a marked paragraph; a toast after save; a banner in the pane). If the answer is "they will look in Settings", it fails.
3. **Defaults that just work.** The feature does something useful with zero configuration. Rare knobs go to Advanced or to the guide tables, never to the first Settings screen.
4. **Same vocabulary.** All labels come from 5.1. New terms get added to 5.1 in the same change.
5. **Same card.** Any new finding appears in the existing list and card (kind, why, rule, example, Accept/Reject), not in a new window. New data for the card, not a new UI.
6. **Same safety.** Every document write is a tracked change in one undo step and shows what changes first. Batch writes name the count and the undo path.
7. **Same feedback loop.** Reject records a reason. Every rule has a stable id and a human title, and appears in Review feedback.
8. **No modal for background work.** Anything that runs without the writer asking (live check, build, switch topic, CLI) reports through the pane or a toast, never a modal.
9. **State is visible.** It shows loading, empty, partial, and failed states with the wording in 4.6 (what happened, what still works, one next step).
10. **Keyboard and screen reader.** Reachable by tab order; every control has AccessibleName; status changes announce; no information in color alone; works at 125 % and 150 % DPI and at 320 px wide.
11. **Honest errors.** No raw exception text; message = what happened, why, what to do; "Copy details" for support.
12. **Count the new surface.** The plan states how many buttons, menu entries, settings and message boxes it adds. Default target: zero buttons, at most one menu entry, at most one Advanced setting.

### 5.3 Applying it to the five gaps

**New rule types (structural rules, readability, file-name, severity/penalty).**
- Authoring happens in the guide tables (the owner's home), never in plugin dialogs. The writer sees only the result: a suggestion in the same list with a source chip and, for score-based rules, a one-line why ("Readability grade 14; target 10").
- Per-rule severity changes the glyph (●/▲/○), not a new control. "Turn off this rule" already becomes "Change level" (off, suggestion, warning, error), as RuleSettingForm does today.
- Validate must test new rule types (the "tested by their Example" promise) and the results must appear in the grouped Validate report of F-12 with a one-line explanation per type.
- Whole-topic metrics (readability score, penalty) are shown in the pane header as one number with a tooltip, not as a list item that cannot be accepted. A score without an action is not a suggestion.
- File-name rules operate on a topic, not a paragraph: show them as a "Topic" group at the top with a rename action available from the card overflow.

**CLI (build pipelines).**
- No UI in Flare is needed beyond one thing: the Project tab's "Save report" must produce the same report the CLI produces (same ids, same severities), so a lead can reproduce a failing build locally in one click.
- The CLI uses the same glossary and same rule titles in its output ("suggestion", rule title plus id).
- Exit codes and thresholds are configured in the guide (`FlareAI/`), not in the plugin Settings. Document one example for GitHub Actions and one for Flare's own build pre-step.
- Text output is for people (one line per suggestion with file:line), `--json` is for machines. Summary line last, always.

**Readability.**
- Lives in the pane header and on the card of a flagged paragraph. Never a ribbon button. A paragraph-level reading-grade finding says what to do ("Split into two sentences") and Reword is available.
- Show the target next to the score, and show it as a range the owner set. Do not show a bare number.
- Opt-out is by rule level in the guide.

**Markdown import.**
- Import is a one-time conversion, so it belongs with Export in **More ▾** and in Flare's own Project/Import conventions: "Import Markdown…" next to "Export for AI assistants…" under one heading "AI assistants and Markdown". Not a ribbon button.
- Contextual entry: when the clipboard holds Markdown and the user pastes into the editor, offer "Paste as Flare content" in the right-click submenu, and when a reply comes back from an AI chat ("Export for AI assistants" flow), the dialog's last step is "Bring it back" so that round trip is one path.
- Never write directly: show a preview with a tracked-change diff, same as Rewrite, and keep the same Accept flow. Report what was not converted (conditions, snippets), in a list with locations, not a log.
- First time only: explain what is preserved and what is not in two lines in the dialog.

---

## 6. Quick wins and larger redesigns

### 6.1 Quick wins (each at most 1 day)

| # | Change | Fixes | Effort |
|---|---|---|---|
| Q1 | Let Check style and Check project open with lint when there is no key; show an inline banner instead of the modal; never prompt on topic switch | F-01 | 0.5-1 d |
| Q2 | Replace the string "Open AI > Settings" in `AnthropicClient.cs:29`, `BifrostClient.cs:25-26` with "Open House Style > Settings" | F-01 | 0.1 d |
| Q3 | Fix Settings clipping: make the project-layer checkbox AutoSize with wrap; widen the Clear key link; label fast and deep fields; remove "Clear stored key" confirmation gap | F-10, F-32 | 0.5 d |
| Q4 | Diagnostics caption "House Style — Diagnostics"; add icon; Copy button | F-19 | 0.25 d |
| Q5 | Remove the Close button from the pane; move Re-check to a small ⟳ pane toolbar button; add Escape | F-03 | 0.5 d |
| Q6 | Delete rejects (last reason), Shift+Delete or Shift+R opens the reasons; J/K and A/R keys; show keys in tooltips | F-08, F-15 | 1 d |
| Q7 | Apply the glossary to every string: mechanical to fix, Re-analyze to "Check this paragraph again", Propose all to "Write all as tracked changes", drop "layer" and "linter" in text | F-07, F-26, F-30 | 1 d |
| Q8 | Show source chips in the list column: Google / Our rules / AI | F-30 | 0.5 d |
| Q9 | Project-mode markers: use the open topic's issues in either pane mode | F-06 (partial) | 0.5 d |
| Q10 | Add AccessibleName and TabIndex to every control in FindingsPanel, SettingsForm, AskForm | F-04 (first pass) | 1 d |
| Q11 | Ask dialog: Run disabled until text; placeholder; scope line | F-22 | 0.5 d |
| Q12 | Collapse "Working well" group in Tune by default; show rule titles before ids; hide email addresses | F-13 | 0.5 d |
| Q13 | Validate: put the summary line in a big first row; collapse override warnings | F-12 (partial) | 1 d |
| Q14 | Disabled buttons get a reason tooltip ("Select a topic to write tracked changes") | F-05, F-24 | 0.5 d |
| Q15 | Remove dead `Plugin.cs:351` branch; unify MessageBox captions and icons via one helper | F-20, F-39 | 0.5 d |
| Q16 | Fix docs: Delete behavior, "Ignore rule…", export names | F-21 | 0.5 d |

### 6.2 Larger redesigns

| # | Change | Fixes | Effort |
|---|---|---|---|
| L1 | New ribbon (6 controls) and shared command table feeding ribbon, Tool Strip, right-click submenu | F-02, F-17, F-34 | 2-3 d |
| L2 | Review pane rebuild: card with primary and overflow actions, responsive layout, glyph column, two tabs (topic/project), live-region announcements | F-03, F-04, F-06, F-09, F-18, F-29, F-36 | 6-8 d |
| L3 | Project backlog mode: group by rule, filters, per-rule Fix with preview, Done state, export report | F-05, F-14, F-16 | 5-6 d |
| L4 | Settings regroup with Test connection, model list, inline validation | F-10, F-11, F-33, F-37 | 2-3 d |
| L5 | Validate and Tune as panes (not modal text/list): grouped results, Open rule links, owner actions | F-12, F-13, F-24 | 4-5 d |
| L6 | Notification system: queued toasts, status-bar badge, Undo links; one error helper with Copy details | F-20, F-23 | 2 d |
| L7 | Accessibility audit with Narrator and keyboard-only pass on every dialog; 125 % and 150 % DPI test; high-contrast theme | F-04, F-35 | 3 d |
| L8 | First-run experience: banner states (no key, no guide), "Test connection", a 3-step sample topic check | F-01, F-27 | 2 d |

Suggested order: Q1, Q2, Q3, Q5, Q6, Q7, Q10 (about 4 days, biggest trust and daily-use gain), then L2 (the pane is where every future rule type shows up), then L1, L3, then L4-L8.

### 6.3 Measuring success (cheap)

- Time from install to first accepted suggestion with no key (target under 60 s).
- Clicks to clear 20 same-rule suggestions in a project (target: 2 clicks plus one confirmation).
- Buttons visible on the ribbon: 6. On the card: 2 plus overflow.
- Zero modal dialogs triggered by background actions.
- Narrator can complete Check, Accept, Reject without the mouse.
