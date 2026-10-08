---
name: docstyle
description: Review or edit technical documentation (MadCap Flare topics, XHTML, Markdown) against the Google developer documentation style guide with the company style guide layered on top. Use when asked to check style, lint docs, rewrite for style, or answer "what does our style guide say about X". Uses the docstyle MCP server (lint, style_lookup, glossary_lookup).
---

# docstyle: Google style + company style, applied the same way everywhere

The style guide has two layers. The **Google developer documentation style guide** is the baseline;
the **company guide** sits on top and wins on any conflict. The same layers drive the linter (Vale:
`Google` + `Company` styles), this skill, and the FlareAI plugin in Flare. Never invent a rule that is
in neither layer. When unsure, call `style_lookup` and cite the section.

## Tools (docstyle MCP server)
- `lint(texts, headings)`: one string per text run (no markup); `headings` = indices of runs that are
  headings. Returns exact spans, the rule, its layer (`google` | `company`), a guide link, and an exact
  `replacement` when the fix is mechanical (`null` when it needs judgment).
- `style_lookup(query)`: matching guide sections from both layers; `""` returns the full layered guide.
- `glossary_lookup(term)`: canonical spelling, abbreviation, first-use note.

If the server is not connected, say so and fall back to the guides in `mcp/docstyle/guides/`.

## Review workflow (check style)
1. Split the content into text runs (text between tags). Never lint or quote across markup.
2. Call `lint`. These findings are authoritative and come first; don't restate them in your own words.
3. Do one judgment pass for what a linter cannot catch, ranked by reader impact: non-imperative
   instructions, passive or unclear sentences, two actions in one step, location after action, wrong
   audience terms, missing first-use expansions, promissory claims. At most 8; fewer is better;
   skip anything subjective or trivial.
4. Report every finding as: **excerpt** (verbatim, shortest unique span inside one run) → **replacement**
   (exact text that can be swapped in, or "needs rewrite") — *why* (≤ 15 words, name the layer).

## Edit workflow (rewrite)
- Fix every lint finding plus clear style breaks; leave compliant sentences word-for-word. Every
  change becomes a tracked change someone must review, so minimal diffs win.
- Re-run `lint` on your result and fix what is left before returning it.

## Flare / XHTML constraints (non-negotiable)
- Preserve every `MadCap:` element and attribute exactly (variables, xrefs, snippets, conditions,
  drop-downs). Never add one. Keep `href`, `src`, `id`, `name`, `MadCap:conditions` unchanged.
- Don't quote or edit text inside snippets, variables, or index keywords.
- Output well-formed XHTML; keep the block structure unless asked to restructure.

## Changing the rules
- Company guidance: `mcp/docstyle/guides/company.md` (prose) + `mcp/docstyle/styles/Company/*.yml`
  (enforceable Vale rules). Keep them in sync; run `uv run --script mcp/docstyle/server.py --selftest`.
- To override a Google rule, disable or re-level it in `mcp/docstyle/.vale.ini`
  (for example `Google.Will = NO`) and say why in `company.md`.
- `styles/Google` is the vendored errata-ai/Google Vale package; update with `vale sync`, don't edit by hand.
