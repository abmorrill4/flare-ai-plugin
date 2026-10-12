# Gap 5: Markdown back into Flare

Status: plan, 2026-10-12, amended to comply with the design gate (`ux/plan-critique.md` 1.5, 2.7, 2.10, 5 "Gap 5").
Nothing in the repo besides this file was changed.

## Gate amendments (2026-10-12)

The design gate rejected the first version. This version changes:

1. **No new window and no ribbon button.** `MarkdownChangesForm`, `MarkdownLauncher` and the "Markdown..." ribbon button are gone.
   Results of "Paste Markdown as suggestions…" appear as suggestion cards in the review pane (`ux/review-deliberate.md` 4.2).
2. **One right-click home.** Two entries in the single "House Style ▸" submenu, no nested submenu: "Copy as Markdown" and
   "Paste Markdown as suggestions…".
3. **Milestone 0 is the mixed-operation spike**, with a stated go/no-go (4.7). If it fails, v1 ships "words only" (no inserts
   or removes). No silent split into several undo steps.
4. **v1 = Markdown engine + round trip.** Folder import, TOC writer, link map, admonitions, manifest and CLI moved to **v1.1**
   (section 8), using gap 2's verbs `housestyle md export|import` and exit codes 0/1/2/3. No `--md-*` flags.
5. **Pane vocabulary.** Kinds are Fix / Reword / Needs you. The card's write button is **Apply** (not "Accept"). Severities
   display as Error / Warning / Tip. The word "tokens" never appears in UI. "Held back", "Locked", "Conflict" and "Strict tokens"
   are replaced (3.2).
6. **"Strict tokens" option dropped.** The style-rule header is always included (capped at 25 lines), with no UI.
7. **Reject and accessibility** come from the pane (reason model, Undo link, keyboard and screen-reader behavior).
8. **Effort for v1 is about 19 days** (section 7). v1.1 about 11 days; v2 6 to 8 days.

## 0. Recommendation in one paragraph

Build **one Markdown engine** (a dialect, a small hand-written parser, a topic-to-Markdown writer, a Markdown-to-XHTML builder)
and ship v1 as **the Markdown round trip**: results land as suggestions in the review pane. Docs-as-code folder import is a
later front door on the same engine (v1.1, section 8).

- **The round trip is worth building, but as a thin layer, not a headline.** Our in-plugin AI is better than a chatbot round trip
  for the same job (no copy and paste, validated, scoped, tracked). The round trip earns its place in four cases the plugin cannot
  reach: (1) orgs whose policy allows Copilot or ChatGPT Enterprise but not a personal Anthropic key, (2) SMEs and reviewers who edit
  Markdown in VS Code or a phone and never open Flare, (3) offline or long-running edits, (4) multi-topic context in a chatbot.
  It is named **"Markdown"**, not "AI", in the UI (it is a different job from "Export for AI assistants…", 3.5). Its differentiator
  over AI Helper: the reply lands as **suggestions you apply as tracked changes**, merged **three-way** against the topic as it is
  now, **checked against the team's style guide**, never an overwrite.
- **Folder import is worth building** (the $179 Markdown plugin exists for it) but is deferred to v1.1: it has its own surface
  (window, More ▾ entry, CLI) and real unknowns around writing project files (section 8).
- **The engine is shared; the orchestration is not.** The round trip is "reconcile against an existing topic"; folder import is
  "create files, a TOC and link/image maps".

## 1. Goal and user stories (v1)

**Writer.** "I copied this topic to Copilot, it rewrote two paragraphs and added a step. I paste the reply back and see exactly
those edits as suggestions in the pane, with my variables, snippets, conditions and xrefs intact, and I can reject any of them."

**Reviewer / SME (no Flare).** "I get `install.md`, fix the facts in VS Code, send it back. The writer imports it and sees my edits
as suggestions."

**Style-guide owner.** "Anything that comes back from an outside tool is checked against our guide before it touches the topic."

v1.1 stories (docs lead migrating from docs-as-code, build engineer running a CLI in CI) are in section 8.

## 2. What the competitors do, and where we are better

| | Competitor | Us |
|---|---|---|
| AI Helper ($99): export Flare-aware Markdown, paste to chatbot, import the reply as "structured Flare content" | Unknown whether import is tracked; no published diff/merge behavior | Reply is aligned to the topic and shown as per-block suggestions with the word-level diff; **Apply** writes **tracked changes in one undo step**; unchanged blocks are never touched; edits made in Flare after the export survive (three-way) |
| | Prompts are the user's problem | Header tells the assistant the rules to keep and includes the **relevant slice of the team's style guide** (from `GuideScope`), so a Copilot-only org gets our rules too |
| | | Imported text is checked against the guide before applying |
| Markdown plugin ($179): Markdown <-> topics, TOC from folders, code as snippets, xrefs, conditions/metadata, preview editor | Own editor/preview UI (a lot of surface) | (v1.1) No editor of our own: Markdown lives in the user's editor. Folder-to-TOC, xref, code, image mapping; dry-run report, CLI for CI, re-sync as tracked changes |

We deliberately do not build a Markdown preview editor (surface we would have to maintain, and Flare already is the editor).

