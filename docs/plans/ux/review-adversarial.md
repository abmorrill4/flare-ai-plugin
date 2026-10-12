# House Style: adversarial UX review

Reviewer stance: break it. Evidence is cited as `file:line` (repo `E:\flare-ai-plugin`, HEAD 8ccadc3) or screenshot `NN` (scratchpad `ux-screens\NN-*.jpg`). `inv` = `docs/plans/ux/inventory.md` (section/item). Anything I could not verify from source is marked **(unverified)**. Screenshot 09 shows a writer's email address in the Tune list; it is not repeated here, but see risk 9.

Verified in this pass beyond the inventory: `LlmClientFactory.Create` throws without a key (`AnthropicClient.cs:29`), so inventory item 37 is confirmed. No read-only, lock or source-control handling exists anywhere in `src` (grep for ReadOnly/CheckOut/IsReadOnly returns only text boxes). No Accessible*/TabIndex/mnemonic (`&`) in `UI/` (grep count 0). No language/`xml:lang` handling in `ActionRunner.cs`. No way to undo a Reject (no Undismiss/Restore in `Core/Actions`).

---

## 1. Top 10 risks, ranked by likelihood x damage

### 1. No API key = the first click on the product is an error, then a modal Settings dialog. The free linter is locked behind the key. (likelihood: every new user; damage: abandonment)
- Repro: fresh install, no `ANTHROPIC_API_KEY`, open a topic, click **Check style** (ribbon, first button, screenshot 01).
- What happens: `RunAction` -> `NewRunner` -> `LlmClientFactory.Create` throws (`Plugin.cs:429`, `:460`, `AnthropicClient.cs:29`). A warning box shows `No API key configured. Open AI > Settings.` (`Plugin.cs:463`), then Settings opens on top (`:464`), then nothing opens. The message points at an `AI` menu that does not exist (the tab is "House Style", screenshot 01; stale text in `AnthropicClient.cs:29`, `BifrostClient.cs:25-26`).
- The linter, structure checks, markers, project check and save/build reports all work without a key (`LintRunner`, `Plugin.cs:517-525`), and `Check project` is documented as costing nothing, yet `OnCheckProject` also calls `NewRunner` first and dies the same way (`Plugin.cs:172-173`). The product's best free feature is invisible on day one.
- Fix: `NewRunner` must degrade, not throw. With no key, Check style opens the pane with lint results and a single status-line link "Add an API key to also get AI review" (opens Settings). Check project, Validate (table part) and Tune must never construct an LLM client. Fix the string to "House Style > Settings". Do not open Settings automatically from a ribbon click or from a tab switch.

### 2. "Accept" does not accept. It writes a tracked change into the topic. Three different buttons do the same write. (likelihood: every user; damage: trust, wrong mental model, quiet text changes)
- Repro: Check style on any topic, press Enter on a card (Enter = Accept, `FindingsPanel.cs:187`).
- What happens: the text changes in the document (tracked, `Plugin.cs:621`), the card is struck through (`:891`). Flare's own Review tab also has Accept/Reject, so "Accept" now means two things: "put this in the topic" (ours) and "make this change final" (Flare's). The status text "Applied N as tracked change(s)" is the only explanation (`:621`).
- Three entry points to the same write: per-card **Accept**, **Accept all mechanical (N)**, **Propose all as tracked changes (N)** (screenshots 02, 03; `:90-91`). The difference is the set (exact fixes only vs exact + AI wording) and whether a confirmation appears (never vs more than 3 files vs always: `:575`, `:590`; inv item 33). A user cannot predict which prompts them.
- Fix: rename Accept to "Apply as tracked change" (or "Suggest in topic"), keep Enter. Merge the two bulk buttons into one "Apply all (N)" with a menu of "Rule fixes only" / "Rule fixes and AI wording", one confirmation policy (always show the count; no modal when N <= 5), and one status line that says what Ctrl+Z does.

### 3. Fix lands on the wrong occurrence after the writer edits nearby. (likelihood: medium, esp. long topics with repeated words; damage: silent wrong edit)
- Repro: topic with "this page" twice in one paragraph. Run Check style. Edit the words directly before the second occurrence. Accept the second proposal.
- What happens: `FindingLocator.TryLocate` matches excerpt plus preceding context; when the context no longer matches it falls back to the first occurrence in the block (`FindingLocator.cs:83-90`). Only `rewrite` source is exempt. So the apply succeeds on the wrong "this page" and reports success; the card is not marked stale. The proposal was verified against stale context.
- Same function drives Accept-all (`ActionRunner.cs:682`, `:697`).
- Fix: when the context does not match, treat the finding as stale (`IsStale`, "text changed - re-analyze") instead of applying to the first occurrence. Allow the fallback only when exactly one occurrence exists in the block. Add a test: two occurrences, edited context, expect skip.

