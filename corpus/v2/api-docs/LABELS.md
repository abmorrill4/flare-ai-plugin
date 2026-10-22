# api-docs: labels

`labels.jsonl` follows `corpus/schema/labels.schema.json` (design: `docs/testing/CORPUS.md`). Project id `api-docs`; ids `api-docs/L####` (seeded) and `api-docs/N####` (natural).
It passes `corpuskit check` and `FlareAI.Scoring validate --kinds corpus/schema/defects.yaml --root corpus/v2/api-docs` (0 problems).

## Density

| | Count |
|---|---|
| Topics, snippet files | 153, 17 |
| Seeded violations (`origin: seeded`, `verified`, `by: [generator]`) | 123 in 69 files (about 0.8 per topic; low, because API reference text is edited text) |
| Natural `exempt`, from source markup (`verified`, `by: [source-markup]`) | 1,712: inline code 1,320 (every span with a space in it, plus a fixed one-in-six sample of the rest), code blocks 392 |
| Natural `clean` UI labels (`verified`, `by: [source-markup]`) | 33 |
| Natural `exempt` quoted text (`proposed`, `by: [pattern]`) | 23 |
| `block-complete` | **0** |
| `ambiguous`, `disputed` | 0 |
| Split | 70/30 by file (FNV-1a of the path); test = 47 of 153 topics, 8 of 17 snippets (`project.json` lists them); 40 of 123 seeded violations are in test files |

Seeded kinds (all in `corpus/schema/defects.yaml`): utilize 23, exclaim 11, prior_to 8, ui_not_bold 8, latin 7, the_user 7, vague 6, click_here 5, url 5, wordy 5, claims_free 4, claims_soon 4, passive 4, phone 4, blacklist 3, claims_guaranteed 3, marketing 3, in_order_to 2, please_note 2, undefined_abbr 2, we 2, will 2, heading_skip 1, is_able_to 1, whitelist 1.
Line contexts of the seeded spans: paragraph 97, table-cell 12, list 7, heading 5, note 1, warning 1. Span contexts: emphasis 6, link-text 5, variable-adjacent 5, xref 3, in-snippet-block 3, conditional 1, boilerplate 1.

## How the labels were made

1. **Seeded.** `corpuskit inject` writes an exact span (block, quote, prefix, suffix, fix) for every defect it inserts. Seeded labels are `verified` by the generator: it knows exactly what it changed. No second labeller has read the changed sentences, so "still reads naturally" and "no other defect introduced" are **not** checked yet.
2. **Natural, from markup.** `source-prep/natural_labels.py` reads the project's block records from `corpuskit contexts` (CorpusKit's own DOM walker; no linter output) and writes `exempt` for every code block, `exempt` for inline code, and `clean` for the few UI labels the source marks as such. These are facts of the markup, not judgments, hence `verified`.
3. **Natural, from a pattern.** Quoted text (`"..."` with two words or more) outside code is `exempt` for `google:*` and `company:*`; status `proposed`, so it is not scored until a person or the labelling pipeline (#78) agrees.

Not produced here, and needed before the shared acceptance is met: the blind two-labeller pass over the natural text (design 1.4 steps 2 and 3), which gives the natural `violation` labels and the `block-complete` labels (the 70% target); adjudication with decoys, kappa and the human spot check (steps 4 and 5, #78). Until then the project has seeded violations and explicit false-positive guards, but no claim that any block is fully labelled, so any finding outside a label is "unscored", never an FP.

## Known limits

- `rules` on seeded violations come from the injector's built-in catalogue and are **hints** (`company:plain-language`, `google:exclamation-points`): #78 or #67 must map kinds to rule ids before per-rule recall is meaningful.
- Units in table cells and quoted text are the only "gray" contexts covered by pattern; boilerplate exemptions are not labelled because a repeated block's exemption depends on the rule (see the print boilerplate in `docs/testing/CORPUS.md`).
- Seeded labels inside a snippet file (3) apply to every topic that includes it; the scorer sees the snippet file once.
- Variables are expanded in label quotes (`General.ProductName` = Acme Relay).