## 3. UX surface (v1)

Principle: **no new ribbon button, no new window, nothing to configure. Everything topic-level is a right-click away; every result
is a card in the review pane.**

### 3.1 Right-click in the editor (the only entry point)

Two entries in the single "House Style ▸" submenu (spec: `ux/plan-critique.md` 2.7), after a separator, one level only:

```
House Style ▸
  Check topic
  Ask AI…            Rewrite selection
  ───────────────
  Copy as Markdown   Paste Markdown as suggestions…
```

(Entries other than the two Markdown ones belong to other plans; shown for position only. Gap 4's probe layer decides which
entries exist on a given Flare version.)

- **Copy as Markdown** (selection if any, else the whole topic) writes the Flare-aware Markdown to the clipboard and shows a toast
  (`Toast.cs`): `Copied 14 blocks as Markdown. Bring the reply back with right-click > House Style > Paste Markdown as suggestions…`
  With a selection: `Copied the 3 selected blocks as Markdown. ...`
- **Paste Markdown as suggestions…** reads the clipboard. If it is not Markdown for this topic, the pane status line says
  `The clipboard doesn't hold Markdown from this topic. Copy the reply from your assistant and try again.` Nothing else happens.
  There is no "Open file…" window in v1; a reviewer's `.md` file is opened in any editor and copied (the file-picker path returns
  with v1.1).
- Both entries are disabled when no topic is open.

### 3.2 Results in the review pane (no new window)

Pasting runs the import (no model call; AI is not required) and fills the pane's **This topic** tab with suggestions carrying the
source chip **Markdown**. Each is a card exactly as in `ux/review-deliberate.md` 4.2: severity glyph column, source chip, a
"Why" line, the same keyboard model (J/K, A or Enter = **Apply**, R = Reject, Shift+R = reject with reason, O, `/`), same
screen-reader row announcement, same status-line live region. The pane header shows counts as `3 fixes · 1 reword · 2 needs you`.

Mapping of import results to pane kinds:

| Import result | Pane kind | Primary button | What the card says |
|---|---|---|---|
| Edited block, word-level plan succeeds | **Fix** | **Apply** | word-level diff of the block, "Why: your Markdown changed this wording." |
| Edited block, restructure (block replaced) | **Reword** | **Apply** | before/after of the block |
| Added block | **Reword** | **Apply** | the new text; "Adds a step after step 2." |
| Removed block | **Reword** | **Apply** | the removed text struck through |
| Block that cannot be written safely (formerly "Held back") | **Needs you** | **Go to** | "Flare can't redo this edit. Insert it yourself (Insert > Variable)." plus a Copy text action in ⋯ |
| Changed in both places (formerly "Conflict") | **Needs you** | **Go to** | "Changed in both places: kept yours." Shows both versions |
| Placeholder deleted for something not editable in Markdown (formerly "Locked") | **Needs you** | **Go to** | "A drop-down was removed in the Markdown. Not editable in Markdown, so it's still in the topic." |
| Token dropped or altered in an edited block (validator failure) | **Needs you** | **Go to** | "The reply changed something Flare needs, so this edit wasn't made." |

Severities display as **Error / Warning / Tip**. Import entries are Tips by default; a Needs you entry about a removed
not-editable item is a Warning. Style findings in the new text are ordinary pane suggestions (source chip Google / Our rules) from
`ActionRunner.Lint(proposedBlocks)` and appear in the same list.