### 4. Project-wide "Accept all mechanical" writes to files the user never saw, saves and closes them, and cannot be undone with Ctrl+Z. (likelihood: docs leads, high; damage: high at scale)
- Repro: Check project on the 252-file project (screenshot 07: 183 issues in 120 files, "Accept all mechanical (29)"). Click it.
- What happens: confirmation appears only when >3 files (`:575`). Files not already open are opened, edited, then `SaveAndClose` (`:612-617`); files open are left unsaved. "Ctrl+Z undoes them" (`:621`) is false for every closed file, and the status line says it anyway. Mixed-state outcome: some files saved to disk, some dirty in tabs. No list of which files changed, no per-file preview, no count of skipped. The rules involved are context-blind exact replaces like `"customer" -> "member"` (screenshot 03 row 5), which are right in prose and wrong in UI labels, code, quoted customer text, or product names. The Core fix is applied by string match with no knowledge of where in the doc it sits (`ApplyFixes`, `ActionRunner.cs:670-703`).
- Source control: no checkout, no read-only/locked handling anywhere in `src` (grep). A read-only (not checked out) file will either throw into `Diagnostics.Log("Apply: ...")` and be silently skipped (`:610`, `:616` log only) or be saved around the lock **(unverified)**. Git/Perforce/SVN users get a dirty working tree of 120 files with no summary.
- Fix: project-wide apply becomes a two-step flow: a preview list (file, count, first example) with checkboxes by rule, default unchecked for files not open, then apply, then a result report (changed, skipped with reason, failed) that stays in the pane. Remove "Ctrl+Z undoes them" unless the file is still open. Never save-and-close without the preview. Skip read-only files and say so.

### 5. Typing while a check runs; pane follows you; AI "units" and stale state. (likelihood: very high for working writers; damage: confusion, occasional wrong target)
- The panel invites editing: "(you can work on these now)" (`:246`). Proposals are re-located by text (see risk 3), so offsets are not the issue; the issue is quality of detection of staleness: `ReviewOrder.cs:53` marks stale only if the excerpt vanishes, not if its context changed.
- Live check is a WinForms Timer on the UI thread (`EditorIntegration.cs:5` imports only `System.Windows.Forms`; `:72` `new Timer`), so each 1.2 s pause lints and structure-checks the whole topic synchronously (`:126-129`) and rewrites a CSS file and calls `UpdateView` (`:194-199`). On a very long topic or a topic with big snippets this will stutter on every pause **(unverified timing; code path is synchronous on the UI thread)**. It also fires for every tab with `ContentChanged`.
- No Stop/Cancel button for a running AI review; Re-check is disabled while reviewing (inv 3.8) and the worker is cancelled only when the pane handle is destroyed (`:157`). A mistaken Check style on a 300-paragraph topic cannot be stopped without closing the pane. See risk 6 for the cost side.
- "Typing over" is how word-level tracked edits are applied (`FlareDocumentAdapter.cs:158-159`): it selects the span and types over it. The writer's caret and selection are moved by an apply; restoring them is **(unverified)**. If the writer is in the middle of a sentence when they press Enter on a card, their caret jumps.
- Fix: run live lint off the UI thread with a size cap (skip live check above N blocks and say so once); add Stop to the status line while reviewing; mark a proposal stale when its before-context changed; restore caret after apply.

### 6. The plugin spends money on one click and never says so. (likelihood: every user; damage: bill shock, loss of trust)
- Repro: click **Check style** on a 60-paragraph topic.
- What happens: `aiOnStart` is true from the ribbon (`Plugin.cs:434`), so every uncached paragraph unit is a model call (`FindingsPanel.cs:222-234`, parallelism 6, `Plugin.cs:471`). The status says "Reviewing with AI - X of Y paragraphs" (`:246`) but only after it started. There is no estimate before, no total after, no key/model/cost anywhere in the pane or Settings. Token usage is only written to the diagnostics log (`ActionRunner.cs:107`). The Validate dialog is the one place that warns ("This makes M model call(s)", `GuideReportForm.cs:107`), proving the team knows the pattern and did not apply it to Check style, Topic assists, Export, Build, Tune Draft.
- The ribbon label "Check style" (screenshot 01) hides that it is free-lint-plus-paid-AI. Project check is lint only (good), so the same word "check" means free and paid in neighbouring buttons.
- Live check and check-on-save are lint-only (`EditorIntegration.cs:132-145`, `:147-164`): they cannot spend. That is a strong, unadvertised guarantee.
- Fix: split the status into two visible phases: "Style rules: 7 found (free)" immediately; then a button "Review wording with AI (about 12 paragraphs)" with a one-time "don't ask again for topics under N paragraphs". Show "AI used this session: N calls" in the pane footer. Put one sentence in Settings next to the key: which actions use AI.

### 7. Rejecting is permanent and invisible. Tracked changes from colleagues are not considered. (likelihood: medium; damage: lost proposals, silent clash)
- Reject hides the proposal "for this text" in `dismissed.json` (`FindingsPanel.cs:635`, `:641`), per machine (`%APPDATA%`). There is no "Show rejected" or undo (no restore API found in Core). A mis-click on "Wrong" or "Rule doesn't apply" also writes feedback that the owner will later act on (Tune group "Often rejected", screenshot 09 / `TuneGuideForm`), so a slip pollutes team data. Delete opens the Reject menu rather than rejecting (`:188`), so the key behaves differently from docs (inv item 4).
- Colleague tracked changes: the plugin lints the DOM as it is; whether deleted-tracked text is excluded from `PickAllBlocks` / `TextRuns` is **(unverified)**. If tracked deletions are still in the DOM, the linter flags words a colleague already deleted, and an Apply may land inside someone else's pending change. The adapter comment says "Flare's node API can't do sub-element tracked edits" (`FlareDocumentAdapter.cs:158`), which suggests tracked content is not a first-class case. A reviewer-heavy shop will hit this at once.
- Fix: a "Rejected (n)" link in the pane that lists and restores; reject reasons are optional and default to "Skip" semantic unless chosen; tests for tracked-change fixtures from another author.

