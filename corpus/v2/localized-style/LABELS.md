# Labels: localized-style

Span labels for the 30-topic localized-style project.

## Density

| | |
|---|---|
| Topics | 30 (files under `Content/`, snippets included: 32) |
| Blocks (plain-text blocks the labels anchor to) | 425 |
| Labels | 681 |
| `violation` | 29 (1.0 per topic) |
| `clean` | 169 |
| `exempt` | 58 |
| `ambiguous` | 0 |
| `block-complete` | 425 (100% of blocks; status `proposed`, see below) |
| Violations written by CorpusKit `inject` | 22 |
| Violations written by the corpus author's generator | 7 |
| Files in the `test` split | 10 of 32 (31.2%) |

## Violations by kind

| kind | violations |
|---|---|
| bank | 2 |
| customer | 1 |
| exclaim | 11 |
| idp | 1 |
| oidc | 1 |
| please | 4 |
| saml2 | 1 |
| simply | 2 |
| sso_spelling | 1 |
| utilize | 1 |
| we | 1 |
| whitelist | 1 |
| will | 2 |

## How the labels were made

- **Authored violations** (`origin: seeded`, `seed.generator` names the author script: `release-notes-author@1`, `localized-style-author@1` or `legacy-gen@1`, `by: [generator]`, `status: verified`): the generator that wrote the text knows exactly which spans it made defective, so these labels are exact. The quote, prefix and suffix are computed from the final DOM.
- **Injected violations** (`seed.generator: corpuskit/inject@1`): `corpuskit inject --seed N` on the finished text, in blocks that carry no other label. CorpusKit writes `status: proposed` and rule hints that are not scorer rule ids (`company:plain-language`, `structure:hard-coded-value`); a post-step replaces `rules` with the scorer's families (`google:*`, `company:*`, `structure:AltText`, `structure:HardCoded`, ...) and sets `status: verified`, because the generator is exact. No label comes from running the linter.
- **`clean` and `exempt`** (`by: [hand-read]`, `status: verified`): derived from the markup by rule, then read as a class, not span by span: bold UI labels in numbered steps, `code` and `kbd` text, quoted program messages, values with units in table cells, and boilerplate. Each carries a `reason`.
- **`block-complete`** (`status: proposed`, `by: [generator]`): one label per block. It asserts that every lint-detectable problem in the block is labelled. That is true of the generator-authored text by construction only for the defect kinds in `corpus/schema/defects.yaml`; it has **not** been confirmed by the blind two-labeller pass of design 1.4 (no labeller tool exists yet), so the labels are `proposed` and the scorer ignores them until that pass promotes them. Do not read the 100% as a verified figure.
- Contexts (`context.line`, `context.span`) are written by `corpuskit contexts --update`, never by `FlareAI.Core`.
- `split`: FNV-1a bucket of the file path (CorpusKit `Rng.Bucket`, seed 0), below 70 is `dev`; if that left fewer than 30% of the files in `test`, the remaining files with the highest bucket were added to `test`. The list is in `project.json`.

## Project-specific notes

- The Company guide (`Content/Resources/StyleGuide/Company/`) is part of the fixture. Its rows, by id: `Google-overrides` turns `google:word-list/443`, `637` and `638` (the log in / sign in guidance and the lint row that flags log in and log out) and `google:word-list/53` (app) off and lowers `google:word-list/211` (disable) to a suggestion; `Terminology` rule-3 to rule-12 are lint rows (users not customers/end users, organization not tenant, log in not sign in, single sign-on, identity provider, OpenID Connect, SAML 2.0); `UI-references` says labels are verbatim. The guide pages are not labelled and are excluded from block-complete; inject was run before they were written so no defect was injected into them.
- Company-over-Google cases: every "log in", "log out" and "login" in running text has a `clean` label for `google:word-list/443`, `637` and `638` (the company turns them off). Planted company violations (kinds `bank`, `customer`, `sso_spelling`, `idp`, `oidc`, `saml2`, rules `company:terminology/*`) are "sign in", "end user", "Single sign on", "Identity Provider", "SAML2", "OpenId Connect". One vendor mention of "Single Sign-On" is `exempt`.
- UI labels that must stay verbatim although a Google rule dislikes them (Whitelist, E-mail address, Kill session, Kill all sessions, Master key, Sign-On Settings, Disable, Enforce whitelist) are `exempt` for `google:word-list/*` and `company:terminology/*`, in bold and in the reference table.
- The company taxonomy kinds are borrowed from the DigitalBanking kinds (`customer`, `bank`, ...): `bank` is used for "sign in" and "tenant" (generic term where the guide requires the product's term). A dedicated kind would describe them better.

## Known gaps

- Defects in the taxonomy only: wording problems that no kind names (for example a misspelling in the real changelog text, or a Google rule with no taxonomy kind) are not labelled, so a finding there is "unscored", not a false positive, until a block is promoted to `block-complete` after the blind pass.
- Hard-coded email addresses are labelled with kind `url` (hard-coded value) because the taxonomy has no email kind; `structure:HardCoded` flags them.
- Inter-annotator agreement (kappa) is not recorded: there was one author and no second labeller.
