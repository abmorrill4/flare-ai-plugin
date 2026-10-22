# FR-03: Style guide and Confluence

## Decision
The Flare guide project is the **single source of truth**. Confluence never holds a second copy of the rules.

## Design
1. **Source:** the guide project (`Company/` Rule/Setting table, variables, glossary) lives on a shared, versioned
   folder (Git repo or a team share with history). Writers' plugins point at it; changes go through review there.
2. **Confluence:** the style guide page is frozen to a short pointer: what the guide is, who owns it, and a link to
   the **published HTML5 build** of the guide project (hosted on the intranet or Confluence attachments). Edit the
   guide in Flare, never in Confluence.
3. **Owners:** listed in `FlareAI/owners.txt` (one name or email per line). They approve rule changes and receive
   "propose turning off a rule" feedback.

## Optional later step
A CI script builds the guide project to HTML5 on every merge and pushes it to Confluence through the REST API
(`PUT /wiki/rest/api/content/{id}` for the pointer page's version and attachments). Needs a service account and token
stored in CI secrets, not in the repo.

## Deferred
Confluence to Flare import (reverse direction). It would create a second source of truth; revisit only if a team has
rules that exist solely in Confluence.