### 8. Two-pane, snippet, and locked-file cases write to places the writer did not look. (likelihood: medium; damage: broad, silent)
- Snippet proposal: card head says "changes N file(s) that use it" (`:438`), but Apply opens the snippet and edits it (`:599-601`, `:619`), so one click on a 1-line card edits a shared source used by up to 50 topics (screenshot 02 shows "Snippet: LegacyApp - used in 9 files" listed in the same pane as local topic issues). No confirmation, no preview of which topics change.
- Topic open in two panes: `Attach` is keyed by path (`EditorIntegration.cs:70`), so the second view of the same document shares one state; marker stylesheet is written once per path. Behaviour when both panes are dirty is **(unverified)**.
- Flare Text Editor mode (screenshot 02 tab strip shows "XML Editor | Text Editor"): `IsEditableTopic` gates actions with the generic "Open a topic in the XML Editor first." (`Plugin.cs:426`). A writer who is in Text Editor mode gets that message for every click and does not know to switch tab. **(unverified whether Text Editor mode returns false)**.
- Read-only/locked: see risk 4.
- Fix: snippet apply needs its own confirmation naming the number of topics; unify the "can't edit this here" message with the reason (mode, read-only, not a topic).

### 9. Team data and a "writer" identity are exposed in the Tune screen. (likelihood: every team; damage: privacy, politics)
- Screenshot 09: the Writers column lists individual identities (an email address and "feedback") next to Rejected counts per rule. Settings promises "each writer's review decisions are pooled" (`SettingsForm.cs`, hint at `:54`) but never says that decisions are attributed by name or that rejects are visible to the owner. The tune item "Writers disagree" and per-writer rows make it a performance-review tool by accident. Writers who distrust AI will stop rejecting honestly once they know.
- Fix: say it at setup ("Your accept/reject decisions and your name are shared with the guide owner"), default the Writers column to counts ("3 writers") with per-name detail opt-in, and aggregate by default.

### 10. Terminology and layout failures that break comprehension at the moment of decision. (likelihood: certain; damage: slow, wrong choices)
- Same category named four ways: "mechanical", "rule fix", "exact", "exact rule fix" (inv item 1, `FindingsPanel.cs:413`, `:492`, `:149`, `:911`). Three words for "again": Re-check, Re-analyze, Refresh (inv item 3). Pane summary mixes "proposal", tooltips say "issue", toasts say "style issue(s)" (inv item 25).
- Pane too narrow for its own labels: screenshot 03: Rule column truncated ("Company - Plain ...", "Google - docume..."); screenshot 07: status clipped after "(linter only). Accept opens the file." at fixed 36 px (`FindingsPanel.cs:87`), title "project DigitalBanking (252" cut; card area an empty grey block with nothing selected; the Accept button appears in a disabled row of five greyed buttons. Screenshot 06: Settings checkbox "Also use the open project's variables, glossary and" is cut off mid-label, the "Style guide project" hint and "Clear stored" link are clipped, the MCP include tools field is invisible, and the form is 692 px tall for 17 controls.
- Fix: one noun ("suggestion" or "issue", pick one), one verb per concept, and fixed layouts replaced by wrap/auto-size; the status line grows or becomes a two-line header.

---

## 2. Persona walkthroughs

Verdict key: OK / FRICTION / BREAKS / UNVERIFIED.

### P1. Overwhelmed new writer (day one, no key, 30 topics, never read docs)
| Step | Expect | Actual | Verdict |
|---|---|---|---|
| Find the tab | "House Style" tab, obvious entry | Tab exists (01). 11 buttons in 3 groups; icons are small outlines; labels like "Topic assists", "Validate style guide", "Tune style guide", "Build style guide", "Diagnostics" mean nothing to a writer; four of them are owner/maintainer tools | FRICTION |
| Click first button, "Check style" | List of problems | Warning box "No API key configured. Open AI > Settings." (`AnthropicClient.cs:29`) then Settings opens (`Plugin.cs:464`); there is no "AI" menu | BREAKS |
| Settings | A key box and "Save" | A form with Provider, Base URL, 3 model names, key, MCP tools, guide project, feedback folder, timeout, 6 checkboxes (06). Base URL and model names are first, above the key | FRICTION |
| Paste key, Save | Check begins | Save only closes; user must click Check style again. The key hint reads "Using ANTHROPIC_API_KEY from the environment." even when nothing is set on some states (`:97-102`) **(unverified exact states)** | FRICTION |
| Read the pane (02, 03) | A short list they can act on | "7 proposal(s) in 2 sections: 5 mechanical - 1 wording - 1 need a rewrite." Jargon: proposal, mechanical, wording, rewrite. Two buttons "Accept all mechanical (4)" and "Propose all as tracked changes (5)". Rule column "AI - Google: choose" | BREAKS |
| Press Enter on a card | Final accept | Document changed with a tracked change; card struck through. User now thinks the topic is changed permanently; it is only tracked | FRICTION |
| "Rule id google:word-list/128" (03) | Not needed | Tiny grey text on grey (low contrast), only place a rule id appears | FRICTION |
| Terms: layer, rule id, Re-analyze vs Re-check | | "Re-check" re-runs the whole topic and spends AI; "Re-analyze" re-runs one paragraph and also spends AI. Tooltip for Re-check: none (`:138`). Re-analyze tooltip is the only explanation (`:147`) | BREAKS |
| "Propose all as tracked changes" | | Tooltip uses "review package" (`:148`), a term not defined anywhere else (inv item 34) | FRICTION |

