# Labels: hardware-install

Labels for this project are in `labels.jsonl` (schema: `corpus/schema/labels.schema.json`, explained in `docs/testing/CORPUS.md`). `project.json` lists the sources, the seed and the test split.

## What is in the file

| Label | Origin | Status | By | Count |
|---|---|---|---|---|
| clean | natural | proposed | generator | 146 |
| exempt | natural | proposed | generator | 38 |
| exempt | natural | verified | dom-check | 219 |
| violation | natural | verified | dom-check | 50 |
| violation | seeded | verified | generator | 127 |

580 labels over 91 topics and 4 snippets (2069 blocks in all).
Split of labels: 400 dev, 187 test. Split of files: 29 of 95 files (31%) are in `test`.

## How each group was made

**Seeded violations (origin `seeded`, status `verified`, by `generator`).** `corpuskit inject` inserted each defect and wrote an exact span label from the final DOM, so the span is right by construction. The injector's catalog is the CorpusKit default with the appended sentences rewritten for a device guide and a `unit_space` kind added (`_prep/hw-defects.yaml`); the shared `corpus/schema/defects.yaml` has no operations, so it cannot be passed to `inject --defects`. The generator's rule hints were mapped to the rule ids the engine and `corpus/DigitalBanking` use (for example `structure:alt-text` to `structure:AltText`, `company:plain-language` to `company:plain-language/*`). `heading_skip` labels cover the whole heading and carry no quote, as in DigitalBanking, because the quote would span a variable value that `FlareAI.Scoring validate` does not resolve. They are `verified` by the generator, which knows exactly what it changed (status conventions: `docs/testing/CORPUS.md`); whether the sentence still reads naturally is for the two-labeller pass (#78) and the `block-complete` labels.

**Natural violations (origin `natural`, status `verified`, by `dom-check`).** A fact of the markup, no judgment: an `img` with no alt text (`structure:AltText`) and a table with no header cell (`structure:TableHeader`); 7 of these `alt_missing` labels sat on images whose alt text the injector had removed and duplicated the seeded label, so `FlareAI.Labeler qa --fix` removed them. The source figures that have a title got it as alt text during conversion; the rest have none.

**Natural exempt labels (verified, `dom-check`).** Text inside `code` (pin names, file names, commands) and every `pre` block are `exempt` for `lint`: a prose rule matches the text but it is written exactly as the product shows it.

**Natural clean and exempt labels (status `proposed`, by `generator`).** Device labels and model variants (`STATUS`, `JIG ETH`, `10-inch`) are `clean` for any rule; values with units in table cells (`Around 25 × 24 mm`, `3 g`, `2.4 GHz`) are `clean` for `google:units-of-measure/*` and `google:numbers/*`; the `Related topic:` label and the print-notice sentence, repeated in every topic, are `exempt` for `lint`. These are rule-based readings and need the hand-read or two-labeller confirmation before they count.

### Seeded violations by kind

| Kind | Count |
|---|---|
| unit_space | 14 |
| exclaim | 11 |
| the_user | 9 |
| we | 8 |
| alt_missing | 7 |
| marketing | 6 |
| prior_to | 6 |
| claims_free | 5 |
| claims_guaranteed | 5 |
| click_here | 5 |
| passive | 5 |
| please | 5 |
| simply | 5 |
| whitelist | 5 |
| heading_skip | 4 |
| vague | 4 |
| claims_soon | 3 |
| please_note | 3 |
| undefined_abbr | 3 |
| url | 3 |
| utilize | 3 |
| in_order_to | 2 |
| ui_not_bold | 2 |
| wordy | 2 |
| latin | 1 |
| will | 1 |

Surface variants (case and position): allcaps+plain=5, append=50, append+link=5, mid-sentence+across-inline=7, mid-sentence+adjacent-variable=1, mid-sentence+bold=4, mid-sentence+link=1, mid-sentence+plain=27, prepend=13, remove-alt=7, retag:h2>h4=4, sentence-start+across-inline=2, sentence-start+plain=1.

### Natural violations by kind

- alt_missing: 49
- table_header: 1

### Contexts covered by the labels

Line contexts: caption=1, code-block=35, heading:h2=2, heading:h4=4, heading:h5=2, list=44, note=11, paragraph=227, step=96, table-cell=158, table-header=2, warning=6.

Span contexts: boilerplate=37, conditional=37, emphasis=51, in-snippet-block=1, inline-code=184, link-text=7, ui-label=1, xref=1.

## Density

Seeded violations are about 6% of the 2,069 blocks that carry text (good prose, as the design asks), spread over the kinds listed above. The text comes from a maintained product guide, so it already contains the usual natural problems; those are not labelled yet (see below).

Units are written with a space in the source. The `unit_space` kind removes that space in a seeded sample (millimetres, gigahertz, megahertz, gigabytes, volts, grams, watts).

## Not done yet

- The blind two-labeller pass over every block (design 1.4 steps 2-3) has not run: `tools/FlareAI.Labeler` exists (#78) but the pass costs money and has not been executed (`FlareAI.Labeler label` prices it first). So natural violations in the prose (passive voice, `will`, `e.g.` and so on) are not labelled, no block is marked `block-complete`, and none of the 70% `block-complete` target is met. Do not read a missing label in this project as proof that the text is clean.
- Seeded labels are `verified` (the generator is exact); the LLM check that each sentence still reads naturally (design 1.4 step 3) is part of that pass.
- The project has not been opened in Flare 22 or built to HTML5 and PDF by hand; `corpuskit check` passes (every file parses, every xref, snippet, image, variable and condition resolves, every label anchors once).
- Quoted text: the source has no quotation blocks, so this project has no quote labels. The images carry the source product's marks; only the surrounding text, alt text and file names were made fictional.

## Rebuilding

```powershell
# source: git clone --depth 1 https://github.com/raspberrypi/documentation (commit in project.json)
# _prep holds the converter and the steps; paths inside are the ones used on the build machine
python -I _prep/adoc2html.py _prep/hw-curated.json   # AsciiDoc to HTML topics, names made fictional
uv run --with pillow python _prep/shrink_images.py <out>/images
.\scripts\make-corpus-v2.ps1 ...   # import, scaffold, then _prep/enrich_hw.py, inject x2, contexts, _prep/natural_labels.py, contexts --update, check
.\tools\FlareAI.Scoring\bin\Release\net48\FlareAI.Scoring.exe validate --labels corpus\v2\hardware-install\labels.jsonl --kinds corpus\schema\defects.yaml --root corpus\v2\hardware-install
```
`_prep/build-hw.ps1` runs the whole chain in order.

<!-- labeler:qa -->
## Label QA

Written by `FlareAI.Labeler qa --update-labels-md`; deterministic (no model, no linter). Rerun after any label change.

| | |
|---|---|
| Result | 0 errors, 0 warnings, 0 notes |
| Topics, snippets | 91, 4 |
| Blocks with text, with at least one label | 2069, 429 (20.7%) |
| `block-complete` blocks, verified | 0, 0 |
| Violations, verified (scored) | 177, 177 |
| Verified violations in `test` | 57 |
| Violations per topic | 1.95 |
| Scored labels (verified, not ambiguous or block-complete) | 396 |
| Share of files in `test` | 30.5% |

Labels by type and status: clean proposed 146, exempt proposed 38, exempt verified 219, violation verified 177.

Kinds with fewer than 3 verified violations in `test` (their per-kind recall there is noise): claims_free (1 in test), claims_guaranteed (2 in test), claims_soon (0 in test), click_here (2 in test), heading_skip (2 in test), in_order_to (0 in test), latin (0 in test), marketing (0 in test), passive (1 in test), please (2 in test), please_note (1 in test), prior_to (2 in test), table_header (0 in test), ui_not_bold (2 in test), undefined_abbr (0 in test), utilize (0 in test), vague (0 in test), we (1 in test), whitelist (0 in test), will (0 in test), wordy (1 in test).

<!-- /labeler:qa -->
