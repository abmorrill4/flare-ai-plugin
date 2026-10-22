# House Style test plan

For testers. Each case says what to do and what you should see. Mark it pass or fail. For a fail, file a report with
[BUG-TEMPLATE.md](BUG-TEMPLATE.md). The user guide (`docs/user-guide`, a Flare project) explains every feature; this
plan is how to check them.

## Setup (once)

1. Install House Style with `Install.cmd` (user guide: *Install and update*).
2. Select **House Style > Settings > Run setup again**, choose how you use AI, and select **Test connection**.
3. The test corpus is in the repository: `corpus/DigitalBanking` (a Flare project: 25 feature areas × 10 topics,
   snippets, variables, conditions, images, a TOC and an HTML5 target) and `corpus/ground-truth.json`, which lists
   every problem planted in it: file, the exact words, and which check should catch it.
4. To start over, close the project in Flare and restore the folder from Git (`git checkout -- corpus`).

Planted problems (429 in all): wordy or banned words (`utilize`, `in order to`, `please`), legacy terms (`customers`,
`whitelist`, `Single Sign-On`), claims (`coming soon`, `guaranteed`), typed product names that should be variables,
Google rules (`e.g.`, `will`, `we`, `!`), judgment calls for the AI (passive voice, wordiness, vague steps,
unexplained `MFA`), and structure (images without alt text, skipped heading levels, a second h1, a table without a
header row, "click here", typed phone numbers and URLs). One shared snippet (`LegacyApp.flsnp`, used by 9 topics) says
"Customers".

## S. Setup and Settings

| # | Do | Expect |
|---|---|---|
| S1 | Rename `%APPDATA%\FlareAI\settings.json`, then select **Check style**. | The three-step setup opens. After **Finish**, Check style runs. Put the file back afterward. |
| S2 | In setup, choose the gateway and enter the Base URL as `http://<address>/` with a trailing slash. | It's saved as `https://<address>`, with no slash. |
| S3 | Paste half of the key and select **Test connection**. | A plain message about the key, not a generic error. |
| S4 | Open **Settings**. | Four tabs: AI, Style guide, Checking, Advanced. **Run setup again…** is at the bottom. |
| S5 | **File > Options > Plugins**. | House Style shows its real version, not 0.1.0. |

## A. Check style (one topic)