### P2. Skeptical senior writer
| Step | Expect | Actual | Verdict |
|---|---|---|---|
| Does anything change a topic without consent? | No | Yes in three places without a per-change decision: Accept all mechanical (no confirm for <=3 files, `:575`), project-wide Accept (saves and closes files, `:612-617`), snippet Apply (shared source, `:599-601`). Live check, save and build checks do not write (verified: lint only, `EditorIntegration.cs:119-145`) | FRICTION |
| Undo everything | Ctrl+Z | One Ctrl+Z per open topic (stated, `:621`); not for closed files; Flare 22 cannot redo variable edits (`ResultForm.cs:125`, honest warning) | FRICTION |
| Type during check | Stale proposals flagged | Proposals re-located by text; fallback to first occurrence if context changed (`FindingLocator.cs:87-90`), wrong target possible | BREAKS |
| Colleague's tracked changes already in Review tab | Plugin ignores pending changes, never nests | Unknown how tracked-deleted text is treated by `TextRuns` **(unverified)**; Flare edit method is "type over the span" (`FlareDocumentAdapter.cs:158`), which may collide with a colleague's pending change | UNVERIFIED |
| Reject | Reversible | Permanent per machine, no restore; feeds team stats (`:635-636`) | BREAKS |
| "Where does my text go?" | Clear data statement | Model prompts and topic text go to the configured provider; there is no first-run statement of what is sent. Privacy is in the README **(unverified in-product)**. Key is DPAPI-protected (`PluginSettings.cs:107-115`) and never shown back (good) | FRICTION |

### P3. Guide owner (300 company rules)
| Step | Expect | Actual | Verdict |
|---|---|---|---|
| Add a rule | Form or table row | Edit a table in a Flare topic under `Company/`; there is no in-plugin rule editor. "Build style guide" (10) proposes rules from content; the list is empty on open with only the head sentence (10). Add button disabled unless owner (`BuildGuideForm.cs:215`) | FRICTION |
| Test it | Run on sample | "Validate style guide" tests rows against their own Example, not against the project. There is no "run this rule on the project and show hits" | BREAKS |
| Rule misfires on 200 topics | See where, switch off or scope | Check project lists per file but no filter/group by rule and no "show only rule X". Turn off rule is per-card, whole-project or whole-team (`RuleSettingForm`); no "this product only" other than editing conditions by hand | BREAKS |
| Turn off for one product only | Scope control | Not offered. Dialog radios: whole team / this project only (inv 4.8). Rule/Setting table has no Product column in the screenshot-visible UI **(unverified in Core)**; Flare conditions could scope it, but nothing guides the owner there | BREAKS |
| Read Validate's report (08) | Short, prioritized | A monospace log: 13 warnings of the shape "Overrides google:tone/28 for 'please': company says '(delete)', google says ''. The company wins." These are intentional overrides, shown as warnings; 1570 prose rules counted. First screen has no summary actions (08) | BREAKS |
| Read Tune (09) | Rules to fix first | Raw ids in two formats in one list: `company:plain-language/5` and `Google.Contractions`. "Working well" rows (100% accepted) fill the visible list, while the one item to look at is first but needs a scroll-free eye. Owner names shown (see risk 9) | FRICTION |
| Edit with AI | | "Draft a change with AI..." is owner-only, deep model, modal result (`TuneGuideForm.cs:144`); no cost shown | FRICTION |

### P4. Docs lead before release (183 issues, 120 files, screenshot 07)
| Step | Expect | Actual | Verdict |
|---|---|---|---|
| Open the report | Summary by severity, file, rule | One status line: "183 issue(s) in 120 of 252 files (linter only)" (07). Group by file only; no severity count, no filter, no sort by rule, no search, no top-10 rules | BREAKS |
| Triage | Fix top rule across the project | List is a flat per-file tree; the 36 px status clips; severity is a dark-red text for errors only (`:891-897`), so amber/blue is not distinguishable in the list | BREAKS |
| Export | CSV/HTML for managers or tickets | None. The only output is the pane and the log | BREAKS |
| "Accept all mechanical (29)" across 120 files | Safe? | See risk 4: confirms only if >3 files; no per-file list; saves and closes unopened files; read-only/source-control unhandled | BREAKS |
| Non-open files | Know what happened | No result list; per-file failures only in diag log (`:610`, `:616`) | BREAKS |
| Gate the release | Fail build on errors | Build toast says "Building. N style issue(s) in M file(s), E error(s)" (`EditorIntegration.cs:233`) and then vanishes in 7 s (`Toast.cs:38`); does not stop or mark the build. CLI is planned (gap 2) | FRICTION |

