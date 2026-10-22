# AI cost and quality under realistic use

Measured October 2026 on `guide-accuracy` (85e231f) with the plugin's built-in routing: review, rewrite, restructure,
alt text and extract on Sonnet 5.5 (medium effort), refine and topic assists on Haiku 5.5, Tune "Draft a change" on
Opus 5.5 (high). Prices: Anthropic list prices per million tokens (Haiku 5.5 $0.10 in / $0.50 out / $0.01 cache read,
Sonnet 5.5 $2 / $10 / $0.20, Opus 5.5 $4 / $20 / $0.20; cache writes 1.25x input for 5 minutes, 2x for 1 hour). These
are the same prices as `Usage` in `Routing.cs`.

**Total spent on this measurement: $15.73** (635 model calls, cap $25).

## Headline

| | Monthly cost |
|---|---|
| Writer, light / typical / heavy | about **$8 / $22 / $48** |
| Style guide owner, on top of their writing | about **$19** as shipped (Validate's AI check is $17 of it); about **$2** if Validate checks only the team's own rules |
| Team of 5 (4 writers + 1 owner who also writes) | about **$130** typical, **$260** in a heavy month |
| Recommended gateway budget for 5 | **$200/month** with alerts at 50% and 80%, per-key limit $60/month; raise to $300 if Validate stays as is and the owner runs it more than once |

More than half of a typical writer's cost is not the work itself: it is rewriting the ~35k-token prompt cache when a task
is first used in a session. The improvements below (scope Validate, cache lifetime per task, smaller prompts, cheaper
re-checks) would cut a typical writer month from about $22 to about $10-12 and the owner's from $19 to $2 (estimated from
the measured token mix, not measured), with no change in what the writer sees.

> **Since this measurement** most of the improvements have been made (branch `ai-quality`). The figures in this report are
> the measurement as it was; [Changes since the measurement](#changes-since-the-measurement) lists what changed and gives
> **estimated** new costs: about **$6 / $13 / $30** a writer-month and **$1.50** for the owner, about **$65** for a team of 5.

## Method

- A throwaway harness (`dist/ai-run/`, git-ignored) drives the real Core code: `ActionRunner` (`PickCheckBlocks`, `Lint`,
  `StructureCheck`, `LintSnippets`, `PrepareReview` → `CallReview` → `AnchorReview` with a `ReviewCache` in front, exactly as
  the Findings panel does; `ApplyFixes`; `Rewrite`; `Restructure` when the wording pass says the task needs it, as the
  dialog's button does; `Refine`; `Assist`; `SuggestAlt`; `ExtractRules` on `NoteText.ReadFile` output; `VerifyExamples`;
  `Rewrite(..., lint: false, "draft")` with `GuideEdits.Instruction`). Topics are loaded from disk and edited in memory only.
- The model client is a copy of `AnthropicClient.Complete` (same `BuildBody`, headers, `Parse`, `Check`) that also records
  every call (tokens, cache reads and writes, latency, stop reason, errors) and refuses calls past the spend cap.
- Corpora: `corpus/DigitalBanking` (250 labelled topics) and four v2 projects (api-docs, end-user-help, hardware-install,
  policy; read-only from their worktrees). Topics per writer week: 30% short, 40% medium, 30% long by file size, half of them
  topics with labelled AI-kind violations (for scoring), plus 2 snippets.
- Quality: AI review findings scored with `tools/FlareAI.Scoring` against DigitalBanking's labels (limited to the topics
  checked: 58 topics, 56 AI-kind violations). The v2 labels use a newer rule-id and `status` scheme that the scorer on this
  branch does not read yet (it reports 0% for them), so v2 quality is from a hand read only. Hand reads: 20 random AI
  findings from the v2 runs, every Ask (16), the rewrites, 20 alt texts, 20 refines, the assists, both imports, 3 drafts.
- Pilot first (~$2), then five writer weeks, one labelled-quality sample and one owner month.

### Usage model

