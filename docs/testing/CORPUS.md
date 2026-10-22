# Corpus labels and scoring

How the test corpus says what the right answer is, and how a run is scored against it. Design: epic #66, work package #67.
Code: `tools/FlareAI.Scoring` (library and CLI), tests: `tests/FlareAI.Scoring.Tests`, schema: `corpus/schema/`.

## Why span labels

The v1 ground truth (`corpus/ground-truth.json`) lists seeded defects only. Anything else a rule flags is "not seeded", which can be a
real problem or noise, so precision had to be hand-read. A span label says what is right for a stretch of text, including "this is fine",
so precision and recall can be computed per rule, per layer, per context, per project and per split.

## Files

| File | Content |
|---|---|
| `corpus/<project>/labels.jsonl` (v2 projects: `corpus/v2/<project>/labels.jsonl`) | One JSON object per line, UTF-8, LF. Diff-friendly and mergeable by several agents |
| `corpus/schema/labels.schema.json` | JSON Schema (draft 7) of one line. A test keeps it in step with the code |
| `corpus/schema/defects.yaml` | Defect taxonomy: the `kind` of a violation, independent of rule ids |

Label ids are `<project>/<name>` (`DigitalBanking/V0001`); the project part drives per-project scores.

## A label

```json
{"id":"DigitalBanking/V0009","file":"Content/transfers/about.htm","block":"/html/body/p[2]",
 "quote":"go!","prefix":"ew transfer is ready to ","suffix":" Our best-in-class trans",
 "label":"violation","kind":"exclaim","rules":["google:*"],
 "context":{"line":["paragraph"],"span":[]},
 "origin":"seeded","seed":{"generator":"v1","defect":"transfers/about.htm#1","source":"lint","rule":"google"},
 "status":"verified","by":["generator"],"split":"dev"}
```

| Field | Meaning |
|---|---|
| `file`, `block` | Topic or snippet (relative to the project root, forward slashes) and the block element, as an XPath of qualified names with 1-based same-name sibling indexes (`/html/body/ol[1]/li[3]/p[1]`; `html`, `head` and `body` carry no index). The importer never stamps ids into topic markup |
| `quote`, `prefix`, `suffix` | Text-quote selector over the block's plain text (below): the quote, and up to 24 characters either side to pick one occurrence. No quote: the label covers the whole element at `block` (an `img`, a `table`) |
| `label` | `violation`: a finding is expected. `clean`: no finding from `rules` may touch the span. `exempt`: a rule would match but the text is fine here (UI label, quoted text, code, boilerplate); counts as clean for the listed rules. `ambiguous`: excluded from scoring. `block-complete`: every problem a lint rule can find in this block is labelled, so any other finding in it is a false positive. Carries no quote |
| `kind` | Defect taxonomy id (`defects.yaml`), required for violations. About the text, never about a rule |
| `rules` | Violations: acceptable rules. Clean/exempt/block-complete: the rules that must not fire. Entries: an exact id (`google:word-list/221`, `structure:AltText`, `project:variables`), a family (`google:word-list/*`), a layer (`google:*`), `*` (anything), `lint` (anything that is not AI), `ai` (any AI finding), `ai:<kind>` (an AI finding of that judgment kind) |
| `fix`, `reason` | The corrected text; why a span is clean or exempt (required for those) |
| `context.line` | `heading:h1`..`heading:h6`, `step`, `list`, `table-header`, `table-cell`, `note`, `warning`, `caption`, `quote`, `code-block`, `dropdown-head`, `toggler`, `glossary-def`, `paragraph` |
| `context.span` | `ui-label`, `link-text`, `xref`, `emphasis`, `inline-code`, `kbd`, `variable-adjacent`, `in-snippet-block`, `in-snippet-text`, `conditional`, `boilerplate` (block text repeated in 5+ topics) |
| `origin` | `seeded` (injected) or `natural` (in the source text) |
| `status`, `by` | `proposed`, `verified` or `disputed`; the sources that agreed (`generator`, `llm-a`, `llm-b`, `human`, `hand-read`, ...). Only `verified` labels are scored |
| `split` | `dev` or `test`, chosen by file and fixed per project. DigitalBanking: FNV-1a hash of the path, 30% test |