### P5. Accessibility user
| Check | Evidence | Verdict |
|---|---|---|
| Keyboard: ribbon key tips `Q C R A P I S V T B L D` | `Plugin.cs:77-93`; `I` and `L` not in labels (inv item 23) | FRICTION |
| Tab order in the docked pane | No `TabIndex`/`TabStop`/`AccessibleName` in `UI/` (grep). Default WinForms order = creation order; list, then bottom bar, then card buttons, so Tab order differs from visual order (the action row is created before the bottom bar but laid out above it) **(unverified)** | UNVERIFIED |
| Accept on keyboard | Enter handled on the list (`:187`); Delete opens a menu (`:188`) rather than rejecting; Escape closes the floating window (`PanelHost.cs:54`), which could dismiss unsaved Edit input if focus is wrong | FRICTION |
| Focus trap | Floating fallback is `TopMost`-free but dock-less; the docked pane is Flare's own, so Esc/F6 behaviour is Flare's **(unverified)** | UNVERIFIED |
| Screen reader | Controls have no accessible names or descriptions; list items convey meaning with prefix glyphs `✓` `⚠` and `→`; the Card uses two read-only RichTextBoxes; strike-through/underline/green carry the change (`:443-451`) | BREAKS |
| Color-only markers | Margin bar red/amber/blue (docs, `MarkerCss` **(unverified)**; visible as amber bars in 02) with no text/shape alternative; list severity = dark red only for errors (`:891`) | BREAKS |
| Toast WCAG 2.2.1 | Auto-hides at 7 s (`Toast.cs:38`), is `WS_EX_NOACTIVATE` so never receives keyboard focus (`:19`), link is a `LinkLabel` inside it; no pause, no persistent copy, not announced. The "Show them" / "Open the report" action is unreachable for keyboard and screen-reader users | BREAKS |
| Tiny grey "Rule id" | `FindingsPanel.cs:465`; visible in 03 as a few pixels of grey on grey | FRICTION |
| High contrast | Hard-coded colors: Firebrick, green, yellow highlight (`:443-451`, `:891-897`), toast `(45,45,48)` (`Toast.cs:25`); system colors ignored | BREAKS |
| 150% DPI | Fixed pixel layouts: status label 36 px (`:87`), card head 34 px, What-changes 64 px (`:103-105`), dialogs 520x692 etc. (`SettingsForm.cs:27`). Truncations already visible at 100% in 06 and 07, so 150% will clip more | BREAKS |
| Contrast of ribbon buttons | Screenshot 01: pale icons/labels; legible but low weight | FRICTION |

### P6. Cost-anxious user
| Question | Answer from code |
|---|---|
| When does it spend? | Check style (`aiOnStart` true, `Plugin.cs:434`), Re-check (`:138`), Re-analyze, Refine, Rewrite selection, Ask, Alt text, Topic assists, Build from notes, Draft change, Validate AI check. Does not spend: live check, save, build report, Check project, Validate tables, Export. |
| Visible before? | Only Validate's prose-rule check (`GuideReportForm.cs:107`). Nowhere else. |
| Visible after? | No. Diag log only (`ActionRunner.cs:107`). |
| Can live check or save trigger AI? | No: `LintRunner` passes `null` client (`Plugin.cs:524`). Confirmed. This is a promise worth stating in the UI. |
| Does tab switching spend? | Pane follows topics with cache only (`Plugin.cs:542`, `aiOnStart` false). Good. But it still calls `NewRunner`, which with a bad key pops a modal and Settings on every tab switch (`Plugin.cs:541`, `:463-464`, inv item 16). |
| Cache | `ReviewCache(300)` (`Plugin.cs:497`): good, saves repeat spend, but 300 entries is under one 2,000-topic project, so Check project plus later AI reviews re-spend |
| Model choice | Default standard model with unlabelled Fast/Deep fields; no hint on what costs more (06) |
Verdict: FRICTION on trust. Fix as in risk 6.

