# Labels: release-notes

Span labels for the 60-topic release-notes project (24 releases of an API gateway changelog plus 12 original hub topics).

## Density

| | |
|---|---|
| Topics | 60 (files under `Content/`, snippets included: 64) |
| Blocks (plain-text blocks the labels anchor to) | 1299 |
| Labels | 2215 |
| `violation` | 49 (0.8 per topic) |
| `clean` | 20 |
| `exempt` | 843 |
| `ambiguous` | 4 |
| `block-complete` | 1299 (100% of blocks; status `proposed`, see below) |
| Violations written by CorpusKit `inject` | 46 |
| Violations written by the corpus author's generator | 3 |
| Files in the `test` split | 20 of 64 (31.2%) |

## Violations by kind

| kind | violations |
|---|---|
| blacklist | 1 |
| claims_guaranteed | 1 |
| exclaim | 27 |
| latin | 1 |
| phone | 1 |
| please | 1 |
| prior_to | 3 |
| simply | 2 |
| url | 2 |
| utilize | 6 |
| whitelist | 3 |
| will | 1 |

## How the labels were made

- **Authored violations** (`origin: seeded`, `seed.generator` names the author script: `release-notes-author@1`, `localized-style-author@1` or `legacy-gen@1`, `by: [generator]`, `status: verified`): the generator that wrote the text knows exactly which spans it made defective, so these labels are exact. The quote, prefix and suffix are computed from the final DOM.
- **Injected violations** (`seed.generator: corpuskit/inject@1`): `corpuskit inject --seed N` on the finished text, in blocks that carry no other label. CorpusKit writes `status: proposed` and rule hints that are not scorer rule ids (`company:plain-language`, `structure:hard-coded-value`); a post-step replaces `rules` with the scorer's families (`google:*`, `company:*`, `structure:AltText`, `structure:HardCoded`, ...) and sets `status: verified`, because the generator is exact. No label comes from running the linter.
- **`clean` and `exempt`** (`by: [hand-read]`, `status: verified`): derived from the markup by rule, then read as a class, not span by span: bold UI labels in numbered steps, `code` and `kbd` text, quoted program messages, values with units in table cells, and boilerplate. Each carries a `reason`.
- **`block-complete`** (`status: proposed`, `by: [generator]`): one label per block. It asserts that every lint-detectable problem in the block is labelled. That is true of the generator-authored text by construction only for the defect kinds in `corpus/schema/defects.yaml`; it has **not** been confirmed by the blind two-labeller pass of design 1.4 (no labeller tool exists yet), so the labels are `proposed` and the scorer ignores them until that pass promotes them. Do not read the 100% as a verified figure.
- Contexts (`context.line`, `context.span`) are written by `corpuskit contexts --update`, never by `FlareAI.Core`.
- `split`: FNV-1a bucket of the file path (CorpusKit `Rng.Bucket`, seed 0), below 70 is `dev`; if that left fewer than 30% of the files in `test`, the remaining files with the highest bucket were added to `test`. The list is in `project.json`.

## Project-specific notes

- The imported entries are real text; the labels do not describe its natural problems (typos such as "Addded", "braking changes", doubled commas and unfinished sentences exist in the upstream text and were left in on purpose). Only the injected and authored defects are violations.
- `exempt` labels on time words (334 of 2215): release notes legitimately say now, currently, soon, new, latest and no longer, and the project's `overrides.ini` turns `google:timeless-documentation/1` to `20` off. A finding from those rows anywhere in this project is a false positive; this is the fixture for the "turned off, still flagged" class of bug.
- Seeded kinds are dominated by `exclaim` because the injector's other operations need a matching phrase that list-style release notes rarely contain.

## Known gaps

- Defects in the taxonomy only: wording problems that no kind names (for example a misspelling in the real changelog text, or a Google rule with no taxonomy kind) are not labelled, so a finding there is "unscored", not a false positive, until a block is promoted to `block-complete` after the blind pass.
- Hard-coded email addresses are labelled with kind `url` (hard-coded value) because the taxonomy has no email kind; `structure:HardCoded` flags them.
- Inter-annotator agreement (kappa) is not recorded: there was one author and no second labeller.
