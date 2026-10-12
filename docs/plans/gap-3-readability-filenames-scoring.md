# Gap 3: Readability rules and file-name rules

Status: plan, amended at the design gate (2026-10-12). Depends on the Foundations package (Checks loader skeleton, `FindingLocator.ForTopic`, `ProjectChecker`, `SourceMap`; plan-critique section 3.2 step 1) and on gap 1's Checks header names. Scoring is not part of this plan in v1.

## Gate amendments (2026-10-12)

Gate verdict: **Approve with changes** (`ux/plan-critique.md` sections 1.3, 2.1, 2.2, 2.4, 2.8, 2.9, 5 "Gap 3").

- Sentence length and file-name rules are rows of the shared **Checks** table (`Check = sentence length`, `Check = file name`). No Readability table, no File-name table, no Gate table.
- Cut from v1 (moved to section 9 with rationale): section grade, Rewrite to grade, grade measures and the diff header, issue density and weights, `ProjectHealth`, the Gate table, `--max-errors`, `--max-density`, the baseline fingerprint, the image/snippet name sweep, the orphan image sweep, "in <file>" entries, the "since last check" delta.
- **No score anywhere in the product in v1.** The status line counts issues by severity.
- Display: severities show as **Error / Warning / Tip** (stored tokens `error`, `warning`, `suggestion`; `tip` accepted as an alias). Pane noun: **suggestion**. Count noun: **issue**.
- Starter rows ship at **Tip** (`suggestion`) severity. Sentence length stays **off** in the starter guide until the project backlog pane (L3) ships.
- The file-name check covers the topic's own file name only, anchored on the h1, in the **Topic** group, with no Accept button.
- Ids use one scheme: `company:<topic>/<key>` (for example `company:file-names/1`). No `Company.Readability1` style names.
- Effort: **5 days** for this plan. Foundations is costed separately (3 days).
- Supersedes IA 5.3 "one number in the pane header": in v1 there is no number.

## 1. Goal and user stories

**Writer.** "Long sentence" appears in the review pane next to the other suggestions, with a fix I can accept as a tracked change (Rewrite, or split it myself). If a topic's file name breaks the team's convention, I'm told when I check that topic, with the name I should use and how to rename it in Content Explorer.

**Style-guide owner.** I set limits and naming conventions as rows of the Checks table in the guide project, the same way I write Avoid/Use rows. I turn a rule off or change its level from a suggestion's card (existing Rule/Setting table). I don't maintain per-rule penalty numbers.

**Docs lead / build engineer.** Issue counts by severity, per project and in the CLI report (gap 2). A CI gate uses gap 2's flags. Nothing ranks topics or writers.

Non-goals (v1): any score, grade or density number; a readability badge on a topic; a rename button; passive-voice percentages; readability for non-English topics.

## 2. What the competitor does and where we should be better

Mad Quality: `Readability <score>` is a *Function* in the Excel rule sheet (Flesch score threshold), `FileNameRegex` is another Function (regex on the file name, `FileFilters` limits which files), and every rule has a numeric `Score` penalty. The `/score` CLI flag returns the average score as the exit code.

Where we should be better in v1:

| Mad Quality | House Style (v1) |
|---|---|
| One Flesch score per topic; a failing topic gets one message with no location. | Sentence-level suggestions anchored on the sentence. The fix is Rewrite or a manual split, accepted as a tracked change. |
| Readability counts code, UI labels and variable text unless the author writes IgnoreContext rules. | Code, `pre`, `kbd`, `samp`, `var`, opaque MadCap elements and headings are excluded by construction (`TextRuns`/`TextLines`). |
| File-name check is a regex in a sheet; violations show in a log. | Same idea (regex per glob), but the suggestion shows in the review pane, says what the name should be, and carries a rule id. |
| A free-form penalty number per rule; the average becomes an exit code. | No per-rule numbers and no score. Severity is the one dial (Error / Warning / Tip). |

## 3. UX surface

