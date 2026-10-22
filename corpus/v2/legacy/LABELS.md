# Labels: legacy

Span labels for the 149-topic messy legacy project.

## Density

| | |
|---|---|
| Topics | 149 (files under `Content/`, snippets included: 151) |
| Blocks (plain-text blocks the labels anchor to) | 3267 |
| Labels | 4390 |
| `violation` | 813 (5.5 per topic) |
| `clean` | 156 |
| `exempt` | 21 |
| `ambiguous` | 133 |
| `block-complete` | 3267 (100% of blocks; status `proposed`, see below) |
| Violations written by CorpusKit `inject` | 131 |
| Violations written by the corpus author's generator | 682 |
| Files in the `test` split | 46 of 151 (30.5%) |

## Violations by kind

| kind | violations |
|---|---|
| alt_missing | 39 |
| claims_free | 5 |
| claims_guaranteed | 6 |
| claims_soon | 8 |
| click_here | 46 |
| exclaim | 93 |
| heading_skip | 13 |
| in_order_to | 5 |
| is_able_to | 13 |
| latin | 9 |
| passive | 12 |
| phone | 65 |
| please | 31 |
| please_note | 6 |
| prior_to | 13 |
| second_h1 | 3 |
| simply | 19 |
| table_header | 29 |
| the_user | 9 |
| ui_not_bold | 215 |
| undefined_abbr | 2 |
| url | 72 |
| utilize | 4 |
| vague | 5 |
| variable_company | 40 |
| variable_product | 22 |
| we | 7 |
| will | 16 |
| wordy | 6 |

## How the labels were made

- **Authored violations** (`origin: seeded`, `seed.generator` names the author script: `release-notes-author@1`, `localized-style-author@1` or `legacy-gen@1`, `by: [generator]`, `status: verified`): the generator that wrote the text knows exactly which spans it made defective, so these labels are exact. The quote, prefix and suffix are computed from the final DOM.
- **Injected violations** (`seed.generator: corpuskit/inject@1`): `corpuskit inject --seed N` on the finished text, in blocks that carry no other label. CorpusKit writes `status: proposed` and rule hints that are not scorer rule ids (`company:plain-language`, `structure:hard-coded-value`); a post-step replaces `rules` with the scorer's families (`google:*`, `company:*`, `structure:AltText`, `structure:HardCoded`, ...) and sets `status: verified`, because the generator is exact. No label comes from running the linter.
- **`clean` and `exempt`** (`by: [hand-read]`, `status: verified`): derived from the markup by rule, then read as a class, not span by span: bold UI labels in numbered steps, `code` and `kbd` text, quoted program messages, values with units in table cells, and boilerplate. Each carries a `reason`.
- **`block-complete`** (`status: proposed`, `by: [generator]`): one label per block. It asserts that every lint-detectable problem in the block is labelled. That is true of the generator-authored text by construction only for the defect kinds in `corpus/schema/defects.yaml`; it has **not** been confirmed by the blind two-labeller pass of design 1.4 (no labeller tool exists yet), so the labels are `proposed` and the scorer ignores them until that pass promotes them. Do not read the 100% as a verified figure.
- Contexts (`context.line`, `context.span`) are written by `corpuskit contexts --update`, never by `FlareAI.Core`.
- `split`: FNV-1a bucket of the file path (CorpusKit `Rng.Bucket`, seed 0), below 70 is `dev`; if that left fewer than 30% of the files in `test`, the remaining files with the highest bucket were added to `test`. The list is in `project.json`.

## Project-specific notes

### Eras

The 149 topics are written in four voices, assigned at random: A (Word paste, 40 topics), B (copy and paste from the web, 46), C (Flare-native but inconsistent, 36), D (modern but verbose, 27). Defects are written into the sentences by the generator at a rate that falls from A to D, so density differs by era as in a real project.

### Legacy features present (census of the delivered files)

| Feature | Files (or count) |
|---|---|
| pasted Word markup (MsoNormal) | 40 |
| o:p elements | 40 |
| inline style attributes | 62 |
| runs of three or more no-break spaces | 18 |
| br used as a paragraph break | 39 |
| empty paragraphs | 62 |
| bold or underlined paragraph used as a heading | 70 |
| deprecated HTML (font, center, u, align) | 46 |
| tables with 100 or more rows | 3 |
| nested tables | 3 |
| lists nested 7 levels | 3 |
| MadCap:snippetText | 2 |
| MadCap:snippetBlock | 31 |
| inline conditions (MadCap:conditions on spans, list items) | 14 |
| deprecated MadCap elements (MadCap:HelpControl) | 5 |
| MadCap:dropDown | 2 |
| MadCap:variable | 12 |
| images without alt attribute or with empty alt | 39 |
| curly quotes or apostrophes | 26 |
| straight apostrophes or quotes in text | 35 |
| mojibake (UTF-8 read as Windows-1252) | 4 |
| UTF-8 with BOM | 14 |
| saved as Windows-1252 | 10 |
| .doc.htm file names | 13 |
| file names with spaces | 21 |
| xrefs to topics that do not exist | 4 |
| TOC entries to topics that do not exist | 6 |
| topics missing from the TOC (orphans) | 13 |
| drifted duplicate topics | 16 |