### P7. Scale and edge cases
| Case | Evidence | Verdict |
|---|---|---|
| 2,000-topic project: Check project | `LintFiles` runs in a BackgroundWorker (`FindingsPanel.cs:271-273`, progress text). Pane with 5,000 grouped rows in a single ListView, no filter | FRICTION |
| 2,000 topics: report before build | Lints the whole Content folder on every build, in background, toast at the end (`EditorIntegration.cs:213-236`). No cache across builds, no progress, no way to skip for a "just build my small target" | FRICTION |
| Snippet in 50 topics | Card says used in N files (`:366`, `:438`); Apply edits the shared file with no warning (see risk 8) | BREAKS |
| Non-English topics | No language gate in `ActionRunner` (grep 0); style rules and prompts are English-only; every `xml:lang="de"` topic gets English findings and AI "corrections" **(unverified outcome, no guard found)**. Gap 3 plans a skip for readability only | BREAKS |
| Very long topic | Whole-topic AI = one call per paragraph (`PrepareReview`); no cap, no estimate; live lint on UI thread | FRICTION |
| Topic open in two panes | See risk 8 | UNVERIFIED |
| Text Editor mode | Generic "Open a topic in the XML Editor first." (`Plugin.cs:426`) | FRICTION |
| Read-only / locked / source-controlled | No handling anywhere (grep) | BREAKS |
| Network guide folder offline | `Team.ReadStatus` runs in a background thread and failures are only logged (`Plugin.cs:205-209`); guide load for the engine on a missing folder is **(unverified)**; the Tune screen says "Read failed" text only if exceptions bubble (`TuneGuideForm.cs:98`) | UNVERIFIED |
| Check on save toast | Fires on every save while issues exist (`EditorIntegration.cs:157-160`): the same toast again and again; no snooze, no per-topic mute | FRICTION |

---

## 3. Kaizen-trap analysis

Counts (from inventory section 7, re-verified against `Plugin.cs:79-93`, `EditorIntegration.cs:82-84`):

| Measure | Count today | Notes |
|---|---|---|
| Ribbon buttons | 11 (5 + 5 + 1) | Competitor Kaizen: 40+. We are already at a quarter of that for one product |
| Ribbon buttons a writer needs daily | 1 to 3 (Check style, Rewrite, Ask) | The other 8 are owner/maintenance (Validate, Tune, Build, Diagnostics, Settings, Export, Check project, Topic assists) |
| Entry points to Check style | 5 (ribbon, tool-strip menu, toolbar, right-click, save-toast link) | |
| Entry points to Rewrite / Ask | 4 each | |
| Distinct pane controls | 15 plus 3 label variants of Accept (`inv 7`) | A card shows up to 6 action buttons + 3 bottom buttons + 2 triage buttons (screenshot 03): 11 clickable things for one proposal |
| Labelled actions overall | about 46 (inv 7) | |
| Modal form types | 10 | |
| Message boxes | 29 call sites, 4 different icons plus none (inv item 8) | |
| Settings in the form | 17 editable (inv 7); 6 checkboxes | 4 of them describe the same area (markers, live check, save, build) |
| Terms for the same thing | mechanical/rule fix/exact (3-4); proposal/issue/finding (3); Re-check/Re-analyze/Refresh (3); Accept/Apply (2) | |
| Jargon in user-visible strings | layer, lint/linter, rule id, XHTML, permanent ids, Avoid/Use row, prose rule, table rule, word-level, snippet (inv item 26) | |
| Old names | "FlareAI diagnostics" (`Plugin.cs:410`), "Open AI > Settings" (`AnthropicClient.cs:29`), "Ignore rule..." in docs | |
| Features not reachable in Tool Strip mode | 4 of 11 (inv item 22) | An inconsistency users on that UI will read as a bug |

Signs of sprawl, specifically:
1. **The ribbon mixes two audiences.** Writers (Check style, Rewrite, Ask) and the style-guide maintainer (Validate, Tune, Build) share one flat strip. The only group labels are "Assist" and "Plugin" (01), which say nothing about audience.
2. **Pane overload.** Screenshot 03 shows 11 buttons and 2 triage buttons on one card, in four rows. Edit, Refine, Re-analyze, Guide, Turn off rule, Re-check, Close each solve a rare need and sit at equal weight with the primary action.
3. **Duplicate paths with different behaviour.** Delete = menu vs docs "Reject"; Accept all vs Propose all; "Rewrite selection" vs card "Rewrite..." vs "Restructure instead...".
4. **Settings as a dumping ground.** Base URL, MCP include tools, three model names, timeout are in the same form as "Check as I type" (06). Two properties (`MaxTokens`, `Parallelism`) are hidden, so some knobs are discoverable and some not, with no principle.
5. **Dialog sprawl.** Ten modal types; most are one-time owner tools that could be panes or one tabbed window.
6. **The planned work adds more entry points**, see section 4: structural-rule messages, a Markdown ribbon button, CLI toast text, a version toast, scoring line.

### Hard budget (proposed, enforceable by a test or a review checklist)

| Surface | Budget | Rationale |
|---|---|---|
| Ribbon buttons, writer-facing | **max 4**: Check style, Rewrite, Ask, Check project | Everything else is contextual or owner-only |
| Ribbon buttons, owner/maintenance | **max 1 split-button "Style guide"** (Validate, Tune, Build as its dropdown) plus **Settings** | Owners find it; writers don't see 3 extra icons |
| Total ribbon buttons | **max 7**; adding one requires removing one or a written reason in the plan | Currently 11 |
| Right-click items | **max 1 submenu** "House Style" with at most 4 entries | Today 3 flat items; gap 5 plans another |
| Visible actions per card | **max 3** primary: Apply, Reject, a "More" menu holding Edit, Refine, Re-analyze, Guide, Turn off rule | Today 6 + 3 |
| Triage buttons | **max 1** ("Apply all..." with a menu) | Today 2 |
| Settings visible by default | **max 6**: key, "AI model quality" (Standard/Fast/Deep as one dropdown), guide project, and 3 checkboxes; everything else under "Advanced" (collapsed) | Today 17 |
| Message-box confirmations | one component, one wording, one icon policy | 29 today, 5 styles |
| Vocabulary | 1 noun for a thing the user can act on ("suggestion"), 1 verb for the write ("Apply") | Lint the UI strings in a test |
| New terms | each new user-visible noun requires a glossary entry in the user guide and a tooltip on first use | |
| Toasts | max 1 type of toast text per trigger, never the only way to reach an action | |