Principle: **no new ribbon buttons, no new settings entries.** Everything is a suggestion in the review pane, or a line in the project report, so a writer meets it only when it applies.

### 3.1 Sentence length

- Shows as an ordinary suggestion in the review pane and as a margin marker, on the same path as every lint finding (live check and check on save).
  - Card text: `Long sentence (34 words; the guide allows 30). Split it where one idea ends.`
  - Kind: restructure. The card's existing Rewrite path applies, with the instruction: the suggestion summary plus "Keep UI names, code, variables and meaning unchanged."
  - No key: kind **Needs you**, no AI button (IA F-01: lint still works without a key).
- Overflow menu on the card: "Turn off this rule…" and "Change level" (existing `RuleSettingForm`).
- Off in the starter guide until L3 ships (see 4.1). Owners who turn it on get the behavior above.
- English only. A topic whose language is not `en*` is skipped. The project report says `Sentence length skipped for 12 topics not in English.` Never a pane warning.

### 3.2 File names

- Checked: the topic's own file name, when the topic is checked. Images and snippets are not checked in v1.
  - Card text: `File name "Setup_Guide v2.htm" breaks the file-name rule (lowercase, hyphens, no spaces). Suggested name: setup-guide-v2.htm. Rename it in Content Explorer (right-click, Rename) so Flare updates the links.`
  - The suggested name appears only when it satisfies the row's own test (4.2).
  - Case-only change: the text adds "Content Explorer may need two renames on Windows."
- Anchoring: the suggestion has no text of its own, so it anchors on the topic's h1 via `FindingLocator.ForTopic`. Source chip **Our rules**, group **Topic**. Kind **manual**: no Accept button. Go to selects the h1. `Line = null` (see plan-critique 2.3).
- Project report: file-name suggestions appear first in each topic's group. No separate image or snippet entries.
- **No rename action.** See 4.5.

### 3.3 Counts in the status line

- Project report and pane summary strip, one line:
  `121 issues in 83 files: 2 Errors, 41 Warnings, 118 Tips.`
- No per-topic number, colour, rank or sort-by-score. The project report sorts by path.
- No per-writer or per-author numbers (the plugin does not know authors).
- The CLI (gap 2) prints the same counts from the same `CheckResult`. Thresholds are gap 2's `--fail-on` and `--max-warnings`.

## 4. Design

### 4.1 Checks rows for these two rules

Both rules live in ordinary guide topics (Company layer; starter shipped in `styleguide/`), as rows of the shared **Checks** table. Header: `Check | Where | Test | Message | Severity | Why | Bad | Good | Except | Files`. The loader recognises it by `Check`, `Where` and `Test` (Foundations). Rows get permanent keys (`id="rule-N"`) through `AssignKeys`, so `company:file-names/1` survives reordering and works in the Rule/Setting table, feedback and dismissals.

How the two checks use the columns:

- `sentence length`: `Where` blank (all prose); `Test` is the maximum word count; `Message` generated when blank.
- `file name`: `Where` is a glob of topic files (`**/*.htm`); `Test` is `must match <regex>` or `must not match <regex>`; `Except` is a glob of names to skip (for example generated folders). `Files` is not used.
- `Bad` must trigger the row and `Good` must not. Validate runs both through the same evaluator as lint.
- Conditions (`Content.Steps`, `Product.*`) are the row's `MadCap:conditions`, applied by the existing `Scope()`.

Starter rows (`Company/File-names.htm`):

```html
<tr id="rule-1"><td>file name</td><td>**/*.htm</td><td>must match ^[a-z0-9][a-z0-9-]*\.htm$</td><td></td><td>suggestion</td><td>Lowercase, hyphens, no spaces.</td><td>Setup_Guide v2.htm</td><td>setup-guide.htm</td><td></td><td></td></tr>
```

Sentence length ships commented out in `Company/Readability.htm`, to be enabled after L3 ships:

