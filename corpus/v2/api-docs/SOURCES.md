# api-docs: sources

REST API developer documentation for a fictional team-chat product, **Acme Relay** (company: Acme Software), built from permissively licensed open-source API documentation. Issue #72, epic #66. Design: `dist/audit/TEST-STRATEGY.md` section 1.2.

## Source

| | |
|---|---|
| Project | Zulip server repository (REST API documentation, OpenAPI description, API code examples) |
| URL | https://github.com/zulip/zulip |
| Commit | `4bae1c54f50c6ef523d69f084b47aed1fcc57c4e`, committed 2026-10-21T14:32:09-07:00 (shallow clone of the default branch, retrieved 2026-10-21) |
| Licence | Apache License 2.0. `LICENSE` at the repository root of that commit is copied here as `LICENSE`; the repository `NOTICE` is reproduced in `NOTICE.md` |
| Files used | `api_docs/*.md` (19 pages), `zerver/openapi/zulip.yaml` (88 of its 166 operations), `zerver/openapi/python_examples.py` and `zerver/openapi/javascript_examples.js` (the code examples) |
| Not used | The Zulip help centre, the integrations docs, `api_docs/changelog.md` beyond its first release sections, any file with another licence (`docs/THIRDPARTY` lists the third-party works in the repository; none of them is used) |

Apache-2.0 asks for the licence text, the notice, a statement of changes and no use of the licensor's names to endorse. All four are in this folder (`LICENSE`, `NOTICE.md`, the changes below). No NC or ND source, no customer or pilot content.

## What the topics are

153 topics in six sections:

| Section | Topics | Origin |
|---|---|---|
| `general` | 1 | landing page (`api_docs/index.md`) |
| `getting-started` | 8 | REST overview, client bindings, API keys, HTTP headers, first message, scheduled message (`api_docs/*.md`) |
| `concepts` | 9 | real-time events, message formatting, roles and permissions, group settings, narrows, URLs and IDs, webhooks, push notifications, API changelog (first releases only) |
| `reference/<tag>/` | 88 | one page per endpoint, generated from the OpenAPI description: summary, description with the upstream "Changes" notes, usage examples (upstream Python and JavaScript examples, generated curl), parameter table, parameter details, response table, error examples |
| `sdk` | 14 | Python and JavaScript recipes per area, assembled from the upstream examples |
| `errors` | 33 | the error-handling page and one page per error `code` found in the OpenAPI examples |

## What we changed (Apache-2.0 requires saying so)

- Converted Markdown, YAML and code examples to Flare XHTML with `tools/FlareAI.CorpusKit` (`import`), then added project structure: variables, conditions, snippets, TOCs, targets, glossary.
- **Names.** The product, company, example people, places, outside services and hosts are replaced by fictional names (`stage.py`, tables `THIRD` and `LOWER`). The product and company are `MadCap:variable`s (`General.ProductName` = Acme Relay, `General.CompanyName` = Acme Software); the REST API version is `API.Version` = 1. Domain names in examples are `example.com`. Links to outside sites are removed except to Wikipedia, MDN and the IETF. The names of the upstream project and its authors appear only in this folder's attribution (`NOTICE.md`, this file, `project.json`, `LICENSE`).
- **Added by us** (marked in `project.json` under `build.generatedByUs`): the curl examples; for every write endpoint a pair of rate-limit paragraphs, one `Default.Cloud` and one `Default.OnPrem` (they become two conditional snippets used by 62 topics each); the sentence "This endpoint is part of version 1 of the REST API" (variable `API.Version`); the SDK recipe pages' "Before you start" steps; the error-code pages' headings and the closing note; the glossary.
- **Targets and TOCs.** `Project/Targets/HTML5.fltar` (WebHelp2, TOC `Main`, excludes `Default.OnPrem`) and `PDF.fltar` (TOC `Print`, excludes `Default.Cloud`).
- **Defects inserted** by `corpuskit inject` with a fixed seed (see `LABELS.md`). Every inserted defect has a label with its exact span.

## What is in the project (counts from `project.json`)

153 topics and 17 snippet files (317 block-snippet references; none is a text snippet); 612 variable uses; 818 xrefs; 49 drop-downs; 122 tables with header rows (514 `th`); 392 code blocks with comments; about 3,600 inline `code` spans; headings h1 to h4; conditions `Default.Cloud` and `Default.OnPrem`; notes, tips and warnings (`p.note`, `p.tip`, `p.warning`, mostly through snippets); two targets, two TOCs, one glossary of six terms.

## Rebuild

`source-prep/build.ps1 -Upstream <clone of the repository at the commit above>` rebuilds the project byte for byte (needs `dotnet`, `uv`). Seed 72001, injection density 0.08. The scripts in `source-prep/` are corpus-construction tools, not part of the Flare project.

## CorpusKit notes (found while building this project; tool code was not changed)

1. `--defects corpus/schema/defects.yaml` fails: `defects.yaml needs a top-level 'defects' list`. The shared file is a taxonomy (`- id:` entries), the injector reads its own ops format. The built-in catalogue was used (25 kinds, all present in the shared taxonomy). The `rules` it writes are family hints (`company:plain-language`), not final rule ids.
2. One injector pass skews the mix: a period turned into `!` (`exclaim`) matched in half of the 141 defects of a first trial. The build runs three passes with `--kinds`.
3. Snippet extraction moves a block into `Content/Resources/Snippets/` but keeps its topic-relative `href`/`src`; `check` then reports the xref as unresolved. `source-prep/enrich_common.py` rebases them.
4. `scaffold --conditions` wires target expressions only for the names `Print` and `ScreenOnly`; the HTML5 and PDF targets are patched in `enrich.py targets`.
5. Injected labels are `status: proposed`; there is no `project.json` writer. Both are done by scripts in `source-prep/`.
6. `FlareAI.Scoring validate --root` does not expand variables, `corpuskit` does, so a label whose quote, prefix or suffix includes a variable's value cannot be anchored by `validate`. None of this project's labels is affected.

## Not done

- Opening the project in Flare 22 and building the HTML5 target once by hand (shared acceptance) was not done in this run; the files pass `corpuskit check` and `FlareAI.Scoring validate`, which is not the same thing. A screenshot is still needed for the PR.
- `NOTICE.md` at the repository root has no entry for this folder yet (outside this work package's owned paths): add "Corpus v2 `api-docs`: derived from the Zulip documentation, Apache-2.0, see `corpus/v2/api-docs/NOTICE.md`".
