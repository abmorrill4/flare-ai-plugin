# Labels: policy

Labels for this project are in `labels.jsonl` (schema: `corpus/schema/labels.schema.json`, explained in `docs/testing/CORPUS.md`). `project.json` lists the sources, the seed and the test split.

## What is in the file

| Label | Origin | Status | By | Count |
|---|---|---|---|---|
| clean | natural | proposed | generator | 29 |
| exempt | natural | proposed | generator | 8 |
| exempt | natural | verified | dom-check | 331 |
| violation | seeded | verified | generator | 151 |

519 labels over 84 topics and 7 snippets (12959 blocks in all).
Split of labels: 331 dev, 188 test. Split of files: 29 of 91 files (32%) are in `test`.

## How each group was made

**Seeded violations (origin `seeded`, status `verified`, by `generator`).** `corpuskit inject` inserted each defect and wrote an exact span label from the final DOM. The catalog is the CorpusKit default with the appended sentences rewritten for a policy set and a `date_format` kind added (`_prep/pol-defects.yaml`); the shared `corpus/schema/defects.yaml` has no operations, so it cannot be passed to `inject --defects`. Rule hints were mapped to the rule ids the engine and `corpus/DigitalBanking` use, and `heading_skip` labels cover the whole heading with no quote (see the hardware-install project for the reason). They are `verified` by the generator, which knows exactly what it changed (status conventions: `docs/testing/CORPUS.md`). `google:dates-and-times/*` hints were normalised to `google:dates-times/*`.

**Natural exempt labels (verified, by `dom-check`).** Every block inside a `blockquote` (the NIST control text, quoted verbatim) is `exempt` for `lint`: the standard's own wording, including `[Assignment: organization-defined ...]` placeholders, must not be rewritten.

**Natural clean and exempt labels (status `proposed`, by `generator`).** Periods and dates in the time-limits table (`6 years`, `60 calendar days`, `April 20, 2005`) are `clean` for the number, unit and date rules; the `Related topic:` label and the print-notice sentence, repeated in many topics, are `exempt` for `lint`.

**Structure that makes the project realistic and is not a label:** a document-control snippet at the top of 76 topics that reads the `Policy.*` variables and `General.CompanyName`, a Print-only notice snippet, ScreenOnly `Related topic` paragraphs, a glossary, and `p.note` paragraphs for the NIST discussion text.

### Seeded violations by kind

| Kind | Count |
|---|---|
| latin | 23 |
| we | 10 |
| claims_guaranteed | 9 |
| exclaim | 9 |
| whitelist | 9 |
| heading_skip | 8 |
| marketing | 8 |
| phone | 8 |
| passive | 6 |
| utilize | 6 |
| wordy | 6 |
| claims_free | 5 |
| click_here | 5 |
| in_order_to | 5 |
| ui_not_bold | 5 |
| undefined_abbr | 5 |
| vague | 5 |
| blacklist | 4 |
| claims_soon | 4 |
| date_format | 3 |
| simply | 3 |
| prior_to | 2 |
| will | 2 |
| is_able_to | 1 |

Surface variants (case and position): allcaps+across-inline=1, append=94, append+link=5, capitalized+plain=2, mid-sentence+across-inline=5, mid-sentence+bold=7, mid-sentence+link=2, mid-sentence+plain=19, prepend=3, retag:h2>h4=8, sentence-start+bold=1, sentence-start+plain=4.

### Natural violations by kind


### Contexts covered by the labels

Line contexts: glossary-def=36, heading:h2=1, heading:h3=4, heading:h4=8, note=22, paragraph=67, quote=342, step=7, table-cell=32.

Span contexts: boilerplate=7, conditional=7, emphasis=8, link-text=5, xref=2.

## Density

Seeded violations are well under 2% of the 12,959 blocks that carry text, because the design sets a low density for the formal policy register and most of the text is long regulation and control wording.

The text is a regulation and a standard, so replace-type defects only land where the wording they replace occurs (for example `use`, `for example`, `before`, month names); the appended-sentence kinds (claims, wordy, vague and so on) land in any paragraph. The kind counts above show the resulting mix.

## Not done yet

- The blind two-labeller pass over every block (design 1.4 steps 2-3) has not run: `tools/FlareAI.Labeler` exists (#78) but the pass costs money and has not been executed (`FlareAI.Labeler label` prices it first). Natural violations in the prose are not labelled, no block is marked `block-complete`, and none of the 70% `block-complete` target is met. Do not read a missing label as proof that the text is clean.
- Seeded labels are `verified` (the generator is exact); that each seeded sentence still reads naturally is for the two-labeller pass.
- The project has not been opened in Flare 22 or built to HTML5 and PDF by hand; `corpuskit check` passes.
- The project has no images (the sources have none) and no UI labels. Source names such as the Department of Health and Human Services and NIST are public bodies named in the regulation itself and were kept; the fictional organization is `General.CompanyName`.

## Rebuilding

```powershell
# sources: eCFR API (URLs in project.json) and git clone --depth 1 https://github.com/usnistgov/oscal-content (commit in project.json)
# _prep holds the converter and the steps; paths inside are the ones used on the build machine
python -I _prep/policy_build.py <out>   # eCFR XML and OSCAL JSON to HTML topics
# then: corpuskit import, scaffold, _prep/enrich_pol.py, inject x2, contexts, _prep/natural_labels.py, contexts --update, check
.\tools\FlareAI.Scoring\bin\Release\net48\FlareAI.Scoring.exe validate --labels corpus\v2\policy\labels.jsonl --kinds corpus\schema\defects.yaml --root corpus\v2\policy
```
`_prep/build-pol.ps1` runs the whole chain in order.

<!-- labeler:qa -->
## Label QA

Written by `FlareAI.Labeler qa --update-labels-md`; deterministic (no model, no linter). Rerun after any label change.

| | |
|---|---|
| Result | 0 errors, 0 warnings, 0 notes |
| Topics, snippets | 84, 7 |
| Blocks with text, with at least one label | 12955, 332 (2.6%) |
| `block-complete` blocks, verified | 0, 0 |
| Violations, verified (scored) | 151, 151 |
| Verified violations in `test` | 46 |
| Violations per topic | 1.80 |
| Scored labels (verified, not ambiguous or block-complete) | 482 |
| Share of files in `test` | 31.9% |

Labels by type and status: clean proposed 29, exempt proposed 8, exempt verified 331, violation verified 151.

Kinds with fewer than 3 verified violations in `test` (their per-kind recall there is noise): blacklist (0 in test), claims_free (1 in test), claims_guaranteed (1 in test), claims_soon (1 in test), click_here (2 in test), date_format (2 in test), heading_skip (1 in test), in_order_to (1 in test), is_able_to (1 in test), passive (1 in test), prior_to (0 in test), simply (0 in test), ui_not_bold (1 in test), undefined_abbr (2 in test), utilize (2 in test), vague (2 in test), we (2 in test), whitelist (1 in test), will (1 in test), wordy (1 in test).

<!-- /labeler:qa -->