```html
<!-- Off in the starter until the project backlog pane (L3) ships. Uncomment to enable. -->
<!-- <tr id="rule-1"><td>sentence length</td><td></td><td>30</td><td></td><td>suggestion</td><td>Split at the end of an idea.</td><td>(a sample longer than 30 words)</td><td>(a sample of 30 words or fewer)</td><td></td><td></td></tr> -->
```

The `Bad` and `Good` cells hold real sample text in the shipped row; the placeholders above are illustrative.

Validate (one line per Check type in the grouped Validate report, F-12): invalid regex, empty `Where` on a file-name row, `Test` that is not a positive number, `Bad` that does not trigger, `Good` that does trigger, unknown `Check` value.

### 4.2 Algorithms and where they plug in

**Sentence length** is a lint-pipeline rule, not a new pass in the pane. `Composition.Lint(texts, headings)` already receives whole lines (inline markup flattened, variables resolved, code and opaque content excluded by `TextLines`). After the table-rule loop, add a measure step: for each non-heading line, split into sentences and emit a `LintHit` (Start and Length cover the sentence, `Replacement = null`, `Rule = "Sentence length"`, `RuleId = "company:readability/1"`, severity from the row). Everything downstream works unchanged: `MapToRun`, `FindingLocator.FromLint`, severity override from the Rule/Setting table, dismissals, margin markers, `LintFiles`, the bench, the MCP `lint` tool.

Sentence splitter (Core, `Readability.cs`): split on `.`, `!`, `?` followed by whitespace and an uppercase letter or digit. No split after common abbreviations (e.g., i.e., etc., vs., Dr.), inside version numbers (`2.1`), file extensions or ellipses. Words are tokens containing a letter or digit; a hyphenated compound counts as one. A line with no terminal punctuation and fewer than 8 words (list item, table cell, button label) is not a sentence and is skipped.

**Exclusions** (decided once, by the existing walkers): `code, pre, kbd, samp, var` (`TextRuns.Literal`), opaque MadCap elements, headings, tracked deletions. Variable values are resolved and counted. Verify during implementation that `TextLines` skips `Literal` descendants and that a `pre` block yields no line; a test guards this.

**Language gate:** callers pass the topic language (`html/@xml:lang`, else the project default read once from the `.flprj` target). Non-English topics skip the check and count toward the skipped figure.

**File-name check.** `FileNameCheck` (Core, no Flare types) has `Of(topicPath, projectRoot, rows)`. It checks the topic's own file name only. `ActionRunner.Lint` calls it when `topicPath` is known; `LintFiles` calls it per topic path. Findings use `Source = "filename"`, `RuleId = "company:file-names/<key>"`, `Line = null`, anchored via `FindingLocator.ForTopic`. The glob matcher is shared with the Checks `Where` column.

**Suggested name** (`FileNameSuggest.For(name)`): lowercase, strip diacritics (NFD and drop marks), replace spaces and underscores with hyphens, drop characters outside `[a-z0-9.-]`, collapse repeated hyphens. It is offered only when the result differs from the original and satisfies the row's own test. Otherwise the message gives no suggestion.

### 4.3 Scoring: none in v1

Decision: **no score, no density, no weights, no penalty column in v1.** Severity is the one dial.

- Two scales (severity plus a number) make owners reason about both.
- A total that depends on arbitrary weights is not comparable across projects or time, and it invites tuning for the number rather than the reader.
- CI needs a pass/fail with a reason. Gap 2 gives that with `--fail-on` and `--max-warnings` and a baseline keyed by gap 2's fingerprint.
- Owners already get the same power in a form they can explain: promote or demote a rule (Rule/Setting table).

Surfaces: the status line in 3.3 and the CLI counts, both from one `CheckResult` (Foundations `ProjectChecker`). The product strings must not contain "score", "density" or "grade" (test in section 6).

### 4.4 Core vs Flare split

Core: `Readability` (sentence splitter), `FileNameCheck`, `FileNameSuggest`, the two Checks evaluators. Foundations provides the Checks loader, `ProjectChecker`, `SourceMap` and `FindingLocator.ForTopic`; this plan uses them and adds nothing to `IStyleService`.

