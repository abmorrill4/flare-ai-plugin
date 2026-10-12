# Gap 2: A command-line checker for build pipelines

## Gate amendments (2026-10-12)

- Surface cut from about 47 flags to 11 (section 4.4); the rest are listed under "Later, on request" (section 7).
- Formats in v1: text, json, github, sarif. junit, azure and the step summary move to "Later".
- AI review in CI removed from this plan; the cost-cap design is kept as notes in section 7.
- CLI-only overrides removed (`--rule`, `--skip-rule`, `--layer`, `--severity-override`, `--min-severity`). Severity and rule control come only from the guide's Setting table, so CI cannot disagree with Flare.
- Baseline trimmed to fingerprint, the `baseline` verb, `--baseline`, and a stale-count note. Ratchet, `--merge`, `--strict-baseline`, `--show-baselined` cut.
- `.housestyleignore`, `.housestyle.json`, the findings-pane footer link and the `--changed-lines-only` flag cut. Env vars: `HOUSESTYLE_GUIDE` only.
- Pane "Check project" and the CLI share one Core `ProjectChecker` and one JSON report (the pane's Save report writes it). Test `PaneAndCliProduceSameCounts`.
- Line/column come from a Core `SourceMap` built on the file-loading path only (shared with gap 1); topic-level issues have no line.
- Severities display as **Error / Warning / Tip**; stored token `suggestion` stays; `--fail-on` accepts `error|warning|suggestion|tip|never`. CLI counts use the noun **issue**.
- Exit codes 0/1/2/3 are the single table for gaps 3 and 5 too.
- Effort reset to about 6 working days (phase 1 + baseline + SARIF).

Recommendation in one line: add a `check` verb to `FlareAI.Mcp.exe` (already shipped in the plugin folder), backed by one Flare-agnostic
Core class `ProjectChecker` that the pane's Check project and the CLI both call, with a baseline file for legacy projects. The check is
deterministic (no AI, no key, no network) and is the CI default.

## 1. Goal and user stories

- **Docs lead / build engineer**: "Fail the pull request when a topic breaks the guide, and show the problems as annotations on
  the changed lines. On our 4,000-topic legacy project, only fail on issues someone just introduced."
- **Style-guide owner**: "The pipeline uses the same guide, severities and rule ids the writers see in Flare. When I change
  `Company/` rules or turn a rule off, CI follows without a second config."
- **Writer**: "Nothing new to learn in Flare. When the pipeline fails I see the same rule text and a fix suggestion, and I can
  reproduce locally with one command."

Out of scope: auto-fixing from the CLI (fixes are tracked changes inside Flare), building the Flare project (madbuild does that),
AI review in CI (section 7).

## 2. What the competitor does and where we should be better

Mad Quality Commander: `MadQualityCommander.exe <folder> <rules.xlsx> <log.txt> [/i subdirs] [/xml] [/score]`. It writes a text or
XML log; `/score` turns the exit code into the average score (not pass/fail, so you cannot gate on "no errors"). No baseline, no
changed-files mode, no CI-native formats, rules must be a spreadsheet path.

Where we should be better:

| Area | Mad Quality | House Style |
|---|---|---|
| Exit code | average score | 0 pass, 1 issues at/over threshold, 2 usage/config error, 3 internal error |
| Formats | text, XML | text, JSON, SARIF 2.1.0, GitHub `::error` annotations |
| Legacy adoption | none | `--baseline` file: fail only on new issues; `housestyle baseline` writes it |
| Speed on PRs | whole folder | `--changed-since <git ref>` or `--files-from <list>` |
| Guide | xlsx path | the same layered guide as the plugin (Google + company + project variables/glossary), by `--guide` or default location |
| Rule control | spreadsheet columns | the guide's Setting table only; no CLI-only overrides, so CI cannot disagree with Flare |
| Same numbers as the editor | n/a | the pane's Check project and the CLI run one engine and write one JSON report |

## 3. UX surface

No ribbon button, no new settings, no Flare UI. Surface is a CLI that ships in the existing plugin folder
(`Flare.app\Plugins\FlareAI\FlareAI.Mcp.exe`, already copied by `scripts/install-plugin.ps1` and `scripts/package.ps1`) and as a
standalone zip asset on each GitHub release so a CI agent does not need Flare installed (it never references B3 assemblies).

Discovery (a build-engineer feature, so discovery happens where they look):
1. User guide: new topic "Check topics in a build pipeline" with the snippets in section 4.11, linked from the existing
   "Report before build" topic ("Want this to fail the build? Use the command-line checker.").
2. `FlareAI.Mcp.exe check --help` prints the quick start.
3. The report-before-build toast/tooltip mentions the CLI when it reports errors. No new button, no pane footer link.
4. README: a "CI" section, and a `docs/ci/` folder with the copy-paste YAML and the madbuild `.bat`.

Defaults that just work: `FlareAI.Mcp.exe check <project>` with no other flags checks the whole project against the guide next to
the exe, prints text, and exits 1 on any Error.

Naming: the exe is called FlareAI.Mcp.exe for historical reasons. Ship a copy named `housestyle.exe` (same binary, copied in
package.ps1; the doc snippets use `housestyle check`). `FlareAI.Mcp.exe` keeps its MCP behaviour when run with no verb (existing
MCP configs keep working).

Copy for messages (text format; no color, no emoji):
```
topics/install.htm:41:9  Warning  Use "select", not "click on"  [google:word-list/12]  fix: select
topics/install.htm       Error    This image has no alt text: describe what it shows ...  [Structure.AltText]

House Style: 3,842 topics checked in 4.1 s. 12 new issues (2 errors, 10 warnings). 1,930 existing issues in the baseline (not shown).
FAILED: 2 errors at or above the threshold (--fail-on error).
```
Pass: `House Style: 3,842 topics checked in 4.1 s. No new issues.`

## 4. Design

### 4.1 Extend FlareAI.Mcp.exe vs a separate exe: decision

**Extend `FlareAI.Mcp.exe` with verbs.** Reasons:
- Smallest surface: it is already built (net48), already in `package.ps1` and `install-plugin.ps1`, already signed in the release
  pipeline, already references Core, and already hosts a non-MCP mode (`--llm-export`) that madbuild post-build events call.
- No new csproj, no new CI/signing/packaging entry, no second thing to version-match against Core.
- Cost: the name says "Mcp". Mitigation: ship the `housestyle.exe` copy and document that name.

Rejected: a separate `FlareAI.Cli` project. It doubles packaging/signing surface for no capability difference. Revisit only with
the cross-platform work in section 4.10.

`Program.Main` dispatches on `args[0]`: `check`, `baseline`, `export llm` (alias of `--llm-export`, flag kept), `mcp`, `--help`.
No verb (or only `--guide`) = MCP server, unchanged. The CLI lives in `tools/FlareAI.Mcp/Cli/` (`Args.cs`, `CheckCommand.cs`,
`GitScope.cs`, `Help.cs`) so `Program.cs` stays small.

### 4.2 Core vs exe split (Core stays Flare-agnostic)

New in **Core** (net40, no Flare/WinForms/WPF/Process needed):
- `src/FlareAI.Core/Check/ProjectChecker.cs`: input `CheckOptions` (paths, guide roots, file filter, fail-on, baseline); output
  `CheckResult` (files checked, `List<Issue>`, timing, guide roots). Wraps `StyleEngine` and the `ActionRunner.LintFiles` logic
  with no LLM client (`llm: null`; verified by a test). **This is the single engine for both the pane's Check project and the CLI.**
- `Issue`: `File` (project-relative, forward slashes), `Line`, `Column`, `EndLine`, `EndColumn` (nullable), `RuleId` (`google:word-list/12`
  or `Structure.AltText`), `Severity` (`error|warning|suggestion`; shown as Error / Warning / Tip), `Message`, `Excerpt`,
  `Replacement`, `Link`, `Layer`, `Fingerprint`, `Source` (lint|structure).
- `src/FlareAI.Core/Check/SourceMap.cs`: element document-order index to (line, column), built once per file on the file-loading
  path (section 4.3). Shared with gap 1's Foundations work; the live editor never uses it.
- `src/FlareAI.Core/Check/Baseline.cs`: read/write/compare fingerprints. **The fingerprint is defined here once**; gap 3 reuses it.
- `src/FlareAI.Core/Check/ProjectVariables.cs`: resolve `Set.Name` from `Project\VariableSets\*.flvar` on disk, using the
  `.flvar` reader extracted from `GuideLayer.cs` (one copy). Without it, lines with variables lint differently from Flare.
  Snippets are checked as their own files, matching "Report before build".
- `src/FlareAI.Core/Check/Reports/`: `TextReport.cs`, `JsonReport.cs`, `SarifReport.cs`, `GitHubAnnotations.cs`. Pure string output
  to a `TextWriter`, testable on any OS.

New in **exe only** (OS/process concerns): argument parsing, `git diff --name-only` shelling, file-list reading, exit-code mapping,
stdout/stderr.

The pane's Check project calls `ProjectChecker` and its Save report writes the same `JsonReport` document. Counts match by construction.

### 4.3 Line and column numbers

`StyleFinding` has no position today. Line/column are computed **only on the file-loading path** (`LintFiles` and the CLI), never in
the live editor, by `SourceMap` (one pre-scan per file that maps element document-order index to line/column).
- Structural findings use the matched element's start position (exact).
- Word findings use the offset of the excerpt in the run text; the column is exact only for a single text node, otherwise the
  element's start column.
- Topic-level findings and file-level findings have `Line = null`. The SARIF region is omitted and the GitHub annotation is
  file-level; both formats accept that.
- `Line`/`Column` on `StyleFinding` are `[JsonIgnore]`, so the plugin's feedback log shape does not change.

Tests: `LineNumberTests.Finding_reports_line_and_column_of_excerpt`, `Single_line_file_falls_back_to_column`,
`Topic_level_issue_has_no_line`.

### 4.4 Arguments (the v1 surface: 11 flags)

```
housestyle check <path>... [options]

<path>                 A Flare project folder (.flprj), a folder, or a topic/snippet file (.htm .html .flsnp). Default: current directory.
                       A folder inside a project checks only that subtree but uses the whole project's guide.

--guide <dir>          Style guide folder (Content\Resources\StyleGuide with Google and Company). Default order: --guide,
                       env HOUSESTYLE_GUIDE, the guide next to the exe, the bundled guide. The project's own variables and
                       glossary are always the last layer. The CLI never reads %APPDATA%, so a build agent matches any other machine.
--fail-on <level>      error (default) | warning | suggestion (alias: tip) | never. Lowest severity that fails the run.
--max-warnings <n>     Also fail when new warnings exceed n. Default: unlimited.
--baseline <file>      Issues listed in the file are existing: not shown, not counted for the exit code.

--format <f>           text (default) | json | sarif | github. One format per run.
--output <file>        Write the report to a file instead of stdout. Stdout carries only the report, so --format json > out.json is clean.
--changed-since <ref>  Only files changed since a git ref (git diff --name-only --diff-filter=ACMR <ref>...HEAD). PRs: origin/main.
--files-from <file|->  Newline list of paths; '-' reads stdin.
--exclude <glob>       Repeatable (e.g. "Content/Archive/**"). Default include: *.htm *.html *.flsnp.
--quiet                Summary line only.
--verbose              Timings, resolved guide folders, skipped files, on stderr.

housestyle baseline <path>... [--guide] [--exclude] [--changed-since] [--files-from]
                       Writes the baseline (default .housestyle-baseline.json, or --output). The one way to write a baseline.
housestyle export llm  Alias of --llm-export (built output).
housestyle --help
```

Fixed behaviour, not flags: no files matched = exit 2 (a broken glob cannot pass a gate). Conditions are not evaluated (topic
source is checked, as in Flare). Always skipped: `Content\Resources\StyleGuide` and files over 2 MB (as `ActionRunner.ProjectFiles`).
Env vars: `HOUSESTYLE_GUIDE` only.

### 4.5 Output formats

All reports are pure Core code over `CheckResult`; no libraries. Reports list **new** issues only; baselined issues are counted in
the summary.

- **text**: sorted by file, line. `--quiet` prints only the summary. Shown in section 3.
- **json**: `{ "version": 1, "tool": "housestyle", "guide": [...roots...], "summary": {files, issues, errors, warnings, suggestions, baselined, seconds}, "issues": [ {file,line,column,endLine,endColumn,rule_id,severity,message,excerpt,replacement,link,layer,source,fingerprint} ] }`.
  Snake_case, using the same names as the MCP `lint` output (`rule_id`, `replacement`, `link`, `layer`, `severity`, `message`), so one
  schema serves both. `severity` uses the stored token (`error|warning|suggestion`). This is **the same document the pane's Save report writes** (IA 4.5).
- **sarif**: SARIF 2.1.0, one `run`, `tool.driver.name = "House Style"`, `rules[]` from the distinct rule ids (`helpUri` = guide link,
  `defaultConfiguration.level` = error|warning|note), `results[]` with `ruleId`, `level` (suggestion maps to note), `message.text`
  (plus the suggested replacement), `locations[].physicalLocation` (repo-relative `uri`, `uriBaseId: %SRCROOT%`, `region` when the line is known),
  `partialFingerprints: { "housestyle/v1": fingerprint }` (GitHub code scanning uses it to track an alert across commits), and
  `fixes[]` only when `replacement` exists.
- **github**: `::error file=Content/a.htm,line=41,col=9,endLine=41,endColumn=20,title=google:word-list/12::message` (and `::warning`, `::notice`
  for suggestions). Properties and message are escaped per the workflow-command spec (`%`, `\r`, `\n`, `:`, `,`). GitHub shows at most
  10 errors, 10 warnings and 10 notices per step, so emit by severity and end with a `::notice::` "N more issues: see the SARIF report" when capped.

Diagnostics go to stderr. The API key never appears in output.

### 4.6 Baseline (legacy adoption)

Problem: 4,000 topics, thousands of existing issues; "fail on any error" is un-adoptable.

- `housestyle baseline <project> --output .housestyle-baseline.json` records every current issue as a fingerprint. Commit the file.
  `housestyle check <project> --baseline .housestyle-baseline.json` then fails only on issues not in the file.
- **Fingerprint** survives unrelated edits (line shifts, reflow) yet differs for a real new occurrence:
  `sha1( ruleId | normalized project-relative path | normalized excerpt (collapse whitespace, lower-case) | normalized 40-char context before | occurrenceIndex )`
  where `occurrenceIndex` is the N-th identical (rule, excerpt) within the file. Moving the existing issue is not new; adding a second
  "click on" to a topic that already had one is. Reuse `Dismissals.Key` normalization. No line numbers. Defined once in `Baseline.cs`.
  Renamed topics look new; that is accepted in v1 (the path-ignoring escape hatch is in section 7).
- **File format** (human-diffable, grouped by file, sorted):
  ```json
  { "version": 1, "tool": "housestyle", "guide_stamp": "42117:6381920000", "created": "2026-10-12",
    "files": { "Content/Install/setup.htm": { "google:word-list/12": [ { "h": "a1b2c3d4", "x": "click on" } ] } } }
  ```
  `h` = short fingerprint (12 hex), `x` = excerpt (for reading the file; ignored for matching). `guide_stamp` is informational.
- **Stale count**: `check` prints "38 baselined issues are fixed: re-run `housestyle baseline` to shrink the file." It never fails on that.
- **Guide changes**: after a guide change, re-run `housestyle baseline`. Until then, a rule that is newly enabled or renamed flags old
  text as new. Documented in the user guide; no merge flag in v1.
- Baseline and `--changed-since` both apply (changed files, minus baselined issues).

Adoption recipes (documented):
- Main branch gate: `housestyle check . --baseline .housestyle-baseline.json`.
- PR gate on legacy content: `housestyle check . --changed-since origin/main --baseline .housestyle-baseline.json`.

### 4.7 Exit codes

| Code | Meaning |
|---|---|
| 0 | Ran; no new issue at/over `--fail-on` (baselined issues and below-threshold issues do not fail) |
| 1 | New issues at/over `--fail-on`, or `--max-warnings` exceeded |
| 2 | Usage/config error: bad args, no project or guide found, unreadable baseline, no files matched |
| 3 | Internal error |

This is the one exit-code table. Gap 3 and gap 5 reuse these codes; no other plan defines its own.

A single unreadable or invalid topic does not abort the run (matches the `LintFiles` try/catch). It is reported as a warning issue with
`rule_id` `check:unreadable-file`, so it shows in SARIF and fails only with `--fail-on warning`.

Decision logic lives in Core `CheckResult.ExitCode(options)` so it is testable without the exe.

### 4.8 AI review in CI

Removed from this plan. See section 7.

### 4.9 Performance

`LintFiles` already batches 1,500 lines per linter call and is what "Report before build" uses on the whole project. Target: under
5 s for 1,000 topics on a CI agent after the guide is compiled. Measure with the `tools/FlareAI.Bench` corpus (250 topics) and print guide
load time in `--verbose`. Single-threaded in v1; parallelism is a later optimisation after `Lint` is verified thread-safe.
`--changed-since` and `--files-from` make typical PR runs sub-second.

### 4.10 Platform: Windows-only, Linux CI not supported in v1

- The exe is net48 (Core is net40). On GitHub it runs on `windows-latest` (already what `build.yml` uses). Madbuild runs on Windows, so the post-build case is native.
- **Mono on Linux: do not promise it.** Guide and project paths are hard-coded with backslashes (`Content\Resources\StyleGuide`,
  `Project\VariableSets`, `Project\Glossaries`), and net48 on Mono is not tested in CI.
- Path forward if Linux demand appears: section 7.
- v1 release asset: `HouseStyle-cli-<ver>.zip` (exe, dlls, bundled StyleGuide with compiled Google tables, no B3 files), so a pipeline
  downloads one file and needs no network or Opus at run time. `build.yml` attaches it to the release and runs a smoke job:
  `housestyle check` against the repo's `styleguide/` fixtures on `windows-latest`.

### 4.11 Copy-paste pipeline snippets (ship in the user guide and `docs/ci/`)

**GitHub Actions** (annotations on the changed lines, plus SARIF to code scanning):
```yaml
name: docs-style
on:
  pull_request:
    paths: ['Content/**', 'Project/**']
jobs:
  style:
    runs-on: windows-latest
    permissions: { contents: read, security-events: write }
    steps:
      - uses: actions/checkout@v4
        with: { fetch-depth: 0 }
      - name: Get House Style
        shell: pwsh
        run: |
          gh release download --repo Praeviderant/house-style --pattern 'HouseStyle-cli-*.zip' --dir hs
          Expand-Archive hs\*.zip -DestinationPath hs\cli
        env: { GH_TOKEN: '${{ github.token }}' }
      - name: Check topics
        shell: pwsh
        run: |
          hs\cli\housestyle.exe check . --guide styleguide --changed-since origin/${{ github.base_ref }} --baseline .housestyle-baseline.json --format github
          hs\cli\housestyle.exe check . --guide styleguide --changed-since origin/${{ github.base_ref }} --baseline .housestyle-baseline.json --format sarif --output housestyle.sarif
      - uses: github/codeql-action/upload-sarif@v3
        if: always()
        with: { sarif_file: housestyle.sarif }
```
(Repo owner/name placeholder: fill in at implementation. The check runs twice because one run writes one format.)

**madbuild post-build event** (Flare target > Advanced > Post-Build Event, or a `.bat` the target calls; a non-zero exit fails the build step):
```bat
"C:\tools\housestyle\housestyle.exe" check "%1" --guide "C:\docs\styleguide" --baseline "%1\.housestyle-baseline.json" --fail-on error --format text --output "%1\Output\style-report.txt"
if errorlevel 1 exit /b 1
```
Check the **source** (topics), not the built output. Run madbuild and the check as separate pipeline steps if the post-build event is not wanted.

Azure DevOps and Jenkins snippets: section 7.

## 5. Files to add / change

Add:
- `tools/FlareAI.Mcp/Cli/Args.cs`, `CheckCommand.cs`, `GitScope.cs` (git diff shelling, changed-file list), `Help.cs`
- `src/FlareAI.Core/Check/ProjectChecker.cs`, `CheckOptions.cs`, `CheckResult.cs`, `Issue.cs`, `Baseline.cs`, `SourceMap.cs`, `ProjectVariables.cs`
- `src/FlareAI.Core/Check/Reports/{TextReport,JsonReport,SarifReport,GitHubAnnotations}.cs`
- `docs/ci/github-actions.yml`, `docs/ci/madbuild-post-build.bat`
- `docs/user-guide/Content/...` topic "Check topics in a build pipeline" and "Adopt the checker on an existing project"; link from "Report before build"

Change:
- `tools/FlareAI.Mcp/Program.cs`: verb dispatch; MCP stays the default.
- `src/FlareAI.Core/Actions/ActionRunner.cs`: `LintFiles` uses `SourceMap`; extract `LoadBlocks(path)`; the pane's Check project calls `ProjectChecker`.
- `src/FlareAI.Core/Actions/ResponseParser.cs` (`StyleFinding`): add `Line`, `Column`, `EndLine`, `EndColumn` (all `[JsonIgnore]`).
- `src/FlareAI.Core/Style/GuideLayer.cs`: extract the `.flvar` reader to a reusable static.
- `scripts/package.ps1`, `scripts/install-plugin.ps1`: also copy the exe as `housestyle.exe`; package.ps1 emits `HouseStyle-cli-<ver>.zip`.
- `.github/workflows/build.yml`: attach the zip to the release; CLI smoke job.
- `README.md` (CI section), `CLAUDE.md` Commands block (one line for `housestyle check`), `docs/MANUAL-TESTS.md`.

Tests (in `tests/FlareAI.Core.Tests`, run on any OS):
- `ProjectCheckerTests.Lints_project_like_the_plugin` (same findings as `ActionRunner.LintFiles` for a fixture project, including variable-resolved text).
- `ProjectCheckerTests.Skips_style_guide_folder_and_large_files`, `Honors_exclude`, `Unreadable_file_is_warning_issue`.
- `PaneAndCliProduceSameCounts`: the pane's Check project and `housestyle check` return identical counts and JSON for one fixture.
- `LineNumberTests` (section 4.3).
- `BaselineTests.Moved_issue_is_not_new`, `Second_identical_occurrence_is_new`, `Edit_elsewhere_in_file_keeps_fingerprints`, `Fixed_issues_reported_as_stale`, `Roundtrip_is_sorted_and_stable`.
- `ExitCodeTests.*`: threshold, max-warnings, no-files-matched, unreadable baseline.
- `ReportTests.Sarif_has_required_fields_and_partial_fingerprints`, `Sarif_levels_map`, `GitHub_escapes_properties_and_message`, `GitHub_caps_with_overflow_notice`, `Json_schema_snake_case_matches_mcp_lint` (golden files under `tests/.../Golden`).
- `GitScopeTests`: `Parses_changed_files`, `Renames_and_deletions_ignored` (Core with injected diff text).
- CI smoke: bench corpus via CLI equals `FlareAI.Bench --no-ai` counts.

## 6. Risks, edge cases, decisions; effort; sequencing

Risks / edge cases:
1. **Line numbers**: approximate for word findings in long paragraphs; structural findings are exact. Degrades to file-level annotations (GitHub accepts a missing line).
2. **Result parity**: the plugin applies personal `Dismissals` (`%APPDATA%\dismissed.json`); the CLI cannot see them. Decision: the CLI ignores personal dismissals; team-wide suppression is the guide's Setting table and the baseline. Document "I dismissed it in Flare but CI still complains."
3. **Conditions and snippets**: topics are checked as source, same as the plugin. Snippet content is checked in its own file, so findings are reported once.
4. **Guide location in CI**: the guide may live in another repo. The clone step goes in the snippets; `--guide` and `HOUSESTYLE_GUIDE` are the only ways to point at it. Print guide roots in `--verbose`.
5. **Google guide is compiled output** (GuideCompiler): the CLI only reads it. The release zip includes the compiled tables, so CI needs no network or Opus.
6. **Annotation noise** on first adoption: mitigated by the baseline and `--changed-since`; GitHub's 10-per-type cap is handled.
7. **Flare-agnostic Core**: all new Core code uses System.Xml/IO/Newtonsoft only. Process and git live in the exe.
8. **Naming and back-compat**: copy, not rename, so existing MCP configs keep working.
9. **Signing / SmartScreen / AV** on a downloaded exe in CI: the same SignPath pipeline signs it; the zip carries it.
10. **Mono/Linux**: state "Windows runner required" in the docs.

Decisions (resolved at the gate):
- Default `--fail-on` is `error`, matching the in-product meaning of Error.
- The `housestyle.exe` copy is a second file; the project output is not renamed.
- No `.housestyle.json` in v1: the pipeline line is the policy, and it is short enough.
- No public composite GitHub Action in v1.

Effort: **M, about 6 working days** for phase 1 and 2.
- Phase 1 (~4 days): verb dispatch, `ProjectChecker` over `LintFiles` + structure checks, `SourceMap`, variable resolution, the 11-flag surface, exit codes, text + json + github, `--files-from` and `--changed-since`, the pane's Check project wired to `ProjectChecker`, packaging as `housestyle.exe` and the CLI zip, tests, first user-guide topic with the GitHub and madbuild snippets. This alone gates a build.
- Phase 2 (~2 days): `baseline` verb and `--baseline`, stale-count note, SARIF, CI smoke job in `build.yml`, the "Adopt the checker" topic.

Sequencing rationale: ship phase 1 first because even without a baseline it serves greenfield projects and the madbuild case. Ship the baseline immediately after: without it legacy teams cannot turn the gate on, which is the real adoption blocker.

## 7. Later, on request

Each item is cut from v1 and returns only when someone asks for it. Designs are kept as notes.

- **Flags**: `--rule`, `--skip-rule`, `--layer`, `--severity-override`, `--min-severity` (rule control stays in the guide's Setting table); `--include`; `--no-project-layer`; `--use-plugin-settings`; `--relative-to`; `--color`; `--allow-empty`; `--strict-files`; `--changed-lines-only` (uses `git diff -U0` hunks and `SourceMap` lines; falls back to the whole file for single-line topics).
- **Baseline**: `--update-baseline` (the `baseline` verb covers it); `--merge` for guide changes; `--strict-baseline` (fail on stale entries); `--show-baselined`; `--baseline-ignore-path` (match on file name, rule and excerpt for renamed topics).
- **Formats**: junit (one testcase per issue, failures at or over threshold, `--junit-pass-cases` off by default); azure (`##vso[task.logissue]`, `--azure-set-result`); `$GITHUB_STEP_SUMMARY` Markdown table. Snippets for Azure DevOps (`PublishTestResults` on the JUnit file) and Jenkins (`junit 'hs.xml'`) go with junit.
- **Config files and ignores**: `.housestyle.json` (guide, baseline, fail-on, excludes in version control); `.housestyleignore` (gitignore-style globs).
- **Other verbs**: `guide validate` (the MCP `validate_guide` tool already does it).
- **Env vars**: `HOUSESTYLE_FORMAT`, `HOUSESTYLE_FAIL_ON`, `ANTHROPIC_API_KEY`.
- **AI review in CI** (`--ai`), with these guardrails kept as notes: key from `ANTHROPIC_API_KEY` or `--key-env NAME` only (no key-valued flag, no DPAPI, never echoed); runs only on files in scope, refuses an unscoped run over 25 topics unless `--ai-all`; cost cap `--ai-max-usd` (default 1.00) checked against `Usage.Snapshot()`, remaining files reported as "not AI-reviewed (budget)"; AI findings advisory (`suggestion`) unless `--ai-severity`; `--ai-cache` reuses `ReviewCache`; exit 3 only with `--ai-required`; topic text leaves the machine, so print "AI review sends topic text to {BaseUrl}" in `--verbose`. Estimated as a separate project of about 3 days after the deterministic gate is trusted.
- **Cross-platform CLI**: normalise guide path building through one `GuidePaths` helper; multi-target Core to `netstandard2.0`; a `tools/FlareAI.Cli` net8 single-file publish for `win-x64;linux-x64;osx-arm64`. The `check` code lives in Core plus `Cli/`, so lifting it is a move. Effort L, about 8 to 12 days. Triggered by Linux CI demand.
- **Public composite GitHub Action** (`praeviderant/house-style-action@v1`), a thin wrapper that downloads and runs the exe.
- **Footer link in the findings pane** ("Use this in your pipeline"): cut because the pane toolbar is crowded; revisit only if the toast link proves insufficient.
