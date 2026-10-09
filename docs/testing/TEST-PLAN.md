# FlareAI test plan

For testers. Each case says what to do and what you should see. Mark it pass/fail; for a fail, file a report with
[BUG-TEMPLATE.md](BUG-TEMPLATE.md). The user guide (`docs/user-guide`, a Flare project) explains every feature; this
plan is how to check them.

## Setup (once)

1. Install the plugin (user guide: *Install the plugin*). In **AI > Settings**, paste your API key.
2. The test corpus is in the repository: `corpus/DigitalBanking` (a Flare
   project: 25 feature areas × 10 topics, snippets, variables, conditions, images, a TOC and an HTML5 target) and
   `corpus/ground-truth.json`, which lists every problem planted in it: file, the exact words, and which check should
   catch it. Five simulated authors wrote it, each with their own habits (see `authors` in the ground truth).
3. To start over at any point, close the project in Flare and restore the folder from git (`git checkout -- corpus`),
   or regenerate it: `uv run --script scripts/make-corpus.py corpus` (needs uv; delete `corpus` first).

Planted problems by kind (429 in all): banned or wordy words (`utilize`, `in order to`, `please`…), legacy terms
(`customers`, `bank`, `whitelist`, `Single Sign-On`…), claims (`coming soon`, `guaranteed`…), typed product and
company names that should be variables, Google rules (`e.g.`, `will`, `we`, `!`), judgment calls for the AI (passive
voice, wordiness, vague instructions, unexplained `MFA`, timeline promises, "the save button"), and structure (images
without alt text, skipped heading levels, a second h1, a table without a header row, "click here", hard-coded phone
numbers and URLs). One shared snippet (`LegacyApp.flsnp`, used by 9 topics) says "Customers".

## A. Check style (one topic)

| # | Do | Expect |
|---|---|---|
| A1 | Open `transfers/about.htm`, put the caret anywhere, **Check style**. | Pane opens; lint and structure findings at once; AI findings within ~20 s. Every planted problem in the topic is listed (compare with ground truth). |
| A2 | Select one proposal. | Card shows the words around it, the change in red/green, the rule, and its source ([Google] / [Company] / [AI]). |
| A3 | **Accept** a mechanical fix. | Tracked change in the topic, word-level only; the next proposal is selected. Ctrl+Z removes it. |
| A4 | **Reject ▾ > wrong** on an AI proposal. | It disappears; `%APPDATA%\FlareAI\feedback.jsonl` gets a `rejected` line. |
| A5 | Edit a sentence that has a proposal, then select that proposal. | Marked ⚠, can't be applied; **Re-analyze** refreshes it. |
| A6 | **Refine…** with "keep it shorter". | The proposal updates; the card shows the new text. |
| A7 | On the "Customers who still use the legacy app" finding (from the snippet): **Accept**. | The snippet opens, gets the tracked change, and the topic comes back to the front. Card showed "used by 9 files". |
| A8 | **Accept all mechanical**. | All exact fixes applied in one undo step. |
| A9 | **Propose all as tracked changes**, then Flare's **Review** tab. | Every exact proposal is a tracked change you can accept or reject in Flare. |
| A10 | Close and reopen the topic, **Check style**. | Saved proposals appear at once; **Re-check** runs a fresh review. |
| A11 | Open a topic with a missing alt text (ground truth `alt_missing`), select that finding. | Button reads **Suggest alt text…**; the AI drafts a sentence; applying it adds `alt` as a tracked change. |

## B. Rewrite and Ask