Flare side is display only: the status line text in `FindingsPanel.StartProject`. `DiffView` and `RuleSettingForm` do not change in v1. The Accept button stays disabled for kind "manual" (already so).

### 4.5 Rename: why we don't offer one

`docs/api-dump.txt` (Flare 22 PluginAPIKit) exposes `OpenDocument`, `SavedAs`/`SavingAs` events and node edit methods, and nothing for renaming or moving a project file or updating links. Flare rewrites xrefs, TOC entries, snippet and image references only when the user renames through its own UI (Content Explorer). Renaming on disk behind Flare's back would break links silently, and rewriting links ourselves means editing every referencing topic, TOC, target and condition file, which our constraints rule out. So the writer gets the correct name and the Content Explorer instruction. If a future Flare version exposes a rename API, revisit and record it in `docs/flare-api-notes.md`.

## 5. Files to add and change

Add:
- `src/FlareAI.Core/Style/Readability.cs` (sentence splitter only)
- `src/FlareAI.Core/Style/FileNameCheck.cs` (+ `FileNameSuggest`)
- `styleguide/Content/Resources/StyleGuide/Company/Readability.htm` (sentence-length row, commented out) and `File-names.htm` (file-name row, shipped at `suggestion`)
- Sections in `docs/user-guide/Content/check-style.htm` and `style-guide.htm` (prefer sections to new topics)
- `tests/FlareAI.Core.Tests/ReadabilityTests.cs`, `FileNameTests.cs`

Change:
- `src/FlareAI.Core/Style/StyleEngine.cs`: `Composition.Lint` adds the sentence measure; overrides and dismissals apply to the new ids.
- `src/FlareAI.Core/Actions/ActionRunner.cs`: `Lint` and `LintFiles` call `FileNameCheck` for the topic's own name.
- `src/FlareAI.Flare/UI/FindingsPanel.cs`: status line text (3.3).
- `tools/FlareAI.Mcp` (lint hits include the new ids), `tools/FlareAI.Bench` (sentence-length and file-name expectations on the 250-topic corpus).
- `docs/flare-api-notes.md`: "No rename/move API" fact. `PLAN.md`: Checks table section.

Not in v1 (removed from the earlier file list): `ReadabilityCheck.cs`, `ProjectHealth.cs`, `ProjectHealthTests.cs`, the image sweep in `ProjectFiles`, the word-total hook for density, the `DiffView` grade header, `IStyleService` `Measures`/`FileNameRules` members.

## 6. Tests

- `Sentence_split_ignores_abbreviations_versions_and_extensions`: "Use e.g. v2.1 or setup.htm." is one sentence.
- `Sentence_length_flags_over_limit_with_rule_id`: an over-limit sentence yields one hit `company:readability/1`, `Replacement` null.
- `Code_ui_and_variable_text_are_excluded_from_word_count`: `code`, `pre`, `kbd` text adds 0 words (guards the `pre` concern).
- `Headings_and_short_fragments_are_not_sentences`.
- `Non_english_topic_skips_sentence_length`; `Rule_setting_off_disables_sentence_row`; `Severity_override_applies`.
- `Filename_must_match_row_flags_nonmatching_name`; `Filename_must_not_match_row_flags_matching_name`; `Glob_double_star_matches_nested_folders`.
- `Filename_finding_anchors_on_h1_in_topic_group_with_null_line`.
- `Suggested_name_lowercases_hyphenates_and_strips_diacritics`; `Suggested_name_omitted_when_it_would_not_satisfy_the_rule`.
- `Checks_bad_must_trigger_and_good_must_not` (Validate); `Checks_validate_reports_one_line_per_check_type` (X6, grouped Validate report).
- `Checks_rows_feedback_is_turn_off_only` (shared with gap 1, item 8): Reject on these rows never edits the guide text.
- `Starter_file_name_row_ships_at_suggestion_and_sentence_row_is_off`.
- `Product_strings_contain_no_score_density_or_grade` (scans user-facing strings in `FindingsPanel` and the status-line builder).
- `Status_line_counts_issues_with_error_warning_tip_names`.
- Bench: sentence-length and file-name hits appear in `--no-ai` runs with expected counts per fixture topic.

