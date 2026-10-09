# The full Google guide in the plugin — 2026-10-09

## What changed

**Before:** the AI reviewed against a 27-bullet summary of the Google developer documentation style guide. The linter ran
36 third-party Vale pattern rules.

**Now:** the whole guide is compiled into the plugin's style guide project (`styleguide/…/StyleGuide/Google`). The work
ran in three steps, each with its own tool:

- **Snapshot** (`tools/google-style/snapshot.py`): all 70 pages of developers.google.com/style, 78,796 words, split into
  3,855 citable source units. CC BY 4.0. Google's AI-generated page summaries are left out. Seventeen unlisted URLs were
  checked: they all redirect to listed pages.
- **Scope** (`tools/google-style/coverage.json`): each page is core, a content pack, the word list, or reference. The 7
  reference pages each have a written reason: change log, about, philosophy, two summary pages, and two pages about
  HTML/Markdown source.
- **Compile** (`tools/FlareAI.GuideCompiler`, Opus 5.5 at high effort, $12 for the full run): per page, a strict output
  contract, enforced coverage, and every lint row run through the real validator.

| | |
|---|---|
| Source units in compiled pages | 3,133 |
| Covered by rules | 2,749 |
| Not applicable, each with a reason | 380 |
| Uncovered | 4 (listed in `coverage.md`) |
| Rules dropped by the validator | 6 (listed) |
| Rules | 2,023: 1,565 prose (AI), 344 lint rows, 114 patterns |
| Word list | 545 terms: lint rows where the fix is mechanical, otherwise Term/Rule rows that reach the AI only when the text uses the word |

## How the AI gets it

Each review call carries three blocks:
1. **Always-on (cached for 1 hour):**
   - instructions;
   - untagged rules;
   - the packs nearly every section needs: Headings, UI, Lists and Steps. Measured on 262 sections, they appear in
     58–100%; every other pack appears in 22% or less.

   This block is about 32k tokens, written once and then read at a tenth of the price. A team changes the list in
   `FlareAI/models.json` (`"always_packs"`).
2. **Other packs** for what the topic contains, such as numbers, code, tables, images, punctuation or abbreviations.
   Every section in one check gets the same set, so the block is cached for 5 minutes and shared by the topic's
   sections. A one-section topic gets them uncached, because a cache write would never be read.
3. **Word-list rules** for words the section uses. Not cached.

Two other rules for what gets sent:
- Lint rows never go to the AI: the linter enforces them, and its findings are in the prompt.
- The output schema is the same on every call. **The schema is part of the cached prefix:** per-call enums of run ids
  and rule ids had made almost every call write the cache instead of reading it. Run ids and rule ids are now checked
  in code, and the model may cite only the rules it was sent.

## Results

**Google's own examples.** 150 rules, round-robin across every compiled page: each rule's Bad and Good examples were
reviewed with each guide.

| | Bad example flagged | Flagged citing that exact rule | Good example left alone |
|---|---|---|---|
| 27-bullet summary (before) | 65% | 0% (no such rules) | 60% |
| **Full compiled guide** | **98%** | **64%** | **81%** |

How to read the misses:
- Most "Good example flagged" cases are other real Google rules. A Good example written for one rule can break another,
  for example "log in" in an abbreviations example.
- Most citation misses name a sibling rule from the same page (dates rule 33 instead of 34).

**Synthetic corpus.** 45-topic sample, 76 planted problems: both guides catch 100%, because this corpus is too easy to
tell them apart. Findings now cite specific Google rules (for example a word-list entry) instead of broad buckets.

**Cost and speed** (Sonnet 5.5, medium effort): about 3.5 s per topic.

| Run, 45 topics, cold cache | Review cost | Per topic |
|---|---|---|
| 27-bullet summary (before) | $0.34 | $0.008 |
| Full guide, first version | $2.07 | $0.046 |
| Full guide, after the caching fixes | $1.28 | $0.028 |

A warm session costs about $0.02 per topic check: roughly $0.60 a day for a writer checking 30 topics. Parallel calls
now really run in parallel. .NET Framework allows 2 connections per host by default, so before the fix only 2 ran at a
time. The first call of a check also runs alone, so the rest read the cache it writes.

## Known limitations

- Some faithful Google rules are noisy for end-user help:
  - "this page" → "this document";
  - "sign-on" is on the avoid list, which also flags it in UI labels;
  - a non-breaking space is wanted between numbers and units.

  Teams turn these off in the Company folder's "Changes to the Google guide" table.
- "we" and "will" are now AI judgment, not lint: Google states both rules as depending on context.
- Rule ids are positional within each generated topic. When Google changes a page and it's recompiled, ids on that page
  can shift, and feedback history for those rules restarts.
- The 4 uncovered units and 6 dropped rows are listed in `tools/google-style/coverage.md` for a follow-up compile.

## Reproduce

```
uv run --script tools/google-style/snapshot.py                 # refresh the source (maintainers)
tools\FlareAI.GuideCompiler\bin\Debug\net48\FlareAI.GuideCompiler.exe            # compile changed pages
tools\FlareAI.Bench\bin\Debug\net48\FlareAI.Bench.exe --examples 150                  # Google's own examples
tools\FlareAI.Bench\bin\Debug\net48\FlareAI.Bench.exe --sample 40 --rewrites 8        # corpus
```