**Fix all.** The pane toolbar's **Fix all (N) ▾** menu applies every Fix and Reword from the paste as one operation ("Write all as
tracked changes"). Needs you cards are never part of Fix all. Success status line: `Applied 3 changes as tracked changes. Ctrl+Z
undoes them all.`

**Reject.** Uses the pane's reason model (R = last reason, Shift+R = menu, Undo link in the status line). A rejected entry is
removed from the list and the topic is not touched. Reasons feed Review feedback like any pane suggestion.

**Keyboard and screen readers.** Inherited from the pane (checklist items 7 and 10); no checkbox grid exists. Row announcement
example: `Tip. Change "license key" to "product key". Markdown. 2 of 5.`

**Fallback.** If the spike (4.7) shows the pane cannot take insert/remove proposals, use the existing `ResultForm` with a list.
Never add a new window.

### 3.3 Settings and files

None added to the UI. No "Strict tokens". The style-rules header is always included, capped at about 25 lines. Conventions are
detected (4.8). An optional hidden `Content/Resources/StyleGuide/Company/markdown.json` can override the class map; nothing in the
UI mentions it unless detection fails, then the card says `Your notes use class "Hint"; treated as a note. Edit markdown.json to
change this.` Not a style rule, so it does not go in the guide tables.

### 3.4 Vocabulary (applies to every string in this feature)

| Never in UI | Use |
|---|---|
| Held back | Needs you: Flare can't redo this edit |
| Locked | Not editable in Markdown |
| Conflict | Changed in both places: kept yours |
| Accept | Apply (the card's write button) |
| tokens, Strict tokens | (not mentioned; say "variables, snippets and links" when naming things) |
| Markdown changes (window) | suggestions in the pane |
| Apply N as tracked changes | Fix all (N) ▾ > Write all as tracked changes |

### 3.5 Relationship to "Export for AI assistants…" (two-line user-guide text)

"Export for AI assistants…" (More ▾; the UI name for `--llm-export`) exports the **built output of the whole project**, with an
`llms.txt`, for a tool to read. "Copy as Markdown" copies **one source topic (or selection)** in an editable dialect so you can
bring the edits back as suggestions. Different jobs, different dialects, same plugin.

## 4. Design

### 4.1 Decision: write a small parser, do not vendor Markdig

- Markdig (current releases) targets `netstandard2.0/2.1`, `net462`, `net8.0` and later. `FlareAI.Core` is **net40**; a net40
  project cannot reference a net462-only package. Older Markdig releases did carry a `net40` target, but I have not verified which
  ones, and we would be pinned to an unmaintained version. Treat Markdig on net40 as **unavailable** unless a spike proves otherwise.
- We would have to extend it anyway (tokens, `:::` containers, trailing attribute lists, front matter) and then walk its AST back
  into XHTML. Roughly the same work as owning the parser, with a ~600 KB dependency and a licence notice (BSD-2).
- A CommonMark *subset* parser is 700 to 900 lines: ATX and setext headings, paragraphs, bullet and ordered lists (nested, loose
  and tight), fenced and indented code, blockquotes, thematic break, GFM pipe tables, front matter (YAML subset), `:::` containers;
  inline: emphasis/strong, code spans, links, images, autolinks, hard breaks, strikethrough, a whitelist of inline HTML (`<br>`,
  `<kbd>`, `<sup>`, `<sub>`), backslash escapes, tokens. Hand-writing also lets us record **source spans** per block, which the
  aligner needs. Test it against CommonMark spec examples filtered to the subset.
- Escape hatch: if the subset proves too small for real-world Markdown (reference-style links, footnotes, nested HTML), add
  those one at a time. The engine's AST is ours.

### 4.2 The dialect ("Flare Markdown")

GFM plus a small token set (internal term; never shown in UI). Rules for every token: (1) one obvious spelling, (2) survives a
chatbot (no HTML comments, which assistants strip; no underscores that Markdown would italicise; tolerant on read), (3) ignorable
by a plain Markdown renderer.

| Flare construct | Markdown spelling | Notes |
|---|---|---|
| Variable `<MadCap:variable name="General.ProductName"/>` | `{{var:General.ProductName}}` | Importer also accepts `\_` escapes and extra spaces (`{{ var: General.Product\_Name }}`), which chatbots produce |
| Snippet block `<MadCap:snippetBlock src="../Resources/Snippets/Note.flsnp"/>` | `{{snippet:../Resources/Snippets/Note.flsnp}}` alone in its paragraph | |
| Inline snippet `<MadCap:snippetText src=.../>` | same token, inside a paragraph | Position decides block vs inline |
| Xref `<MadCap:xref href="topic.htm#a">text</MadCap:xref>` | `[text](xref:topic.htm#a)` | Empty text = auto title: `[](xref:topic.htm)` |
| Plain link `<a href="https://x">` | `[text](https://x)` | |
| Bookmark `<a name="anchor"></a>` | `{#anchor}` at end of the heading line | Only emitted when something links to it or it exists |
| Condition on a block container | `:::condition Product.Pro,Default.Print` ... `:::` | One level of nesting; condition lists are comma-joined as in Flare |
| Condition on a list item, paragraph, row or inline span | trailing `{condition="Product.Pro"}` | Inline span: `[text]{condition="Product.Pro"}` |
| Paragraph/heading/span class or id | trailing `{.Note #id}` | Matched blocks keep their original attributes regardless (see 4.5); the list only matters for new blocks |
| Images `<img src alt/>` | `![alt](../Resources/Images/a.png)` | Width/height go in `{width=120}` |
| Index keyword / concept | `{{keyword:term}}` inline | `Parent:Child` kept |
| Anything we do not model (dropDown, toggler, popup, miniToc, relationships, complex tables, MadCap:conditionalText ranges, forms) | `{{locked:dropDown:a1b2}}` placeholder | **Not editable in Markdown** (internal name "locked"): content is *not* in the Markdown. The hash points at the nth such element of the topic. The importer restores it or, if the placeholder is deleted, shows a Needs you card (`a drop-down was removed`). Assistants cannot corrupt what they cannot see. v2 can open the common ones (dropDown, toggler) as editable `:::dropdown` |
| `<b>`/`<strong>`, `<i>`/`<em>` | `**x**`, `*x*` | Import re-uses whichever tag the original block used (Flare's editor writes `<b>`/`<i>`) |
| `<code>`, `<pre>` | `` `x` ``, fenced block with optional language | `<pre>` stays `<pre>`; language becomes a class only when new |
| Simple tables | GFM pipe tables | Colspan/rowspan/header-less/nested content => not editable in Markdown |
| Notes `<p class="Note">` | normal paragraph with `{.Note}` | Admonition syntaxes from other tools map to the same class in v1.1 (8.2) |

Front matter written by the exporter (the importer reads it; hand-written Markdown needs none):

```yaml
---
flare: 1                         # dialect version
topic: Content/Install/install.htm
base: 9c41e07a                   # hash of the exported blocks; keys the local snapshot (4.6)
scope: blocks 1-14               # or "selection 3-5", used to limit the import
title: Install the product
description: How to install on Windows.
---
```

Header comment for the assistant, always included (`<!-- ... -->` is fine here because losing it does no harm; no UI, no option):

```markdown
<!-- Edit the wording. Keep every {{...}} token, every ::: line and every {...} at the end of a line exactly as written.
     Reply with the whole file. Style rules for this topic:
     - Use "select", not "click". - Spell out numbers under 10. (3 of 9 rules shown; source: Company guide) -->
```
The rules come from `GuideLayer`/`GuideScope` for the exported blocks (always-on pack plus the packs the content triggers),
capped at about 25 lines. They are never hard-coded. (This header is the differentiator for Copilot-only orgs.)

### 4.3 Full example: export, edit, re-import

Topic (`Content/Install/install.htm`, abbreviated; pending tracked changes already resolved by `AcceptedView`):

```xml
<h1>Install <MadCap:variable name="General.ProductName" /></h1>
<p class="lead">Before you start, read <MadCap:xref href="requirements.htm#hardware">the requirements</MadCap:xref>.</p>
<MadCap:snippetBlock src="../Resources/Snippets/Prereqs.flsnp" />
<ol>
  <li>Run <code>setup.exe</code>.</li>
  <li MadCap:conditions="Product.Pro">Enter the <b>license key</b>.</li>
</ol>
<div MadCap:conditions="Default.Print"><p>See page 4.</p></div>
<p class="Note"><b>Note:</b> Restart afterward.</p>
<pre>msiexec /i setup.msi /qn</pre>
<MadCap:dropDown><MadCap:dropDownHead><MadCap:dropDownHotSpot>Troubleshooting</MadCap:dropDownHotSpot></MadCap:dropDownHead>
  <MadCap:dropDownBody><p>Check the log.</p></MadCap:dropDownBody></MadCap:dropDown>
<p><img src="../Resources/Images/setup.png" alt="Setup window" /></p>
```

Exported:

````markdown
---
flare: 1
topic: Content/Install/install.htm
base: 9c41e07a
scope: blocks 1-9
title: Install
---

# Install {{var:General.ProductName}}

Before you start, read [the requirements](xref:requirements.htm#hardware). {.lead}

{{snippet:../Resources/Snippets/Prereqs.flsnp}}

1. Run `setup.exe`.
2. Enter the **license key**. {condition="Product.Pro"}

:::condition Default.Print
See page 4.
:::

**Note:** Restart afterward. {.Note}

```
msiexec /i setup.msi /qn
```

{{locked:dropDown:a1b2}}

![Setup window](../Resources/Images/setup.png)
````

An outside editor changes step 2 ("Enter the **product key**."), adds a step 3 ("Select **Finish**."), and rewords the note.
Pasting the reply produces three cards in the pane (all source chip Markdown):

1. **Fix** on step 2: word-level `license` -> `product` (`WordDiff.Plan` succeeds: same elements, only text differs).
2. **Reword** (added): a new `li` "Select **Finish**." after step 2, written as a tracked insertion on Apply.
3. **Fix** on the Note: word-level change; `class="Note"` untouched.

Everything else is **not touched** (no spurious suggestions), including the drop-down, snippet, variable, condition and xref.
**Fix all (3) ▾ > Write all as tracked changes** applies all three in one undo step.

Losslessness has two meanings, tested separately:
- *Zero-edit round trip*: export then import with no edits => **no suggestions** and an unchanged DOM, byte for byte.
- *Rebuild round trip*: import with every block forced through the builder (`ImportOptions.ForceRebuild`) => XHTML canonically
  equal to the original for everything in the supported subset (the proof the dialect carries enough information).

### 4.4 Core / Flare split and the new Core namespace `FlareAI.Core.Markdown` (net40, no WinForms, no Flare types)

| Type | Job |
|---|---|
| `MdParser`, `MdBlock`, `MdInline`, `MdFrontMatter` | CommonMark-subset parser with source spans; tokens parsed into typed inline nodes (`VariableToken`, `SnippetToken`, `LockedToken`...) |
| `TopicMarkdown` | DOM -> Markdown: input blocks (already `AcceptedView`), output text **and a block table** (`MarkdownBlock { Index, Node, Markdown, Key }`) the aligner uses; walks `XmlNode`, not strings (so not a fork of `HtmlToMarkdown`, which scans built HTML and is lossy by design) |
| `MarkdownToXhtml` | AST -> `XmlNode`s imported into the target document, using a **template element** (the base block) for tag choice (`b` vs `strong`), classes and attributes; reuses `XhtmlFragment.ParseInto` for well-formedness |
| `BlockAligner` | LCS on normalised block text, then similarity pairing of the gaps (same kind, word-set Jaccard > 0.5) => Same / Edited / Added / Removed |
| `MarkdownImporter` | Orchestrates: parse, align, build, validate, word-plan, style-lint, produce `ImportProposal`: entries of kind Fix / Reword / Needs you, each with the data to preview and apply (internal statuses Ok / CannotWrite / ChangedBoth / Invalid map to 3.2) |
| `ImportSnapshots` | Per-machine store of exported Markdown keyed by `base` hash (`%LOCALAPPDATA%\HouseStyle\exports`, newest 50, 30 days) |

Flare side: `EditorIntegration` (two submenu entries), `FindingsPanel` (accepts proposals of kind insert/remove and the Markdown
source chip), `FlareDocumentAdapter` (3 small additions, 4.7). No new window, no `Plugin.cs` change. Nothing in Core references Flare.
The v1.1 types (`FolderImporter`, `TocWriter`, `LinkMap`, `AdmonitionMap`) are listed in section 8.

### 4.5 Import algorithm

1. **Parse** the Markdown; read front matter if present (`scope` limits which base blocks are in play; absent = whole topic).
2. **Base**: `AcceptedView.Of(BlockSelector.SelectBlocks(...))` of the live topic, written with `TopicMarkdown` => base table (live
   state).
3. **Three-way, when the snapshot exists** (`base` hash found in `ImportSnapshots`): align *imported vs snapshot* to get the
   assistant's edits; align *snapshot vs live* to map each edit to the live block. Edited live block unchanged since the export =>
   suggestion; changed in Flare *and* in Markdown => Needs you ("Changed in both places: kept yours", both versions shown); changed
   only in Flare => leave. No snapshot (other machine, a `.md` file from a colleague): two-way against live, with the single note
   `This Markdown wasn't copied from this machine, so changes you made in Flare since then may show as reverted. Check each one.`
   (shown once in the pane status line).
4. For each **Edited** block build the proposed element with the base element as template, then run what `Propose` already runs:
   `XhtmlValidator.Validate(view, proposed)` (fails closed if something Flare needs was dropped: `Dropped or altered:
   MadCap:variable[name=...]` is internal detail; the card says "The reply changed something Flare needs, so this edit wasn't
   made."), then `WordDiff.Plan(view, proposed, live)`. Non-null => word-level edits through the existing re-locating path (a Fix
   card); null => block replacement via `ReplaceBlocks` (a Reword card).
5. **Added** blocks: built fresh (class map, 4.8), validated for vocabulary, inserted with a tracked insert after the aligned
   predecessor. **Removed**: tracked remove. A not-editable-in-Markdown item is never removed silently: its removal is its own
   Needs you card.
6. **Needs you** (cannot be applied safely): any added or restructured block that contains a `MadCap:variable` (verified Flare 22
   behavior: tracked node ops that include a variable crash on Redo; `docs/flare-api-notes.md`), blocks with pending tracked
   changes (`TrackedChanges.HasPending`, same refusal text as `Apply`), and opaque blocks (`BlockSelector.IsOpaque`). Card text
   for the variable case is the honest-limit message in 6.2.
7. **Style check**: `ActionRunner.Lint(proposedBlocks)` on the changed text only; findings appear in the same pane list. No model
   call; AI is not required for import.
8. **Apply** (one card, or Fix all) in one operation: word edits (right-most first, as `ApplyFixes` does), then block replacements,
   inserts, removes. One Ctrl+Z. Gated by the mixed-operation spike (4.7).

Why block-level reconciliation instead of "convert everything and replace the topic": Markdown cannot carry every attribute, so a
full rebuild would silently drop classes, ids, styles and anything not editable in Markdown. Untouched blocks are never rebuilt,
so what Markdown cannot express is preserved by construction; the dialect only has to carry what the *edited* blocks need.

### 4.6 Why a snapshot and not only a hash

A hash only detects drift; it cannot repair it. Without the exported text the importer cannot tell "the assistant deleted this
sentence" from "the writer edited this sentence in Flare after copying". The snapshot is a small local text file; its absence
degrades to two-way with a visible note, never to an overwrite.

### 4.7 Milestone 0: the mixed-operation spike, and `IDocumentAdapter` / `FlareDocumentAdapter` changes

**Milestone 0 (1 day, before any other v1 work, in Flare 22).** In one operation, mix a word-level `ReplaceSpan`, a tracked
insert and a tracked remove; then Ctrl+Z, Ctrl+Y, Save, close and reopen the topic. Also confirm the pane can hold and apply
insert/remove proposals (3.2 fallback). Record results under "Verified write behavior" in `docs/flare-api-notes.md`.

**Go/no-go (stated up front).**
- **Go** = all of: one Ctrl+Z reverts every part; Ctrl+Y restores every part; after Save and reopen the tracked changes are
  present, attributable and individually rejectable in Flare's own review; no corrupt topic or exception in any step.
  Then v1 ships the full round trip (words, inserts, removes).
- **No-go** = any of the above fails or is flaky (fails in 1 of 3 repeats counts as fail). Then **v1 ships "words only"**: only
  Fix cards (word-level edits via `WordDiff.Plan`) are written. Added, removed and restructured blocks become **Needs you** cards
  ("Flare can't apply this edit yet. Copy text.") rather than being applied. We do **not** split into several undo steps
  silently, and we do not ship a mixed mode that undoes in pieces. Block insert/remove returns when the spike passes on a later
  Flare build.
- Either outcome is shippable; the engine, parser, snapshots and cards are the same.

Adapter changes (full-round-trip path):

- `Edit`/`ReplaceBlocks` become **re-entrant** (depth counter around `StartOperation/EndOperation`) so word edits and block ops
  share one undo step. `ActionRunner.ApplyFixes` is split into a core (no `Edit` wrapper) and the existing wrapper.
- New `InsertBlocks(op, parent, index, newNodes)` and `RemoveBlocks(op, nodes)`: `ReplaceBlocks` already calls
  `InsertDocumentNode`/`RemoveDocumentNode` tracked, so no new Flare API is used.
- `FakeDocumentAdapter` (tests) records them like `Replacements`.

### 4.8 Conventions (v1)

- **Class map** (detected once per project, cached): scan existing topics for `p.Note`, `p.Warning`, `p.Tip`, `p.Caution`,
  `div.note` etc. and the project stylesheet; default `Note`, `Warning`, `Tip`, `Caution`. Used when building new blocks.
- **Code**: fenced code => `<pre>` (language => `class="language-xyz"`).
- **Tables**: GFM => `<table>` with `<thead>`; column alignment is dropped with a note on the card.
- **Raw HTML in Markdown**: inline whitelist only (`br`, `kbd`, `sup`, `sub`, `code`, `b`, `i`, `a`); `<script>`, `<iframe>`, event
  attributes, and unknown tags are dropped and counted in the status line.
- **Input limits**: 2 MB of Markdown, UTF-8 with or without BOM, CRLF/LF normalised; all output XML built through the DOM
  (no string concatenation).

The docs-as-code conventions (file names, front matter to head meta, links, images, admonitions, TOC) are v1.1: section 8.2.

## 5. Files to add / change (v1)

Add (Core): `src/FlareAI.Core/Markdown/{MdParser,MdAst,MdFrontMatter,TopicMarkdown,MarkdownToXhtml,BlockAligner,MarkdownImporter,ImportSnapshots,MarkdownConventions}.cs`.

Change: `Editing/IDocumentAdapter.cs` (+InsertBlocks, RemoveBlocks), `src/FlareAI.Flare/FlareDocumentAdapter.cs` (re-entrant ops, two
methods), `Actions/ActionRunner.cs` (split `ApplyFixes`), `src/FlareAI.Flare/EditorIntegration.cs` (two entries in the shared
"House Style ▸" submenu), the review pane (`FindingsPanel`: accept proposals of kind insert/remove, source chip "Markdown",
Needs you cards from import entries, Fix all for a paste), `tests/FlareAI.Core.Tests/Fakes.cs`, `docs/flare-api-notes.md` (spike
results), `README.md` (feature line).

Not in v1 (removed by the gate): `FolderImportForm`, `MarkdownLauncher`, `MarkdownChangesForm`, `FolderImporter`, `TocWriter`,
`LinkMap`, `AdmonitionMap`, any `Plugin.cs` change, any `Program.cs` CLI change.

After using any new Flare API member: regenerate `ref/` stubs per `CLAUDE.md` (none expected: only `InsertDocumentNode`/`RemoveDocumentNode`, already in use or in the dump).

User guide (`docs/user-guide/Content/`): new `markdown.htm` (round trip with the full example, the two-line difference from
"Export for AI assistants…" in 3.5, dialect reference); update `llm-export.htm` (one sentence pointing to `markdown.htm`: "That
exports built output. To edit a topic outside Flare, see..."), `editor.htm` (the two right-click entries), `overview.htm`, and the TOC.

### Tests (`tests/FlareAI.Core.Tests/`, fixtures under `fixtures/markdown/`)

- `MdParserTests`: headings (ATX/setext), nested lists (tight/loose), fenced/indented code, GFM tables, emphasis edge cases
  (intraword `_`), links with parentheses, escapes, front matter, `:::` containers, trailing `{...}` lists, CommonMark spec cases
  filtered to the subset (data-driven).
- `TopicMarkdownTests`: golden `*.htm` -> `*.md` pairs, one per construct in the 4.2 table, plus pending tracked changes resolved.
- `TokenRoundTripTests`: variable, snippet (block and inline), xref with/without text/anchor, condition (container, li, inline),
  keyword, placeholder; tolerant read (`\_`, spacing, smart quotes inside tokens, `{{ VAR : x }}`); a deleted token => validator
  error `Dropped or altered`; a placeholder deleted => its own Needs you entry, not silent.
- `RoundTripTests`: `ZeroEditImport_ProducesNoSuggestions` and `ForceRebuild_IsCanonicallyEqual` over every fixture **and the
  250-topic bench corpus** (`tools/FlareAI.Bench`), canonical compare ignoring inter-block whitespace; the corpus run also asserts
  the share of blocks not editable in Markdown (< 10% target) so dialect gaps are visible.
- `BlockAlignerTests`: insert, delete, reorder, split one paragraph into two, merge, full rewrite (all Edited, none lost), duplicates.
- `ThreeWayTests`: assistant edits block A while writer edits block B (both produce suggestions); both edit A (Needs you, writer
  kept); snapshot missing (two-way note present); `scope: selection` limits suggestions to those blocks.
- `MarkdownImportApplyTests` (with `FakeDocumentAdapter`): same elements => word-level plan, no `Replacements`; restructure =>
  `ReplaceBlocks`; new block with a variable => Needs you; pending changes => refused with the `Apply` message; all ops under one
  operation name; words-only mode (spike no-go) turns inserts/removes/restructures into Needs you; lint findings reported.
- `PaneIntegrationTests`: import entries map to Fix / Reword / Needs you with Apply / Go to; Reject records a reason and removes
  the entry without touching the topic; Fix all excludes Needs you; row announcement text.

## 6. Risks, edge cases, open questions

1. **Assistants mangle tokens** (escape underscores, add spaces, change quotes, drop `:::`). Mitigations: tolerant reader, header
   instruction, validator fails closed, and the pane shows the drop rather than hiding it. Measure with a small fixed set of real
   chatbot replies kept as fixtures (collect during dogfooding).
2. **Variables and Redo (Flare 22)**: any tracked node op including a variable crashes Redo. Policy: a new or restructured block
   containing a variable is a Needs you card. For the one case that does write (a word-level edit inside a block that already
   holds a variable, where the variable is untouched), the card says: `You can undo this with Ctrl+Z but not redo it.` Re-test on
   the live Flare each release; if Redo is fixed, remove the limit.
3. **Mixed operation stability** (word edits + insert/remove in one operation) is unverified: milestone 0 (4.7) gates the full
   round trip; "words only" is the stated fallback.
4. **Block alignment errors** on heavy rewrites: a wrongly paired block produces a large, wrong-looking diff. The card always
   shows the diff before applying and Reject is one key; a "Treat as new / removed" item in ⋯ is cheap insurance.
5. **Ordinals of not-editable placeholders drift** if the topic gains a drop-down after the export; the 4-hex content hash
   disambiguates; a hash miss falls back to ordinal and shows a Needs you card.
6. **Dialect coverage**: Flare topics in the wild use `span` classes, nested lists with images, `MadCap:conditionalText`,
   numbered-step styles. The corpus test quantifies the not-editable share; widen the dialect by evidence, not guesses.
7. **Pane volume**: a large rewrite can produce many cards in a 185 px pane. Depends on the pane redesign (F-16, L2/L3: grouping
   and filter); the "Markdown" source chip makes the paste filterable.
8. **Encoding and line endings**: read UTF-8 with or without BOM, normalise CRLF/LF.
9. **Open questions for the owner**: (i) interest in exposing `topic_to_markdown` over MCP (cheap once the exporter exists)?
   (ii) v1.1 questions are in 8.4.

## 7. Effort and sequencing (v1)

| Milestone | Content | Effort |
|---|---|---|
| 0. Spike | Mixed-operation and pane-proposal spike (4.7), go/no-go recorded in `flare-api-notes.md` | **S, 1 day** |
| 1. Engine | `MdParser` + AST, `TopicMarkdown`, `MarkdownToXhtml`, dialect tokens, golden + corpus round-trip tests | **M-L, about 9 days** |
| 2. Round trip | Aligner, three-way + snapshots, importer + validator + word plan, adapter changes, pane integration (Fix / Reword / Needs you cards, Fix all, Reject), two right-click entries, user guide | **M-L, about 9 days** |

Total v1: **about 19 working days** for one engineer (1 + 9 + 9; pane integration is folded into milestone 2). Shippable as one
release: "Markdown round trip". Next: v1.1 about 11 days (section 8); v2 re-sync 6 to 8 days.
If the spike is a no-go, milestone 2 shrinks by about 2 days (no insert/remove adapter work) and v1 ships words only.

## 8. v1.1: docs-as-code folder import (deferred; not in v1)

Everything below was cut from v1 by the gate and is kept here so the design is not lost. Nothing here ships until v1 is out.

### 8.1 Entry points when built

- **UI:** one More ▾ entry, "Import Markdown folder…", opening a small window (`UI/FolderImportForm`, the only window this feature
  adds). Fields: **Folder** (browse), **Put topics in** (default `Content/Imported/<folder name>`), checkbox **Create a table of
  contents** (default on; creates a *new* `.fltoc`, see 8.2). **Preview** runs a dry run and lists topic count, images, link counts
  and warnings (`12 links point outside the folder`, `3 pages have no title: using the file name`). **Import** writes files.
  Result: `Imported 38 topics, 21 images, 1 table of contents (Imported-docs.fltoc). 2 links could not be resolved: see the list.`
  Re-running over an already-imported folder says `38 topics already exist. Re-sync them as suggestions instead?` (v2).
  The "Paste Markdown as suggestions…" entry may also gain a file picker here.
- **CLI (gap 2 verbs, no `--md-*` flags):**
  ```
  housestyle md export TOPIC.htm [--out FILE.md] [--no-rules]
  housestyle md import SRC_DIR --project FLARE_PROJECT_DIR [--into Content/Imported/x] [--toc Name] [--dry-run]
  ```
  Output JSON (`topics`, `images`, `warnings[]`, `unresolved_links[]`). Exit codes from gap 2: **0** clean, **1** issues found
  (for example unresolved links or warnings under `--strict`), **2** usage or input error, **3** internal or environment error.
  `md export` works on source topics (not built output; `housestyle export llm` stays as the alias for `--llm-export`, built
  output). CLI mode assumes Flare is closed.
- Stories: **Docs lead:** "I point the plugin at `docs/` from our GitHub repo and get topics, a TOC that follows the folders, xrefs
  for links between pages, code blocks, and images in Resources; I can re-run it and review the differences as suggestions."
  **Build engineer:** "`housestyle md import ... --dry-run` runs in CI against a checked-out Flare project and fails the job when
  links are unresolved."

### 8.2 Design

- **Types** (Core): `FolderImporter`, `TocWriter`, `LinkMap`, `AdmonitionMap`. Flare: `UI/FolderImportForm`.
  `tools/FlareAI.Mcp/Program.cs` gains the `md export|import` verbs (shared with gap 2's verb dispatcher).
- **Admonitions:** mapped on import: GitHub `> [!NOTE]`, MkDocs `!!! note`, Docusaurus `:::note`, bold-lead `**Note:**`, to the
  detected class map (4.8).
- **File names:** relative path -> kebab-case `.htm`, folder structure preserved under the target, collisions get `-2`.
  `README.md`/`index.md` become the folder's landing topic.
- **Topic skeleton:** `<html xmlns:MadCap=...><head><title/><meta name="description"/></head><body>`; `title` from front matter,
  else the first `h1`, else the file name. First `h1` stays in the body (Flare convention). Front matter `description`,
  `keywords`/`tags` become `meta description` and `MadCap:keyword` entries; other keys are listed in the report, not guessed at.
- **Links (`LinkMap`):** to another imported page => `MadCap:xref href="relative.htm#slug"`; to an anchor in the same page =>
  `a href="#slug"` with `<a name="slug"/>` bookmarks on the target headings (GFM slug rules); external => `a href`. Unresolvable
  => keep as text link and list in the report. Links that escape the source root are never followed or rewritten.
- **Images:** copied to `Content/Resources/Images/<folder>/`, `src` rewritten relative to the topic; missing file => warning, alt
  text kept. Only `.png/.jpg/.jpeg/.gif/.svg/.webp`; refuse anything else and any path that resolves outside the source root.
- **Code:** fenced code => `<pre>`. Saving blocks over N lines as snippets is not in v1.1 (see 8.4).
- **TOC (`TocWriter`):** a new `Project/TOCs/<name>.fltoc` file (CatapultToc/TocEntry shape; copy a real file's header from the
  test project at build time), order = front matter `nav_order`/`weight`/`order`, else numeric filename prefix, else alphabetical;
  folders become entries whose link is their landing topic when present. v2: read `SUMMARY.md` (GitBook) and `mkdocs.yml nav`.
  Never edit a `.fltoc` the user has open; always create a **new** TOC file and tell them to merge or set it as primary.
- **Manifest** `Content/Imported/<x>/.mdimport.json`: source relative path, topic path, source hash. Enables re-sync and the
  "already imported" message; no effect when absent.

### 8.3 v1.1 risks

1. **Writing project files while Flare is open:** unverified whether Project Explorer picks up new files in `Content` without a
   reload, and whether a `.fltoc` created on disk appears under TOCs. Spike on day 1 of v1.1.
2. **`.fltoc` and topic skeleton fidelity:** copy the exact header, attributes and encoding from a Flare 22 project (and check
   2020 = v16 per gap 4); do not write the shape from memory.
3. **Untrusted Markdown:** treat folder import as untrusted input (path traversal, giant files, hostile HTML, hostile image
   names). Limits: 2 MB per file, 2,000 files, allow-listed extensions, no path outside the root, all output XML built through the DOM.

### 8.4 v1.1 open questions and tests

Questions for the owner: (i) is `Imported/<name>` the right default target? (ii) save code blocks as snippets?

Tests: `FolderImportTests` (layout => topics + `.fltoc` golden XML, link rewrite xref/anchor/external/unresolved/outside root,
image copy and rewrite, `../` escapes refused, name collisions, README landing, front matter -> head meta, admonition mapping,
dropped unsafe HTML counted, manifest written, re-run detects existing files, exit codes 0/1/2/3 via the CLI entry) and
`MarkdownCliTests` (`md export` of a fixture equals the golden file; `md import --dry-run` writes nothing).

### 8.5 v1.1 effort and v2

| Stage | Content | Effort |
|---|---|---|
| v1.1 | Folder walker, links/images/admonitions/front matter, TOC writer, spike (8.3), folder window, More ▾ entry, `md export|import` verbs, tests, docs | **M, about 11 days** |
| v2 (only on demand) | Re-sync of mapped topics as suggestions, `SUMMARY.md`/`mkdocs.yml` TOC, editable drop-downs, code as snippets | M, 6 to 8 days |
