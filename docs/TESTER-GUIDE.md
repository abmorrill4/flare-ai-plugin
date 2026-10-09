# FlareAI — tester guide (proof of concept)

Thanks for trying this. It adds an **AI** tab to Flare Desktop with three actions. Every edit it makes
arrives as a **tracked change**, so you accept or reject it exactly like a reviewer's edit, and Ctrl+Z
undoes it in one step.

## Install (needs admin, or ask IT)
1. Close Flare.
2. Before unzipping a downloaded copy: right-click the zip → **Properties** → tick **Unblock** → OK.
   (Otherwise Windows blocks the plugin and Flare silently won't list it. Already unzipped? Run
   `Get-ChildItem <folder> -Recurse | Unblock-File` in PowerShell.)
   Unzip. Copy the `FlareAI` folder into
   `C:\Program Files\MadCap Software\MadCap Flare <version>\Flare.app\Plugins\`
3. Start Flare → **File > Options > Plugins** → select FlareAI → **Enable** → restart Flare.
4. Switch to Ribbon UI mode if you use the tool strip (View > Interface).

Also install **uv** once (it runs the style checker; no admin needed):
`winget install astral-sh.uv`. The first Check style downloads the checker (about a minute).

## First run
**AI > Settings** → paste the API key you were given → Save. The key is encrypted to your Windows
account; nobody else on the machine can read it.

## The actions
| Button | What it does | Writes to the topic? |
|---|---|---|
| **Check style** | Opens a findings list for the selection (or whole topic): instant linter results, then AI review | Only when you click Fix |
| **Rewrite selection** | Rewrites the selected paragraph(s) for clarity and style | Yes, word-level tracked changes |
| **Check project** | Lints every topic and snippet in the project (no AI cost); results grouped by file | Only when you click Fix |
| **Ask…** | Does what you type ("make this a numbered list", "shorten to two sentences") | Yes, tracked |

Selection rule: it works on whole paragraphs/lists/tables containing your selection. Caret only =
whole topic.

## Working the review pane
The pane lists every proposal in reading order, grouped under the topic's headings, with a summary on top
("12 proposals in 3 sections: 7 mechanical · 3 wording · 2 need a rewrite"). Select one to see its card:
- **What changes:** the words around it with the edit marked (removed text struck red, added text green).
- **Why:** the reason, the style-guide rule it applies (with its Bad/Good example when the guide has one), and where
  it comes from: **[Google]**, **[Company · section]**, or **[AI · Company: section]** for AI judgment calls.
- **Impact:** for text inside a snippet, how many files use that snippet.

Decide on each one:
- **Accept** (Enter, or double-click): applies it as a tracked change and moves to the next proposal. For "needs a
  rewrite", the button reads **Rewrite…**: the AI rewrites that paragraph and shows you the result as marked-up text
  first. **Accept all mechanical** applies every exact rule fix in the topic at once (one Ctrl+Z undoes them).
- **Reject ▾** (Delete): pick why: *wrong*, *fine here*, or *rule doesn't apply*. It stays hidden for that text, and
  the reason is recorded so the guide and the AI can be tuned. *Skip for now* just moves on.
- **Edit…**: type the replacement yourself, then apply it. **Refine…**: tell the AI how to change this one proposal
  ("keep the term SSO", "shorter").
- **Re-analyze**: review that paragraph again after you edited it. Proposals whose text you already changed show
  with ⚠ and can't be applied until you re-analyze.
- **Guide ↗** opens the rule's page (company rules open in Flare). **Ignore rule…** turns a rule off for the whole
  project — it writes to the project's `Content/Resources/StyleGuide/overrides.ini`, so your team gets the same decision.

Fixes inside a snippet are applied in the snippet (Flare opens it so the change is tracked there) and the topic comes
back to the front. AI reviews are saved, so reopening a topic you already reviewed shows its proposals immediately;
click **Re-check** for a fresh review.

## Editing the company style guide
It's a Flare project (`CompanyStyle`). Its `About` topic explains the format. After editing, run
**AI > Validate style guide**: it checks every rule table against its examples, and **Check prose rules with AI…**
shows which written rules the AI actually applies.

## Known Flare limitation
After a paragraph-level AI edit of text that contains a variable, Ctrl+Z works but Ctrl+Y (redo) can make Flare show
an error dialog. Word-level fixes and rewrites are not affected. For "type the variable" findings, click the row (the
text is selected) and use Insert ▸ Variable — Flare replaces the selection.

## What it will refuse
- Snippet blocks and variables selected on their own (it cannot edit snippet content inline).
- Any result that would drop or change a variable, cross-reference, snippet, or condition — you'll see
  "Rejected" and nothing changes. That is the safety net working; please report it anyway.

## When something goes wrong
- `%APPDATA%\FlareAI\diag.log` has timings and errors (no document text, no key). Attach it to your report.
- **AI > Diagnostics** shows what the plugin thinks is selected — useful if an action picked the wrong blocks.
- Flare itself should never crash or freeze. If it does, that is bug #1 — tell us what you clicked.

## Please note as you go
What you asked, what you expected, what you got, and whether you kept the edit. A sentence each is plenty.