Enforcement: add a unit test that loads `Plugin.cs` button definitions (or a table) and fails when the count exceeds the budget; add a string-table lint test for banned terms (`mechanical`, `lint`, `rule id` in user-visible text, `AI >`).

---

## 4. Attack the roadmap (the five gaps)

Plan files read: `gap-1` to `gap-5` in `docs/plans/`.

### Gap 1: user-defined structural rules (selector-targeted assert/report)
How it can make the product worse:
- **Misfire at scale becomes undebuggable.** An Assert rule with a CSS selector that matches nothing is violated in every topic (Mad Quality's own failure mode). A 300-rule guide plus a bad selector floods Check project with 2,000 findings (screenshot 07 already shows 183 on a 252-topic sample). The plan's Validate checks examples only (`gap-1` section 3, 4.5), not blast radius on the real project.
- **Findings with no text to point at.** Plan: "elements with no text: the nearest text" (`gap-1` section 3, writer row). The marker and Go-to will highlight the wrong paragraph, and the review card says "This section is missing a required X" with nothing to Apply: more "manual" cards, which already have a different button label (inv item 11).
- **Authoring wall.** CSS subset plus `:text(/regex/)` plus `xpath:` fallback is a programming language for style guide owners (`gap-1` section 4.1). Mistakes show as Validate errors in a monospace dump (08), which is already the worst screen.
- **Ignore mechanics** ("Ignore this once / this rule here") add actions on a card that already overloads (risk 10, section 3).
Guardrails:
1. A mandatory **"Try this rule" dry run** in Validate: runs the new rule over the open project and shows "would flag N topics, first 5" before saving; refuse to save a rule that flags >20% of topics without explicit confirmation.
2. Default severity for new structural rules is *suggestion* until the owner promotes it.
3. The cards for structural findings reuse the existing card; do not add card buttons. Ignore goes in the "More" menu.
4. Plain-language rule builder first (pick a template: "heading must be followed by text"), selector syntax only under "Advanced".
5. Validate report gets a summary line and groups by rule, not a raw log.

### Gap 2: command-line checker
How it can make the product worse:
- **Two sources of truth for "what fails the build".** The toast says "N style issues" (`EditorIntegration.cs:233`); the CLI will say errors/baseline (`gap-2` lines 63-64). Writers see 183 issues; the pipeline fails on 2 errors, or the reverse, and nobody knows why.
- **Baselines hide debt** and rot when the guide changes (`gap-2` section 4.6: guide changes invalidate fingerprints); a baseline file in Git becomes something no writer opens.
- **Discoverability via toast** (`gap-2` item 3) repeats the 7-second-toast problem (risk 5, P5).
- Naming: `FlareAI.Mcp.exe` plus a `housestyle.exe` alias is two names for one binary (`gap-2` line 54): more of the duplicated-name pattern from section 3.
Guardrails:
1. The CLI and the in-product report must print the **same summary line** and use the same severity map; a test compares them on the bench corpus.
2. In the project report, a persistent muted footer, "Build rule: fail on 2 errors", shows what the pipeline gates on (read from a repo file).
3. Baseline file carries the guide version and warns on mismatch (the plan has the ratchet; make the warning impossible to miss).
4. No toast carries CLI information; the pane footer link only.

### Gap 3: readability, file names, scoring
How it can make the product worse:
- **Sentence-length rule is the loudest rule in any project.** A 30-word limit on 252 topics will out-number every other finding, and the card kind "restructure" means each of them is an AI call to fix (`gap-3` section 3.1), so one default setting drives both noise and spend.
- **File-name findings with no Apply** (`gap-3` section 3.2: "No rename action"). Findings you cannot act on, anchored to the h1, are a trust leak ("why does it point at my heading?"). Also a rename in a source-controlled tree is a project-wide link edit.
- **Scoring**: "0.8 problems per 1,000 words" and weights (errors 3, warnings 1) turn into a team KPI or a manager's metric, whatever the plan says about no per-writer numbers (`gap-3` section 3.3). It also conflicts with the Tune screen already showing names (risk 9). The grade-level text shows a "grade 13" number whose accuracy on technical docs is poor.
- **English-only skip is announced only in the project report** (`gap-3` line 61), so a German topic silently has no readability findings while still getting English style findings from other rules (see P7 non-English).
Guardrails:
1. All three ship **off or at suggestion severity** until the owner turns them on; ship readability disabled by default in the bundled guide.
2. A hard cap on readability findings per topic (for example top 3 longest sentences) with "and 12 more".
3. File-name findings appear **in the project report only**, never as a pane card on a topic and never as a marker.
4. The score is a single owner-facing line in the Validate/Tune area, never in the writer's pane or toast; no persistence of per-writer data (and remove writer identity from Tune per risk 9).
5. Language gate for every rule pack, not just readability.

### Gap 4: older Flare versions (2020 to 2026)
How it can make the product worse:
- **Disabled-but-visible Settings with a suffix** (`gap-4` line 38) means more clutter in the form that is already too long (06); a writer with 2020 sees half the checkboxes greyed.
- **"Tested" claims**: the plan relies on diagnostics text that most writers never open (`Diagnostics` is a raw message box, screenshot 11), so a version-specific failure shows up as a generic "House Style failed to start" (`Plugin.cs:57`) or a silent no-op. Silent feature drop (no live check, no markers) will read as a bug.
- **Untested-version toast** (`gap-4` line 39) is another auto-hiding message that is not announced.
- **Edit path risk**: the word-level write uses a UI "type over" trick (`FlareDocumentAdapter.cs:158-159`) that was verified on Flare 22 only; on an older build a wrong write is worse than no feature. Behaviour of tracked changes differs by version **(unverified)**.
Guardrails:
1. **Capability gating at the write, not the UI**: if the tracked-change path is not verified on this version, the plugin offers copy-to-clipboard or "Go to" only, never a write.
2. One quiet status in the pane footer ("Some features need a newer Flare: details"), not greyed settings; remove, don't grey, settings that cannot work.
3. Turn Diagnostics into a readable "About / Status" panel (version, capabilities, key present, guide path), not a raw dump.
4. A matrix test in CI against the ref stubs per version for every API member used.

### Gap 5: Markdown back into Flare
How it can make the product worse:
- **A second write path into topics**, the highest-risk area already (risks 3, 4, 8). Pasted Markdown replaces blocks; the plan holds back variable paragraphs and flags conflicts (`gap-5` section 3.2), but the user must read a diff of structure for every change.
- **New ribbon button** "Markdown..." (`gap-5` section 3.3) breaks the budget; the plan also adds a context submenu and a folder-import window. That is 3 entry points for a niche flow.
- **Silent data loss on round trip**: conditions, snippets, MadCap elements not expressible in Markdown can be dropped or flattened unless the diff is thorough. The window defaults everything safe to checked (`gap-5` line 89), a bulk "Apply 3" whose safety is the plan's own claim.
- **Folder import creates many topics at once** into `Content/Imported/...` and a new `.fltoc` (`gap-5` section 3.4), in a source-controlled project with no rollback and no preview of file names.
- **AI-reply workflow depends on the user not editing in Flare** between copy and paste (conflict path is explained in a single sentence per paragraph).
Guardrails:
1. No new ribbon button; the Markdown actions live only in the right-click submenu and in the Check/Export window. If a button is unavoidable, it replaces "Export for LLMs" (one button "Markdown" with a menu, which also fixes the five-names problem, inv item 6).
2. Import never applies by default: checkboxes start unchecked for anything except pure text edits; show counts per category ("3 text edits, 1 new step, 2 held back").
3. Always tracked, always one undo step, with the same explicit status text.
4. Folder import is a dry-run report first (files, names, link breaks), writes only on a second explicit click; refuses read-only targets.
5. Round-trip test corpus: every MadCap element must survive or be listed as held back.

---

## 5. What works well (verified)

- **Apply path is conservative in design**: every DOM write goes through the adapter and is tracked (`CLAUDE.md`, `ActionRunner.ApplyFixes` runs inside one `doc.Edit`, `:691`). Skipped-because-text-changed is counted and reported (`FindingsPanel.cs:621`).
- **Free checks never spend**: live check, save and build use a client-less runner (`Plugin.cs:524`); the pane follows tab switches from cache only (`Plugin.cs:542`). The Validate AI check asks first with a number of calls (`GuideReportForm.cs:107`).
- **Key handling**: DPAPI, never echoed, blank keeps the stored key (`PluginSettings.cs:107-115`, `SettingsForm.cs:124`).
- **Status copy that explains**: "Applied N ... Ctrl+Z undoes them" (accurate for open files), "That text changed since the review. Re-analyze the paragraph." (`:506`), the variable-redo warning (`ResultForm.cs:125`).
- **Card design for a single proposal**: what changes (struck/underlined diff) separate from why plus rule plus example is a good pattern (screenshot 03). First open proposal is auto-selected and Next() advances after each decision (`:263`, `:524`).
- **Docked pane that follows the topic**, with a floating fallback (`PanelHost.cs:42-116`), and a Reject menu that asks "why" in plain words (screenshot 04).
- **Topic assists** (screenshot 12): clear section headings with the Flare destination ("Topic Properties > Description"), Copy buttons, and honest "writes nothing into the topic" scope. One defect: Index keywords display one per line but Copy emits semicolons (`AssistForm.cs:49`).
- **Roadmap discipline**: each plan states "no new ribbon button" up front (gaps 1 to 4); only gap 5 breaks it. The restraint instinct exists; it needs enforcement (section 3).
