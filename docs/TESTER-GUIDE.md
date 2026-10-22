# House Style tester guide

Thank you for testing House Style. This page gets you started. The full user guide is a Flare project in
`docs/user-guide` (open `FlareAI-Guide.flprj`). For what changed since 0.1, see **House Style > What's new** or
`docs/release-notes/0.2.0.md`.

## Install or upgrade

1. Extract the whole download.
2. Close Flare.
3. Double-click `Install.cmd` and approve the administrator prompt. It replaces a copy you installed by hand and keeps
   your settings.
4. Start Flare. If House Style isn't enabled, select **File > Options > Plugins > House Style > Enable**, and restart
   Flare.

From now on, House Style updates itself. When a notice says a new version is available, select **Update now**, then
**Install when Flare closes**, and close Flare.

If House Style isn't on the ribbon, see the [install page](https://abmorrill4.github.io/flare-ai-plugin/install.html).

## Set up

New writers see a three-step setup the first time they select a House Style command. You already have settings, so
open it yourself: **House Style > Settings > Run setup again**.

1. **How will you use AI?** For your company's gateway, choose **My company's AI service (gateway)**. Enter the
   **Base URL** and **Gateway key**, and select **Test connection**.
2. **Whose style rules?** Choose **My team's shared style guide folder** and browse to the folder your style guide
   owner gave you. Or keep **Google's guide plus my own changes**.
3. Select **Finish**.

## Try these

| Do this | What to look for |
|---|---|
| Open a topic and select **Check style**. | Style rule suggestions at once, AI suggestions shortly after. |
| Select a suggestion and select **Apply**. Then press Ctrl+Z. | The fix is a tracked change. After Ctrl+Z, the suggestion is back in the pane. |
| Select **Check project**. | Suggestions grouped by rule. Try **Fix all**, **Review one by one**, **Go to** and **Open file**. Right-click a suggestion. |
| Select **Style rules**. | Every rule, with search and filters. Turn one off, then turn it back on. |
| Select **Turn off rule…** on a suggestion. | If you're an owner, the rule is off on the next check. If not, you see **Request a change…**. |
| In Settings, choose **No AI**, and check a topic again. | The pane says "AI review is off" and shows no AI suggestions. |
| Select **Build style guide > Add notes or an old guide…**, and load a Word file. | Suggested rules you can edit or remove before you add them. |

## Changing the team's style guide

Your team's guide has **owners**, listed in the guide's `FlareAI/owners.txt`. Only owners change the team's rules.
The guide folder is read-only for everyone else.

**If you're a writer:**

- Select **Request a change…** on a suggestion, or a **Request…** button in **Style rules**. Say what you want and why.
- The rule stays as it is until an owner decides. Its suggestions show **Request sent**.
- On your next check after the owner decides, a notice in the pane says **approved** or **declined**, with the owner's
  note. An approved exception for your project is written into the project for you.
- To see or withdraw your requests, select **Tune style guide**.

**If you're an owner:**

- Change rules directly from the pane or **Style rules**. Writers get the change on their next check.
- Select **Tune style guide** to see requests under **Requests to the style guide owners**. Select **Approve or
  decline…**. You can adjust a change before you approve it.
- Requests arrive through the team feedback folder. Every writer sets it in **Settings > Advanced**. Without it, writers
  can't send requests.

**Limits:**

- Changes made to the guide outside House Style aren't recorded.
- If a writer can't reach the guide folder, for example offline, House Style checks with Google's guide only and says so.
- Requests contain the rule, the words it flagged, the topic's file name and the reason. They contain a writer's name
  only if the guide's `attribution.txt` says `names`.

Setting up a guide for a team, step by step: user guide topic *Set up a team style guide*. Publishing it to
Confluence: *Publish the style guide to Confluence*.

## Known issues

- **Check again** doesn't ask the AI again for paragraphs you haven't changed. It shows the saved review.
- After an AI edit of a paragraph that contains a variable, Ctrl+Y (redo) can make Flare show an error. Ctrl+Z works.
  To fix a "use the variable" suggestion, select **Go to**, then use **Insert > Variable**.
- **Rewrite…** can't restructure a paragraph with pending tracked changes. Accept or reject them first.
- House Style refuses any AI change that would drop or change a variable, cross-reference, snippet or condition. You
  see "Rejected", and nothing changes. Please report it anyway.

## Report a problem

- Attach `%APPDATA%\FlareAI\diag.log`. It has no document text and no key.
- **House Style > Diagnostics** shows your version and what House Style sees in the open topic. Copy it into the report.
- Use `docs/testing/BUG-TEMPLATE.md`. A sentence each is enough: what you did, what you expected, what happened.
