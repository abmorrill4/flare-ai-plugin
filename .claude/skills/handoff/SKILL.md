---
name: handoff
description: Write a handoff note so a fresh chat can continue this work without re-reading the repo. Use when the user says handoff, wrap up, switching chats, new chat, context is long, or "save where we are"; also proactively offer it when a task spans more than one session.
---

# /handoff: save where this work stands for the next chat

The note goes in the project folder's `planning/handoff/`, outside the repo and not git tracked. The main checkout is
`<project>/repos/house-style`, so every worktree resolves the same folder:

```bash
echo "$(cd "$(git rev-parse --path-format=absolute --git-common-dir)/../../.." && pwd)/planning/handoff"
```

Name the file `YYYY-MM-DD-HHMM-<topic-slug>.md`, using local time and a 2–4 word slug such as `signed-updates`.
If an earlier note on the same topic exists, move it into `planning/handoff/done/`, so only the newest note per topic
stays at the top level.

## Note template
Keep the note under about 60 lines. Write facts the next session can check, not a story.

```markdown
# <Topic>: <one-line goal>

Updated: <date time> · Status: in progress | blocked on <who/what> | done

## Where it is
- Worktree: <path> · Branch: <name> · Last commit: <sha> <subject>
- PR: <url or none> · CI: <green/red/pending> · Issues: <#n …>
- Released: <version or no>

## Done
- <what is finished and verified, with how it was verified (tests, Flare check)>

## Decisions (and why)
- <choice>: <reason; who decided: user or Claude>

## Next
1. <next concrete step, with file:line or command>
2. …

## Watch out
- <gotchas found: flaky tests, Flare quirks, things that looked right but weren't>

## Key files
- <path>: <why it matters>
```

## Rules
- **Never put secrets in a note:** no keys, tokens, `.env` values or signing material. Point to where a secret
  lives, not to its value.
- **Record the user's decisions in their own terms,** and mark anything you assumed as assumed.
- **Check facts before you write them:** run `git status`, `git log -1` and `gh pr view` for the state lines.
- **Durable facts belong elsewhere.** A note is for one piece of work in progress. If you learned something every
  future session needs, such as a new constraint, command or gotcha, add it to the right CLAUDE.md too. Use the root
  file, or the area file under `src/FlareAI.Flare`, `src/FlareAI.Core/Update` or `tools/feedback-worker`.
- **When you're done, tell the user the note's path,** and the one line to start the next chat with, for example
  "continue signed-updates".

## Resuming (for the next chat)
When the user says "continue <topic>", read the newest matching note first. Then:
1. Re-check its state lines with git and gh.
2. Enter or recreate the worktree it names.
3. Continue from **Next**.

If the note and the repo disagree, trust the repo and say so.
