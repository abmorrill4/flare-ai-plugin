# FlareAI — Build & Test Plan

AI editing assistant for MadCap Flare Desktop. Phase 1 talks to the Anthropic API
directly; Phase 2 swaps the transport for the Bifrost gateway (virtual key, MCP
tools, agent mode) without touching the Flare-facing code.

Status legend: `[ ]` todo · `[~]` in progress · `[x]` done

---

## 0. Ground truth we are building on

What the Flare plug-in API gives us (verified against the Flare 2026 docs):

| Need | API |
|---|---|
| Entry point | `IPlugin` (`Initialize(IHost)`, `Execute()`, `Stop()`, `IsActivated`, `GetName/GetVersion/GetAuthor/GetDescription`) |
| UI surface | `IHost.GetNavContext().GetRibbon()` → `AddNewRibbonTab` → `AddNewRibbonGroup` → `AddRibbonButton(RibbonControlData)` (WPF ribbon; `RibbonControlData.Command` is an `ICommand`) |
| Active topic | `IHost.GetEditorContext().GetActiveDocument()` → `IDocument` |
| Read topic | `IDocument.GetXmlDocument()` (live `System.Xml.XmlDocument`), `GetDocumentXml()`, `GetSourceUrl()` |
| Read selection | `IDocument.Selection.GetText()`, `Selection.GetXmlNodeList()` (nodes the selection spans) |
| Write back | `IDocument.ReplaceDocumentNode(old, new, trackChanges)`, `InsertDocumentNode`, `RemoveDocumentNode` |
| Undo grouping | `IDocument.StartOperation(name)` … `EndOperation()` |
| Schema | `IEditorContext.GetMadCapSchema()` — list of MadCap element names, useful for validation |
| Constraint | Plugin DLL **must target .NET Framework 4.0** |

Unknowns that only the real DLL can answer (resolved by `scripts/dump-api.ps1`):

- Exact namespace/assembly of `RibbonControlData`, `ButtonCommand`, `IRibbonTab` etc.
- Whether `GetXmlDocument()` returns the live DOM (edits reflected) or a copy.
- Whether `ReplaceDocumentNode` requires the new node to be imported into the same `XmlDocument`.
- What `Selection.GetXmlNodeList()` returns for a selection inside a single `<p>` (text node? the `<p>`?).
- ~~Which CLR version~~ → Flare 22 API DLL targets 4.8; plugin targets net48.

## 1. Architecture

```
┌────────────────────────── Flare Desktop (WPF/WinForms, .NET 4.x) ──────────────────────────┐
│  FlareAI.Flare.dll  (net40)                                                                │
│   ├─ Plugin.cs            IPlugin: ribbon tab "AI" with 4 buttons, lifecycle               │
│   ├─ FlareDocumentAdapter IDocumentAdapter over IDocument/ISelection (thin, no logic)      │
│   └─ UI/                  AskForm, ResultForm, SettingsForm (WinForms, BackgroundWorker)   │
│              │ IDocumentAdapter + ILlmClient                                               │
│  FlareAI.Core.dll   (net40, no Flare references — builds & tests on any OS)                │
│   ├─ Editing/   IDocumentAdapter, BlockSelector, XhtmlValidator, XhtmlFragment            │
│   ├─ Actions/   ActionRunner (CheckStyle, Rewrite, Ask, Summarize) + PromptBuilder         │
│   ├─ Llm/       ILlmClient, AnthropicClient (Phase 1), BifrostClient (Phase 2)             │
│   └─ Settings/  PluginSettings (DPAPI-protected key, JSON in %APPDATA%\FlareAI)             │
└────────────────────────────────────────────────────────────────────────────────────────────┘
                                       │ HTTPS
                     Phase 1: api.anthropic.com/v1/messages
                     Phase 2: bifrost/v1/chat/completions (x-bf-vk, agent mode, MCP tools)
```

Design rules:

1. **Nothing in Core references Flare.** Core is unit-tested with a `FakeDocumentAdapter`
   fed real `.htm` topics from `tests/fixtures`.