User-guide updates: `check-style.htm` (what the two suggestion types mean, how to turn them off or change the level), `style-guide.htm` (the Checks rows for these two checks, with examples), `check-project.htm` (the counts line and the "issues" wording).

## 7. Risks, edge cases, open questions

- **Noise on adoption.** Starter file-name row ships at Tip, so a legacy project with `Setup_Guide.htm` names is quiet in the pane until the owner promotes it. Sentence length is off in the starter until L3 ships (pane volume, plan-critique 2.8). The 250-topic bench sets the defaults for when it turns on.
- **Syllable and grade heuristics** are out of v1 with the grade measure (section 9).
- **Sentence splitter edge cases**: list items without punctuation, steps with embedded UI paths. Handled by the fragment rule and tested on the bench corpus.
- **Overlap with AI review.** The AI may also say "this sentence is long"; the existing `Duplicates` filter drops AI repeats of lint hits for the same span.
- **File-name findings on generated or imported files.** Use `Except` on the row to skip folders. (This answers the earlier `.houseignore` question: no ignore file is added.)
- **Case-only renames** and shared file names are the writer's call; we only point.
- **Topic-scope overlap with gap 1.** File-name suggestions use the same `Topic` group and `ForTopic` anchor as gap 1's topic-scope rules; the Foundations change is shared, not duplicated.
- Open: confirm the class names Flare projects use for UI labels (`uicontrol`, `GUI-Element`) so they are excluded from sentence length; read from the project's style sheet if cheap.

## 8. Effort and sequencing

**Total: 5 days** for this plan (file names 1.5, sentence length 2.5, status line 0.5, docs 0.5). Foundations (3 days) is a separate prerequisite.

1. Foundations (3 days, separate): Checks loader skeleton, `FindingLocator.ForTopic`, `ProjectChecker`, `SourceMap`, Validate dispatcher.
2. File-name rule on the Checks table (1.5 days): table evaluator, suggestion, anchoring, tests. Pure Core, deterministic, no model.
3. Sentence length through the lint pipeline (2.5 days): splitter, conditions, tests. Ships commented out in the starter.
4. Status line (0.5 day) with the pane rebuild (L2). Docs (0.5 day, interleaved).

Items 2 and 3 are Core and run in parallel with L2. Their Flare-side display waits for L2 and L3 (plan-critique 2.8).

## 9. v1.1 / later (deferred, with rationale)

| Item | Rationale for deferring |
|---|---|
| Section grade (Flesch-Kincaid, `ReadabilityCheck.Sections`), `Grade level` measure, `Target` column | Needs bench data; the syllable heuristic is about ±0.3 grade on technical text; adds a second measure and a section pass. About 3 days. |
| Rewrite to grade, before/after diff header (`RewriteProposal.MeasureBefore/After`, `DiffView`) | Depends on the grade measure; changes the rewrite path. Plain Rewrite covers sentences in v1. |
| Flesch Reading Ease in the report | A second readability number with no owner decision behind it. |
| Image and snippet file-name checks, orphan image sweep in `ProjectFiles`, "in <file>" entries | One suggestion per referencing topic; edits nothing in the topic; needs a new project sweep. Revisit with L3 grouping. |
| Issue density, weights, `ProjectHealth`, Gate table, `--max-errors`, `--max-density` | A second number to explain; the score is rejected for v1 (section 4.3). If wanted later, derive it from the CLI JSON, do not define it twice. |
| Readability baseline fingerprint | Gap 2 owns the baseline and its fingerprint. |
| "Since last check" delta | Cut in v1. Ask the owner before building it; it can read as a scoreboard. |
| Sentence length on in the starter guide | Turns on when L3 ships and the bench has tuned the default. |
| Readability for non-English topics | Out of scope; the English heuristics do not transfer. |
