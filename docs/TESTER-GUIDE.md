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
| **Ask…** | Does what you type ("make this a numbered list", "shorten to two sentences") | Yes, tracked |

Selection rule: it works on whole paragraphs/lists/tables containing your selection. Caret only =
whole topic.

## Working the findings list
The list stays open while you edit. The style guide is Google's developer documentation style guide with
our company rules on top; each finding says which (**[Google]**, **[Company]**, or **[AI]** for judgment calls).
- Click a row: the text is highlighted in the topic. **Guide ↗** opens the rule's page.
- **Fix** (or Enter / double-click): applies the shown replacement as a tracked change.
  **Fix all** applies every one-click fix in one step; one Ctrl+Z undoes them all.
- "(needs rewrite)" rows: select one and click **Rewrite paragraph…** — the AI rewrites that paragraph to
  resolve its findings and shows you the result before anything changes. Rewrites arrive as word-level
  tracked changes, and any tracked changes already in the paragraph stay pending for you to review.
- **Re-check** after a round of fixes to see what's left.

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