| # | Do | Expect |
|---|---|---|
| A1 | Open `transfers/about.htm`, put the caret anywhere, **Check style**. | The pane opens; style rule suggestions at once, AI suggestions within about 20 seconds. Every planted problem in the topic is listed. |
| A2 | Select a suggestion. | The card shows the words around it, the change, the rule, and its source (Google, Our rules, This project, AI). |
| A3 | **Apply** a fix. Then press Ctrl+Z. | A word-level tracked change, and the next suggestion is selected. After Ctrl+Z, the suggestion is back in the pane. |
| A4 | **Reject ▾ > Wrong** on an AI suggestion. | It disappears; `%APPDATA%\FlareAI\feedback.jsonl` gets a `rejected` line. |
| A5 | Edit a sentence that has a suggestion, then select that suggestion. | Marked ⚠ and can't be applied; **Check this paragraph again** refreshes it. |
| A6 | **Refine…** with "keep it shorter". | The suggestion updates. |
| A7 | On the "Customers who still use the legacy app" suggestion (from the snippet): **Apply**. | The snippet opens, gets the tracked change, and the topic comes back to the front. The card said how many files use the snippet. |
| A8 | **Apply all fixes**. | All exact fixes applied; one Ctrl+Z undoes them. |
| A9 | **Write all as tracked changes**, then Flare's **Review** tab. | Every suggestion with exact text is a tracked change you can accept or reject. |
| A10 | Close and reopen the topic, **Check style**. | Saved suggestions appear at once, with no AI cost. (Known issue: **Check again** doesn't ask the AI again for unchanged paragraphs.) |
| A11 | Select a missing alt text suggestion. | The button reads **Suggest alt text…**; the draft is 125 characters or fewer; applying it adds `alt` as a tracked change. |
| A12 | In Settings, choose **No AI**, and check the topic again. | The pane says "AI review is off" and shows no AI suggestions, including saved ones. |

## B. Rewrite and Ask

| # | Do | Expect |
|---|---|---|
| B1 | Select a paragraph with `In order to` and `prior to`, **Rewrite selection**. | Result window with the changes marked; **Apply** gives word-level tracked changes; both phrases gone. |
| B2 | Select a paragraph that contains a variable and a bold UI label, **Rewrite selection**. | The variable and the bold label are untouched. |
| B3 | Select one step, **Ask…** "split this into two steps". | The result offers **Restructure instead…**; the restructured result is validated before you can apply it. |
| B4 | Select a step list, **Ask…** "make this a table". | Either a valid restructure or a clear refusal; never a broken topic. |
| B5 | Select a sentence with "should", **Ask…** "make this stronger". | A change from "should" to "must" isn't applied for you. |

## E. In the editor

| # | Do | Expect |
|---|---|---|
| E1 | Right-click in a topic. | **House Style: Check style**, **Rewrite selection** and **Ask…** once each. |
| E2 | Type "Please utilize the customer list." at the end of a paragraph and stop. | About a second later, the paragraph gets a margin bar. |
| E3 | Save (Ctrl+S) a topic with issues. | A notice with the number of style issues. The saved file has no marker in it. |
| E4 | Start a build. | A notice with the number of issues; the build runs normally. |
| E5 | On the **Checking** tab of Settings, turn each option off. | The matching behavior stops; the others still work. |

## C. Check project

| # | Do | Expect |
|---|---|---|
| C1 | Open any topic, **Check project**. | About 250 files checked in under a minute; one row per rule, most frequent first; no AI cost. |
| C2 | Select a rule, **Fix all**. | A preview with files and counts; nothing changes until **Apply**. |
| C3 | **Fix all automatic suggestions…**, then **Stop after this file** partway. | Files already fixed keep their tracked changes; the rest are untouched. Flare stays responsive. |
| C4 | Select a rule, **Review one by one**, then Esc. | Only that rule's suggestions, then all rules again. |
| C5 | Select a suggestion in a file that isn't open, **Go to**. | The file opens with the text selected. |
| C6 | Right-click a suggestion. | **Go to**, **Open file**, **Copy file path**. |
| C7 | Apply a fix in another file, switch to it, press Ctrl+Z, and return to the pane. | The suggestion is back. |
| C8 | **By file** (Alt+F), then **By rule** (Alt+R). | The list switches; the choice is remembered next time. |

## R. Style rules

| # | Do | Expect |
|---|---|---|
| R1 | **Style rules**. Search for `click on`. | The Google word-list rule, with its examples, level and source. |
| R2 | Filter **State** to off. | House Style's default changes are listed, each labeled **Default** with a reason. |
| R3 | **Turn off…** a rule, with a reason. Check a topic. | The rule no longer appears, from the style rules or the AI. |
| R4 | **Turn back on** the same rule. | Offers other rules turned off with it. After the next check, the rule appears again. |
| R5 | Resize the window, close it and open it again. | Same size. |

## D. Style guide and team

| # | Do | Expect |
|---|---|---|
| D1 | **Validate style guide**. | Every rule table row checked against its example; failures listed with the row. The AI check covers only your team's rules unless **Include Google's rules** is selected. |
| D2 | **Build style guide > Mine this project** on the corpus. | Competing forms found (for example `customers` and `members`) with counts and sentences. |
| D3 | **Build style guide > Add notes or an old guide…**, load a `.docx`, **Extract rules**. | Suggested rules quote your words; **Edit rule…** and **Remove** work before **Add checked rules to the guide**. |
| D4 | Load a file that's still open in Word. | It loads, or a plain message says why. No crash. |
| D5 | Use a team guide whose `FlareAI/owners.txt` lists someone else, and whose folder is read-only for you. **Turn off rule…** on a suggestion. | The button reads **Request a change…**; the request is sent; the suggestion shows **Request sent**. |
| D6 | As an owner, **Tune style guide > Approve or decline…** on that request. | After the writer's next check, a notice says approved or declined, with the note. |
| D7 | Ask for an exception for this project only, and have an owner approve it. | On the writer's next check, the exception is written into the project's `overrides.ini`. |
| D8 | Make `owners.txt` unreadable for the writer. | Every change from that writer becomes a request. |
| D9 | Point Settings at a team guide folder you can't reach. | Checks use Google's guide only, with a note that the team guide couldn't be read. |

## O. Output and assists

| # | Do | Expect |
|---|---|---|
| O1 | **Topic assists** on any topic. | Description, keywords, synonyms, micro content and glossary drafts, each with Copy; nothing written into the topic. |
| O2 | Build the HTML5 target, then **Export for LLMs**. | `llms.txt`, `llms-full.txt` and one `.md` per topic. (Trial Flare scrambles output text; the files are still produced.) |

## U. Updates

| # | Do | Expect |
|---|---|---|
| U1 | **Diagnostics**. | Your version, "Automatic updates: on", and the last update check. |
| U2 | When a new release is out: **Update now**, **Install when Flare closes**, close Flare, start it. | The new version runs; **What's new** opens. No administrator prompt. |
| U3 | Clear **Keep House Style up to date** on the **Advanced** tab. | Diagnostics says updates are turned off in Settings. |

## F. Headless bench (optional, needs the .NET SDK)

```
dotnet build tools/FlareAI.Bench
tools\FlareAI.Bench\bin\Debug\net48\FlareAI.Bench.exe --no-ai               # style rules and structure, free
tools\FlareAI.Bench\bin\Debug\net48\FlareAI.Bench.exe --sample 40 --rewrites 8   # AI review, about $1 on Sonnet
tools\FlareAI.Bench\bin\Debug\net48\FlareAI.Bench.exe summarize bench-out
```

Each run writes `bench-out/<run>/report.md`, `metrics.json`, `findings.jsonl` and per-author `feedback/*.jsonl`.
Options: `--model`, `--effort low|medium|high`, `--unit block|section`.

## What always counts as a bug

- Flare crashes or freezes.
- A change you didn't apply appears in a topic, or a change isn't tracked.
- A variable, cross-reference, snippet or condition is lost or changed by an AI edit.
- A suggestion points at the wrong text.
- The key appears anywhere (logs, files, screenshots of settings).
