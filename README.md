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
- **Your layers** (Settings → Style layers, one folder per line; default `layers/company`): any folder with
  `guide.md`, `rules/*.yml` (Vale), `accept.txt`, `glossary.json`, `overrides.ini`. A network share, a git checkout,
  or a folder in a Flare project all work — where the company guide lives is a setting, not a code change.
- **This project** (Settings toggle, on by default): generated from the open Flare project — variable values
  ("insert the variable instead of typing it"), glossary terms (canonical casing; definitions go to the AI), and
  `Content/Resources/StyleGuide/` if the project has one.
- Claude Code: `.mcp.json` registers the server; the `docstyle` skill (`.claude/skills/docstyle`) applies it.
- Needs `uv` (`winget install astral-sh.uv`); Vale and the MCP SDK are fetched by `uv` on first run.
  Check it: `uv run --script mcp/docstyle/server.py --selftest`.

## Where things live

| Thing | Path |
|---|---|
| User settings (DPAPI-protected key, model) | `%APPDATA%\FlareAI\settings.json` |
| Diagnostics log | `%APPDATA%\FlareAI\diag.log` |
| Installed plugin | `C:\Program Files\MadCap Software\MadCap Flare <ver>\Flare.app\Plugins\FlareAI\` |
| API dump from the real DLL | `docs\api-dump.txt` (generated) |
