# FlareAI — AI assistant plugin for MadCap Flare Desktop

Adds an **AI** ribbon tab to Flare Desktop with *Check style*, *Rewrite selection*, and
*Ask…* actions. Edits come back as Flare tracked changes so the author accepts or rejects
with the normal review tools.

Phase 1 uses the Anthropic API directly. Phase 2 moves to the Bifrost enterprise gateway.
See `PLAN.md`.

## Quick start (Windows box with Flare installed)

```powershell
cd E:\flare-ai-plugin
.\scripts\bootstrap.ps1          # git init, find Flare, copy API DLL, dump API, build
.\scripts\smoke-api.ps1          # real API call using ANTHROPIC_API_KEY from .env
dotnet test
.\scripts\install-plugin.ps1     # run as admin: copies build output into Flare.app\Plugins\FlareAI
.\scripts\run-flare.ps1          # launches Flare with ANTHROPIC_API_KEY from .env
```

Then in Flare: **File > Options > Plugins > FlareAI > Enable**, restart Flare, look for the
**AI** tab (ribbon UI mode only).

## Prerequisites

- MadCap Flare Desktop (any recent version; plugin targets .NET Framework 4.0 as required)
- .NET SDK 8+ (`dotnet --version`) — builds the net40/net48 projects via reference assemblies
- Git
- `.env` at the repo root (git-ignored) with `ANTHROPIC_API_KEY=...`
- `uv` for the docstyle linter

## Style guide and linter (docstyle)

One source of style truth, used by the plugin, Claude Code, and (later) the Bifrost gateway:
`mcp/docstyle`, an MCP server with `lint`, `style_lookup`, `glossary_lookup`.

Layers apply in order, later wins:
- **Google developer documentation style guide** (built in): `layers/google` — condensed, linked guide plus the
  vendored errata-ai/Google Vale package.
- **Your layers** (Settings → Style layers, one path per line; default `layers/company`): a **Flare project**
  (folder containing a `.flprj`) or a plain folder. Put it on a network share or in a git checkout so everyone
  points at the same copy — where the company guide lives is a setting, not a code change.
- **This project** (Settings toggle, on by default): generated from the open Flare project — variable values
  ("insert the variable instead of typing it"), glossary terms (canonical casing; definitions go to the AI), and
  `Content/Resources/StyleGuide/` if the project has one.

### The company style guide is a Flare project
`layers/company` is a starter project (`CompanyStyle.flprj`) — open it in Flare and replace the placeholder rules.
The linter and the AI read `Content/Resources/StyleGuide/*.htm`, the project's variable sets and its glossary:
- **Bullets** are prose rules for the AI. Nest `Bad: …` / `Good: …` items under a bullet so the rule can be tested.
  Paragraphs are context, not rules.
- **Avoid / Use / Severity / Why / Example tables** become lint rules with one-click fixes. `Use` = `(delete)`
  deletes the text; empty `Use` only flags it. Comma-separated `Avoid` entries pair with the same number of `Use` entries.
- **Rule / Setting / Reason tables** switch Google rules off or change their level (`off`, `error`, `warning`, `suggestion`).
- Every rule gets a stable ID (`company:terminology/1`). The AI must cite one, so findings read
  "[AI · Company: Terminology]" and **Guide ↗** opens the rule's topic in Flare.
- **AI ▸ Validate style guide** tests the guide itself: each table row against its Example (free), then, on request,
  each prose rule's Bad/Good examples against the AI ("AI applies 5 of 5 prose rules as written").

### Teams: one guide, strict ownership
- Keep the company guide (a Flare project) in git; every writer clones it and points Settings → Style layers at the
  clone. The pane warns when a writer's clone is behind ("Style guide … 2 change(s) behind: pull it").
- `FlareAI/owners.txt` in the guide lists the owners (git `user.email`, one per line). With owners listed, only they
  change shared rules (enforce it with branch protection); other writers' "Ignore rule…" becomes a request to them.
- `FlareAI/layer.txt` names the layer (`company`): rule ids stay the same in every clone.
- Settings → Team feedback folder: a shared folder (OneDrive/SharePoint-synced, network share, or a repo writers can
  push to). Each writer's review decisions append to their own file there; no merge conflicts.
- **AI ▸ Build style guide** (for owners) proposes rules from evidence: **Mine this project** finds spelling variants,
  competing words, inconsistent capitalization and heading case in your topics (with counts and real sentences; pick the
  preferred form); **Add notes or an old guide** turns pasted or dictated notes, an old guide, or another AI tool's
  prompt into rules that quote their source. Checked rules go into `StyleGuide/From-our-content.htm`; run Validate.
- **AI ▸ Tune style guide** (for owners) pools everyone's decisions: rules writers disagree on, rules that are often
  rejected, repeated hand edits (a better replacement), and requests to turn rules off — each with counts, reasons,
  real excerpts and who; **Open rule in the guide** goes straight to the rule in Flare, and **Draft a change with AI**
  rewrites the rule from that evidence as tracked changes. **Customize AI prompts** puts the AI's instructions in the
  guide (`FlareAI/prompts`) so the team tunes them like rules.

- Claude Code: `.mcp.json` registers the server; the `docstyle` skill (`.claude/skills/docstyle`) applies it.
- Needs `uv` (`winget install astral-sh.uv`); Vale and the MCP SDK are fetched by `uv` on first run.
  Check it: `uv run --script mcp/docstyle/server.py --selftest`.

## LLM-ready output
AI ▸ **Export for LLMs** turns built HTML5 or Clean XHTML output into one Markdown file per topic (`llm/…`, with
title/source/URL front matter) plus `llms.txt` (index) and `llms-full.txt` (everything) at the output root, so they
publish with the site. Built output already has snippets, variables and conditions resolved. Settings → "After each
HTML build…" does it automatically when you build in Flare. For `madbuild` / CI, add a post-build event to the target
(Target Editor ▸ Build Events):

```
uv run --quiet --script "C:\Program Files\MadCap Software\MadCap Flare 22\Flare.app\Plugins\FlareAI\docstyle\server.py" --llm-export "$(OutputDirectory)" --url https://docs.example.com
```

## Where things live

| Thing | Path |
|---|---|
| User settings (DPAPI-protected key, model) | `%APPDATA%\FlareAI\settings.json` |
| Diagnostics log | `%APPDATA%\FlareAI\diag.log` |
| Installed plugin | `C:\Program Files\MadCap Software\MadCap Flare <ver>\Flare.app\Plugins\FlareAI\` |
| API dump from the real DLL | `docs\api-dump.txt` (generated) |
