# House Style: design-review gate for the five gap plans

Inputs: `_brief.md`, `gap-1` to `gap-5`, `ux/review-deliberate.md` (IA in 2.2, glossary in 5.1, 12-point checklist in 5.2), `ux/review-adversarial.md`. Code facts checked: `GuideLayer.LoadTable` today knows exactly 3 table kinds (Avoid|Pattern / Use; Term / Rule; Rule / Setting).

Verdicts: **Pass**, **Partial**, **Fail**, **n/a**. Surface verdicts: **keep**, **merge** (into something that exists or is proposed), **hide behind X**, **cut** (v1) / **defer**.

## 0. Gate decision

| Plan | Gate | One line |
|---|---|---|
| Gap 1 structural rules | Approve with changes | Right idea, right home (guide tables). Merge its table into one shared "Checks" table, cut the ignore-by-class and migration from v1, fix false claims about existing right-click items. |
| Gap 2 CLI | Approve with changes | Right feature, wrong size: about 47 flags, 6 formats, 3 files to learn. Keep a 10-flag core plus baseline. Cut per-run overrides that break "CI follows the guide without a second config". |
| Gap 3 readability, file names, scoring | Approve with changes | Cut section grade, rewrite-to-grade, density, Gate table, image/snippet name sweep. Keep file-name (own file) and sentence length. Reuse gap 1's table. |
| Gap 4 Flare versions | Approve with changes | Probe layer is the right call and must go first because it rewrites the same files as the ribbon/pane redesign. Fix the user-visible wording and the "refuses to start" dead end. Claim fewer versions. |
| Gap 5 Markdown | **Reject as written**, approve a reduced round trip | Adds a ribbon button, 3 dialogs and a nested submenu against the IA. Ship only copy/paste round trip, reusing the pane or one existing result window. Folder import and CLI are v1.1. |

---

## 1. Plan-by-plan: checklist, surfaces, contradictions

### 1.1 Gap 1: structural rules

| # | Checklist item | Verdict | Why |
|---|---|---|---|
| 1 | One home | Pass | Guide tables + Validate + existing pane. No button. |
| 2 | Discovery at moment of need | Partial | Writers: findings just appear (good). Owners: only via a starter topic and a user-guide topic; Validate empty state should say "No structure rules yet. See Structure.htm." |
| 3 | Defaults just work | Pass | Zero config for writers; 3-cell minimum row. |
| 4 | Same vocabulary | Partial | "finding", "Go to", "Ignore this once", "Ignore this rule here", "Ask AI to fix" are not in the glossary. Glossary says suggestion, Reject, "Turn off this rule", Reword. |
| 5 | Same card | Pass | Normal finding with rule id; kind "manual" maps to **Needs you**. |
| 6 | Same safety | Fail (for 4.9 only) | `hs-ignore` class written by "Ignore this once" is a content change used as a suppression flag. The writer expects "hide this", not "edit my markup". Also `delete` fix unverified in Flare 22. Rest passes. |
| 7 | Same feedback loop | Partial | Adds `RuleId` (good). But `GuideEdits`/`FeedbackLog` assume 5-cell Avoid/Use rows; plan lists it as risk 7 but no fix in the files list beyond "read the real column count". Reject on a structural rule must never rewrite an XPath cell. |
| 8 | No modal for background | Pass | n/a. |
| 9 | State visible | Partial | The 250 ms per-rule guard "disables the rule for the session with a log line". Silent rule loss is a visible-state failure: surface it in Validate/Diagnostics ("Rule X was skipped: too slow"). |
| 10 | Keyboard/SR | Pass | No UI. Severity glyph rules come from the pane. |
| 11 | Honest errors | Pass | Validate messages carry the position inside the selector. Good. |
| 12 | Count surface | Partial | Plan says 0 buttons / 0 settings (true) but hides a new table kind (8 columns), a selector sub-language (about 20 constructs plus `xpath:`), a class convention, a bundled `Structure` layer. |

New surfaces:

| Surface | Verdict |
|---|---|
| Structure table kind (`Selector | Type | Message | Severity | Why | Bad | Good | Except | Files`) | **merge** into the unified Checks table (section 2.1) |
| Starter `Company/Structure.htm` with commented examples | keep (becomes a Checks-table topic; same file name is fine) |
| Selector sub-language | keep but **trim** (cut `:text(/regex/)`, `:first-text`, `~`, `:nth-of-type`; `xpath:` covers them) |
| Validate messages (Bad/Good/selector/vacuous Assert) | keep; show in the grouped Validate report (F-12), one line per type |
| Bundled `Structure/` layer for migrated built-ins | **defer** (Ship 2) |
| `hs-ignore`, `hs-ignore-<key>` class convention | **cut** v1 |
| "Ignore this once / Ignore this rule here / Copy for AI / Ask AI to fix" right-click items | **cut**; plan claims they exist. They do not (checked: only `Reject ▾` reasons and "Turn off rule…" exist; "Ignore this rule in this project" is a `Dismissals` writer, not a menu item). Use the card overflow from the IA. |
| `Fix` column (`delete`, `unwrap`) | **defer** |
| "Fix with AI" using the Good example | **defer** (card overflow "Ask the AI to change it…" already exists in the IA) |
| Per-rule timing guard | keep, but report it |
| `Files` glob column | keep as the unified table's optional `Files` column (shared with gap 3) |

Contradictions: none with the ribbon. Two with the IA: invents right-click items (above); proposes a new "Structure" pane group while the IA has source chips (Google / Our rules / AI) and a "Topic" group for topic-level items. Use: chip "Our rules", group "Topic" for topic-scope rules, and no "Structure" group title.

### 1.2 Gap 2: CLI checker