Writer, per week (typical): opens 20 topics, runs Check style with AI on 17 (short and long, some snippets), applies about
60% of the fixable proposals, re-checks about 10 topics after editing, reopens a few unchanged ones; Rewrite selection 10x on
dense paragraphs (30+ words, preferring ones with variables, code, links); Ask 5x ("Make this a numbered list", "Shorten this
to two sentences", "Make this a bulleted list of the options", "Rewrite this in plain words for a first-time user", "Split
this into two paragraphs"); Refine 5x; Topic assists on 5 topics; Suggest alt text on 5 images. Light = about 40% of that,
heavy = about 2.5x (see the table under "Per writer").

Style guide owner, per month: imports the team's old guide once (Build style guide > notes: a synthetic ~1,900-word,
four-page wiki-style team guide with a word list table, comments and page chrome, as `.txt` and as `.docx`), runs Validate
style guide's AI prose check once, uses Tune "Draft a change with AI" 3x.

## Per action

Measured over all runs. "Warm" means the always-on prompt block was already cached (read at 0.1x); see "Cold starts" for the
first use in a session.

| Action | Model | Calls per action | Tokens per action: uncached in / cached / out | $ per action (warm) | p50 / p95 seconds | Failures |
|---|---|---|---|---|---|---|
| Check style, AI review (whole topic) | Sonnet | 1.8 | 11.0k / 78k / 0.6k | 0.053 (see sizes below) | 5.2 / 14.4 | 0 errors; 4 findings dropped by the contract in 361 review calls |
| Re-check after edits | Sonnet | 1.5 | 11.7k / 76k / 0.6k | 0.046 | 4.9 / 14.9 | 0 |
| Re-check, unchanged topic | — | 0 | — | 0.000 | 0.1 | review cache hit 33/33 units |
| Rewrite selection (one paragraph) | Sonnet | 1.7 | 17.3k / 59k / 0.3k | 0.049 | 3.7 / 15.9 | 26 contract retries in 93 rewrite calls, 16 still partial after the retry; 0 truncations |
| Ask (wording pass, then Restructure when needed: 10 of 16) | Sonnet | 1.7 | 13.9k / 59k / 0.4k | 0.044 | 6.4 / 10.3 | 0; 10/10 restructures valid |
| Refine one proposal | Haiku | 1.0 | 0.8k / 35k / 0.6k | 0.0007 | 4.4 / 7.8 | 0 |
| Topic assists (3 calls) | Haiku | 3.0 | 5.2k / 104k / 2.7k | 0.003 | 11.1 / 15.7 | 0 |
| Suggest alt text | Sonnet | 1.0 | 0.7k / 35k / 0.06k + image | 0.009 | 2.8 / 9.3 | 0 |
| Build style guide import, .txt (18 chunks) | Sonnet | 18 | 8k / 599k / 11k | 0.24 (+0.14 cold) | 29 s total | 0 |
| Build style guide import, .docx (3 chunks) | Sonnet | 3 | 4.6k / 106k / 8.7k | 0.12 | 40 s total | 0 |
| Validate style guide, AI check, per rule with examples | Sonnet | 1.4 | 7.3k / 50k / 0.1k | 0.026 | 3.2 per rule, sequential | 0 |
| Tune "Draft a change" | Opus | 1.3 | 3.3k / 26k / 0.6k | 0.02-0.03 warm, 0.30 cold | 7.1 / 9.2 | 0 |

Check style by topic size (first check, warm):

| Topic words | Topics | Review units | $ per check | p50 / p95 s |
|---|---|---|---|---|
| under 300 | 50 | 1.0 | 0.033 | 3.8 / 6.4 |
| 300-800 | 26 | 1.8 | 0.093 | 7.2 / 14.9 |
| 800-2,000 | 12 | 5.5 | 0.140 | 10.3 / 14.5 |
| over 2,000 | 2 | 12.5 | 0.267 | 15.9 / 15.9 |

Where review money goes (361 review calls, $10.57): uncached input 44% (the per-unit "terms" block of word rules averages
13k characters, plus the unit text and lint summary), cache reads 31% (the always-on block, ~35k tokens on every call),
cache writes 15%, output 10%.

No call failed: 0 HTTP errors, 0 rate limits, 0 refusals, 0 truncations in 635 calls.

### Review cache

