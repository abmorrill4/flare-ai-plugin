# Planning brief (shared by every planning and review agent)

## Product
**House Style** is a MadCap Flare Desktop plugin (repo `E:\flare-ai-plugin`, internal names `FlareAI.*`). It checks topics
against the full Google developer documentation style guide plus a team's own rules (one Flare project:
`styleguide/`), proposes fixes as Flare tracked changes in one undo step, and integrates into Flare's editor (ribbon tab,
review pane, right-click items, margin markers, live check, check on save, report before build). AI uses the writer's
own Anthropic key. Read `CLAUDE.md` (hard constraints), `PLAN.md` (architecture), `README.md`, and
`docs/user-guide/Content/*.htm` (what users see) before planning.

Key code: `src/FlareAI.Core/Style/*` (StyleEngine = guide + linter + validator; GuideLayer loads rule tables;
StructureCheck = fixed structural checks), `src/FlareAI.Core/Actions/*` (ActionRunner, prompts, feedback),
`src/FlareAI.Flare/Plugin.cs` (ribbon), `src/FlareAI.Flare/EditorIntegration.cs`, `src/FlareAI.Flare/UI/*` (WinForms
dialogs, FindingsPanel = review pane), `tools/FlareAI.Mcp` (MCP server + `--llm-export` CLI), `tests/FlareAI.Core.Tests`.

## Direction from the owner
- One high-quality plugin. No pricing tiers, no feature gating.
- **Wide capability without drowning the user.** The competitor's free "Kaizen" plugin has 40+ buttons and is
  described as overwhelming. Every new capability must say where it lives, how a writer discovers it at the moment
  it's useful, and what stays hidden until needed (progressive disclosure, defaults that just work, contextual entry
  points over new ribbon buttons). Adding a ribbon button needs a strong reason.
- Quality interaction patterns matter as much as features.

## Competitor reference (Improvementsoft, verified 2026-10-12)
- **Mad Quality ($199/yr)**: rules in an Excel sheet. Columns: Name, Enabled, Description, Message (`%s`/`%t` = match /
  suggestion), Type (Assert = violation if pattern absent, Report = violation if present), Context (CSS selector or
  `xpath:...`), IgnoreContext, Function (None, Regex on inner text, RawRegex on raw XML, RegexInline, RegexElementOnly,
  Word `bad:good`, ListOfWords, FileNameRegex, Readability <score>), IgnoreCase, Parameters, Score (penalty),
  FileFilters (`*.htm,*.flsnp`). Special rules (e.g. `check-heading-levels`), `:emptybreaking` pseudo-selector,
  `.mqignore` files, ignore a violation by right-click, violations cached 30 days. CLI: `MadQualityCommander.exe
  <folder> <rules.xlsx> <log.txt> [/i subdirs] [/xml] [/score = exit code is average score]`. Shares rules via OneDrive.
  "Copy for LLM" copies a violation as a prompt. Supports Flare 2020+.
- **AI Helper ($99/yr)**: export topics as Flare-aware Markdown (variables, snippets, conditions preserved), paste
  into any chatbot, import the reply back as structured Flare content; also llms.txt from build output.
- **Markdown plugin ($179/yr)**: Markdown <-> Flare topics, TOC from folders, code samples as snippets, links as
  xrefs, conditions/metadata, built-in preview editor.

## The five gaps to plan
1. User-defined **structural rules** (selector-targeted, assert/report) in our guide format.
2. A **command-line checker** for build pipelines (exit codes, machine-readable output).
3. **Readability**, **file-name** rules and **per-rule severity/penalty scoring**.
4. Support **older Flare versions** (2020 = v16 … 2026 = v22). Note: Flare's B3 assemblies are not strong-named, so
   .NET Framework ignores the referenced version when binding; the open question is which API members exist in each
   version. `ref/` holds stubs of the members we use (`tools/FlareAI.RefStubs`).
5. **Markdown back into Flare** (we only export today: `src/FlareAI.Core/Output/LlmExport.cs`).

## Plan template (every plan file uses it)
1. **Goal and user stories** (writer, style-guide owner, docs lead / build engineer).
2. **What the competitor does and where we should be better** (not just equal).
3. **UX surface**: exactly where it appears (ribbon? pane? right-click? settings? guide tables? CLI?), how a writer
   discovers it when it's relevant, what's hidden by default, copy for every new label/message. Justify any new
   ribbon button or setting; prefer none.
4. **Design**: data formats (show examples of the guide-table rows / file formats), algorithms, how it fits StyleEngine /
   GuideLayer / Validate / FeedbackLog / rule ids, Core vs Flare split (Core never references Flare or WinForms).
5. **Files to add/change** (paths), **tests** to add (names + what they assert), docs/user-guide topics to update.
6. **Risks, edge cases, open questions**; **effort** (S/M/L with rough days) and **sequencing** (what ships first).
Keep plans concrete and buildable; no code beyond short illustrative snippets.
