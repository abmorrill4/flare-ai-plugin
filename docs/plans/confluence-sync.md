# FR-03: Style guide and Confluence (issue #42, epic #83)

## Decision
The Flare guide project is the **single source of truth**. Confluence holds a **published copy**, one page per guide
topic, overwritten on every publish. Direction is one way: Flare to Confluence.

## Design
1. **Source:** the guide project (`Company/` Rule/Setting table, variables, glossary) on a shared, versioned folder.
   Writers' plugins read it directly; changes go through the owners (#83).
2. **Publish:** `scripts/publish-guide-to-confluence.ps1` (Windows PowerShell 5.1, no install) reads the guide's
   Content topics and creates or updates one page per topic under a parent page, using the Confluence **Data Center 10**
   REST API v1 with a personal access token.
   - *Why topics, not the HTML5 build:* topics are well-formed XML, so conversion is deterministic and needs no Flare
     install or build step (works in CI). HTML5 output is a JavaScript shell around topics. The guide still has an HTML5
     target (`styleguide/Project/Targets/HTML5.fltar`, TOC `Main.fltoc`) so owners can Build > Publish a standalone
     copy without any setup; `scripts/update-guide-toc.ps1` keeps the TOC in step with the Google topics.
3. **Conversion:** headings, paragraphs, lists, tables (rows wrapped in `tbody`), `pre` to a `code` macro, bold/italic,
   external links; relative links between guide topics become `ac:link` + `ri:page` (by page title, so creation order
   doesn't matter). Flare-only markup (ids, `MadCap:conditions`, variables) is dropped. Every page starts with an
   `info` macro: "Edited in Flare - changes here are overwritten...". Output is parsed as XML and only the macros
   info, note, warning, tip and code are allowed; an invalid page stops the run before any API call.
4. **Idempotence:** pages are found by space + title. A SHA-256 of title + converted XHTML is stored in the content
   property `flare-guide-sync`; unchanged pages are skipped, changed pages get `version = current + 1` (one retry on a
   409). Each page carries the label `flare-style-guide`; a page with the same title but without that label is never
   overwritten (`-AdoptExisting` to take it over).
5. **Removed or renamed topics:** the old page is **not deleted**. It is labelled `flare-orphaned` and reported;
   owners delete it in Confluence when ready. A renamed topic therefore creates a new page and orphans the old one.
6. **Auth and safety:** `CONFLUENCE_BASE_URL` (context path allowed), `CONFLUENCE_PAT`, `CONFLUENCE_PARENT_ID`,
   optional `CONFLUENCE_SPACE_KEY` (default: the parent page's space). The token is sent only as
   `Authorization: Bearer`, never printed. TLS uses the system trust store; there is no option to skip certificate
   checks. 429 is retried honouring `Retry-After`; 401/403 explain what to check.
7. **`-WhatIf`:** prints the plan and writes the converted XHTML to `-OutDir` (default `confluence-preview`);
   no API call.

## Endpoints used (Data Center REST v1)
`GET /rest/api/content/{parent}?expand=space`, `GET /rest/api/space/{key}`,
`GET /rest/api/content?type=page&spaceKey=&title=&expand=version,metadata.labels`, `POST /rest/api/content`
(`ancestors`, `body.storage`), `PUT /rest/api/content/{id}`, `GET /rest/api/content/{id}/child/page` (paged),
`POST /rest/api/content/{id}/label`, `DELETE /rest/api/content/{id}/label/{name}`,
`GET|POST|PUT /rest/api/content/{id}/property[/flare-guide-sync]`.

## Verification status
Tested end to end only against a **local mock** (`tests/publish-guide/MockConfluence.ps1`) built from the Data Center
REST documentation: create, no-change rerun, edit, 409, orphan and restore, 429, 401, 403, title collision, paging,
context path, storage validity. It has **not** been run against a real Confluence 10 server. Run the 3-step smoke test
in the user guide ("Publish the style guide to Confluence") for the first real publish.

## Optional: publish on every merge (not enabled)
```yaml
# .github/workflows/publish-guide.yml - for teams that keep the guide in git
name: Publish style guide
on:
  push:
    branches: [main]
    paths: ['styleguide/Content/**']
jobs:
  publish:
    runs-on: windows-latest        # or a self-hosted runner that can reach an intranet Confluence
    steps:
      - uses: actions/checkout@v4
      - shell: powershell
        env:
          CONFLUENCE_BASE_URL: ${{ vars.CONFLUENCE_BASE_URL }}
          CONFLUENCE_PARENT_ID: ${{ vars.CONFLUENCE_PARENT_ID }}
          CONFLUENCE_PAT: ${{ secrets.CONFLUENCE_PAT }}   # a service account's token, never committed
        run: ./scripts/publish-guide-to-confluence.ps1
```

## Deferred
Confluence to Flare import (reverse direction). It would create a second source of truth; revisit only if a team has
rules that exist solely in Confluence.