### Competing term forms (Mine scenario)

`terms.json` holds the exact counts of competing forms (regex, count over all text and over prose only). Summary:

| Concept | Form | Count (all blocks) | Count (prose only) |
|---|---|---|---|
| work order | work order | 44 | 43 |
| work order | Work Order | 78 | 63 |
| work order | WO | 10 | 9 |
| work order | job ticket | 12 | 11 |
| work order | service call | 9 | 8 |
| technician | technician | 75 | 75 |
| technician | Technician | 26 | 20 |
| technician | tech | 7 | 7 |
| technician | field engineer | 4 | 4 |
| technician | field rep | 3 | 3 |
| customer | customer | 112 | 109 |
| customer | Customer | 67 | 49 |
| customer | client | 14 | 14 |
| log in | log in | 16 | 16 |
| log in | sign in | 7 | 7 |
| log in | log on | 3 | 3 |
| log in | login | 4 | 3 |
| email | email | 20 | 20 |
| email | e-mail | 11 | 11 |
| email | E-mail | 7 | 7 |
| email | Email | 36 | 35 |
| product name | Marlowe Dispatch | 35 | 23 |
| product name | MarloweDispatch | 10 | 10 |
| product name | Marlowe-Dispatch | 7 | 7 |
| product name | MD | 10 | 10 |
| product name | General.ProductName variable | 12 | 12 |

The product name is written four ways (the variable, the literal `Marlowe Dispatch`, `MarloweDispatch`, `Marlowe-Dispatch`, `MD`). Every literal `Marlowe Dispatch` and `Marlowe Systems` outside a variable is a `variable_product` / `variable_company` violation (`project:variables`), because `General.ProductName` and `General.CompanyName` exist. The other forms are the rival spellings the mining scenario must surface; they are not labelled as violations because this project has no glossary that picks a winner.

### Broken on purpose

`CorpusKit check` exits 1 on this project with exactly 10 problems, listed in `known-breaks.json`: 4 xrefs to topics that do not exist and 6 TOC entries that link to missing files. Every other check (well-formed XML, snippets, images, variables, condition tags, label anchors) passes. `FlareAI.Scoring validate` passes with 0 problems.

### Encoding

14 files start with a UTF-8 byte order mark, 10 are saved as Windows-1252 with a matching XML declaration, and 4 contain mojibake (UTF-8 read as Windows-1252: `a`-circumflex followed by Euro and a quote). The Word topics also carry a `meta` that says `charset=windows-1252` although the file is UTF-8, the way Word exports do. `.gitattributes` in this folder (`* -text`) stops git from changing line endings or encodings.

### Labelled and not labelled

Labelled: hard-coded phone numbers, URLs and email addresses, the product and company names where a variable exists, images without alt text, a skipped heading level, a second h1, tables without header cells (including layout tables), vague link text, UI labels that are not bold (quoted, italic, coloured), and the wording kinds in the taxonomy (utilize, in order to, prior to, is able to, please, please note, simply, we, will, the user, e.g., exclamation marks, free of charge, guaranteed, coming soon, passive, wordy, vague, undefined abbreviation, whitelist). Not labelled because the taxonomy has no kind: mojibake characters, no-break space runs, empty paragraphs, `br` breaks, bold paragraphs used as headings, deprecated HTML and MadCap elements, conditions on spans, drifted duplicates, competing term forms. These are markup and consistency features for the extractor and mining tests, not lint findings.

## Known gaps

- Defects in the taxonomy only: wording problems that no kind names (for example a misspelling in the real changelog text, or a Google rule with no taxonomy kind) are not labelled, so a finding there is "unscored", not a false positive, until a block is promoted to `block-complete` after the blind pass.
- Hard-coded email addresses are labelled with kind `url` (hard-coded value) because the taxonomy has no email kind; `structure:HardCoded` flags them.
- Inter-annotator agreement (kappa) is not recorded: there was one author and no second labeller.