| # | Do | Expect |
|---|---|---|
| B1 | Select a paragraph with `In order to` and `prior to`, **Rewrite selection**. | Result window with the changes marked; **Apply** gives word-level tracked changes; both phrases gone. |
| B2 | Select a paragraph that contains a variable (product name) and a bold UI label, **Rewrite selection**. | The variable and the bold label are untouched. |
| B3 | Select one step, **Ask…** "split this into two steps". | The result says it needs restructuring and offers **Restructure instead…**; the restructured result is validated before you can apply it. |
| B4 | Select a step list, **Ask…** "make this a table". | Either a valid restructure or a clear refusal; never a broken topic. |
| B5 | Make a tracked change in a paragraph (don't accept it), then **Rewrite selection** on it. | The AI sees the current text (your insertion, not the deleted text); edits land around your change. |

## B2. In the editor

| # | Do | Expect |
|---|---|---|
| E1a | Right-click in a topic. | Flare's menu has **FlareAI: Check style**, **Rewrite selection**, **Ask…** once each; each works on the text you right-clicked. |
| E1b | Type "Please utilize the customer list." at the end of a paragraph and stop. | About a second later the paragraph gets an amber margin bar; the review pane (if open on the topic) lists the new issues. |
| E1c | Open the pane (**Check style**) and accept or reject proposals. | Markers follow: a paragraph loses its bar when its last proposal is resolved; AI findings mark their paragraphs too. |
| E1d | Save (Ctrl+S) a topic with issues. | A notice at the bottom right: "N style issue(s) in topic.htm. Saved anyway." with *Show them*. The saved file has no stylesheet or marker in it. |
| E1e | Start a build (Project > Build Primary). | A notice: "Building. N style issue(s) in M file(s)" with *Open the report*; the build runs normally. |
| E1f | In Settings, turn each of the four options off. | The matching behavior stops; the others still work. |

## C. Project-wide

| # | Do | Expect |
|---|---|---|
| C1 | Open any topic, **Check project**. | ~250 files linted in under a minute; results grouped by file; no AI cost. |
| C2 | **Accept all mechanical** across the project (~230 fixes in ~140 files). | Files not already open are saved and closed after their fixes; Flare stays responsive and doesn't crash. Changes are tracked in each file. |
| C3 | Select a proposal in a file that isn't open. | The file opens at the problem. |

## D. Style guide and team

| # | Do | Expect |
|---|---|---|
| D1 | **Validate style guide**. | Every rule table row checked against its example; failures listed with the row. |
| D2 | **Build style guide > Mine this project** on the corpus. | Competing forms found (for example `customers` vs `members`, `Single Sign-On` vs `single sign-on`) with counts and sentences. |
| D3 | **Build style guide > Add notes…**, paste: "We never say 'log in' — always 'sign in'. Don't promise dates." | Proposed rules quote your words; checked ones go to the guide's `From-our-content` topic. |
| D4 | Point **Team feedback folder** at `bench-out/<run>/feedback` (produced by the bench, below), **Tune style guide**. | The claims rule shows as disputed (one author rejects it, others accept); often-rejected AI rules are listed with evidence. |
| D5 | With `FlareAI/owners.txt` listing someone else: **Turn off rule…** | You can't turn the rule off; a request goes to the owners instead. |
| D6 | As an owner: **Turn off rule…** on a Google proposal, "For the whole team", level off, a reason. | A row appears in the guide's *Changes to the Google guide* topic (no ID typed); Re-check: the rule no longer appears, from the linter or the AI. |
| D7 | In the Company guide, insert a new bullet above existing rules, copy another bullet, then **Validate style guide** as an owner. | Status says new rules got permanent IDs; existing rules keep their IDs (Tune evidence and earlier feedback still point at them); no other change in the topic files (`git diff` shows only `id="rule-…"` attributes). |

## E. Output and assists

| # | Do | Expect |
|---|---|---|
| E1 | **Topic assists** on any topic. | Description, keywords, synonyms, micro content, glossary drafts, each with Copy; nothing written into the topic. |
| E2 | Build the HTML5 target, then **Export for LLMs**. | `llms.txt` and one `.md` per topic in the output folder. (Trial Flare scrambles output text; the files are still produced.) |

## F. Headless bench (optional, needs .NET SDK)

```
dotnet build tools/FlareAI.Bench
tools\FlareAI.Bench\bin\Debug\net48\FlareAI.Bench.exe --no-ai               # lint + structure, free
tools\FlareAI.Bench\bin\Debug\net48\FlareAI.Bench.exe --sample 40 --rewrites 8   # AI review, ~$1 on Sonnet
tools\FlareAI.Bench\bin\Debug\net48\FlareAI.Bench.exe summarize bench-out
```

Each run writes `bench-out/<run>/report.md` (recall by kind, missed problems, AI findings that weren't planted,
citation accuracy, simulated author decisions and the Tune report, rewrites, cost), `metrics.json`, `findings.jsonl`
and per-author `feedback/*.jsonl`. Options: `--model`, `--effort low|medium|high`, `--unit block|section`.

## What always counts as a bug

- Flare crashes or freezes.
- A change you didn't accept appears in a topic, or a change isn't tracked.
- A variable, cross-reference, snippet or condition is lost or changed by an AI edit.
- A proposal points at the wrong text.
- The API key appears anywhere (logs, files, screenshots of settings).