- Unchanged topic reopened: 33 of 33 units served from the review cache, $0.
- Re-check after applying fixes: 22 of 97 units hit (23%). Any applied fix changes the unit's text and its lint summary, so the
  unit's prompt (the cache key) changes; with 60% of fixable proposals applied most sections change.
- The model's prompt cache: 83% of review input tokens were cache reads in this run, but the run was compressed into two
  hours. In real use the hit rate depends on sessions (next section).

### Cold starts: the biggest cost

Every call carries the same ~35k-token always-on block (instructions, Google always-on rules, the Headings, UI, Lists and
Steps packs), cached for 1 hour at 2x input. The comment in `ActionRunner.Ask` assumes one cache entry is shared by every
task. It is not: the structured-output schema (`output_config.format`) is part of the cached prefix, so each task with its
own contract writes its own entry, and caches are per model. Measured cache writes, 1 minute apart, same system text:
review 35,075 tokens, rewrite and draft 34,891, alt 34,700, restructure (no schema) 34,440, extract 35,242, the three
assist parts three separate writes.

So the first use of each of these in a session pays a fresh 1-hour write:

| Cache entry | Cold first call | Warm call | Premium per cold start |
|---|---|---|---|
| Sonnet: review (also Validate), rewrite (also Ask's wording pass), restructure, alt, extract | about $0.14 | about $0.007 + the call | $0.133 each |
| Haiku: refine, assist x3 | about $0.007 | — | $0.007 each |
| Opus: draft | about $0.28 | about $0.007 + the call | $0.27 |

A writer who works in two sessions a day (more than an hour apart) pays the review write about 10 times a week, and pays a
write for rewrite, restructure and alt almost every time they use them, because those are used a few times a week.

## Per writer

Warm cost from the per-action table plus cold starts at the session counts shown.

| | Light | Typical | Heavy |
|---|---|---|---|
| Topics checked / re-checked | 8 / 3 | 17 / 10 | 40 / 25 (more long topics) |
| Rewrite / Ask / Refine / Assists / Alt | 3 / 1 / 2 / 1 / 1 | 10 / 5 / 5 / 5 / 5 | 25 / 12 / 15 / 15 / 15 |
| Work (warm calls) per week | $0.91 | $2.42 | $6.83 |
| Sessions per week; cold writes | 4; review 4, rewrite 2, alt 1 | 8; review 8, rewrite 6, restructure 3, alt 3 | 10; review 10, rewrite 10, restructure 6, alt 5 |
| Cold starts per week | $0.93 | $2.66 | $4.23 |
| **Per week** | **$1.84** | **$5.08** | **$11.06** |
| **Per month (4.33 weeks)** | **$8** | **$22** | **$48** |

Measured directly, the five compressed writer weeks cost $0.50 (DigitalBanking: short topics, few dense paragraphs),
$1.75, $3.16, $2.80 and $3.50+ (policy: long topics; stopped by the run cap during its rewrites), almost all warm.

## Per style guide owner (per month, on top of their writing)

| Task | Cost | Notes |
|---|---|---|
| Build style guide import, once | $0.12-0.38 | .txt: 18 chunks, 70 rules; .docx: 3 chunks, 59 rules (see quality) |
| Validate style guide, AI check, once | **$17.10** as shipped | `Examples()` returns every rule with a Bad/Good example in every layer: 629 rules (624 of them Google's), 886 model calls, run one at a time: about 34 minutes. The team's own 5 example rules alone: about $0.14 |
| Tune "Draft a change", 3x | $0.90-1.05 | Opus; each draft is usually the first Opus call in its session ($0.30 cold); a draft the AI says needs restructuring adds a Sonnet restructure ($0.15 cold) |
| **Total** | **about $19** (about **$2** with Validate limited to the team's layers) | |

## Team of 5

4 writers + 1 owner who also writes, typical month: 5 x $22 + $19 = **about $130**. Heavy month (deadline, migration):
5 x $48 + $19 = **about $260**. Recommended gateway budget: **$200/month** with alerts at 50% and 80% and a per-key limit of
$60/month (a heavy writer is about $48); budget $300 if the owner will run Validate's AI check more than once a month before
it is scoped. After the improvements below, a typical month is about $60 and $120 is a comfortable budget.

## Quality

### AI review against labels (DigitalBanking, 58 topics checked)

| AI-kind violations | Labelled | Flagged, right rule | Flagged, scored as wrong rule | Missed |
|---|---|---|---|---|
| marketing | 15 | 0 | 15 | 0 |
| passive | 11 | 9 | 0 | 2 |
| timeline | 9 | 9 | 0 | 0 |
| undefined_abbr | 7 | 7 | 0 | 0 |
| wordy | 6 | 0 | 6 | 0 |
| vague | 6 | 0 | 6 | 0 |
| ui_not_bold | 2 | 2 | 0 | 0 |
| **Total** | **56** | **27** | **27** | **2** |

So 54 of 56 (96%) were flagged; half cite a rule the labels don't accept. Marketing claims cite Google's excessive-claims rule
instead of the team's `company:claims` (which overrides it). Wordy and vague findings are mostly uncited ("general"), which
the scorer counts as a wrong rule. Over both runs: 42 AI true positives, 10 false positives on text labelled clean, 35 wrong
rule, 57 on unlabelled text. Plain AI precision on scored findings: 42 of 52 that weren't a citation mismatch (81%).

### Hand read of 20 random AI findings (v2 projects)

14 correct and safe, 1 debatable (sentence case on a regulation's formal control title), 5 problems: "should have reported"
→ "must report" in a policy (changes an obligation); "Clients should" → "Clients must" in an API reference (changes a
normative keyword); "The target message's ID" → "The ID of the message" (drops the API's own term); "This connects GP1 to
XTR" → "R11 connects GP1 to XTR" (invents a fact); one finding citing the "members" terminology rule for an audience
comment it has nothing to do with.

### Rewrite and Ask: the safety net held

- 52 edits applied (36 rewrites, 16 asks), 36 of them in paragraphs with inline markup (37 variables, 27 code spans,
  9 cross-references, 10 bold UI labels, 2 links, 1 snippet): **0 lost or moved markup, 0 malformed documents**, 10 of 10
  restructures passed the validator. 8 rewrites correctly changed nothing (already compliant).
- The run-level contract caught the model rewriting a run that finishes a sentence begun before a variable or code span:
  26 retries in 93 rewrite calls (about 40% more rewrite calls and latency on markup-rich text); 16 stayed partial, and
  the valid changes were kept.
- Problems the validator can't see: a "plain words" Ask that rewrote around a variable produced "The [variable] connected to
  an HDMI monitor" (verb lost; the AI's own note says so). On "Split this into two paragraphs", the wording-only proposal
  deleted a whole sentence it meant to move, and the dialog offers to apply the wording pass before Restructure.
- Rewrites sometimes churn compliant wording ("Check that" → "Make sure that").

### Other actions

- **Alt text:** 19 of 20 useful and specific (pinouts, dimensions, menu items read from the image), but 13 of 20 exceed the
  125-character limit the prompt sets (average 128), and it names hardware by what's printed on it, not by the project's
  product-name variable. One icon misread.
- **Refine:** good when the instruction applies. When it doesn't ("Use select instead of click" on text without "click"),
  it returned the original text or no replacement in 3 of 4 cases, discarding the earlier, better proposal.
- **Topic assists:** descriptions, keywords and micro content are good; glossary "definitions" are often a sentence copied
  from the topic, not a definition.
- **Validate's AI check (40-rule sample):** 33 of 40 rules applied as written; 15 of 20 Bad examples caught, 37 of 39 Good
  examples left clean.
- **Draft a change (Opus):** 2 of 3 drafts were good, targeted rule edits (an exception for fee tables and quoted
  regulations; an exception for exact UI labels). The third correctly refused because the evidence didn't fit the rule,
  but flagged "needs restructure", which costs a pointless Restructure call.

### Build style guide import (synthetic team guide, about 65 intended rules)

- .txt: 70 candidates (31 word-list rows, 39 prose rules), 7 rows auto-skipped as already flagged by the Google layer.
  Rules Google already covers (address the reader as "you", active voice, present tense, sentence-case headings, serial
  comma) were left out as the prompt asks. Nearly every team-specific rule came through, with good Bad/Good examples.
- .docx of the same guide: 59 candidates. Word paragraphs have no blank lines between them, so `GuideBuilder.Chunks`
  produced 3 chunks of up to 4,900 characters (the limit is 3,000) instead of 18: one third of the cost, 15% fewer rules
  (lost "simply" and "easy", folded several rules together, one bad row "and above" → "or later").
- Context-dependent rules became blanket word rows the linter will apply everywhere: "here" (from "click here", "here", "this
  link"), "once" → "after", "below"/"above", "setup" (only the verb is wrong), "hit", "Dashboard", "admin" (the rule allows
  "admin console"). Expect linter false positives unless the owner unchecks or narrows them.
- Duplicates with Google: 7 rows skipped automatically (e-mail, whitelist/blacklist, via, e.g./i.e., in order to, utilize,
  log in); "click here" and the "simply/just" row overlap Google rules but were not skipped.
- Some examples contradict the imported guide itself (a Good example "Click **Save draft**" in a guide that bans "click").

## Top 5 quality issues

1. **Meaning-changing replacements in normative and technical text** (should/must, dropped API terms, invented facts): 4 of
   20 hand-read findings; plus the wording-only Ask proposal that drops a sentence when the task needs restructuring, which
   the dialog still lets you apply.
2. **Citation precedence and grounding:** marketing claims cite Google's rule instead of the team's overriding
   `company:claims` (15 of 15); wordy and vague findings are mostly uncited.
3. **Import turns context-dependent rules into blanket word rows** ("here", "once", "below", "setup", "hit"), which will
   produce linter false positives; .docx imports are chunked too coarsely.
4. **Validate's AI check runs over the whole Google layer:** 886 calls, about $17 and 34 minutes per run, for rules the
   maintainers already verify.
5. **Alt text ignores its own length limit** (13 of 20 over 125 characters), and Refine discards a good proposal when the
   instruction doesn't apply.

## Improvement opportunities (largest saving first)

1. **Scope Validate's AI check to the team's layers** (skip `google`; optionally a 20-rule Google sample): $17 → $0.14 per run,
   34 minutes → seconds. Also run its calls in parallel (`WarmThenParallel`) like the review does.
2. **Stop paying cold writes for rarely used tasks.** Cache the always-on block for 1 hour only on review and rewrite; use the
   5-minute TTL (1.25x) or no `cache_control` for alt, restructure, extract, draft and assist, which are rarely reused within
   the hour. Saves about $0.07 per cold Sonnet call of those (about $0.4 per typical writer week) and $0.14 per cold draft.
3. **Don't send the style guide where it isn't used.** Alt text and topic assists don't apply style rules (alt's prompt is
   self-contained); sending ~35k tokens for a 64-token answer makes alt cost $0.14 cold instead of about $0.003.
4. **Make the prefix shareable or smaller.** Because the schema sits ahead of the system prompt in the cache key, each
   contract is its own 35k-token cache entry. Either shrink the always-on block (it is 100k characters; every call reads
   it, 31% of review cost) or move the shared contracts toward one schema family. Halving the block saves about 30% of all
   review and rewrite cost and halves every cold start.
5. **Cut the uncached "terms" block.** Review sends 13k characters of word rules per unit uncached (44% of review cost);
   a one-paragraph rewrite sends 25k characters (packs are folded into the uncached block for single calls). Send only rows
   whose trigger occurs in the unit's text, and cache packs with a 5-minute TTL for single-call tasks too.
6. **Re-check without re-reviewing what the author just fixed.** After applying the review's own proposals, re-anchor the
   remaining findings instead of re-reviewing the unit (re-check hit rate is 23% today); about $0.46 per typical week.
7. **Fix the rewrite contract retries:** tell the model which runs continue a sentence across markup (or merge them for the
   model), avoiding about 26 extra calls per 93.
8. **Quality fixes:** forbid changing modal verbs and API terms in replacements (or flag them as "needs author"); prefer the
   team layer's rule when it overrides the cited Google rule; disable "Apply" on the wording pass when the AI says the
   task needs restructuring and the wording pass deleted text; enforce alt length in the schema (`maxLength`); split .docx
   notes by line or heading, not blank lines; have the import mark context-dependent word rules as prose rules; make Refine
   keep the previous proposal when the instruction doesn't apply.

Routing itself looks right: Haiku for refine and assists costs fractions of a cent with good results; Opus for drafts is
only noticeable because of its cold write ($0.28), which change 2 removes.

## Changes since the measurement

Made on branch `ai-quality`, one commit per item, each covered by scripted-client tests in
`tests/FlareAI.Core.Tests/AiCostTests.cs` that assert on request shape (what is sent, what is cached, which schema). **No
paid calls were made for this section: every cost below is an estimate** from the measured token mix above (per-call
averages from the 635 recorded calls) and the list prices, not a new measurement.

| # | Change | What it does |
|---|---|---|
| 1 | Validate's AI check scoped | Runs the team's layers (company, team, personal, project) by default; Google's ~620 examples are compiled and validated offline. "Include Google's rules (slow, ~$15)" adds them back. Calls run `Parallelism` at a time after a cache-warming first call. |
| 2 | Cache lifetime per task; one shared prefix | `Routing.CacheFor`: 1 hour for review, rewrite and restructure; 5 minutes for draft (Opus), extract and refine (Haiku); none for alt and assists. Review, rewrite and restructure now use one schema (`Contracts.EditSchema`) and the same system prompt, so they share **one** cache entry; which fields to fill is said in the user message, after the cached block. Teams can change a lifetime in `models.json` (`"cache"`). The comment in `ActionRunner.Ask` that assumed one entry for every task is corrected. |
| 3 | No guide for alt text and assists | A short system prompt (`system-plain.md`), nothing cached. |
| 4 | Smaller rewrite prompts | Word (term) rules were already sent only when the text uses their trigger words (about 1k characters a paragraph). The ~25k characters a one-paragraph rewrite carried were content packs, mostly the Code pack (code font, command syntax, placeholders). Run-level rewrites now leave out formatting packs (Code, Images, Tables, Math, Footnotes, Emphasis); review and restructure keep them. Measured offline on 633 dense paragraphs of the v2 corpora: 32.0k → 20.7k characters of rules per rewrite (−35%). |
| 5 | Re-check re-anchors | After the author applies the review's own fixes (or the linter's), the review cache re-anchors the remaining findings instead of re-reviewing: if every change to a unit's text is one of those fixes, no model call. Any other edit still gets a fresh review. |
| 6 | Fewer rewrite retries | The prompt lists the runs that finish a sentence begun before inline markup, with the words they must start with; when the model drops those words and starts a new sentence, they are put back instead of retrying. |
| 7 | Quality | Modal verbs and code/API terms protected (prompts, plus `MeaningGuard`: a review fix that adds or changes a modal or drops a code term becomes a needs-author finding; such a rewrite change is held back with the reason). Citations name the team's rule when it overrides the cited Google rule (15 of 15 marketing claims cited Google's). Apply is disabled on a wording-only proposal that deleted the sentence it meant to move. Alt text held to 125 characters (one retry, then cut; structured outputs don't accept `maxLength`, so the limit is in the field description and checked after the answer). `.docx` notes chunked like `.txt` (26 chunks from either copy of the synthetic guide, was 3 vs 18). Context-dependent words ("here", "once", "setup", …) become prose rules, not blanket Avoid rows. Refine keeps the earlier suggestion when the instruction doesn't apply. |

Not done: halving the 100k-character always-on block (recommendation 4's first half). It is now one shared entry, but every
review and rewrite still reads it (31% of review cost).

### Estimated cost per action (warm, Sonnet unless noted)

| Action | Measured | **Estimated now** | Why |
|---|---|---|---|
| Check style, AI review | $0.053 | **$0.054** | Unchanged work; the shared schema adds about 20 output tokens of empty fields per call |
| Re-check after applying fixes | $0.046 | **about $0.012** | Assumes 3 of 4 changed units are re-anchored (in the measured runs every edit was an applied fix, so all 75 missed units would have been) |
| Rewrite selection (one paragraph) | $0.049 | **$0.036** | About 2.2k fewer uncached tokens a call (change 4); about 1.45 calls an action instead of 1.7 (change 6, assuming most continuation retries go) |
| Ask (wording pass, then Restructure when needed) | $0.044 | **$0.043** | Cheaper wording pass; restructure unchanged when warm |
| Refine (Haiku) | $0.0007 | $0.0007 | |
| Topic assists (Haiku, 3 calls) | $0.003 | **$0.002** | No 33k-token cache read per call |
| Suggest alt text | $0.009 | **about $0.004** | No guide: about $0.0023 a call; includes a retry for the 13 in 20 that ran long |
| Build style guide import, .txt | $0.24 + $0.14 cold | **about $0.35 + $0.09 cold** | 26 chunks instead of 18 (headings start a chunk); 5-minute cache write |
| Build style guide import, .docx | $0.12 | **about $0.35 + $0.09 cold** | Same 26 chunks as the .txt: costs more, loses fewer rules |
| Validate style guide, AI check | $17.10 per run, 34 min | **about $0.14**, seconds | Team layers only (5 example rules in the bundled guide). With Google's: about $15-17, about 6 minutes at Parallelism 6 |
| Tune "Draft a change" (Opus), cold | $0.30 | **about $0.20** | 5-minute write (1.25x) instead of 1 hour (2x) |

### Estimated cold starts

| Cache entry | Measured: premium per cold start | **Estimated now** |
|---|---|---|
| Sonnet review | $0.133 | **$0.133**, shared with rewrite and restructure: one write per session covers all three |
| Sonnet rewrite, restructure | $0.133 each | **$0** when review (or the other) was used in the last hour, else the shared write |
| Sonnet alt | $0.133 | **$0** (nothing cached) |
| Sonnet extract | $0.133 | **about $0.08** (5 minutes) |
| Opus draft | $0.27 | **about $0.17** (5 minutes) |
| Haiku refine, assists | $0.007 each | refine about $0.004 (5 minutes); assists $0 |

### Estimated per writer and team

Same usage model and session counts as "Per writer" above; one shared Sonnet write per session.

| | Light | Typical | Heavy |
|---|---|---|---|
| Work (warm calls) per week | $0.91 → **$0.76** | $2.42 → **$1.93** | $6.83 → **$5.57** |
| Cold starts per week | $0.93 → **$0.53** (4 shared writes) | $2.66 → **$1.06** (8) | $4.23 → **$1.33** (10) |
| **Per month (4.33 weeks)** | $8 → **about $6** | $22 → **about $13** | $48 → **about $30** |

| | Measured | **Estimated now** |
|---|---|---|
| Style guide owner, per month on top of writing | $19 | **about $1.50** (Validate $0.14, import $0.45, 3 drafts $0.60-0.75); about $16.50 if they tick "Include Google's rules" once |
| Team of 5, typical / heavy month | $130 / $260 | **about $65 / $150** |
| Recommended gateway budget for 5 | $200/month, per-key $60 | **$120/month** with alerts at 50% and 80%, per-key limit **$40/month** (a heavy writer is about $30) |

Worth measuring on the next paid run: the shared-entry cache hit rate in real sessions (the estimate assumes rewrite and
restructure ride on the review's entry), how often re-checks re-anchor when authors also hand-edit, how many continuation
retries remain, and alt-text length with the clearer description.

## Reproduce

```powershell
dotnet build dist/ai-run/AiRun.csproj -c Release       # the harness (not committed; dist/ is git-ignored)
$exe = 'dist/ai-run/bin/Release/net48/AiRun.exe'
& $exe writer --label w1 --cap 4 --corpus corpus/DigitalBanking --topics 20 --snippets 2 --labelled-share 0.5 `
     --rewrites 10 --asks 5 --refines 5 --assists 5 --alts 5 --alt-corpus <project with real images> --seed 101
& $exe owner --label owner --cap 1.5 --extract txt,docx --examples 40 --drafts 3
& $exe report                                           # per phase, task and model: calls, tokens, $, p50/p95
```

The key is read from the process environment or the repo's `.env` and never written anywhere; every call is appended
to `dist/ai-run/out/all-calls.jsonl`, and a run stops before its cap or the $24 hard cap.
