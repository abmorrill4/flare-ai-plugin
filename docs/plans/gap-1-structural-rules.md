# Gap 1: User-defined structural rules

## Gate amendments (2026-10-12)

Applied from `docs/plans/ux/plan-critique.md` sections 2 and 5 (Gap 1) and the glossary in `review-deliberate.md` 5.1:
- Rules are rows of the shared **Checks** table (`Check | Where | Test | Message | Severity | Why | Bad | Good | Except | Files`, `Check = structure`); `Selector` is now `Where`, `Type` is now `Test`; detection is the exact headers `Check`+`Where`+`Test`; the table-collision risk is gone.
- Suppression cut to Rule/Setting, `Except`, `Files` (plus the existing Reject reasons and "Turn off this rule..."). Removed the `hs-ignore` class, "Ignore this once", and the nonexistent right-click items.
- Selector subset frozen (no `~`, `:nth-*`, `:text`, `:first-text`); regex and ReDoS text removed.
- StructureCheck migration moved to v1.1; Foundations task added to give existing structure findings final RuleIds now.
- Glossary applied: severities display Error / Warning / Tip (stored `suggestion` valid, `tip` accepted); "suggestion" in the pane, "issue" for project/CLI counts; primary write button is "Apply" (not "Accept"; structural suggestions are flag-only so their button is "Go to"); source chip "Our rules"/"Google" and group "Topic" replace the "Structure" group.
- Positions come from the shared `SourceMap` on the file path only; the slow-rule guard reports to Validate and Diagnostics; `FeedbackIgnoresChecksRows` test added; fixes and AI reword deferred; effort is Ship 1 only, 5 days.


## 1. Goal and user stories

