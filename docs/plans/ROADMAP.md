# House Style roadmap: close the five gaps without drowning the user

Orchestrated 2026-10-12. Inputs, all in `docs/plans/`:
- **Gap plans** (amended to pass the design gate): `gap-1-structural-rules.md`, `gap-2-cli-checker.md`,
  `gap-3-readability-filenames-scoring.md`, `gap-4-flare-versions.md`, `gap-5-markdown-import.md`.
- **UX:** `ux/inventory.md` (every surface, string and setting), `ux/review-deliberate.md` (heuristics, IA, pane
  redesign, glossary, checklist), `ux/review-adversarial.md` (personas, top 10 risks, Kaizen budget),
  `ux/plan-critique.md` (gate decision, unified Checks table, surface ledger, lean scope).

## 1. What the reviews found (verified in code)

The product's core is sound: tracked changes in one undo step, the card's diff/why/example, cached progressive AI,
reject-with-reason feeding the owner, checks on save and build that never block. But it already shows the Kaizen
trap: 11 ribbon buttons (8 are owner or maintenance tools), 11 clickable controls on one review card, 17 settings, 10
modal dialog types, 3–4 names for the same concept, and no accessibility metadata anywhere.

Trust and correctness problems, verified by the orchestrator in the source, fixed **before** any new feature:

| # | Problem | Evidence |
|---|---|---|
| T1 | With no API key, Check style and Check project refuse to run, though style checks need no key. The message says "Open AI > Settings" (no such tab) and re-fires on topic switch. First click on the product fails. | `Plugin.cs:460-466`, `AnthropicClient.cs:29` |
| T2 | A fix whose context changed is applied to the first occurrence of the same text in the paragraph, reported as success. | `FindingLocator.cs:83-90` |
| T3 | Project-wide "Accept all" opens, edits, saves and closes files the user never saw; no confirmation up to 3 files; the status line claims Ctrl+Z undoes them; no read-only/source-control handling. | `FindingsPanel.cs:575, 607-617` |
| T4 | One click on a snippet suggestion edits a snippet used by up to 50 topics, without saying so. | `FindingsPanel.cs:438, 599-601` |
| T5 | Check style spends money (one model call per paragraph unit) with no estimate before and no total after. | `Plugin.cs:434`, `FindingsPanel.cs:222-246` |
| T6 | The Tune screen shows writers' identities next to their reject counts; Settings never says decisions are attributed. | screenshot 09, `SettingsForm.cs` |
| T7 | No `AccessibleName`/`TabIndex` anywhere; toast auto-hides in 7 s and its link is unreachable by keyboard; severity is colour-only. | grep `src/` = 0; `Toast.cs:19,38` |

## 2. Decisions (orchestrator; owner can overrule)

1. **One glossary, enforced by a string test.** Pane item = *suggestion*; project/CLI count = *issue*; severities shown
   as **Error / Warning / Tip** (`suggestion` stays a valid stored token). Kinds: *Fix*, *Reword*, *Needs you*. Sources:
   *Google*, *Our rules*, *AI*. "Check again", "Turn off this rule" / "Suggest changing this rule". Banned in UI text:
   mechanical, lint, layer, rule id, XHTML, proposal, Re-analyze.
2. **The card's write button is "Apply"**, not "Accept": in Flare, *Accept* means finalizing a tracked change (Review
   tab). Batch: one split button "Apply all (N) ▾" → *Fixes only* / *Fixes and rewording* / *Write all as tracked changes*.
3. **Ribbon: 6 controls** — Check topic · Check project · Ask AI ▾ · Style guide ▾ · Settings · More ▾. One command
   table feeds ribbon, Tool Strip menu and a single one-level "House Style ▸" right-click submenu (max 8 entries).
4. **Card: 2 primary actions + ⋯ overflow**, keyboard A/R/J/K/E/F/O, Delete rejects with the last reason, "Rejected (n)"
   can be restored.
5. **One rule-table model.** Owners learn 4 table kinds, not 7: the existing word, prose and override tables, plus one
   **Checks** table (`Check | Where | Test | Message | Severity | Why | Bad | Good | Except | Files`) for structure,
   sentence length and file names. Bad must trigger, Good must not — one Validate meaning everywhere.
6. **One engine for pane and CLI** (`ProjectChecker` → `CheckResult`): Flare and CI can never disagree. Gap 2 owns
   baselines, thresholds and exit codes (0 pass / 1 issues / 2 usage / 3 internal). **No score** in v1.
7. **Suppression: two mechanisms.** In the guide: the row's level (Rule/Setting) and `Except`/`Files` columns. In CI:
   a baseline file. Personal Reject stays as is. Nothing else.
8. **Markdown (gap 5) is a round trip whose result lands in the review pane** as suggestions, from two right-click
   entries. No ribbon button, no new window. Folder import moves to v1.1.
9. **Flare versions:** one build with capability probes; claim only versions that pass the smoke checklist on a real
   install; missing features show as one line in Diagnostics and a disabled setting with its reason.
10. **Surface budget** (a unit test fails above it): ribbon 6, visible settings 9, card primary actions 2, modal dialog
    types ≤ 10 (target 8), right-click one level ≤ 8, guide table kinds 4, CLI flags ≤ 12.

