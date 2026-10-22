# Bench v2: per-rule metrics, baselines and the CI gate

Design: `dist/audit/TEST-STRATEGY.md` section 4, epic #66, issue #81. Code: `tools/FlareAI.Bench` (`LabelScoring.cs` is the scoring, baseline and
gate; `Program.cs` the runner). Scoring itself is `tools/FlareAI.Scoring` (see `CORPUS.md`); Bench does not reimplement it.

## Run

```powershell
dotnet build tools/FlareAI.Bench -c Release
$b = .\tools\FlareAI.Bench\bin\Release\net48\FlareAI.Bench.exe
& $b --no-ai                       # lint + structure over the corpus, scored against labels.jsonl (about 2 s for 250 topics; no key, no network)
& $b --no-ai --check               # same, then compare with tests/baselines/bench-lint.json; exit 1 on a regression
& $b --no-ai --update-baseline     # rewrite the baseline from this run, list the rule rows that changed
& $b baseline-diff old.json new.json   # rule rows that differ between two baseline files
.\scripts\check-baseline.ps1       # what CI runs: --check plus the pull request label rules
```

Other options: `--labels <file>` (repeatable; default `labels.jsonl` in the corpus project folder or `--corpus` root), `--gate-split all|dev|test`
(default `all`), `--baseline <file>`, `--out <dir>`. `--check` and `--update-baseline` refuse AI runs, `--sample`, `--examples` and `--rewrites`:
the baseline must be deterministic and offline. AI runs (`--model ...`, key from `ANTHROPIC_API_KEY`) stay opt-in and are never part of a PR job; they
also write the label scores, so their per-rule numbers can be compared by eye.

## Outputs (in `--out`, default `bench-out/lint`)

| File | Content |
|---|---|
| `report.md` | v1 report (recall by kind, not-seeded findings, ...) plus **Label scoring**: totals per split, per layer, per `context.line` and `context.span`, worst rules by false positives and by misses (dev split only), wrong-rule pairs |
| `score.json` | The baseline-shaped document for this run (below) |
| `scored-findings.jsonl` | Every finding with its outcome (`TruePositive`, `FalsePositive`, `WrongRule`, `Ambiguous`, `Unscored`) and deciding label id |
| `findings.jsonl`, `metrics.json`, `diag.log`, `feedback/` | As before (v1 recall metrics are kept) |
| `delta.md` | With `--check`: the gate verdict and every changed row |

`score.json` / the baseline: `splits.{all,dev,test}`, each with `overall`, `coverage`, `findings`, `labelsScored`, `unanchoredLabels`, one table per dimension
(`rule`, `layer`, `context.line`, `context.span`, `project`, `split`, `kind`; each row has TP, FP, wrong rule, FN, wrong-rule-only, ambiguous, unscored,
support, detected, precision, recall, F1), `cleanFalsePositives` (`labelId <- ruleId` for every finding on a clean or exempt label) and `wrongRulePairs`
(`expected rule -> fired rule`: count). Nothing time- or machine-dependent is stored, so two runs produce identical bytes.

## The gate

`--check` fails (exit 1) when, in the gated split (default `all`; `--gate-split test` for the strict held-out view once more projects exist):

1. a rule gains a false positive on a `clean` or `exempt` label (new `labelId <- ruleId` entry);
2. fewer labelled errors are caught (duplicate findings on one error do not count);
3. a layer's precision or recall drops by more than 0.5 point;
4. a rule with support (or scored findings) of 10 or more drops more than 5 points in recall (precision);
5. wrong-rule findings rise;
6. label coverage (scored findings / non-ambiguous findings) drops.

It always prints improvements and every changed row per rule, layer, line context, span context and project, so a failure names what moved. Rule rows
are keyed by the rule that fired (precision side) or, for a missed violation, the first rule the label accepts (for example `google:*`).

Why `all` and not `test`: while DigitalBanking is the only project, a 30% split is too small to catch regressions in rules with few hits. The dev/test
split is still stored and printed; the "worst rules" lists use dev only so the test split is not used to tune rules.

## Updating the baseline

Other changes (rules, engine, guide) move the numbers. When the change is intended:

```powershell
& $b --no-ai --update-baseline      # prints the rule rows that changed
git add tests/baselines/bench-lint.json
```

The pull request then needs the `baseline-change` label and a line in its body for every changed rule row (the printed list; the check is that each rule
id appears in the body). The same pull request must not change labels: a PR that changes `corpus/**/labels.jsonl` together with `styleguide/**`,
`src/FlareAI.Core/Style/**` or `tools/FlareAI.GuideCompiler/**` fails unless it carries the `label-fix` label. Both checks are in `scripts/check-baseline.ps1`
(pull requests only; labels and body reach it through `PR_LABELS` and `PR_BODY`).

If another PR merged first, rebase and regenerate: the baseline is a pure function of the code and labels at that commit, so a conflict in it is always
resolved by regenerating, never by editing.

## CI

`build.yml` runs the gate after `dotnet test` (the build output already exists): about 2 s of bench plus about 1 s of startup, far inside the 4 minute budget
of the design; nothing needs a secret, so it runs on fork PRs. The report folder is uploaded as the `bench-lint` artifact. Nightly (full corpus, fresh-seed
holdout) and weekly AI jobs depend on the corpus v2 projects and the CorpusKit injector and are not in this change.

## First baseline

`tests/baselines/bench-lint.json` is generated from DigitalBanking alone (250 topics, 981 labels, 429 violations) with the behaviour of `main` at f71d083.
Google lint matches the hand count in `dist/audit/precision.md`: 23 true positives of 57 findings.
