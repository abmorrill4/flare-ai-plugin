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

## Rules for changing labels

- Labels never come from the linter. Do not add or move a label to make a run pass.
- Label changes and rule/engine changes go in separate PRs (CI check in #81); a PR that changes labels lists each one and why.
- Regenerating DigitalBanking: only when the corpus or the converter changes, and the diff is reviewed like code.