| Surface | Today | Budget after v1 |
|---|---|---|
| Ribbon controls | 11 | 6 |
| Card controls visible | 11 | 2 + ⋯ |
| Visible settings | 17 | 9 (8 under Advanced) |
| Modal dialog types | 10 | 8 (Validate and Review feedback become pane views) |
| Guide table kinds | 3 | 4 |
| CLI flags | ~3 | 11 |
| Names for "fix" / "again" | 4 / 3 | 1 / 1 |

## 3. Phases

Days are one engineer; ranges from the plans and reviews after the gate's cuts.

### Phase 0 — Trust and first run (≈ 8 days). Ship as a point release.
- T1 no-key: pane opens with style checks and a dismissible banner "AI review is off until you add a key"; never a modal
  on topic switch; fix the string (Q1, Q2).
- T2 stale fixes: apply only on exact context, or when the text occurs once in the paragraph; otherwise mark the
  suggestion "Text changed — check again". Test: two occurrences, edited context → skipped.
- T3 project-wide apply: preview (files, counts, 3 examples) → apply → result list (changed, skipped with reason,
  failed); skip read-only files; never claim Ctrl+Z for closed files.
- T4 snippet apply names the number of topics it changes and asks once.
- T5 cost: style-rule results first ("free"), then AI review with the paragraph count and a Stop button; "AI this
  session: N calls" in the pane footer; one sentence in Settings on which actions use AI.
- T6 privacy: Writers column shows counts by default; Settings says decisions are shared with the guide owner.
- Quick wins Q3 (Settings clipping), Q4 (Diagnostics title + Copy), Q5 (pane toolbar), Q6 (keys), Q7 (glossary pass),
  Q10 (AccessibleName/TabIndex first pass), Q15 (one message helper), Q16 (docs).
- UI string test (banned terms) and surface-budget test go in now so later work can't regress.

### Phase 1 — Foundations and the new shell (≈ 17 days)
- **Foundations (3 d):** final RuleIds for today's structure checks; `SourceMap` (line/col on the file path only);
  `ProjectChecker`/`CheckResult` shared by pane and CLI; `FindingLocator.ForTopic`; Checks-table loader skeleton with
  a Validate dispatcher.
- **L1 + gap 4 probe layer together (≈ 6 d):** 6-control ribbon and command table; capability probes in isolated
  methods (`FlareCaps`, `SafeEvents`); plural installer. Start the old-version harvest now (needs installs or
  customer dumps; lead time).
- **L2 review pane rebuild (6–8 d):** two tabs (This topic / Project), card with Apply/Reject + ⋯, glyph + source
  chip, responsive at 185–320 px, live-region status, empty/loading/error states from `ux/review-deliberate.md` §4.6.

### Phase 2 — Rules, scale and CI (≈ 23 days)
- **Gap 1 structural checks (5 d):** CSS-subset → XPath (frozen subset + `xpath:`), Report/Assert, starter
  `Company/Structure.htm` at Tip, Validate runs Bad/Good per row.
- **Gap 3 file names + sentence length (5 d):** Checks rows; file-name items in the pane's *Topic* group (rename in
  Content Explorer, no unsafe rename); sentence length off in the starter guide until L3 ships.
- **L3 project backlog (5–6 d):** group by rule, filter by severity/source, per-rule Apply with preview, Done state,
  Save report (same JSON as the CLI). Prerequisite for turning new checks on by default.
- **Gap 2 CLI (6 d):** `housestyle check` (verb on the existing exe) with text/json/github/sarif, `--fail-on`,
  `--changed-since`, `baseline` verb + `--baseline`; pipeline snippets for GitHub Actions, Azure DevOps, madbuild.
- **L4 Settings regroup + Test connection (2–3 d).**

### Phase 3 — Markdown round trip and polish (≈ 25 days)
- **Gap 5 (≈ 19 d):** milestone 0 = live spike of mixed word/insert/remove tracked operations (go/no-go; fallback
  "words only"). Then the Markdown engine (dialect, parser, writer, builder; lossless round-trip tests over the 250-topic
  corpus) and "Copy as Markdown" / "Paste Markdown as suggestions…" landing in the pane, three-way merge against the
  export snapshot.
- **L5** Validate and Review feedback as grouped pane views (4–5 d); **L7** accessibility audit with Narrator, 125/150 %
  DPI, high contrast (3 d); **L8** first-run banners and sample check (2 d).
- Gap 4 support claim: run the 18-item smoke checklist on each available Flare version; publish only what passed.

**Total ≈ 73 engineer-days** (Phase 0: 8, 1: 17, 2: 23, 3: 25). Without gap 5: ≈ 54.

### v1.1 (deferred on purpose)
Section grade and "Rewrite to grade N"; structure-check migration into the Checks table; CLI junit/azure,
`--changed-lines-only`, AI in CI with a cost cap; Markdown folder import, TOC writer and `housestyle md export|import`;
version-surface CI guard once two real API dumps exist.

## 4. Gate for every future feature
A plan passes only if it names one home, discovery at the moment of need, zero-config defaults, glossary words only,
the same card and safety model, visible states, keyboard/screen-reader support, honest errors, and a one-line surface
ledger (`ux/review-deliberate.md` §5.2 plus `ux/plan-critique.md` X2). New ribbon buttons require removing one.

## 5. Open decisions for the owner
1. "Apply" vs keeping "Accept" for the card's write button (decision 2).
2. Gap 5 in v1, or ship v1 at ≈ 54 days and do Markdown in v1.1.
3. Minimum supported Flare version: depends on getting 2020–2024 installs (ask MadCap for previous-version access).
4. Feedback attribution: counts only, or names visible to the owner by opt-in.