| # | Checklist item | Verdict | Why |
|---|---|---|---|
| 1 | One home | Pass | CLI + docs. No Flare UI. |
| 2 | Discovery | Partial | User guide + README only. Good moment of need: the build-report toast/tooltip ("Want this to fail the build?"). Footer link in the pane: cut (pane toolbar is crowded; IA moves Save report there). |
| 3 | Defaults | Pass | `check <project>` with no flags works. |
| 4 | Vocabulary | Partial | CLI says "issues", "findings", "layer", "baselined", "existing". Needs one noun (see 5.1 of this file on the "suggestion" collision). `--layer` leaks "layer". |
| 5 | Same card | n/a | Pass by absence. |
| 6 | Same safety | Pass | Read-only. |
| 7 | Feedback loop | Partial | CLI ignores personal dismissals (documented). No reject reasons, fine. But the plan offers a second set of suppressions (`.housestyleignore`, baseline, `--skip-rule`, `--severity-override`) beside Rule/Setting. |
| 8 | No modal | Pass | |
| 9 | State visible | Pass | Exit codes, summary line, stale-baseline note. |
| 10 | Keyboard/SR | n/a | |
| 11 | Honest errors | Pass | Exit 2 with usage message; unreadable file becomes a visible issue. |
| 12 | Count surface | **Fail** | About 38 flags on `check` alone, 3 verbs, 6 report formats, 4 env vars, 3 new files (`.housestyle-baseline.json`, `.housestyle.json`, `.housestyleignore`). That is the "40 buttons" problem in a different medium. |

New surfaces (flags grouped):

