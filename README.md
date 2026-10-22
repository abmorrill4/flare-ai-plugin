# House Style — AI style checking for MadCap Flare Desktop

Adds a **House Style** ribbon tab to Flare Desktop with *Check style*, *Rewrite selection*, and
*Ask…* actions. Edits come back as Flare tracked changes so the author accepts or rejects
with the normal review tools.

Checks every topic against the full Google developer documentation style guide plus your team's own rules, kept in
one Flare project. Bring your own Anthropic API key, or a Bifrost gateway virtual key. Architecture: `PLAN.md`.

## Install (writers)

1. Download `HouseStyle-<version>.zip` from [Releases](../../releases) and unzip the whole thing.
2. Close Flare, then double-click **`Install.cmd`** (accept the administrator prompt). It unblocks the files, picks the
   newest installed Flare (or `.\Install.ps1 -Flare 21` for another) and copies the plugin.
3. Start Flare > **File > Options > Plugins > House Style > Enable**, then restart Flare again.
4. **House Style > Settings**: paste your [Anthropic API key](https://console.anthropic.com/settings/keys), or a
   gateway virtual key (Provider: *Bifrost gateway*, see `docs/bifrost-migration.md`).

| Install folder | Flare version |
|---|---|
| `MadCap Flare 22` | Flare 2026 |
| `MadCap Flare 21` | Flare 2025 (including 2025 R2) |

Manual fallback: **unblock** the zip first (right-click > Properties > Unblock, or `Unblock-File`), copy the `FlareAI`
folder into `...\MadCap Flare <n>\Flare.app\Plugins\`, and check there is no wrapper folder (the DLL must be at
`Plugins\FlareAI\FlareAI.Flare.dll`). Restart Flare after copying and again after enabling. Details:
`docs/README-INSTALL.md`.

Tested with MadCap Flare 2026 (22). The style checker is free; AI review costs about 2¢ per topic on your key.

## Privacy

Style checks run locally. When you ask for an AI review, rewrite or answer, the text of the sections being checked (and, for alt text, the image) and the
relevant style rules go to the Anthropic API under your key, or, if you choose the Bifrost gateway provider in Settings,
to the gateway URL you enter under its virtual key. Nothing else is sent (no telemetry; nothing goes to the
authors). Your key is stored with Windows DPAPI in `%APPDATA%\FlareAI\settings.json` and never logged.

## Docs

- User guide: `docs/user-guide` (a Flare project — open `FlareAI-Guide.flprj`).
- Testers: `docs/TESTER-GUIDE.md` (quickstart), `docs/testing/TEST-PLAN.md`, `docs/testing/BUG-TEMPLATE.md`.
- Stress test, model bake-off and routing decisions: `docs/testing/STRESS-REPORT.md`.
- The full Google guide in the plugin (compile, coverage, results): `docs/testing/GOOGLE-GUIDE-REPORT.md`.
- Test corpus: `corpus/` (250 topics + ground truth; regenerate with `uv run --script scripts/make-corpus.py corpus`); headless bench:
  `tools/FlareAI.Bench`.

## Quick start (Windows box with Flare installed)

```powershell
cd E:\flare-ai-plugin
.\scripts\bootstrap.ps1          # git init, find Flare, copy API DLL, dump API, build
.\scripts\smoke-api.ps1          # real API call using ANTHROPIC_API_KEY from .env
dotnet test
.\scripts\install-plugin.ps1     # run as admin: copies build output into Flare.app\Plugins\FlareAI
.\scripts\run-flare.ps1          # launches Flare with ANTHROPIC_API_KEY from .env
```

Then in Flare: **File > Options > Plugins > House Style > Enable**, restart Flare, look for the
**House Style** tab (ribbon UI mode only).

## Prerequisites

- MadCap Flare Desktop 2026 (22)
- .NET SDK 8+ (`dotnet --version`) — builds the net40/net48 projects via reference assemblies
- Git
- `.env` at the repo root (git-ignored) with `ANTHROPIC_API_KEY=...`

Writers need nothing but the plugin folder and an API key: the style guide, the linter and the LLM export all run
inside the plugin.

## The style guide

One style guide project, used by the plugin, Claude Code (through the optional MCP program) and the test bench:
`styleguide/`, a Flare project that installs as `Plugins\FlareAI\StyleGuide`.

- `Content/Resources/StyleGuide/Google/` — the Google developer documentation style guide, compiled into rules (one
  topic per Google page). Generated: don't edit; change Google rules from the Company folder.
- `Content/Resources/StyleGuide/Company/` — the team's rules. Company wins over Google.
- The open documentation project adds its own layer: variable values ("insert the variable instead of typing it"),
  glossary terms, and its own `Content/Resources/StyleGuide/` if it has one. It wins over both.

A team copies the starter project, keeps it in git, and points Settings → Style guide project at their clone.

### Writing rules (same format in both folders)
- **Bullets** are prose rules for the AI. Nest `Bad: …` / `Good: …` items under a bullet so the rule can be tested.
  Paragraphs are context, not rules.
- **Avoid / Use / Severity / Why / Example tables** become lint rules with one-click fixes. `Use` = `(delete)`
  deletes the text; empty `Use` only flags it. Comma-separated `Avoid` entries pair with the same number of `Use`
  entries. A table with **Pattern** instead of Avoid takes a regular expression, for formats words can't express.
- **Rule / Setting / Reason tables** turn rules off or change their level (`off`, `error`, `warning`, `suggestion`).
  Owners don't write these by hand: **Turn off rule…** in the review pane writes the row (for the team, in the
  guide) or a line in the project's `overrides.ini` (for one project). A turned-off rule stops both the linter and
  the AI.
- **Condition tags scope a rule** (a bullet, a table row, or the heading above a group): **Content** tags (Code, Steps,
  Lists, Numbers, Tables, Links, Headings, Images, UI, Notes, Examples, Math, API, Footnotes, Emphasis) send the rule
  to the AI only for sections with that content — a row tagged Headings is linted in headings only. **Product** tags
  limit a rule to products; the guide's Product tag set lists the products it covers, and rules for any other product
  (including Google's Android, Google Cloud and Google Workspace entries) never load.
- Every rule has a permanent id (`company:terminology/1`, `google:procedures/12`) that the AI must cite and that team
  feedback is keyed on. It's stored on the rule's bullet or row as `id="rule-…"`, invisible in Flare's normal
  editor. Validating as an owner gives new or copied rules their keys; the Google compiler carries keys forward
  when Google rewrites a page (matching rules by the source text they cover) and retires keys it never reuses.
  Feedback also records a fingerprint of the rule's wording, so Tune doesn't count decisions about an earlier
  wording against a rewritten rule.
- **House Style ▸ Validate style guide** tests the guide: each table row against its Example, contradictions, duplicates and
  overrides between Company and Google, override tables that name missing rules, and (on request) each prose rule's
  Bad/Good examples against the AI.

### What the AI gets per section
Each review call carries the instructions, the always-on rules (untagged), and only the rule packs the section's
content calls for (a procedure gets Steps, Lists and UI; a section with code gets the code pages). The always-on part
and the packs are separate cached blocks, so repeated checks pay for the guide once.

### Keeping the Google rules current
`tools/google-style/snapshot.py` copies developers.google.com/style (CC BY 4.0) into `tools/google-style/source`
with a hash per page; `git diff` shows what Google changed. `tools/FlareAI.GuideCompiler` recompiles changed pages
into `Google/` topics: every source unit must be covered by a rule or marked not applicable with a reason, and every
lint row must pass the validator. `tools/google-style/coverage.md` is the report.

### Teams: one guide, strict ownership
- Keep the guide in git; every writer clones it and points Settings → Style guide project at the clone. The pane warns
  when a writer's clone is behind ("Style guide … 2 change(s) behind: pull it").
- `FlareAI/owners.txt` in the guide lists the owners (git `user.email`, one per line). With owners listed, only they
  change shared rules (enforce it with branch protection); other writers' "Ignore rule…" becomes a request to them.
- `FlareAI/layer.txt` names the company layer (`company`): rule ids stay the same in every clone.
- `FlareAI/models.json` sets models, effort and the review unit for the team.
- Settings → Team feedback folder: a shared folder where each writer's review decisions append to their own file.
- **House Style ▸ Build style guide** (owners) suggests rules from evidence: **Mine this project** (spelling variants,
  competing words, capitalization, heading case, with counts and sentences) and **Add notes or an old guide**.
- **House Style ▸ Tune style guide** (owners) pools everyone's decisions: disputed rules, often-rejected rules, repeated hand
  edits, requests to turn rules off — with evidence; **Draft a change with AI** edits the rule as tracked changes.

### In the editor
Right-click items (Check style, Rewrite selection, Ask…) in Flare's own context menu; margin markers on paragraphs with
open suggestions (a stylesheet applied to the editor view only, never saved; blocks addressed by CSS2 sibling chains
because Flare's CSS engine has no `:nth-of-type`); a live style check a second after typing stops; an issue count after save;
and a project report when a build starts. All non-blocking, each with a setting.

### Outside Flare
`tools/FlareAI.Mcp` is the same engine as an MCP stdio server (`lint`, `style_lookup`, `glossary_lookup`,
`validate_guide`, `guide_examples`, `mine_style`). `.mcp.json` registers it for Claude Code, and the `docstyle` skill
(`.claude/skills/docstyle`) applies it.

## LLM-ready output
**House Style ▸ Export for LLMs** turns built HTML5 or Clean HTML output into one Markdown file per topic (`llm/…`, with
title/source/URL front matter) plus `llms.txt` (index) and `llms-full.txt` (everything) at the output root, so they
publish with the site. Built output already has snippets, variables and conditions resolved. Settings → "After each
HTML build…" does it automatically when you build in Flare. For `madbuild` / CI, add a post-build event to the target
(Target Editor ▸ Build Events):

```
"C:\Program Files\MadCap Software\MadCap Flare 22\Flare.app\Plugins\FlareAI\FlareAI.Mcp.exe" --llm-export "$(OutputDirectory)" --url https://docs.example.com
```

## Where things live

| Thing | Path |
|---|---|
| User settings (DPAPI-protected key, model) | `%APPDATA%\FlareAI\settings.json` |
| Diagnostics log | `%APPDATA%\FlareAI\diag.log` |
| Installed plugin | `C:\Program Files\MadCap Software\MadCap Flare <ver>\Flare.app\Plugins\FlareAI\` |
| API dump from the real DLL | `docs\api-dump.txt` (generated) |

## License

Code: MIT (`LICENSE`). Releases are signed through SignPath: see the [code signing policy](docs/code-signing.md). The Google style guide rules are derived from the
[Google developer documentation style guide](https://developers.google.com/style) under CC BY 4.0. See `NOTICE.md`.
Not affiliated with MadCap Software or Google.
