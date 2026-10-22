# end-user-help: labels

`labels.jsonl` follows `corpus/schema/labels.schema.json` (design: `docs/testing/CORPUS.md`). Project id `end-user-help`; ids `end-user-help/L####` (seeded) and `end-user-help/N####` (natural).
It passes `corpuskit check` and `FlareAI.Scoring validate --kinds corpus/schema/defects.yaml --root corpus/v2/end-user-help` (0 problems).

## Density

| | Count |
|---|---|
| Topics, snippet files | 207, 7 |
| Seeded violations (`origin: seeded`, `verified`, `by: [generator]`) | 179 in 92 files (about 0.9 per topic; higher than `api-docs` on purpose: several authors, mixed quality) |
| Natural `clean` UI labels (`verified`, `by: [source-markup]`) | 196 (bold in steps or after "select", "click"; `span.uicontrol`; one fewer than the generator wrote: bold text the injector made out of `the user` is a seeded violation, see `FlareAI.Labeler qa --fix`) |
| Natural `exempt` (`verified`, `by: [source-markup]`) | code blocks 54, key names (`kbd`) 22 |
| Natural `exempt` (`proposed`, `by: [pattern]`) | quoted text 191; a number and unit with a plain space in a table cell 22 |
| `block-complete` | **0** |
| `ambiguous`, `disputed` | 0 |
| Split | 70/30 by file (FNV-1a of the path); test = 69 of 207 topics, 1 of 7 snippets (`project.json` lists them); 61 of 179 seeded violations are in test files |

Seeded kinds (all in `corpus/schema/defects.yaml`): utilize 20, click_here 18, we 11, exclaim 10, the_user 10, blacklist 9, please 9, please_note 9, vague 9, whitelist 8, in_order_to 7, alt_missing 6, simply 6, claims_soon 5, latin 5, marketing 5, passive 5, url 5, phone 4, ui_not_bold 4, claims_guaranteed 3, will 3, claims_free 2, heading_skip 2, undefined_abbr 2, prior_to 1, wordy 1.
Line contexts of the seeded spans: paragraph 86, step 29, list 27, table-cell 18, note 10, glossary-def 3, quote 2, heading 3, table-header 1, warning 1. Span contexts: link-text 18, emphasis 12, xref 8, in-snippet-block 1, conditional 1, ui-label 1.

## How the labels were made

1. **Seeded.** `corpuskit inject` writes an exact span (block, quote, prefix, suffix, fix) for every defect it inserts. Seeded labels are `verified` by the generator: it knows exactly what it changed. No second labeller has read the changed sentences, so "still reads naturally" and "no other defect introduced" are **not** checked yet. `click_here` is injected as a link whose text is "click here" appended to a sentence; `alt_missing` removes the `alt` of an image and labels the `img` element.
2. **Natural, from markup.** `source-prep/natural_labels.py` reads the project's block records from `corpuskit contexts` (CorpusKit's own DOM walker; no linter output) and writes `clean` for UI labels the source marks as such, `exempt` for code blocks and key names. These come from the upstream authors' markup (bold in a procedure, `:guilabel:`, `:kbd:`, code blocks), not from a judgment of ours, hence `verified`.
3. **Natural, from a pattern.** Quoted text and number-plus-unit in a table cell are `exempt` for the rules that would match; status `proposed`, so they are not scored until a person or the labelling pipeline (#78) agrees.

Not produced here, and needed before the shared acceptance is met: the blind two-labeller pass over the natural text (design 1.4 steps 2 and 3), which gives the natural `violation` labels and the `block-complete` labels (the 70% target); adjudication with decoys, kappa and the human spot check (steps 4 and 5, #78). Until then the project has seeded violations and explicit false-positive guards, but no claim that any block is fully labelled, so any finding outside a label is "unscored", never an FP. The upstream text has plenty of natural problems (title-case headings, "please", "we", "simply"); they are deliberately unlabelled here.

## Known limits

- `rules` on seeded violations come from the injector's built-in catalogue and are **hints** (`company:plain-language`, `google:exclamation-points`): #78 or #67 must map kinds to rule ids before per-rule recall is meaningful.
- No boilerplate exemption is labelled: the footer is a snippet and whether a rule may fire on it depends on the rule (compare the print boilerplate in `docs/testing/CORPUS.md`).
- Togglers are not a block in CorpusKit's walker: no label has the `toggler` context, and no label sits in a toggler paragraph.
- 6 natural labels were dropped because `FlareAI.Scoring validate` cannot anchor text that includes a variable's value (see `SOURCES.md`, CorpusKit notes).
- Variables are expanded in label quotes (`General.ProductName` = Acme Hub).

<!-- labeler:qa -->
## Label QA

Written by `FlareAI.Labeler qa --update-labels-md`; deterministic (no model, no linter). Rerun after any label change.

| | |
|---|---|
| Result | 0 errors, 0 warnings, 0 notes |
| Topics, snippets | 207, 7 |
| Blocks with text, with at least one label | 3148, 552 (17.5%) |
| `block-complete` blocks, verified | 0, 0 |
| Violations, verified (scored) | 179, 179 |
| Verified violations in `test` | 61 |
| Violations per topic | 0.86 |
| Scored labels (verified, not ambiguous or block-complete) | 451 |
| Share of files in `test` | 32.7% |

Labels by type and status: clean verified 196, exempt proposed 213, exempt verified 76, violation verified 179.

Kinds with fewer than 3 verified violations in `test` (their per-kind recall there is noise): alt_missing (0 in test), claims_free (0 in test), claims_guaranteed (2 in test), claims_soon (0 in test), heading_skip (0 in test), in_order_to (2 in test), marketing (0 in test), phone (2 in test), please (2 in test), prior_to (0 in test), simply (2 in test), ui_not_bold (1 in test), undefined_abbr (0 in test), url (1 in test), will (0 in test), wordy (1 in test).

<!-- /labeler:qa -->
