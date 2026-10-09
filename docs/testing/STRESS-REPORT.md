# Stress test report — 2026-10-09

Corpus: `scripts/make-corpus.py` → "Digital Banking Help", 250 topics plus 2 snippets, 5 simulated authors, 429 planted
problems (288 lint-type, 87 judgment, 54 structure). Headless harness: `tools/FlareAI.Bench`. In-Flare runs on Flare
2026 (22.0, trial).

## Results

**Lint and structure, all 250 topics (free, 90 s headless, under 30 s in Flare's Check project):**

- Lint-type problems: 100% caught.
- Structure: 98% after fixes (was 80%).
- Judgment problems: 49% caught by lint alone; the AI covers the rest.

**Model bake-off (45-topic sample, 76 planted problems):**

| Model, effort, unit | Found | Not planted | $/topic | s/topic | Rewrites |
|---|---|---|---|---|---|
| Haiku 5.5, low, block | 100% | 113 | 0.001 | 14 | drops words after bold labels |
| Haiku 5.5, medium, block | 100% | 59 | 0.001 | 17 | drops words after bold labels |
| Sonnet 5.5, low, block | 100% | 94 | 0.014 | 12 | faithful |
| Sonnet 5.5, medium, block | 100% | 107 | 0.013 | 14 | faithful |
| Sonnet 5.5, medium, section | 100% | 75 | 0.008 | 5 | faithful |
| Opus 5.5, medium, block | 100% | 58 | 0.030 | 20 | invents details ("as soon as you save it") |
| **Sonnet 5.5, medium, block, stricter review prompt** | **100%** | **71** | 0.012 | 12 | faithful |

- Contracts held on every model: 0 contract drops, 0 truncations and 0 errors across ~1,400 calls.
- The only retries are the new continuation guard working as intended.
- Recall saturates: the planted judgment problems are too easy to separate the models. Noise, cost and rewrite fidelity
  are what differ.

**Routing decisions taken:**

- Review and rewrites stay on Sonnet 5.5 at medium effort.
- The fast tier (refine, assists) moves to Haiku 5.5 at **medium**: half the noise of low at the same price.
- Per-section review is a team option (`review_unit: section`): 40% cheaper, 2.5× faster, less noise and the same recall
  here. It stays off by default, per the "smallest units" principle, until real topics confirm it.

**Simulated authors → Tune:**

- 250 decisions from 5 personas (8 writers with the existing team files, 515 decisions) produced the expected report.
- Marcus's dispute of claims rules surfaces as "often rejected".
- Rules writers split on show as "disputed", with per-writer evidence.

## Bugs found and fixed

| # | Bug | Found by | Fix | Regression check |
|---|---|---|---|---|
| 1 | **Flare crash** in project-wide Accept all mechanical: every file opened as a tab and never closed; at ~100 tabs Windows ran out of window handles ("Error creating window handle"), then Flare crashed | In-Flare, 233 fixes in 141 files | Files the plugin opens for a bulk apply are saved (changes stay tracked) and closed; files you had open stay open and unsaved | Re-run in Flare: 233 fixes, 141 files saved with tracked changes, no crash |
| 2 | Images alone in their own paragraph (the usual Flare pattern) never got the missing-alt-text finding: the finding needs text to anchor on | Bench, 10 of 10 missed | Anchor on the nearest text in the topic; Suggest alt text finds the image next to it | `ImageAloneInItsParagraph_IsFlagged_AndFoundFromTheFinding` |
| 3 | Rewrites on Haiku dropped the words that finish a sentence after inline markup ("the **Transfers** page. Each…" → "the **Transfers** Each…") | Bench rewrites, every Haiku run | Contract check: a run that continues a sentence after markup must keep its head words, otherwise retry. Verified live: caught "page." and "again." | `RunRewrite_DroppingTheWordsAfterABoldLabel_IsRetried_ThenLeftOut` |
| 4 | Rewrite invented facts to fix a "vague" finding ("…history, which lists the date of the change and the nickname that was set") | In-Flare Rewrite… | Rewrite prompts forbid adding facts; the AI says what the author must supply instead, and the result window shows that note | (prompt) |
| 5 | Linter and AI treated `<code>`/`<pre>` as prose: flagged `FlareAI/owners.txt` as a product name to replace, and hard-coded URLs and phones in code; rewrites could edit code | Dogfooding the user guide | `code`, `pre`, `kbd`, `samp` and `var` are literal: not linted, not structure-checked, never an editable run | `CodeIsLiteral_NotLinted_NotEditable` |
| 6 | Mined example sentences dropped variables ("Staff at choose which…") | In-Flare Build style guide on the corpus | Mining reads variables as their values | docstyle self-test |
| 7 | Company rule "note that → (delete)" fired inside "Adds a note that appears", and its auto-fix would produce "Adds a appears" | Bench lint | Row split: "please note that" (any case) and "Note that" (capitalized only) | bench lint run |
| 8 | AI review too noisy on real prose: 79 findings on the 12-topic user guide | Dogfooding | Stricter bar in the review prompt (an editor would change it before publishing; at most two per paragraph; no style preferences) | Guide 79 → 44; corpus recall unchanged at 100%, noise −34% |

## Known limitations (not fixed)

- **Google Vale rule misfires** on correct text:
  - OxfordComma on ", ready for…", ", without AI…" and ", with counts…".
  - FirstPerson on the button label **Mine this project**.
  - "touch" → "tap".
  - The fix belongs in the Google layer's overrides, per team.
- **Mining overreach:** "choose" counts as a competing UI verb even when it isn't one ("choose which features members see").
  "sign in" and "sign-in" are proposed as variants, but the noun/adjective form is correct. The owner picks or skips these.
- **Bold UI labels are linted like prose:**
  - A label that breaks a terminology rule (a page named "Single Sign-On") gets flagged.
  - Writers should reject with "fine here".
  - Tune then shows the pattern.
- **Haiku is not suitable for rewrites:** the guard catches dropped words, but the retries cost time. Rewrites stay on
  Sonnet.
- **Latency:** 12–20 s per topic for the AI review at six parallel calls. Linter results show at once; AI findings stream
  in paragraph by paragraph.

## Reproduce

```
uv run --script scripts/make-corpus.py corpus
dotnet build tools/FlareAI.Bench
tools\FlareAI.Bench\bin\Debug\net48\FlareAI.Bench.exe --no-ai
tools\FlareAI.Bench\bin\Debug\net48\FlareAI.Bench.exe --model claude-sonnet-5-5 --effort medium --sample 40 --rewrites 8
tools\FlareAI.Bench\bin\Debug\net48\FlareAI.Bench.exe summarize bench-out
```