2. **Every write to the topic is a tracked change** inside one `StartOperation/EndOperation`
   so the author reviews with Flare's native accept/reject and can undo in one step.
3. **The model never touches the DOM directly.** It returns an XHTML fragment; `XhtmlValidator`
   must pass before `ReplaceDocumentNode` is called. Fail closed.
4. **No `async`/`await`, no `HttpClient`, no `ValueTuple`** (net40). HTTP via `HttpWebRequest`,
   JSON via Newtonsoft.Json 13 (supports net40), threading via `BackgroundWorker`.
5. **Secrets** live in DPAPI-protected settings. Dev fallback: `ANTHROPIC_API_KEY` env var,
   loaded from the git-ignored `.env` at the repo root.

## 2. Phases

### Phase 0 — Bootstrap `[x]`

- [x] `scripts\bootstrap.ps1` → git init, locate Flare, copy `B3.PluginAPIKit.dll` to `lib\`,
      run `dump-api.ps1`, restore + build.
- [x] Read `docs\api-dump.txt`; fix any type/namespace mismatches in `FlareAI.Flare`.
      (All types in `B3.PluginAPIKit`; code matched. DLL targets net48 → plugin retargeted to net48.)
- [x] `scripts\smoke-api.ps1` (key from `.env`) → confirms the key and model id work from this box.
- [x] `dotnet test` green (26/26).

Exit criteria: solution builds, tests pass, we know the real shape of the ribbon types.

### Phase 1 — Hello Flare `[x]`

- [x] Deploy with `scripts\install-plugin.ps1` (needs admin — copies to `Flare.app\Plugins\FlareAI`).
- [x] Enable on File > Options > Plugins → **AI** tab appears immediately (no restart needed in Flare 22).
- [x] Button "Diagnostics" dumps: active doc path, selection text, selection node names,
      block nodes `BlockSelector` would pick → to `%APPDATA%\FlareAI\diag.log`.
- [x] Settings dialog: store API key (DPAPI), model, base URL. Verify round-trip.

Exit criteria: we can see the selection → block mapping is right on a real topic.

### Phase 2 — The three actions, direct API `[x]`

- [x] **Check style**: send selection (or whole topic if nothing selected) + style rules;
      show findings list in `ResultForm` (rule, excerpt, suggestion). Read-only.
- [x] **Rewrite selection**: prompt → model returns `<revised>…</revised>` XHTML →
      validate → replace block nodes as tracked changes.
- [x] **Ask…**: free-form instruction on the selection; same write-back path.
- [x] Round-trip safety: `XhtmlValidator` rejects dropped `MadCap:*` elements, changed
      snippet/variable/xref refs, unknown elements, namespace drift.
- [x] Error UX: network/auth failures surface in the dialog, never crash Flare.

- [x] Pending tracked changes: Check style omits deleted text; Rewrite/Ask refuse blocks with
      pending changes (Flare stores them as `MadCap:changes` ids → `head/MadCap:changeData`).
- [x] `docs\MANUAL-TESTS.md` pass: 18/20 ✅, 2 skipped (network-off, tool-strip mode) — see results table there.

Verified live in Flare 22 (2026-10-08): live DOM (edits reflected), `ReplaceDocumentNode` accepts
imported nodes, rewrite shows as Added/Deleted tracked changes, one Ctrl+Z undoes the whole edit,
selection inside a `<p>`/`<h1>` yields that element; inside an `<li>` maps to the whole `<ol>`.

Exit criteria: tester can run all three on their own project for a day without a crash.

### Phase 2b — docstyle: layered style guide + linter over MCP `[x]`

- [x] `mcp/docstyle`: Python MCP stdio server (official SDK, run via `uv`) with `lint`, `style_lookup`,
      `glossary_lookup`. Layers: Google developer documentation style guide (baseline: condensed guide +
      vendored errata-ai/Google Vale package) with the company guide + `Company` Vale style on top.
- [x] Plugin is an MCP client (`McpStdioClient`, net40): Check style = Vale lint (instant, exact spans,
      mechanical fixes) + model review for judgment issues only; findings must quote the topic verbatim.
- [x] Findings window (modeless): select a row → text highlighted in Flare; Fix / Fix all → word-level
      tracked changes, one undo step; Rewrite paragraph → model resolves that paragraph's findings; Guide ↗.
- [x] Rewrite/Ask: lint findings go into the prompt; output is linted and repaired once if needed.
- [x] `docstyle` Claude skill (`.claude/skills/docstyle`) and `.mcp.json` so Claude Code applies the same layers.
- [x] Rewrites land word-level: when the proposal keeps the structure, `WordDiff` turns it into word
      replacements applied like fixes (one undo step); restructures fall back to a block replacement.
- [x] Rewrites over pending tracked changes: the model sees the text as the author reads it (`AcceptedView`),
      edits land word-level around the pending changes, which stay pending. Only restructuring such text is refused.

### Phase 2c — Close the gaps (benchmarked against Markup AI's documented limitations) `[~]`

Order matters: 1 unblocks the company-guide decision; 2 is the biggest UX change; the rest are independent.

1. `[x]` **Configurable style layers + project layer.** Google (built in) → any number of layer folders
   (`guide.md` + `rules/*.yml` + `accept.txt`; network share, git checkout, or inside a Flare project) →
   optional **project layer generated from the open Flare project**: variable values ("type the variable,
   not the text"), glossary terms (canonical spelling, definitions for the AI, spelling vocabulary),
   `Resources/StyleGuide/` if present. Settings: ordered layer list + "Use this project's terms" toggle.
   Same config format for the plugin, Claude Code, and the gateway.
2. `[x]` **Docked sidebar.** Host the findings list as a native Flare pane via B3.AppKit (`IPaneDock`/`Pane`/
   `IPaneView`; internal, not public API) behind one adapter; fall back to the floating window if Flare
   changes. Done: native pane (Flare's own dock/float/close), responsive layout; Flare places it with the left
   panes and remembers where the author drags it. Results following the active topic is item 7.
3. `[x]` **Tool Strip mode** (Markup AI has it, we don't): `CreateCustomToolBar` buttons for the same actions.
4. `[x]` **Mark of the Web** (Markup AI's #1 install issue, same for us): installer runs `Unblock-File`;
   tester guide says so. (A blocked plugin never loads, so in-plugin detection is moot.)
5. `[ ]` **Variables resolved** (Markup AI: not checked). Lint sees variable values in context (sentence rules
   work across them); findings inside a value point to the variable set, not the topic; the AI review gets
   the values. **Insert variable** fix for project-variable findings: replace the typed text with the
   `MadCap:variable` (tracked; the validator allows introducing only variables that exist in the project).
6. `[ ]` **Snippets checked** (Markup AI: not checked). Check style includes referenced snippets' text,
   labelled "in snippet X"; Fix opens the snippet and applies there, tracked.
7. `[ ]` **Results that survive** (Markup AI: ~20 topics, lost on rename). Lint re-runs instantly on topic
   switch; AI review cached by file path + content hash (rename-proof); dismissed findings remembered.
8. `[ ]` **Project / TOC check** (Markup AI: one topic only). "Check project": lint every topic (no AI cost),
   report grouped by topic → open topic + go to finding; optional AI review per topic on demand.
9. `[ ]` **Dismiss / ignore** (Markup AI has it): dismiss a finding; "ignore rule for this project" writes
   to the project layer.
10. `[ ]` **Finer blocks for tables and lists**: send the row/item, not the whole table, when the selection is
    inside one cell/item.
11. `[~]` Manual tests: 20 (Tool Strip) ✅; 17 (network off) still open.

Won't fix: Flare Online (Desktop plugin; docstyle MCP + skill cover repo-based authoring); plugin enable
is per Windows user (Flare behavior; documented).

Already better than Markup AI: fixes and rewrites are tracked changes (theirs are not); selection checks;
pending changes preserved; no WebView2/SaaS dependency; same rules in Claude Code and the gateway.

### Phase 3 — Tester handoff `[ ]`

- [x] `scripts\package.ps1` → `dist\FlareAI-<ver>.zip` with install instructions.
- [x] Write `docs\TESTER-GUIDE.md` (install, enable, enter key, the three buttons, how to
      report problems, where `diag.log` is).
- [ ] Feedback loop: 1-week trial, collect findings in `docs\FEEDBACK.md`.

### Phase 4 — Bifrost integration `[ ]`

- [ ] `BifrostClient : ILlmClient` → `POST {gateway}/v1/chat/completions`, header `x-bf-vk`,
      non-streaming, optional `x-bf-mcp-include-tools`.
- [ ] Gateway side: register style-guide + linter MCP as a STDIO client on the gateway host;
      mark its read-only tools `tools_to_auto_execute`; create a tester virtual key scoped to
      the doc model + that MCP client.
- [ ] Settings dialog gets a "Provider" switch (Anthropic direct / Bifrost).
- [x] Rules move from embedded markdown to MCP tool calls (`style_lookup`, `lint`) — done locally in Phase 2b;
      on Bifrost the same server is registered on the gateway instead of spawned by the plugin.
- [ ] Remove the direct Anthropic path once Bifrost is stable (or keep behind a flag).

See `docs\bifrost-migration.md`.

## 3. Test strategy

| Layer | How | Where it runs |
|---|---|---|
| Core unit | xunit, `FakeDocumentAdapter`, fixture topics | Mac or Windows, `dotnet test` |
| Validator property tests | Feed every fixture through Rewrite with a mock LLM that returns the input unchanged → must be a no-op; mock that drops a `MadCap:snippetText` → must be rejected | `dotnet test` |
| Prompt snapshot | Prompt builder output for each action is snapshotted; diffs are reviewed | `dotnet test` |
| API smoke | `scripts\smoke-api.ps1` (real call, tiny prompt) | Windows, `.env` key |
| Flare integration | Manual script in `docs\MANUAL-TESTS.md`: 12 scenarios (selection inside p, across two ps, inside a list item, inside a table cell, inside a dropdown, no selection, topic with snippets/variables/conditions, non-topic file open e.g. CSS, no doc open, bad key, network off, cancel mid-request) | Windows + Flare |
| Tester trial | One writer, one week, real project | Their machine |

## 4. Repo layout

```
flare-ai-plugin/
  PLAN.md                      ← this file
  README.md                    ← quick start
  CLAUDE.md                    ← working notes for Claude Code sessions on the Windows box
  FlareAI.sln                  ← created by bootstrap.ps1
  Directory.Build.props
  lib/B3.PluginAPIKit.dll      ← copied from Flare install, git-ignored
  rules/                       ← style rules shipped with the plugin (Phase 1–3)
  src/FlareAI.Core/            ← net40, no Flare refs
  src/FlareAI.Flare/           ← net40, the plugin
  tests/FlareAI.Core.Tests/    ← net48, xunit
  tests/fixtures/              ← real-shaped Flare topics
  scripts/                     ← PowerShell tooling
  docs/                        ← api-dump.txt, flare-api-notes.md, bifrost-migration.md, manual tests
```

## 5. Risks

- **Ribbon types are WPF.** The plugin must reference PresentationCore/PresentationFramework/
  WindowsBase/System.Xaml. If `RibbonControlData` lives somewhere unexpected, fix after the dump.
- **Ribbon mode only.** Tab is invisible in "Tool Strip" UI mode — add a context-menu fallback
  (`IDocument.GetUserPendingContextMenuItems`) in Phase 2 if the tester uses that mode.
- **Selection granularity.** If `GetXmlNodeList()` returns text nodes, `BlockSelector` walks up
  to the nearest body-level block. If it returns nothing for a caret, fall back to whole topic.
- **Live DOM vs copy.** If `GetXmlDocument()` is a copy, `ReplaceDocumentNode` must be fed
  nodes obtained from the same call that will be replaced; the adapter hides this.
- **Plugins folder is under Program Files.** Tester needs admin or IT to copy the folder.
- **Model id drift.** Default model is a setting; smoke-api validates it before handoff.