Let a style-guide owner write rules about document *structure* ("a heading inside a `<p>`", "topic without exactly one h1",
"image without alt", "note div must start with a bold label", "no empty paragraphs", "heading levels skip", "table without
header row") as rows in a Flare topic, in the same guide-table format as every other rule, tested by Validate, and shown to
writers through the findings surfaces they already use.

- **Writer**: sees structural problems in the margin markers / pane / report with a plain-language message, like word
  rules. Never sees selectors. Can jump to the element with Go to.
- **Style-guide owner**: adds a row, fills Bad/Good, hits Validate, and knows the rule works before any writer sees it. Can scope
  by condition tag, turn a built-in off or change its level in the existing Rule/Setting table, and exempt an element or file.
- **Docs lead / build engineer**: the same rules run in the bench, the MCP server and (gap 2) the CLI because they live in Core
  and in the guide, not in C#. Structure findings carry rule ids, so they are countable and gateable.

## 2. Competitor and where we should be better

Mad Quality: an Excel row per rule with Context (CSS or `xpath:`), IgnoreContext, Type (Assert = fail if absent, Report = fail
if present), Message, Score, FileFilters, plus a few hard-coded specials (`check-heading-levels`, `:emptybreaking`), ignored via
`.mqignore` or right-click. Weaknesses we can beat:

1. **No test of the rule.** Ours: Bad must trigger, Good must not, run by Validate. This is the main differentiator.
2. Rules live in a spreadsheet, off to the side. Ours are Flare topics in the guide project: versioned with the docs, cite-able
   ids, condition tags, the same Rule/Setting overrides, feedback and mining.
3. Specials are opaque code. Ours are *built-in rows in the same format* (owners can read, copy, retune or switch off).
4. Writers get a plain-language suggestion with a Go to button; mechanical fixes (empty paragraphs) follow in v1.1 (4.6).
5. One finding model: structural and word findings share markers, pane, Reject / Turn off this rule, feedback log (they do not today for
   `Structure.*`, which has no rule id; the Foundations task assigns final ids).

## 3. UX surface

**No new ribbon button, no new setting, no new pane.** Everything reuses existing surfaces. Terms follow the glossary: **suggestion** is an item in the pane, **issue** is the project/CLI count, severities display as Error / Warning / Tip.

| Who | Where | What they see |
|---|---|---|
| Owner | A topic under `Company/` (e.g. `Structure.htm`) with rows of the shared Checks table (`Check = structure`) | Table format in 4.2. A starter topic ships in the template guide with 4 commented examples at Tip severity. |
| Owner | Existing **Validate guide** (owner menu) | New messages per structural row: "The Bad example doesn't trigger this rule", "The Good example triggers this rule", "Where isn't valid: ... near `h1 >> p`". Counts "N structural rules, M tested by example" in the report line. |
| Owner | Existing Rule/Setting table (`Company/Google-overrides.htm`) | `Structure.AltText, warning, our product has decorative images` works for built-ins exactly as for word rules (`off`, Error, Warning, Tip). |
| Writer | Margin marker, pane, Check topic / project, report before build | A structural finding is a normal suggestion card with a plain-language message and severity colour. Source chip is "Our rules" (or "Google"); topic-scope rules sit in group "Topic". The primary button is **Go to** (selects the element's text; for elements with no text the nearest text, today's behavior). Never shows selectors. |
| Writer | Card **Reject ▾** reasons and card overflow **Turn off this rule…** / **Suggest changing this rule…** (IA 4.3) | The real, existing suppression surfaces; see 4.9. No right-click items are added by this plan. |

Discovery: writers do nothing; suggestions appear once the owner adds rows. Owners discover it from the template guide's
`Structure.htm`, which is loaded and validated like any other topic, and from the user-guide topic "Write structural rules".
Hidden by default: selector syntax, the `xpath:` escape hatch, the `Except`/`Files` columns (all optional; a row
with only Check, Where, Test works).

New user-visible strings (the only copy this feature adds):
- No new pane group title: structural suggestions use the source chip "Our rules" (or "Google"), and group "Topic" for topic-scope rules.
- Default fallback message when the Message cell is empty: Report: "This {element} matches a structure rule." Assert:
  "This {scope} is missing a required {what}." (Validate warns that an empty Message is unhelpful.)
- Validate errors: "Where isn't valid: {reason}", "Example (Bad) doesn't trigger this rule: '{markup}'", "Example (Good) triggers this rule: '{markup}'", "Assert scope '{scope}' matches nothing in the Good example, so the assertion proves nothing."
- No new ignore labels: suppression uses "Reject", "Turn off this rule…" and the Rule/Setting table.

## 4. Design

### 4.1 Selector language: recommendation

**Recommend: a CSS-selector subset compiled to XPath 1.0, with `xpath:` as a documented escape hatch inside the same `Where` cell.**
Not XPath-only, not CSS-only.

Why:
- Writers are not programmers but *are* Flare users: Flare's own stylesheets and Mad Quality use CSS selectors, so `p.note`,
  `div.note > p:first-child`, `h2 + h4` are familiar. XPath (`//p[not(normalize-space())]`) is not.
- CSS-only cannot express "topic without exactly one h1" (counting), "heading levels skip" (relative order), "table without a
  header row" (negative descendant) reliably. XPath can, and `System.Xml.XPath` is in the net40 BCL (no dependency).
- Compile-to-XPath means one evaluation engine (`XmlNode.SelectNodes`), one set of semantics, and the escape hatch is free.
- Rejected: a third-party CSS parser (new dependency, net40 fit, trust); our own DOM matcher (a second engine to test).

**Frozen v1 subset** (small on purpose; Validate rejects anything else with a precise message, so no silent mismatches; anything else is `xpath:`):
`tag`, `*`, `.class`, `#id`, `[attr]`, `[attr=v]`, `[attr^=v]`, `[attr$=v]`, `[attr*=v]`, `A B` (descendant), `A > B`, `A + B`
(adjacent sibling), `,` (union), `:first-child`, `:last-child`, `:not(simple)`, `:empty`, `MadCap|name`, plus our additions:

| Pseudo | Meaning |
|---|---|
| `:blank` | no text and no `img`/`MadCap:*` content (the "no empty paragraphs" case; ignores `<br/>` only content, which is how Flare writers get empty lines) |
| `:has(sel)` | contains a match (compiles to `[.//sel]`) |

Not in the subset: `~`, `:nth-child`, `:nth-of-type`, and any text-matching pseudo (`:text(/regex/)`, `:first-text`). Text-pattern checks belong to word rules, which keeps regex (and its ReDoS risk) out of structural rules.

Namespaces: `MadCap|xref` (CSS namespace syntax, familiar from Flare) compiles to `*[local-name()='xref' and namespace-uri()='http://www.madcapsoftware.com/Schemas/MadCap.xsd']`,
tested inline so no `XmlNamespaceManager` needs to be attached to each document. Plain tag names compile to `*[local-name()='p' and not(namespace-uri())]`
(HTML elements in Flare topics have no namespace; this matches how `StructureCheck` and `TextRuns.IsLiteral` already treat them).
Class matching compiles to `contains(concat(' ', normalize-space(@class), ' '), ' note ')`.

Level-skip checks that need relation across siblings are written as XPath (`xpath: //h4[not(preceding::h3 or preceding::h2)]`),
or shipped as built-in rows (4.4). Do not grow the CSS subset to cover them.

Both forms live in the one `Where` cell: `table:not(:has(th))`, or `xpath:count(//h1) != 1`. The prefix `xpath:` is the only
syntax a writer must remember, and the template guide's examples show it.

### 4.2 Rule-table format: structure rows of the shared Checks table

Structural rules are rows of the one shared **Checks** table (plan-critique 2.1), not a separate table kind. Header, exact names, in order:
`Check | Where | Test | Message | Severity | Why | Bad | Good | Except | Files`. Only `Check`, `Where`, `Test` are required; for structure rows `Check = structure`, `Where` is the selector (what to look at), `Test` is how to test it. Owners can keep these rows in their own topic (`Structure.htm`); same table, different topic.

| Check | Where | Test | Message | Severity | Why | Bad | Good | Except | Files |
|---|---|---|---|---|---|---|---|---|---|
| structure | `img:not([alt]), img[alt='']` | Report | Add alt text, or set role="presentation" if the image is decorative. | error | Screen readers announce the file name otherwise. | `<p><img src="a.png"/></p>` | `<p><img src="a.png" alt="Payee list"/></p>` | `img[role=presentation]` | |
| structure | `xpath:count(//h1) != 1` | Report topic | A topic has exactly one h1, its title. | warning | | `<h1>A</h1><h1>B</h1>` | `<h1>A</h1>` | | |
| structure | `div.note > p:first-child` | Assert `b:first-child` | Start a note with a bold label such as **Note:**. | suggestion | | `<div class="note"><p>Careful</p></div>` | `<div class="note"><p><b>Note:</b> Careful</p></div>` | | |
| structure | `p:blank` | Report | Delete this empty paragraph; use spacing in the stylesheet. | warning | | `<p></p>` | `<p>Text</p>` | | |

Starter rows ship at `suggestion` (displayed Tip) so a new project's pane is not flooded (plan-critique 2.8).

Semantics (identical to Mad Quality so migrating users are not surprised, and stated once in the table intro):
- **Report**: every element matching *Where* (and not matching *Except*) is a suggestion.
- **Assert** `<sel>` in the Test cell: every element matching *Where* is a suggestion **unless** it contains/has a match for `<sel>`
  (relative to the element). Test cell grammar: `Report` | `Report topic` | `Assert <relative selector or xpath:...>`.
  Chosen over a separate "Assert" column so the common Report row stays three cells.
- **Report topic** (topic-level rules, "exactly one h1"): `Where` is an `xpath:` expression that evaluates to a boolean or a node-set;
  boolean true = a suggestion on the body (anchored on the first h1 or the title), shown under group "Topic".
  Without `topic`, `Where` must return nodes, otherwise Validate errors ("Where returns true/false; use 'Report topic' in Test").
- **Severity** reuses `GuideLayer.Severities`; empty = warning. Displayed as Error / Warning / Tip; the stored token `suggestion` stays valid and `tip` is accepted as an alias. **Why** is appended to the message like word rules. **Except** =
  Mad Quality's IgnoreContext (elements to skip).
- **Bad/Good**: XHTML fragments typed as text in the cell (Flare writers type `<p></p>` as literal characters;
  `InnerText` gives the markup back). Validate parses them as `<body>` children. Bad must trigger, Good must not (same meaning as every other Check).
- **Conditions**: row-level and table/heading-level `MadCap:conditions` already flow through `Conditions()` / `Scope()` into
  `Applies` / `Products`. For structural rules `Content.*` tags are not meaningful (a rule
  scoped Content.Tables is just a `table` selector), so only `Product.*` is honored; `Content.*` on a structural row is a
  Validate warning "Content tags don't scope structural rules; use the selector."
- **Files**: glob list (`Topics/Concepts/**`, `!pattern` to exclude), matched against the path relative to Content.
- Ids follow the existing convention: row element `id="rule-<key>"` -> `company:structure/<key>`; `AssignKeys` already
  covers it because we create the item through `AddItem(s, tr, ...)`. No `Name` column: display names are generated like today (`Company.StructureK3x`); owners rename via Rule/Setting.

### 4.3 Data model and engine integration

Core, namespace `FlareAI.Core.Style`:

- `StructRule` (new class, sibling of `LintRule`): `Id, Name, LocalName, Layer, Severity, Why, Link, Message, Selector (compiled XPath), AssertXPath, TopicScope, Except (compiled), Files, Applies, Products, File, Row, Bad, Good, SelectorText`. Held in `GuideLayer.StructRules`, aggregated to `Composition.StructRules`.
- `SelectorCompiler` (new static class, ~250 lines): CSS subset -> XPath string, throws `SelectorException(position, message)`; `xpath:` prefix passes through `XPathExpression.Compile` for validation. Pure string work, no DOM. Heavily unit-tested table-driven.
- `StructureEngine` (new): `List<StyleFinding> Check(IList<XmlNode> blocks, Composition c, string relativePath)`. Per rule: `doc.SelectNodes(xpath)` once per rule per topic, not per block (see perf 4.8), then filter to nodes inside `blocks` (supports "check selection", where blocks is a subset), and exclude Except matches and ignored nodes (4.9).
- `Composition.Structure(blocks, path)` wraps it: applies product scope, the `Override(...)` machinery (generalize `Override(LintRule)` to take id+name so StructRule shares it), and ranks layers (later layer wins when two rules flag the same element: dedupe on (element, rule id)).
- `IStyleService` (the Flare side talks to this interface) gets one method `IList<StyleFinding> Structure(IList<XmlNode> blocks, string project, string path)`. Three call sites that now call `StructureCheck.Of(blocks)` (`ActionRunner.LintFiles` line ~285, `EditorIntegration.Lint` ~128, `FindingsPanel` ~209, plus Bench and MCP) switch to it. The old static stays as a thin shim for the Bench until built-ins are migrated (see 4.4).

Rule-table detection in `LoadTable` is one more `else if` branch placed before the `Avoid/Use` branch so existing tables are untouched. Detection is the **exact header names `Check` + `Where` + `Test`** (exact-name, not prefix), so a prose table that merely has a "Selector" or "Type" column is never parsed as rules. Rows with `Check = structure` go to `StructureEngine`; other `Check` values belong to the shared loader's other engines (gaps 2 and 3); an unknown `Check` is a Validate error.

### 4.4 Migrating today's StructureCheck into built-in rules (after v1)

**Foundations task (now, 0.5 day, before Ship 1):** give the existing C# structure findings their final `RuleId`s
(`structure:structure/alt-text`, `/one-h1`, `/table-header`, `/heading-order`, `/link-text`) and keep `Structure.AltText` etc. as the
display/override names (the `Override` name resolution is unchanged). The migration below then swaps implementation, not ids,
so a CLI baseline written before it stays valid.

**The migration itself is v1.1.** It adds risk (messages that name levels, cross-block state) and no new capability. When done:
built-in rows live in a bundled hand-maintained layer `Resources/StyleGuide/Structure/Structure.htm`, loaded before Google
(`Google/` is generated and never hand-edited, per CLAUDE.md), so owners override with the Rule/Setting table.

| Built-in | Representation (Where) |
|---|---|
| AltText | `img:not([alt]), img[alt='']` Except `img[role=presentation]` |
| TableHeader | `table:not(:has(th))` |
| LinkText | the frozen subset has no `:text`, so keep as a C# special (`special:link-text`) |
| OneH1, HeadingOrder | named C# specials (`special:one-h1`, `special:heading-order`): cross-block state and level-naming messages; still rows with id/severity/message override/Except/Files. Specials are an escape hatch for the shipping built-ins only, not a public extension point. |
| HardCoded phone/email/URL | **Not** structural: text-pattern rules. Leave in C#. |

Keep the old tests passing unchanged (they assert names like `Structure.AltText` and that suggestions anchor on text); they become the regression suite for the migration.

### 4.5 How a rule is tested (Validate)

Extends `Composition.Validate()` (same `issue(...)` plumbing, `GuideIssue` with `File`, `Location = "table row N (id)"`):

For each StructRule with an Example:
1. Parse Bad as `<html><body>{Bad}</body></html>` (with the MadCap namespace declared). Run the rule alone (all other rules
   off: `StructureEngine.Check(..., only: rule)`, mirrors `Lint(all: true)` per-rule isolation). **Expect >= 1 finding**, else error
   "The Bad example doesn't trigger this rule".
2. Parse Good; expect **0 findings**, else error "The Good example triggers this rule" and echo the first finding's element.
3. For Assert rules: also check the scope selector matches something in Good (otherwise the assertion is vacuous - warning).
4. Selector compile errors: error with the position inside the selector text (underline offset in the message), not a stack trace.
5. Rows with no Bad: warning "No Example: add one" (same as today's rule table warning).
6. Cross-rule: a built-in and a user row with identical compiled XPath -> warning "Duplicates ..." (cheap string compare; same
   wording as the existing duplicate check).
7. Overrides table naming a structural rule that doesn't exist -> existing "No rule ..." check works if ids/names are added to `known`.

Also run the Good examples of **all** structural rules together as a regression: the combined Good set must stay clean (the
word-rule equivalent is "the Use wording isn't itself flagged").

Owner workflow: edit the row in Flare -> **Validate guide** -> read the messages (grouped by row, linking to the file/row as it does
today). A "Test selector" mini-tool is deliberately *not* built (no new UI); Validate with a Bad example is the test harness. For
fast iteration, the MCP `validate_guide` tool and `--llm-export` CLI already call `Validate`, so an AI assistant can iterate the
row for the owner.

### 4.6 Findings, markers and fixes

`StyleFinding` today is text-anchored (Excerpt + Before context + Block) because tracked edits invalidate node references; this
feature keeps that contract so Go to, markers, the pane and `FindingLocator.TryLocate` need no change. In the pane a structural
finding is a normal suggestion card.

- Anchor selection: reuse `StructureCheck.Add`'s logic (own text, else nearest text before, else block's first text), extracted
  into `FindingLocator.ForElement(XmlElement, ...)` (move, don't duplicate). Excerpt capped at 60 chars as today.
- Fields set: `Source = "structure"`, `Layer = rule.Layer` (shown as the source chip "Our rules" or "Google"; topic-scope rules go in group "Topic"), `Rule = rule.Name`, **`RuleId = rule.Id`** (new for structure; enables
  "Turn off this rule", feedback and "explain this rule"), `Severity` (displayed Error / Warning / Tip), `Suggestion = message`, `Link`, `Replacement = null`.
- **No fixes in v1**: every structural suggestion is flag-only (needs you); the card's primary button is "Go to" (never "Apply", which is reserved for a write). A `delete` fix for empty paragraphs is v1.1, after checking "Verified write behavior" in `docs/flare-api-notes.md`.
- **Positions**: line/column are supplied by the shared `SourceMap` (Foundations) on the file path only (`LintFiles`/CLI), never in the live editor. For structural rules the matched element's start line is exact; topic-level suggestions have no line. `Line/Column` stay `[JsonIgnore]` on `StyleFinding`.
- **Feedback**: `GuideEdits`/`FeedbackLog` treat Checks rows as non-editable by Reject feedback; the only guide change offered is "Turn off this rule" (Rule/Setting). Test `FeedbackIgnoresChecksRows`.
- Elements with no text (an `img` in its own paragraph) keep today's fallback (nearest text in the topic).

### 4.7 Snippets and MadCap elements

- Selectors see the document as Flare saved it: `MadCap:snippetBlock`, `MadCap:xref`, `MadCap:conditionalText`,
  `MadCap:dropDown`, `MadCap:variable` are real elements in the `MadCap` namespace; `MadCap|xref` works. HTML-namespace-less
  tag names never match MadCap elements (the `not(namespace-uri())` guard), so `a` doesn't match `MadCap:xref`: say so in the guide
  topic; for "any link" write `a, MadCap|xref`.
- **Snippet content is not expanded** in the DOM: a `snippetBlock` is one opaque element (consistent with `TextRuns.Opaque`).
  Rules run on the *snippet file itself* when it is checked (snippets are `.flsnp`, already in the project file list used by
  `LintFiles`; confirm the extension filter includes them and body discovery, `BlockSelector.FindBody`, handles snippet root).
  Findings in a snippet are reported against the snippet; topics that include it do not repeat them. Document this.
- Rules whose Selector targets `MadCap|snippetBlock` (e.g. "no snippet directly inside a list") work without expansion.
- Condition tags on *content* (`MadCap:conditions` on the checked document's elements) do not hide elements from structural
  rules in v1 (a conditioned-out paragraph is still checked); `Except` can use `[MadCap|conditions*=Print]`. Open question 3.
- Tracked deletions: elements deleted by a pending change must not trigger (reuse `TrackedChanges.IsDeleted`) - filter in the
  engine after selection.

### 4.8 Performance (250 topics; whole-project check)

- Per topic: compile each selector **once at guide load** (store the `XPathExpression`; reuse `.Clone()` per document if needed
  since expressions are not thread-safe). 30 rules x 250 topics = 7,500 `SelectNodes` calls on small DOMs: milliseconds each.
- Avoid O(blocks x rules): evaluate each rule on the document root once, then bucket by owning block. (Today `StructureCheck.Of`
  walks every element of every block; the new engine evaluates once per rule.)
- `//` queries are the cost driver; the compiler emits `descendant::` relative to the body, not `//` from document root, and
  skips rules whose rightmost tag name is absent from a one-pass tag histogram of the topic (cheap prefilter: build
  `HashSet<localName>` once per topic; a rule whose compiled form requires `img` is skipped when the topic has none). Optional;
  measure first.
- Whole-project: `LintFiles` already loads each topic into an `XmlDocument` once; add the structural pass in the same loop (no
  second parse). Bench `--no-ai` against the 250-topic corpus is the performance test; budget: structural pass < 10% of today's
  lint time (target < 2 s total on the corpus).
- Live check on typing (debounced): run only rules that can be affected? Premature. The live check already runs the whole
  structure pass per debounce on the full topic; the new pass is lighter. Add a per-rule timing in `Diagnostics` and a
  guard: a rule that exceeds 250 ms on one topic is disabled for the session, and the skip is written to the Validate report
  and Diagnostics ("Skipped rule X: too slow on topic Y"), not only the log. This protects against a pathological `xpath:` an owner wrote.

### 4.9 Suppression: three mechanisms only

Structural rules use the same suppression as every other rule (design gate: seven mechanisms proposed across the plans, two needed). Stated once in the user guide: "To stop a rule, change its level. To exempt something, use Except."

1. **Rule/Setting row** (owner, whole guide or product): `Structure.AltText, off` or a different level (Error / Warning / Tip; the stored token `suggestion` stays valid, `tip` is accepted). No new work.
2. **`Except` column** on the row: selector-level exemption, version-controlled with the rule. First choice for "all `img.icon`".
3. **`Files` column** on the row: glob include, `!pattern` exclude, matched against the path relative to Content.

Writer-side surfaces are the existing ones: the card's **Reject ▾** reasons (personal, existing Dismissals) and the card overflow **Turn off this rule…** / **Suggest changing this rule…** (IA 4.3), which edits the Rule/Setting table, never the Checks row. Mad Quality's `.mqignore` equivalent is the `Files` column; no dotfile is added.

Not in v1: the `hs-ignore` class convention, a writer-side "Ignore this once" that writes a class (this also removes the only Flare-adapter attribute edit from the feature), and a separate ignore table.

### 4.10 AI review

- **Do not send structural rules to the AI.** They are exact, deterministic, tested; the AI cannot see the DOM it is judging
  (blocks are text-oriented), would hallucinate violations, and would burn tokens. Mirror how `Lint` rules (`IsLint`) are handled:
  `AddItem` marks `item.IsLint = true`; do the same with a new `item.IsStructural = true` so `Composition.Guide(...)`,
  `RuleIdsFor`, and `GuideFor` exclude them from prompts and from `allowed` ids (ActionRunner line ~395). Check that the existing
  prompt-building filters on `IsLint`; if it filters on a different field, extend it.
- v1.1 (not in v1): let the AI *reword* what the engine flags, as an opt-in follow-up: the suggestion's message + Excerpt + the Good example go into "Reword with AI" (card overflow), producing a validated block rewrite via `ReplaceBlocks`. The Good example is the model's target shape. Cost: one block per click, no new button. In v1 structural suggestions are flag-only: the primary button is "Go to".
- The AI **should** see a short list of "structure conventions" only when generating new content (e.g. Draft/Rewrite): optional
  line "Notes start with a bold label". Skip in v1; the rules are enforceable after the fact. Noted as an idea, not a commitment.
- `mine_style` (StyleMiner) must not propose structural rows from word-level evidence. No change.
- MCP `lint` tool: return structural findings in the same JSON with `rule` ids (so Claude Desktop users get the same output).

### 4.11 Core vs Flare split

All of this is Core (net40, XmlDocument): `SelectorCompiler`, `StructRule`, `StructureEngine`, Validate extension. Flare side only
swaps the three call sites to the service method and, if the class-based ignore is adopted, one adapter attribute edit. Core
stays testable on any OS; no WinForms, no B3 types.

## 5. Files to add / change

Add (Core):
- `src/FlareAI.Core/Style/SelectorCompiler.cs` - CSS subset -> XPath; `SelectorException`.
- `src/FlareAI.Core/Style/StructRule.cs` - rule model.
- `src/FlareAI.Core/Style/StructureEngine.cs` - evaluation, anchors.
- `styleguide/Content/Resources/StyleGuide/Company/Structure.htm` - starter Checks table (`Check = structure`) with 4 commented examples at Tip severity (copied into new guides by GuideBuilder).

Change:
- Foundations (before Ship 1): `StructureCheck.cs` assigns final `RuleId`s to the existing findings (`structure:structure/alt-text`, `/one-h1`, `/table-header`, `/heading-order`, `/link-text`); `Structure.AltText` etc. stay the display/override names.
- `GuideLayer.cs` - Checks table branch (exact headers `Check`, `Where`, `Test`), `StructRules` list, `Problems` for bad rows, item `IsStructural`.
- `StyleEngine.cs` - `Composition.StructRules`, `Structure(...)`, `Override` generalization, `Validate` extension, `IStyleService.Structure`.
- `FindingLocator.cs` - extract `ForElement` anchoring from `StructureCheck.Add`; consume the shared `SourceMap` for element start lines on the file path.
- `ActionRunner.cs` (~285, ~395), `EditorIntegration.cs` (~128), `FindingsPanel.cs` (~209): call `Structure(...)` instead.
- `tools/FlareAI.Bench/Program.cs` (~230), `tools/FlareAI.Mcp` (lint/validate output).
- `GuideBuilder.cs` - the starter guide includes the new owner topic; `GuideEdits.cs` prompt text mentions "five cells" for table rows: Checks rows have 10 columns, so its `isTableRow` instruction must read the real column count, and Checks rows are non-editable by Reject feedback.
- `docs/user-guide/Content/`: new topic "Write structural rules" (selector cheat sheet: 10 recipes), update "Validate your guide" and "Turn rules off or change their level". User-guide wording follows the glossary (suggestion, issue, Apply, Reject, Turn off this rule).
- v1.1: `StructureCheck.cs` shrinks to specials; `styleguide/Content/Resources/StyleGuide/Structure/Structure.htm` (built-in rows).

## 6. Tests (tests/FlareAI.Core.Tests)

- `SelectorCompilerTests`: table-driven (input CSS -> expected XPath fragment and, more usefully, selects the right nodes on a sample DOM): tag/class/id/attr operators, descendant/child/adjacent, `:first-child`, `:last-child`, `:not`, `:empty`, `:has`, `:blank`, `MadCap|xref`, union, `xpath:` passthrough; invalid input (including every removed construct: `~`, `:nth-child`, `:nth-of-type`, `:text`, `:first-text`) -> `SelectorException` with position (unterminated bracket, unknown pseudo, empty selector, stray `>`).
- `ChecksTableLoadTests`: a table with exact headers `Check`+`Where`+`Test` loads; `Check = structure` rows become `StructRule`; a prose table with a "Selector" or "Type" column is never parsed as rules; missing Message warns; bad severity falls back; `tip` loads as the stored token `suggestion`; unknown Test errors; unknown Check errors; keys via `id="rule-x"` produce `company:structure/x`; `AssignKeys` adds keys to rows; Product conditions honored, Content conditions warn.
- `StructureEngineTests`: Report vs Assert semantics; topic-scope boolean rule (group "Topic"); Except; Files; elements under tracked deletion skipped; selection subset of blocks only returns suggestions in those blocks; MadCap elements not matched by bare tag names; layer override/off via Rule/Setting; suggestions carry `RuleId`, `Layer`, source chip ("Our rules"/"Google"), anchor text, and `FindingLocator.TryLocate` finds them after an edit shifts text; on the file path an element suggestion's start line (from `SourceMap`) is exact.
- `ValidateStructuralTests`: Bad not triggering -> error; Good triggering -> error; invalid selector -> error with position; vacuous Assert -> warning; combined Good set clean; duplicate selectors warn; a rule over the 250 ms guard appears in the Validate report as "Skipped rule X: too slow on topic Y".
- `FeedbackIgnoresChecksRows`: Reject feedback on a structural suggestion never produces a `GuideEdits` change to a Checks row; only "Turn off this rule" (Rule/Setting) is offered.
- `StructureCheckTests` (existing): unchanged; the Foundations RuleIds are asserted in addition (`structure:structure/alt-text` etc.) while `Structure.AltText` stays the display/override name.
- `StructureAiExclusionTests`: structural items absent from `Guide(...)` prompt text and `RuleIdsFor`.
- `StructurePerfTests` (Bench, not unit): 250-topic corpus structural pass time; asserts under a generous bound in CI (e.g. 5 s), reports the real number locally.

Risks:
1. **Selector subset sprawl.** The temptation is to add pseudo-classes forever. Mitigation: the subset in 4.1 is frozen for v1; anything else is `xpath:`. Validate rejects unknown pseudos loudly (no silent no-match).
2. **Flare DOM quirks**: editor DOM vs saved file differ (tracked-change wrappers `MadCap:change...`, hidden elements, snippets, Flare auto-wrapping). Selectors that work on a saved file may differ in the live document. Mitigation: unit tests with real tracked-change fixtures from `docs/flare-api-notes.md`; engine runs on the same `blocks` the existing structure check uses; evaluate on the live DOM and the file loader alike.
3. **Message quality.** Owners will write terse messages; writers see them. Validate warns on messages under 4 words or that don't end with an instruction; the template guide models good wording (the suggestion says what to do, not what is wrong).
4. **Pathological `xpath:` cells.** Mitigated by the per-rule time guard (4.8), which reports in the Validate report and Diagnostics.
5. **Later migration regressions** (v1.1, section 4.4): messages that name levels, cross-block state. Keep specials for HeadingOrder/OneH1; existing tests as safety net; RuleIds are fixed in Foundations so baselines survive.
6. Reject feedback and mining (`GuideEdits`, `FeedbackLog`) assume rule cells are Avoid/Use. Checks rows are non-editable by Reject feedback ("Turn off this rule" only), so feedback never rewrites an XPath cell (test `FeedbackIgnoresChecksRows`).

Edge cases: empty `<p>` containing only `<br/>` or only a non-breaking space (`:blank` treats NBSP as blank); an `img` whose alt is whitespace; headings inside `MadCap:dropDown`; tables nested in tables (`:has(th)` counts nested; use `> tr > th` form in the built-in); tag names case (Flare saves lower-case; compile to lower-case compare); `xml:lang`/`MadCap:` attributes in `[attr]` (`[MadCap|conditions]`); duplicated rule keys; topics with no `body` (skip silently, as today); XHTML fragments with entities in Bad/Good cells.

Open questions (for the owner to decide):
1. Is `Assert <relative selector>` inside the Test cell acceptable, or do you prefer a separate "Must have" column? (Recommended: Test cell, fewer columns; the shared Checks header is fixed.)
2. Should `MadCap:conditions` on content hide elements from structural rules (Print-only content checked under Online rules)? v1 says no.
3. Built-in layer location (v1.1): new `Structure` bundled layer (recommended) vs. appending to Company defaults.

Not in v1 (suppression and fixes cut by the design gate): the `hs-ignore` class convention, a writer-side "Ignore this once" that writes a class, any per-element or per-file ignore table, `.housestyleignore`-style dotfiles, the `Fix` column (`delete`, `unwrap`), "Reword with AI" for structure suggestions.

## Effort and sequencing

**Effort: Ship 1 only, about 5 days** (compiler 2, loading + engine + findings 1.5, Validate + tests 1, docs and starter topic 0.5). The 0.5-day Foundations task (final RuleIds for the existing C# structure checks, section 4.4) and the shared `SourceMap` are costed in the Foundations section of the plan set, not here.

1. **Ship 1 (about 5 days)**: `SelectorCompiler` (frozen subset), the `Checks` table branch (`Check = structure`), `StructureEngine`, suggestions with rule ids and positions on the file path, Validate (Bad/Good), Rule/Setting overrides, `Except`, `Files`, AI exclusion, starter topic (rows at Tip) and user-guide topic. Existing `StructureCheck` untouched apart from the Foundations RuleIds.

**v1.1 (not in v1):**
- Migrate AltText/TableHeader/LinkText to built-in rows (OneH1/HeadingOrder as specials) and delete the duplicated C# (section 4.4).
- Optional `delete` fix for empty paragraphs and "Reword with AI" using the Good example (sections 4.6, 4.10).