The context vocabulary is the one the engine's extractor adopts in #53. The labels' contexts are computed from the DOM by the corpus
tools (`Xhtml` in `FlareAI.Scoring`; CorpusKit in #69 has its own walker), never by `FlareAI.Core`, so an engine bug in context extraction
shows up as a score difference instead of matching labels.

### Block plain text

Offsets and quotes refer to the plain text of the block element, defined once in `Xhtml.Extract`:

- A block is `h1`-`h6`, `p`, `li`, `td`, `th`, `dt`, `dd`, `caption`, `figcaption`, `blockquote`, `pre`, `div`, `MadCap:dropDownHead`, `MadCap:toggler`.
  Its text is its own inline content: nested blocks (`li > p`, `li > ul > li`) are separate blocks.
- Text nodes in document order; each run of whitespace becomes one space; both ends trimmed; `br` is a space; `img`, `script`, `style` and
  comments contribute nothing; no-break spaces are kept.
- `MadCap:variable` contributes the variable's value when the reader is given the project's variable sets, else nothing. DigitalBanking
  labels are made with values expanded (`CorpusBlockTextSource` in `FlareAI.Scoring` takes an optional resolver; the CLI's `--root` passes none).
  Both sides of a comparison must use the same choice.
- For an element that is not a block (`table`, `img`), the text is that of the whole subtree with spaces between nested blocks.

## Findings

The scorer reads `ScoreFinding` objects, from JSONL in either shape (property names are case-insensitive):

| Field | v2 | Bench v1 `findings.jsonl` |
|---|---|---|
| file | `file` | `File` |
| rule | `ruleId`: `google:word-list/221`, `company:terminology/3`, `project:variables`, `structure:AltText`; AI without a rule: omit | `RuleId`; when null, `Rule` `Structure.X` becomes `structure:X` and `Project.X` becomes `project:x` |
| origin | `lint`, `structure` or `ai` | `Origin` |
| text | `excerpt`, and optionally `block` plus `start`/`end` (offsets in the block plain text) | `Excerpt` only |
| ai kind | `kind` (matches `ai:<kind>` label rules) | |
| project | `project` (optional) | |

Offsets win when present. Without them the scorer falls back to text: the flagged text and the quote overlap when equal or one contains the
other (ignoring case and whitespace runs; the shorter one needs two characters with a letter or digit, or is a single punctuation mark).
A label without a quote, matched with a finding whose block is unknown, overlaps only if the finding's rule is in the label's `rules`.
`layer` is the part of the rule id before the colon, and `ai` for AI findings.

## Scoring

Per finding, the first that applies:

1. **TP**: overlaps a verified violation and its rule is in the label's `rules`.
2. **FP**: overlaps a verified clean or exempt label whose `rules` include the finding's rule.
3. **Wrong rule**: overlaps a verified violation, rule not accepted (right text, other rule). Counted separately; it is the #34/#54 symptom. Counts against precision.
4. **Ambiguous**: overlaps only ambiguous, proposed or disputed labels. Excluded from precision and from coverage.
5. **FP**: inside a block-complete block (rules match, finding's block known) but overlapping no violation.
6. **Unscored**: no label speaks about it.

Per verified violation: **Detected** (a TP finding overlaps it), **WrongRuleOnly** (only wrong-rule findings do), **FN** (no finding at all).

- `precision = TP / (TP + FP + WrongRule)`, `recall = Detected / violations`, `F1`. Both are 1 when their denominator is 0.
- **Coverage** = scored findings / (findings - ambiguous): how much of what the tool says the labels can judge. Track it so it can only go up.
- Several findings on one violation are several TPs and one detection; one finding over two violations detects both.
- Groups: `rule` (the finding's rule id; a missed violation counts under its first `rules` entry), `layer`, `context.line`, `context.span`
  (a label with several contexts counts under each), `project`, `split`, `kind`. `(none)` collects what has no value.
- `ScoreOptions.Split` scores one split (findings are assigned to a split by their file); `IncludeProposed` scores proposed labels as verified;
  `TextSource` resolves selectors so findings with block and offsets are compared by position, and reports labels that no longer anchor.

## CLI

```powershell
$s = .\tools\FlareAI.Scoring\bin\Release\net48\FlareAI.Scoring.exe
& $s validate --labels corpus\DigitalBanking\labels.jsonl --kinds corpus\schema\defects.yaml --root corpus\DigitalBanking
& $s score --labels corpus\DigitalBanking\labels.jsonl --findings <bench>\findings.jsonl [--root corpus\DigitalBanking] [--split test] --out <dir>
& $s convert-v1 --truth corpus\ground-truth.json --project corpus\DigitalBanking --out corpus\DigitalBanking\labels.jsonl
```

`score --out` writes `score.json` (all groups) and `scored-findings.jsonl` (outcome and label id per finding). Bench v2 (#81, `docs/testing/BENCH.md`) uses the library directly.

## DigitalBanking (v1 converted)

`corpus/DigitalBanking/labels.jsonl` is produced by `convert-v1` and checked by a test (same inputs, same bytes):

- 429 violations, one per v1 defect, `origin: seeded`, `status: verified`, `by: [generator]` (the generator is exact). The v1 `rule` becomes a family
  (`company:terminology/*`, `google:*`, `structure:AltText`, `project:variables`); v1 `ai` and `general` defects add `ai:<kind>`. Defects with no
  excerpt (image without alt, table without header, second `h1`, heading skip) label the element.
  Two kinds accept a second rule because both correctly flag the text: `phone` also accepts `google:phone-numbers/*` (the pilot hand count credited it) and
  `click_here` also accepts `company:plain-language/*`.
- Clean and exempt labels (`origin: natural`, `by: [hand-read]`, each with a reason): every bold UI label is `clean` for `*` (518 spans in all), except
  `Sign-On Settings`, which is `exempt` from `google:word-list/640`; the print boilerplate "Printed copies of this page can be out of date." is `exempt` from
  `google:word-list/221` in every topic, as is "this page" in the legacy-app snippet; file sizes written with a plain space are `exempt` from `google:units-of-measure/*`.
  These are the hand readings of `dist/audit/precision.md` made explicit.
- Not exhaustive: there are no `block-complete` labels, so a finding outside these spans is unscored, not a false positive.

Scoring the lint-only bench findings (`dist/audit/bench/findings.jsonl`, copied to `tests/FlareAI.Scoring.Tests/fixtures/bench-v1-findings.jsonl`):

| | Hand count | Scorer |
|---|---|---|
| Google lint: true positives / findings | 23 / 57 | 23 / 57 (34 FP: 26 `word-list/221`, 7 `word-list/640`, 1 `units-of-measure/5`) |
| Company | 239 / 239 | 239 TP, 0 FP |
| Structure, project | 77 / 77 | 54 + 23 TP |
| Violations detected | 321 / 429 | 322 / 429 |

The one extra detection is the image without alt text that v1 could not tell from its siblings: a v1 finding has no block, so a whole-element label is matched to
it by rule. With block and offsets in the findings (Bench v2) that difference disappears.

## Status, sources and rule ids (conventions for every project)

### Which status a label gets

Only `verified` labels are scored, so the status says whether anyone but the author stands behind the label.

| Label | Made by | Status | Why |
|---|---|---|---|
| Seeded `violation` | an exact generator (`generator`: CorpusKit `inject`, the project authors' scripts) | `verified` | The generator knows what it changed and where; its quote, prefix and suffix are computed from the final DOM. This settles #98 ("injected labels come out `proposed`"): `FlareAI.Labeler normalize` promotes them, once the quote anchors. What the generator cannot know ("the sentence still reads naturally", "nothing else is wrong in the block") is carried by `block-complete`, not by this label. |
| Natural `violation`, `clean`, `exempt` that are facts of the markup (`dom-check`, `source-markup`) | a DOM or markup rule: an image without alt, code, a key name, bold in a procedure | `verified` | No judgment is involved. |
| Natural `clean`, `exempt` read by a pattern or a rule of thumb (`pattern`, a generator's reading) | the corpus tools | `proposed` | A heuristic, not a reading. They count once both labellers agree or a person has looked (`hand-read`, `human`). |
| Natural `violation` from the blind pass | `llm-a` and `llm-b` agree | `verified` | Design 1.4: agreement. |
| | one labeller only | `proposed` | Goes to the adjudication queue. |
| | the labellers disagree on the kind | `disputed` | Goes to the adjudication queue. |
| `block-complete` | a generator's claim | `proposed` | A verified `block-complete` needs `llm-a` and `llm-b` or `human` in `by` (a QA error otherwise). |

`by` names who stands behind a label; QA warns about any other value: `generator`, `source-markup`, `pattern`, `dom-check`, `hand-read`, `llm-a`, `llm-b`, `human`
(a decision taken by an agent other than the two labellers is recorded as `agent:<name>`; it never counts on the `test` split).

### Rule ids in `rules`

A rule entry is one of the forms in the table under "A label". Generators sometimes write hints instead (`structure:alt-text`, `company:plain-language`, `ai:ui-references`, `google:exclamation-points`);
those match no finding, so `FlareAI.Labeler normalize` maps them and QA rejects what is left:

| Layer | Valid | Notes |
|---|---|---|
| `google` | `google:*`, `google:<page>/*`, `google:<page>/<row>` | `<page>` is a page of the compiled Google guide (`dates-times`, `word-list`, `units-of-measure`, ...); a hint naming no page becomes `google:*` because the page that carries a rule moves when the guide is recompiled (#65). A bare `google:<page>` matches nothing. |
| `company` | `company:*`, `company:<page>/*`, `company:<page>/<row>` | Pages of the Company guide: `claims`, `plain-language`, `terminology`, `ui-references`, `google-overrides`. A project that overrides the Company guide (localized-style) is still the `company` layer. |
| `structure` | `structure:AltText`, `OneH1`, `HeadingOrder`, `TableHeader`, `LinkText`, `HardCoded` | The engine's `Structure.*` checks. |
| `project` | `project:variables`, `project:variables/*` | |
| `ai` | `ai:<kind>` with a kind from `defects.yaml` (`ai:undefined_abbr`, `ai:ui_not_bold`) | Not the kebab-case names (`undefined-abbreviation`). |
| any | `*`, `lint`, `ai` | |

## Labeller: normalising, QA and the blind pass (`tools/FlareAI.Labeler`, #78)

```powershell
$l = .\tools\FlareAI.Labeler\bin\Release\net48\FlareAI.Labeler.exe
& $l normalize --all corpus\v2 --write          # rule hints -> scorer ids; exact-generator violations -> verified (idempotent)
& $l qa --all corpus\v2 [--fix] [--update-labels-md]  # deterministic label QA; exit 1 on errors
& $l label --all corpus\v2                      # DRY RUN: prices the blind two-labeller pass, sends nothing (the default)
& $l label --project corpus\v2\legacy --execute --max-cost 5 [--split dev] [--max-blocks 500]   # sends it (needs ANTHROPIC_API_KEY)
& $l agree --project corpus\v2\legacy [--findings <bench findings.jsonl>]   # agreement, kappa per kind, adjudication queue with decoys
& $l merge --project corpus\v2\legacy [--write]  # fold agreement and adjudication/decisions.jsonl into labels.jsonl
```

**QA** (`qa`) checks, per project, with no model and no linter: schema and anchors (every quote still resolves to exactly one place), rule vocabulary, exact duplicates, overlapping violations (same kind: warning;
different kinds: note), a violation that overlaps a clean/exempt label for the same rules (error when both are verified), a violation inside code, a key name or a code block, kind/rule fit (the rule layers of a kind must
overlap those DigitalBanking uses for it; `ai:<kind>` names the label's own kind), `split` against `project.json`, unknown `by` sources, and coverage (blocks with a label, `block-complete` share, verified violations per split,
kinds with fewer than 3 verified violations in `test`). `--fix` applies only the mechanical repairs: an exact duplicate loses its copy; a natural violation that duplicates a seeded one on the same span (a `dom-check` fact read after
the seed was inserted) is dropped; a heuristic `clean`/`exempt` label (`source-markup`, `pattern`, `dom-check`) on exactly the span of a verified seeded violation is dropped. `--update-labels-md` writes the counts into the
project's `LABELS.md` between `labeler:qa` markers. Rule of thumb for what to do with the rest: an error is a label bug, fix it in a labels-only change; a warning needs a look; a note is information.

**The blind pass** (`label`, `agree`, `merge`) follows design 1.4:

- Two labellers on different models (default `claude-sonnet-5-5` and `claude-opus-5-5`) get the same blocks in batches. They see the block's plain text, the kind of element (paragraph, step, heading) and the defect kinds with their summary and example
  from `defects.yaml`, and nothing else: no rule ids, no lint patterns, no plugin findings, no file names, no existing labels. Kinds only the DOM can decide (`alt_missing`, `table_header`, `second_h1`, `heading_skip`, `link_text_url`) are not offered. Code blocks are not sent.
- `label` is a **dry run unless `--execute` is given**, and `--execute` needs `--max-cost <usd>`. The cap holds: each call is priced at its worst case (the batch's input and the full output ceiling, which also bounds reasoning tokens) before it is sent, and the run stops between batches when
  spend plus that price would pass the cap. Both labellers go batch by batch, so a stopped run leaves them level. Answers are stored under `<project>/adjudication/runs/<a|b>/` and reused, so a rerun spends nothing on finished batches and a stopped run resumes.
- Agreement (`agree`): spans of the same kind that overlap agree (the shorter quote is kept); overlapping spans of different kinds contradict; the rest are single-source. Cohen's kappa is per kind over the blocks both labellers saw (does each report the kind in the block).
  Agreed spans that match an existing violation only add `llm-a`/`llm-b` to its `by`; agreed spans inside exempt text, or of another kind than the label there, are conflicts for a person.
- Adjudication queue (`adjudication/queue.jsonl`): the single-source spans, contradictions, conflicts and, with `--findings`, the plugin findings that no label speaks about in blocks that carry a `block-complete` label, shown without provenance (the type of each item is in
  `queue.key.jsonl`, which the adjudicator does not read), mixed with decoys so that 20% of the items are spans nobody reported. Decisions go in `decisions.jsonl` (`{"qid":"Q0003","decision":"accept|reject|ambiguous","by":"human","kind":"..."}`).
  `merge` applies them in code: `accept` is `verified`, `reject` adds nothing, `ambiguous` writes an `ambiguous` label. **A decision on a `test`-split file counts only with `"by":"human"`.** Decoy acceptance (decoys accepted of decoys judged) is reported next to kappa.
- `merge` also writes kinds with kappa below 0.6 as `ambiguous` instead of `verified`; the agreement table, decoy acceptance and the cost per labeller are written into `LABELS.md` between `labeler:agreement` markers. Label ids it creates are `<project>/M####`.

## Rules for changing labels

- Labels never come from the linter. Do not add or move a label to make a run pass.
- Label changes and rule/engine changes go in separate PRs (CI check in #81); a PR that changes labels lists each one and why.
- Regenerating DigitalBanking: only when the corpus or the converter changes, and the diff is reviewed like code.