| Surface | Verdict |
|---|---|
| `check <path>`, `--guide`, `--fail-on`, `--baseline`, `--format`, `--output`, `--changed-since`, `--files-from`, `--exclude`, `--quiet`, `--verbose` | **keep** (11) |
| `baseline` verb | keep (the one way to write a baseline) |
| `--update-baseline`, `--merge`, `--show-baselined`, `--strict-baseline`, `--baseline-ignore-path` | **cut** (verb covers the first; ratchet/merge defer) |
| `--rule`, `--skip-rule`, `--layer`, `--severity-override`, `--min-severity` | **cut**. The user story is "CI follows the guide without a second config". A CLI-only severity makes CI and Flare disagree. Durable control = Rule/Setting table. |
| `--max-warnings` | keep (cheap ratchet; replaces gap 3's `--max-errors/--max-density`) |
| `--include`, `--no-project-layer`, `--use-plugin-settings`, `--relative-to`, `--color`, `--allow-empty`, `--strict-files`, `--changed-lines-only` | **defer** (sane defaults: include = *.htm, *.html, *.flsnp; color auto; no-files = exit 2 always) |
| Formats text, json | keep. github, sarif | keep (annotations + code scanning are the adoption hook). junit, azure, `--junit-pass-cases`, `--azure-set-result`, `$GITHUB_STEP_SUMMARY` | **defer** |
| `--ai`, `--ai-max-usd`, `--ai-model`, `--ai-cache`, `--ai-all`, `--ai-required`, `--ai-severity`, `--key-env` | **cut** v1. Non-deterministic gate, bill risk, 3 extra days; the deterministic gate is the product. |
| `guide validate` verb | defer (MCP `validate_guide` exists) |
| Env vars | keep `HOUSESTYLE_GUIDE` only |
| `.housestyle.json`, `.housestyleignore` | **cut** v1 (`--exclude` and the baseline cover it) |
| Footer link "Use this in your pipeline" | cut |
| `housestyle.exe` copy | keep |
| Zip asset of CLI | keep |

Contradictions: exit code 2 means "usage error" here but "tool error" in gap 3 and `--strict` exits 1/2 in gap 5; gap 2 wins (0/1/2/3). The IA says the pane's "Save report" must produce the same report as the CLI: gap 2 never says the pane calls `ProjectChecker`; it must (one engine, one JSON schema, one set of counts).

### 1.3 Gap 3: readability, file names, scoring

| # | Checklist item | Verdict | Why |
|---|---|---|---|
| 1 | One home | Pass | Findings in the pane, guide tables, status line. |
| 2 | Discovery | Pass | Appears as ordinary findings; relax via card overflow. |
| 3 | Defaults | Pass | Ships at `suggestion`; bench tunes. But see conflict C-8: suggestions flood a 185 px pane until the project view groups by rule. |
| 4 | Vocabulary | Fail | "Rewrite to grade 11", "grade level", "problems per 1,000 words", "penalty/score", "Change this rule" (glossary: "Turn off this rule" / change level), "finding". |
| 5 | Same card | Partial | Sentence/section findings use the card. Image and snippet file-name findings appear "in <file>" in a topic's list and "Go to" an image: a card that edits nothing in the topic. |
| 6 | Same safety | Pass | Rewrite goes through `ReplaceBlocks`; before/after grade shown. |
| 7 | Feedback loop | Partial | Ids are consistent (`company:readability/1`) but the plan also writes `Company.Readability1`; use one scheme. |
| 8 | No modal | Pass | |
| 9 | State visible | Partial | "Readability skipped for 12 topics not in English" is project-report-only (fine). Missing: Rewrite when there is no key (F-01 says lint must work; the readability card's primary is Rewrite). Define: no key = kind **Needs you**, no button. |
| 10 | Keyboard/SR | n/a | |
| 11 | Honest errors | Pass | |
| 12 | Count surface | Partial | 0 buttons, but +2 table kinds, a status-line format, a `Gate|Limit` table, density constants, 3 CLI flags. |

New surfaces:

| Surface | Verdict |
|---|---|
| Readability table (`Measure | Limit | Severity | Why`) | **merge** into Checks table (`Check = sentence length`) |
| File-name table (`Files | Must match | Must not match | Severity | Why | Example`) | **merge** into Checks table (`Check = file name`; `Where` = glob; `Test` = `must match ^re$` / `must not match re`; `Example` becomes Bad/Good so Validate has one semantic) |
| Sentence-length finding | keep |
| Section grade finding on heading + "Rewrite to grade N" + `Grade 13.2 → 10.8` diff header | **defer** v1.1 (needs bench data; Flesch heuristics ±0.3; gets the rewrite loop and `RewriteProposal` changes) |
| File-name finding for the topic's own name | keep, anchored on h1 in the **Topic** group |
| File-name sweep of referenced images/snippets and orphan images in `ProjectFiles` | **cut** v1 (repeats one finding per referencing topic; edits nothing in the topic; costs a new project sweep) |
| Suggested-name text | keep (one line, only if it satisfies the rule) |
| "No rename button" decision | keep, and keep the Content Explorer instruction |
| Status line `41 warnings, 2 errors, 118 suggestions in 83 files` | keep (the IA's project summary strip anyway) |
| Issue density `(3e + w) / kwords`, hover weights | **cut** v1 (a second number to explain; invites the scoring the plan rejects) |
| `Gate | Limit` table | **cut** |
| `--max-errors`, `--max-density`, own baseline fingerprint | **cut** (gap 2 owns baseline and thresholds) |
| "Since last check" delta | cut (plan already leans to cut) |
| `Target` column | cut |
| English-only skip notice in project report | keep |

Contradictions: IA 5.3 says whole-topic numbers go in the pane header as one number with target; gap 3 says never a badge and puts them in the footer/JSON only. Take gap 3 (no per-topic number in v1); IA text "one number in the pane header" should be amended to: none in v1. IA says rewrite to a target needs the key; see item 9.

### 1.4 Gap 4: Flare 2020 to 2026

| # | Checklist item | Verdict | Why |
|---|---|---|---|
| 1 | One home | Pass | Diagnostics, Settings, installer, one guide topic. |
| 2 | Discovery | Pass | Quiet by design. |
| 3 | Defaults | Pass | |
| 4 | Vocabulary | Fail | User-visible sentence names `ContentChanged`; says "capability", "Tool Strip", "untested". Writers read: "Live check isn't available in this version of Flare." |
| 5 | Same card | n/a | |
| 6 | Same safety | Partial | Untested-version toast says "Every change is a tracked change and can be undone." Flare 22 cannot redo edits touching variables (known). Wording must not over-promise: "You can undo changes with Ctrl+Z." |
| 7 | Feedback loop | n/a | |
| 8 | No modal | Partial | Toast is fine, but "below the floor the plugin refuses to start and says why in Diagnostics": there is no Diagnostics button if the plugin never started. One-time startup dialog is the single justified modal. |
| 9 | State visible | Partial | Disabled settings with suffix (good). Missing: ribbon/right-click items that cannot work (no `GetUserPendingContextMenuItems`) simply vanish; Diagnostics lists them, fine, but the Settings "Editor" toggle for markers must show the same reason. |
| 10 | Keyboard/SR | Partial | Disabled-with-suffix needs AccessibleDescription carrying the reason. |
| 11 | Honest errors | Pass | |
| 12 | Count surface | Pass | 0 buttons, 0 settings, +1 toast (once per major), +1 guide topic, +Diagnostics line. |

New surfaces:

| Surface | Verdict |
|---|---|
| Diagnostics first line (version, tested, missing list) | keep; it lives in Settings > Troubleshooting + More ▾ after the IA |
| Settings: disabled with grey suffix | keep (matches the Advanced grouping; one suffix pattern) |
| Once-per-major "not tested" toast | **merge** into the pane banner pattern (dismissible, 4.6), not a new toast type |
| Supported-versions user-guide topic | keep (also feeds README) |
| Installer plural discovery, `-Version`, `-List`, running-process skip, packaged `Install-HouseStyle.ps1` | keep plural discovery + packaged script; **cut** `-Version/-List`, Pester tests (v1.1) |
| `tools/FlareAI.ApiSurface` + `docs/api/flare-<major>.txt` + `compat-optional.txt` | **defer** until two dumps exist (it needs the harvest to be useful) |
| `lib\floor` compile-against-floor | keep once a floor install exists; until then no-op |
| Per-band shim DLL escape hatch | cut (stated as "not built now"; leave it that way) |
| Newtonsoft internalisation | keep as a decision item in the harvest, not a feature |

Contradictions with the IA: gap 4 refactors `Plugin.Execute` into guarded steps and rewrites `EditorIntegration` guards; the IA's L1 (ribbon, shared command table) and L2 (pane) rewrite the same files. Sequence them as one change (section 4). Also gap 4 says "ribbon identical on every version": with the 6-control IA this is easier, with the 11-button ribbon it is harder (more members to probe).

### 1.5 Gap 5: Markdown into Flare

| # | Checklist item | Verdict | Why |
|---|---|---|---|
| 1 | One home | **Fail** | Three homes plus a ribbon button: right-click submenu, a "Markdown..." ribbon launcher with 3 rows that duplicate the submenu, a folder-import window, CLI. The IA puts import in More ▾ and the round trip in right-click. |
| 2 | Discovery | Pass | Right-click on the topic: right moment. |
| 3 | Defaults | Pass | No settings; detected conventions. |
| 4 | Vocabulary | Fail | "Held back", "Conflict", "Locked", "tokens", "Strict tokens", "Markdown changes", "Apply 3 as tracked changes". Glossary: Accept, "Needs you", "Write as tracked changes". |
| 5 | Same card | **Fail** | New `MarkdownChangesForm` with checkboxes instead of suggestions in the pane. Each Edited/Added/Removed block is a suggestion with a diff; Held back and Conflict are **Needs you**. Reuse the card and Accept/Reject/Fix all. |
| 6 | Same safety | Pass (gated) | Best-designed part: tracked changes, one undo step, never touches unedited blocks, held back on variables. Gated by the 4.7 mixed-operation spike; do not ship before it passes. |
| 7 | Feedback loop | Fail | No reject reason, no rule ids on imported suggestions, no hook into Review feedback. At least: Reject uses the pane's reason model. |
| 8 | No modal | Pass | User-initiated. |
| 9 | State visible | Pass | Dry run, counts, warnings, unresolved links list. Strongest section of any plan. |
| 10 | Keyboard/SR | Fail | Not mentioned. A checkbox diff grid is the worst case for screen readers; the pane card gets this from L2. |
| 11 | Honest errors | Pass | "The clipboard doesn't hold Markdown from this topic..." is well written. |
| 12 | Count surface | **Fail** | +1 ribbon button, +3 dialogs (launcher, changes, folder import), +1 nested submenu, 2 CLI modes (7 flags, own exit codes), 2 hidden window options, `markdown.json`, `.mdimport.json`, snapshot store, about 10 dialect tokens. Plan states "we add 1" button; true count is far higher. |

New surfaces:

| Surface | Verdict |
|---|---|
| Right-click "House Style: Markdown >" nested submenu | **merge** into the single "House Style ▸" submenu (two entries after a separator) |
| "Copy as Markdown" (+ toast) | keep |
| "Paste Markdown as tracked changes..." | keep; rename "Paste Markdown as suggestions…" (suggestions land in the pane) |
| `MarkdownChangesForm` | **merge** into pane (preferred) or reuse `ResultForm` with a list; decide after the spike |
| Ribbon "Markdown..." button + `MarkdownLauncher` | **cut** |
| Folder import entry | **hide behind** More ▾ ("Import Markdown folder…") **when built (v1.1)** |
| `FolderImportForm`, `TocWriter`, `LinkMap`, `AdmonitionMap`, `.mdimport.json`, `FolderImporter` | **defer** v1.1 |
| CLI `--md-export`, `--md-import` | **defer** (and when built, use gap 2's `export`/`import` verbs and exit codes) |
| Header comment with the style rules slice for the assistant | keep (it is the differentiator for Copilot-only orgs) |
| Options "Include my style rules when copying", "Strict tokens" | cut the second; keep the first as a default-on, no UI (always include; 25 lines max) |
| `markdown.json` class map | keep as hidden file, mention only on detection failure |
| Snapshots / three-way merge | keep (the differentiator over AI Helper) |
| Locked placeholders for unmodelled constructs | keep |
| "Copy as Markdown" vs existing "Export for LLMs" (built output, folder, llms.txt) | keep both but rename Export per IA ("Export for AI assistants…") and explain the difference in two lines in the user guide |

Contradictions with the IA: (1) a Markdown button on the ribbon; (2) "Export for LLMs" in the Output group (IA moved it to More ▾ and renamed it); (3) a nested submenu inside the House Style submenu; (4) own window for review where the IA's checklist item 5 forbids it; (5) the plan names it "Markdown, not AI" while the IA headed it "AI assistants and Markdown": pick right-click "Markdown" and More ▾ "Export for AI assistants…" separately; they are different jobs.

---

## 2. Cross-plan conflicts and duplication

### 2.1 Guide table kinds: 3 today, 6 as written, 4 recommended

Today: Avoid|Pattern / Use (word rules), Term / Rule (prose), Rule / Setting (overrides). Gaps add Structure (Selector/Type/...), Readability (Measure/Limit/...), File-name (Files/Must match/...), plus gap 3's Gate (Gate/Limit) = **7 with Gate**. Owners must learn seven table shapes, and each re-invents Severity, Why, Example, a glob column and a Bad/Good meaning.

**Proposal: one `Checks` table** (the Mad Quality "Function" column, but ours is tested):

| Check | Where | Test | Message | Severity | Why | Bad | Good | Except | Files |
|---|---|---|---|---|---|---|---|---|---|
| structure | `img:not([alt])` | Report | Add alt text... | error | ... | `<img src="a.png"/>` | `<img src="a.png" alt="x"/>` | `img[role=presentation]` | |
| structure | `div.note > p:first-child` | Assert `b:first-child` | Start a note with a bold label. | suggestion | | `<div class="note"><p>Careful</p></div>` | `<div class="note"><p><b>Note:</b> Careful</p></div>` | | |
| sentence length | (blank = all prose) | 30 | (generated) | suggestion | Split at the end of an idea. | a 34-word sentence | a 20-word sentence | | |
| file name | `**/*.htm` | must match `^[a-z0-9][a-z0-9-]*\.htm$` | (generated) | warning | Lowercase, hyphens. | `Setup_Guide v2.htm` | `setup-guide.htm` | | |
| grade level (v1.1) | | 12 | (generated) | suggestion | | | | | |

- Detection: header has `Check`, `Where`, `Test` (exact names). One `else if` in `LoadTable`; this also removes gap 1's table-collision risk 6.
- Only `Check`, `Where`, `Test` required. `Message` optional for generated checks. Conditions (`Content.Steps`, `Product.*`) are the row's `MadCap:conditions`, already handled by `Scope()`; that replaces gap 3's separate conditions text.
- Bad/Good means the same everywhere: Bad must trigger, Good must not (Validate runs one routine with a per-check evaluator). This replaces gap 3's single `Example` column.
- Ids unchanged in form (`company:<topic>/<key>`); owners can still split rows into `Structure.htm`, `File-names.htm`, `Readability.htm` topics. Same table, different topic.
- Result: table kinds 3 → **4**. Drop `Gate`. Add exactly one conceptual thing for owners to learn: "a Checks row = what to look at, how to test it".
- The unified loader dispatches by `Check` to `StructureEngine`, `ReadabilityMeasure`, `FileNameCheck`. Three engines, one table, one Validate pass, one starter topic.

### 2.2 Baselines, thresholds, density (gap 2 vs gap 3)

Both define a baseline (gap 2: `sha1(rule|path|excerpt|ctx40|occurrence)`; gap 3: "rule id + file + excerpt hash, the same fingerprint Dismissals uses"), both define thresholds (`--fail-on`, `--max-warnings`, `--baseline` vs `--max-errors`, `--max-density`, a `Gate` table), and the exit codes differ (2 = usage vs 2 = tool error).

Decision: **gap 2 owns baseline, thresholds and exit codes; gap 3 contributes nothing here.** `ProjectHealth`, density weights, the Gate table and the three overlapping flags are cut. The pane status line shows counts only (`N errors, M warnings, K suggestions in F files`). "Score" does not appear in the product. If a density number is ever wanted, it is derived from the CLI json, not defined twice.

### 2.3 Finding anchors and line numbers (gap 1 element anchoring vs gap 2 SARIF)

- Gap 1: findings are text-anchored (Excerpt + Before + Block) so tracked edits don't invalidate them; explicitly "do not promise line/column". The engine selects `XmlNode`s.
- Gap 2: needs `file:line:col` and `fixes[]` regions; plans an `XmlTextReader` pre-scan that maps element order index to (line, col) and approximates the column of an excerpt inside a run.
- Gap 3: file-name findings have no text or line; section findings anchor on a heading.

One rule: **line/column are computed only on the file-loading path** (`LintFiles`/CLI), never in the live editor, by one Core class `SourceMap` (element document-order index → line/col, built once per file by the pre-scan). Structural findings use the matched **element's** start position directly (exact, better than gap 2's text approximation); word findings use run offset; heading findings use the heading's position; topic-level and file-name findings have `Line = null` (SARIF region omitted, GitHub annotation file-level, which both formats accept). `Line/Column` stay `[JsonIgnore]` on `StyleFinding`. This closes the gap-1/gap-2 disagreement and gives gap 3 a place to put topic findings.

### 2.4 StructureCheck migration (gap 1) vs rule ids baked into the CLI (gap 2) and file-name findings (gap 3)

- Today `StructureCheck` findings have `Rule = "Structure.AltText"` and **no RuleId**. Gap 2's text/SARIF sample prints `[Structure.AltText]`, gap 1 Ship 2 renames them to `structure:structure/alt-text`. A baseline written by CLI v1 then goes stale (all structure issues reappear as new) the day the migration ships.
- Gap 3 file-name findings "anchor like StructureCheck" but are not structural; they would inherit `Structure.*` names by accident.

Decision: (a) in Foundations (section 4), **assign the final RuleIds to the existing C# structure checks now** (`structure:structure/alt-text` etc., 0.5 day, `Rule` display names kept for overrides). Then migration (later) swaps implementation, not ids, and baselines survive. (b) File-name and readability findings get their own `Source` (`filename`, `readability`) and `FindingLocator.ForTopic` (h1 anchor), shared with gap 1's topic-scope rules; none of them pass through `StructureCheck`. (c) Gap 1 Ship 2 migration is **deferred**; it adds risk (messages that name levels, cross-block state) and no new capability.

### 2.5 Suppression mechanisms: seven proposed, two needed

Proposed across plans: Rule/Setting row (off/level), `Except` column, `Files` column, `hs-ignore` class (gap 1), `.housestyleignore` (gap 2), `.houseignore` (gap 3 open question), `.housestyle.json`, baseline (gap 2), `--skip-rule`/`--severity-override` (gap 2), Dismissals (existing), Reject reasons (existing), right-click "Ignore this once" (gap 1, nonexistent).

Keep: **Rule/Setting (scope: whole guide or product), `Except`/`Files` columns on the row (version-controlled with the rule), baseline (CLI only), existing Reject/Dismissals (personal)**. Cut the rest from v1. This is also one sentence in the user guide: "To stop a rule, change its level. To exempt something, use Except. To adopt on a big project, make a baseline."

### 2.6 One Core engine for pane and CLI

Gap 2's `ProjectChecker` wraps `LintFiles`, gap 3 adds to `LintFiles` (word totals, image sweep, file-name pass), gap 1 adds a structure pass inside the same loop, the IA wants "Save report" and per-rule grouping from the same data. Require: **the pane's Check project and the CLI both call `ProjectChecker`** and both emit the same `CheckResult`. Otherwise counts differ between "what Flare shows" and "what CI says", which is the first support ticket.

### 2.7 Right-click and ribbon, five plans, one submenu

Gap 1 cites 3 nonexistent items; gap 3 adds "Change this rule"; gap 5 adds a nested "Markdown >" submenu and a ribbon button; gap 4 may drop items on old versions. The IA has one "House Style ▸" submenu. Single spec:

```
House Style ▸
  Check topic
  Ask AI…            Rewrite selection
  ───────────────
  Copy as Markdown   Paste Markdown…       (gap 5)
  ───────────────   (only when the caret is in a marked paragraph)
  Show suggestion in the pane
  Turn off this rule…  /  Suggest changing this rule…
```
Maximum 8 entries, one level. Gap 4's probe layer decides which entries exist.

### 2.8 Pane volume: the hidden dependency

Every rule type (word, structure, sentence length, file name) lands in a 185 px pane with no filter. IA finding F-16 and L3 (group by rule, filter, per-rule Fix, Save report) are prerequisites for enabling gap 1 and gap 3 defaults on a real project. Ship new starter rows at `suggestion` (gap 3's own recommendation, apply to gap 1's starter rows too), and do not enable sentence length in the starter guide until L2/L3 land.

### 2.9 Glossary collisions the plans expose

The IA glossary uses **suggestion** for "an item in the pane" and also keeps `suggestion` as the lowest severity. Gap 2 and gap 3 then print "118 suggestions" for a count that excludes weight and gap 2 prints "12 new issues". Decision for the glossary (to be added in 5.1): count noun = **issue** in project/CLI/report contexts, **suggestion** inside the pane for an actionable item; severities display as **Error / Warning / Tip** (stored token `suggestion` stays valid in tables, `tip` accepted as alias). Half a day, removes the ambiguity in every plan. If the owner rejects, keep "suggestion" for both and never count suggestions in the CLI summary line.

Other terms to add to the glossary: **Check** (a row in the Checks table), **topic-level** items (group "Topic"), **baseline** (CLI: issues accepted as existing), **Markdown** (the round trip; not "AI").

### 2.10 Export names and CLI verbs

Three export paths: `--llm-export` (built output, shipping), gap 2 `export` alias, gap 5 `--md-export` (source topic, different dialect). Decide verbs once: `housestyle export llm` (alias of `--llm-export`, kept) and, in v1.1, `housestyle md export|import`. Flags `--md-*` are not added. UI name: "Export for AI assistants…" (IA) for the built-output export; "Copy as Markdown" for the topic round trip.

---

## 3. Scope cuts and lean v1

### 3.1 What to cut or defer, with effort saved

| Plan | As written (days) | Lean v1 (days) | Saved | Cut or defer |
|---|---|---|---|---|
| Gap 1 | 9-12 (mid 10.5) | 5 | 5.5 | Ship 2 migration (3), Ship 3 `hs-ignore`/`delete`/Fix with AI (2), `:text()`/`:first-text`/`~`/`:nth-of-type` and ReDoS guard (0.5), perf prefilter. Add: RuleId on existing checks now (counted in Foundations). |
| Gap 2 | 8-11 + 3 AI (mid 12.5) | 6 | 6.5 | `--ai` and all AI flags (3), junit/azure/step summary (1), `.housestyle.json`/`.housestyleignore`/`guide validate` (1), per-run override flags and rule/layer filters (0.5), `--changed-lines-only` (1, add when asked), ratchet/merge/strict baseline (0.5). Keep: phase 1 + baseline + SARIF + line numbers. |
| Gap 3 | 9-11 (mid 10) | 5 | 5 | Section grade + rewrite-to-grade + `RewriteProposal` measures (3), density/Gate/baseline sharing (1.5), image/snippet name sweep (0.5). Keep: file-name (own file) 1.5, sentence length 2.5, status line counts 0.5, docs 0.5. |
| Gap 4 | 8-12 + 3-5 test (mid 14) | 8 | 6 | `ApiSurface` CI guard until two dumps exist (1.5), Pester tests and `-Version/-List` (1), VM runs on 5 versions down to 2 (2), per-band shim. Keep: harvest, `FlareCaps`/`SafeEvents`, plural installer, packaged script, docs. Claim only the versions that pass the checklist. |
| Gap 5 | 33 (+7 v2) | 19 | 14 | Folder import M3 (11), CLI (1), launcher/ribbon/FolderImportForm (1), `MarkdownChangesForm` if the pane is reused (1 net). Trim parser (setext, indented code, reference links) (0.5 net). Keep: engine, round trip, snapshots/three-way, spike gate. |
| Foundations (new, section 4) | n/a | +3 | -3 | Added work. |
| **Total** | **about 80** | **about 46** | **about 37** | If gap 5 slips entirely to v1.1: about 27 days. |

### 3.2 Lean v1 path (order)

0. UX quick wins Q1, Q2, Q3, Q5, Q6, Q7, Q10 (about 4 days, outside the five plans). The glossary (Q7) must land before any plan writes strings.
1. **Foundations (3 days)**: final `RuleId` on existing structure checks; `SourceMap` (line/col) and `StyleFinding.Line/Column`; `CheckResult`/`ProjectChecker` skeleton shared by pane and CLI; `FindingLocator.ForTopic`; the Checks table loader + `Validate` dispatcher with an empty evaluator list; `Notify` helper hook for Validate messages.
2. **Gap 4 probe layer together with L1 (ribbon + shared command table)**: same files (`Plugin.cs`, `EditorIntegration.cs`); do once. Harvest first (1-2 days of lead time; start now).
3. **Core engines in parallel with L2 (pane rebuild)**: gap 1 structure engine, gap 3 file-name + sentence length on the Checks table. Their Flare-side display waits for L2/L3 (section 2.8).
4. **Gap 2 phase 1 + baseline + SARIF**: needs step 1 (`ProjectChecker`, `SourceMap`) and step 3 ids.
5. **Gap 5 round trip**: starts with the 4.7 spike as a go/no-go; lands after L2 so suggestions go into the new card.
6. **v1.1**: grade + rewrite-to-grade, structure migration, junit/azure, `--changed-lines-only`, folder import, CLI `md`, `--ai` in CI.

---

## 4. Surface budget ledger

Counting rule: visible controls a writer meets (ribbon, dialogs, settings, card, right-click), plus the learning surface for owners and build engineers (table kinds, CLI flags, files).

| Surface | Today | After the review's IA | After all five plans as written | After recommended cuts |
|---|---|---|---|---|
| Ribbon controls | 11 | 6 (4 large, 1 small, 1 menu) | **7** (6 + gap 5 "Markdown...") | **6** |
| Ribbon menu entries behind the 3 menus | 0 | 11 (Ask AI 3, Style guide 4, More 4) | 14 (+3 launcher rows) | 13 (+2 Markdown in More ▾; folder import later) |
| Dialogs (modal windows) | 10 | 8 (Validate and Tune become panes) | **11** (+launcher, changes, folder import) | **9** (+1 Markdown changes; **8** if it is the pane) |
| Settings, visible | 17 | 9 | 9 (+ disabled-state variants from gap 4 on 4 of them) | 9 |
| Settings, advanced/hidden | 0 | 8 | 8 + 2 Markdown window options + `markdown.json` | 8 + `markdown.json` (hidden file) |
| Review-card buttons | 9 | 2 + overflow (3 visible, 5 in overflow) | 3 (no new buttons, but primary label varies: "Rewrite to grade 11") | 3 |
| Pane toolbar controls | 0 | 3 + 2 tabs | 3 + 2 tabs | 3 + 2 tabs |
| Right-click entries (top level in Flare's menu) | 3 flat | 1 submenu, up to 5 entries | 1 submenu + nested "Markdown >" + 3 claimed by gap 1 = up to 10 | 1 submenu, up to 8, one level |
| Guide table kinds owners learn | 3 | 3 | **7** (Structure, Readability, File-name, Gate) | **4** (Checks) |
| Pane groups/chips | n/a | 3 source chips | 3 + "Structure" group + "in <file>" items | 3 chips + "Topic" group |
| New non-modal one-time messages | 0 | 1 (no-key banner) | 3 (no-key banner, "untested version" toast, Markdown copied/applied toasts) | 2 (banner pattern for no-key and untested version; Markdown toasts only) |
| CLI flags / verbs | about 3 flags (`--llm-export`, MCP) | same | **about 47 flags**, 6 verbs/modes, 4 env vars | **11 flags**, 2 verbs (`check`, `baseline`), 1 env var |
| Files/conventions an owner or engineer must know | overrides.ini, dismissed.json | same | +8 (`.housestyle-baseline.json`, `.housestyle.json`, `.housestyleignore`, `.mdimport.json`, `markdown.json`, `hs-ignore` class, `Gate` table, front matter dialect) | +2 (`.housestyle-baseline.json`, the token dialect, which only appears in AI replies) |
| Installer/ops knobs | 1 script | 1 | 4 (`-Version`, `-List`, packaged script, Pester) | 2 (plural install, packaged script) |

Budget caps for the lead's sign-off: ribbon 6, dialogs at most today's 10, settings visible at most 9, guide table kinds at most 4, CLI flags at most 12, right-click one level.

---

## 5. Required changes per plan

Each item is an edit the orchestrator can apply to the plan text. "§" numbers refer to that plan's sections.

### Gap 1 (structural rules)

1. §3/§4.2: replace the Structure table with a row type of the shared **Checks** table (section 2.1 of this file): header `Check | Where | Test | Message | Severity | Why | Bad | Good | Except | Files`, with `Check = structure`. `Selector` becomes `Where`; `Type` becomes `Test`; keep `Report`, `Assert <sel>`, `Report topic`. Update §4.3 detection to the exact header names `Check`+`Where`+`Test` and delete risk 6's mitigation text.
2. §3 and §4.9: delete every mention of right-click "Ignore this once", "Ignore this rule here", "Copy for AI / Ask AI to fix" as existing items. State the real surfaces: Reject ▾ reasons, card overflow "Turn off this rule…" (IA 4.3), and the Rule/Setting table.
3. §4.9 and Ship 3: remove the `hs-ignore` class convention and the "Ignore this once writes a class" flow from v1; keep only Rule/Setting, `Except`, `Files` as suppression. Move to "Not in v1".
4. §4.1: freeze the selector subset at: tag, `*`, `.class`, `#id`, `[attr]`, `[attr=v]`, `[attr^=/$=/*=]`, descendant, `>`, `+`, `,`, `:first-child`, `:last-child`, `:not()`, `:empty`, `:blank`, `:has()`, `MadCap|name`, and `xpath:`. Remove `:text(/regex/)`, `:first-text`, `~`, `:nth-child/:nth-of-type`. Drop §4.8's regex timeout/ReDoS paragraph.
5. §4.4 and Ship 2: mark the migration "after v1". Add the Foundations task: give the existing C# structure findings final `RuleId`s (`structure:structure/alt-text`, `/one-h1`, `/table-header`, `/heading-order`, `/link-text`) now and keep `Structure.AltText` as the display/override name.
6. §3 new strings: change "Pane group title: Structure" to the source chip "Our rules" (or "Google") and group "Topic" for topic-scope rules. Replace "Go to" with the card's primary "Go to" per IA 4.2 (already the term), and replace "Ignore" wording with "Turn off this rule" per the glossary.
7. §4.8: the 250 ms per-rule guard must write to the Validate report and Diagnostics ("Skipped rule X: too slow on topic Y"), not only the log.
8. §4.6/§4.10: require `GuideEdits`/`FeedbackLog` to treat Checks rows as non-editable by Reject feedback ("Turn off" only), and add the test `FeedbackIgnoresChecksRows` to §6.
9. §4.6 line/column: replace "do not promise it" with "positions are supplied by the shared `SourceMap` on the file path only; the element's start line is exact". Link to Foundations.
10. Effort §Ship: change to Ship 1 only, 5 days; Ship 2 and 3 listed under "v1.1".

### Gap 2 (CLI)

1. §4.4: cut to the 11-flag surface in section 1.2 of this file; delete `--rule`, `--skip-rule`, `--layer`, `--severity-override`, `--min-severity`, `--include`, `--no-project-layer`, `--use-plugin-settings`, `--relative-to`, `--color`, `--allow-empty`, `--strict-files`, `--changed-lines-only`, `--update-baseline`, `--merge`, `--show-baselined`, `--strict-baseline`, `--baseline-ignore-path` (move to "Later, on request"). Default `--include` and "no files matched = exit 2" become fixed behavior.
2. §1/§4.4: reword the Rule control row of the competitor table: "Rule control: the guide's Setting table; no CLI-only overrides, so CI cannot disagree with Flare."
3. §4.5: v1 formats are text, json, github, sarif. Move junit, azure, step summary to "Later". State the json schema is the **same document the pane's Save report writes** (IA 4.5 item 5).
4. §4.2/§5: `ProjectChecker` is the single implementation behind both the pane's Check project and the CLI; add the test `PaneAndCliProduceSameCounts`. Refer to Foundations for `SourceMap` (replaces §4.3's ad-hoc prescan) and drop "column approximation" language for structural findings.
5. §4.7: confirm exit codes 0/1/2/3 as the single table for gap 3 and gap 5; add the sentence "Other plans must reuse these codes."
6. §4.8 and §6 Phase 3: remove AI-in-CI from this plan; list under "Later, on request" with the cost-cap design retained as notes.
7. §3: delete the findings-pane footer link and the `.housestyleignore` file; discovery stays: user guide, README, `--help`, and the Report-before-build toast/tooltip.
8. §4.6: cut ratchet, `--merge`, strict mode; keep fingerprint, the `baseline` verb, `--baseline`, and the stale-count note ("38 baselined issues are fixed: re-run baseline"). Define the fingerprint once and reuse it from gap 3's former proposal (it was the same idea).
9. §4.6/§3 text: the issue-count wording follows the glossary decision in 2.9 ("issues" in CLI output is fine; severities Error / Warning / Tip, the `--fail-on` values stay `error|warning|suggestion|never` with `tip` accepted).
10. §4.4 env vars: keep `HOUSESTYLE_GUIDE` only. §6 effort: phase 1 + baseline + SARIF = 6 days.

### Gap 3 (readability, file names, scoring)

1. §4.1: delete the Readability and File-name table definitions; define them as `Check = sentence length` and `Check = file name` rows of the Checks table. `Example` becomes `Bad`/`Good` (a file name that must trigger, one that must pass).
2. §3.1/§4.2: defer section grade, `ReadabilityCheck.Sections`, "Rewrite to grade N", `MeasureBefore/After` and the diff header. Keep sentence length only. Add the no-key behavior: sentence-length card is kind **Needs you** with no AI button when there is no key (IA F-01).
3. §3.2/§4.2: check only the topic's own file name. Delete the referenced-image/snippet pass, the orphan image sweep in `ProjectFiles`, and the "in <file>" pane entries.
4. §3.2: file-name finding anchors on the h1 via `FindingLocator.ForTopic` (Foundations) and appears in the **Topic** group; `Line = null`.
5. §3.3/§4.3: delete issue density, weights, the `Gate | Limit` table, `ProjectHealth`, `--max-errors`, `--max-density`, and the baseline paragraph (gap 2 owns baseline). Keep only the status line `N errors, M warnings, K suggestions in F files` (apply the glossary decision 2.9).
6. §4.1 starter file-name rows: ship `suggestion` severity, not `warning` (a legacy project with `Setup_Guide.htm` files will otherwise light up every file). Use the same kebab-case regex gap 5 uses for imported names and say so.
7. Ids: use one scheme `company:<topic>/<key>` everywhere; remove `Company.Readability1` style names from §4.2.
8. §3 vocabulary pass: "Change this rule" → "Turn off this rule / Change level"; "problems" → per the glossary; "finding" → "suggestion" in user-facing copy.
9. §7: replace the open question about `.houseignore` with "use `Except`/`Files`".
10. §8 effort: 5 days (file name 1.5, sentence length 2.5, status line 0.5, docs 0.5). Section grade and Rewrite-to-grade move to v1.1 with its 3 days.

### Gap 4 (Flare versions)

1. §3 Diagnostics sentence: remove API member names. Replace "Not available in this Flare version: live check (no ContentChanged event), margin markers, report before build" with "Not available in this version of Flare: Check as I type, margin markers, report before build." Keep the technical member in the log only.
2. §3 untested toast: change the text to "House Style hasn't been tested with this version of Flare yet. You can undo changes with Ctrl+Z." and show it with the dismissible pane banner pattern (IA 4.6), once per major.
3. §0/§4.4: define the below-the-floor behavior: a single one-time dialog at startup ("House Style needs Flare 2022 or later. This version is missing: ...") because no ribbon, no Diagnostics button exists. Remove "says why in Diagnostics" as the only channel.
4. §3 Settings: disabled controls need `AccessibleDescription` with the reason (checklist item 10); use the same suffix wording in Settings and Diagnostics (single source: generated from capability names).
5. §5/§9: sequence this plan **with** L1 (ribbon and shared command table) and treat `Plugin.Execute`/`EditorIntegration` as one refactor; do not land them separately.
6. §4.5 `ApiSurface` and `docs/api/*.txt`: defer until at least two real dumps exist; keep the commitment to regenerate `ref/` stubs from the floor once a floor install is available.
7. §4.6: keep plural discovery and the packaged `Install-HouseStyle.ps1`; cut `-Version`, `-List`, Pester tests from v1.
8. §7 support table: claim only versions that pass checklist items 5 to 8 and say "tested on build X". Initial public claim: current Flare plus the oldest install actually passed; 2020/2021 stay "untested, expected to work". Effort 8 days (harvest 1.5, probes 3, installer 1, docs 0.5, two version checklists 2).

### Gap 5 (Markdown)

1. §3.3: delete the "Markdown..." ribbon button and `MarkdownLauncher`. Entry points are the right-click submenu (two entries in the single "House Style ▸" submenu, no nested submenu) and, when folder import ships in v1.1, one More ▾ entry "Import Markdown folder…".
2. §3.1: move "Copy as Markdown" and "Paste Markdown…" into the shared submenu (section 2.7). Rename the second "Paste Markdown as suggestions…" so the glossary holds (Accept, suggestions).
3. §3.2: replace `MarkdownChangesForm` with suggestions in the review pane: each Edited/Added/Removed block is a suggestion with the word-level diff, source chip "Markdown"; Held back and Conflict are **Needs you**; "Apply 3 as tracked changes" is the pane's **Fix all (3) ▾** ("Write all as tracked changes"). If the 4.7 spike shows the pane cannot take insert/remove proposals, fall back to the existing `ResultForm` with a list; never add a new window.
4. §3: add Reject behavior (reason, Undo link) and keyboard/screen-reader requirements (checklist 7 and 10); both come free from the pane.
5. §3.4/§3.5/§4.8/M3: move folder import, `TocWriter`, `LinkMap`, `AdmonitionMap`, manifest, CLI `--md-export/--md-import` to "v1.1". When built, use gap 2's verbs (`housestyle md export|import`) and exit codes 0/1/2/3.
6. §3 options: drop the "Strict tokens" option; always include the style-rule header (default on, capped at 25 lines), no UI.
7. Vocabulary pass: "Held back" → "Needs you: Flare can't redo this edit"; "Locked" → "Not editable in Markdown"; "tokens" never appears in UI; "Conflict" → "Changed in both places: kept yours".
8. §4.7: the mixed-operation spike is milestone 0 with a stated go/no-go; if it fails, v1 ships "words only" (no inserts/removes) rather than splitting into multiple undo steps silently.
9. §0/§2 naming: keep "Markdown" for the round trip; state in the user-guide plan the two-line difference from "Export for AI assistants…" (built output, whole project, llms.txt) and use that name for `--llm-export`'s UI.
10. §5 files: remove `FolderImportForm`, `MarkdownLauncher`, `FolderImporter`, `TocWriter`, `LinkMap`, `AdmonitionMap`, and the `Plugin.cs` change from v1; add the pane integration change (`FindingsPanel` accepts proposals of kind insert/remove).
11. §7 effort: v1 = engine (9) + round trip (9) + pane integration folded in = 19 days; v1.1 folder import 11; v2 re-sync 6 to 8.
12. §6 risk 2: keep "variables held back", but word it as the IA's honest-limit message ("You can undo this with Ctrl+Z but not redo it") for the one case that does write.

### Cross-cutting (amend the UX review and the brief)

X1. Glossary 5.1 additions: Check, Topic (group), Issue vs suggestion decision (2.9), Baseline, Markdown. One day, before any plan writes copy.
X2. Checklist 5.2 item 12 ("count the new surface") must include table kinds, CLI flags and files, not only ribbon/menu/settings; add them to each plan's UX-surface section as a one-line ledger (this file's section 4 format).
X3. IA 2.2: amend More ▾ to include "Markdown: copy / paste" mirror entries (2) so non-right-clickers can find the round trip; amend IA 5.3 Readability bullet ("one number in the pane header") to "no number in v1".
X4. Add L3 (project backlog: group by rule, filter, Save report) as an explicit prerequisite to enabling gap 1/gap 3 starter rows by default.
X5. Add the Foundations package (3 days) to the roadmap: final RuleIds, `SourceMap`, `ProjectChecker`, `ForTopic`, Checks loader skeleton.
X6. Add "Hand-written validation of each new Validate message in the grouped Validate report (F-12)" to each plan's tests, since Validate becomes a pane.
