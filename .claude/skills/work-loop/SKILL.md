---
name: work-loop
description: How every piece of House Style work moves from an idea or request to shipped, with research, a spec, a verification plan and subagent reviews in between. Use when starting, designing, planning, scoping or picking up any issue, theme, feature, fix or idea; when asked "what's the status of X", "is X designed", "what's next"; and when adding or reordering roadmap items.
---

# The work loop

One loop for every piece of work. It scales with the size of the work, so a typo fix stays cheap and a theme gets
the full treatment. The point is consistency: the same stages, the same artifacts, the same reviews, so the owner
can ask where anything stands and get the same kind of answer.

## Sizes

Pick the size first. When unsure, go one size up.

| Size | What it is | Stages it runs |
|---|---|---|
| **Fix** | A bug with a clear cause, a copy change, a small hardening item | Intent (naming the test that reproduces the bug), Build, Verify, Build review, Ship |
| **Feature** | New or changed behavior a writer would notice | All stages |
| **Theme** | An outcome made of several features (a parent issue labeled `theme`) | Intent, Research, Spec and Spec review at theme level, then the loop per feature |

## Stages

**Design** means the first four stages. When you report status, say which of them are done, for example "Design:
intent and research done, spec in progress". Never just "mid-design".

1. **Intent.** One short paragraph in a writer's words: what changes for them, who asked (`from:tester`,
   `from:owner` or `from:research`), why now, and which owner decision or goal it serves (`planning/roadmap.md`,
   owner decisions). Say what is *not* included.
   - **Check with the owner** when the intent goes beyond what was asked (we turned a tester's "admins control
     turned-off rules" into a whole governance system once; that needed a yes first), or when it comes from
     `from:research` and is a Feature or Theme. One plain question, with a recommended answer.
   - Otherwise proceed. A `from:tester` or `from:owner` ask that you build as asked needs no check.
2. **Research** (Feature, Theme). Run subagents in parallel, one per angle, and keep what they find, not their file
   dumps:
   - what the code does today and what it touches (Explore agent);
   - what Flare's API allows (`docs/api-dump.txt`, `docs/flare-api-notes.md`);
   - how others solve it (Flare Online, Acrolinx, Vale, Office, competitors), when that's relevant;
   - earlier decisions, plans and research in `planning/`.
3. **Spec.** Behavior, edge cases, the copy (a copy deck for more than a few strings, per CLAUDE.md), and the
   **verification plan**: the unit tests, Scenarios, `tests/e2e` steps in real Flare and bench checks that will
   prove it works, written before any code. If you can't say how you'll verify it, the spec isn't done.
4. **Spec review** (Feature, Theme). Run the `work-loop-review` workflow with `stage: "spec"`. It reads the spec
   through four lenses in parallel and verifies each blocking finding with a skeptic:
   - *adversarial*: is this the right thing to build, is there a simpler way, what breaks it;
   - *consistency*: one vocabulary, one home per feature, the standing UX rules, existing patterns in the code;
   - *security*: the invariants in CLAUDE.md (shared folders are untrusted, model output is data, no key in logs);
   - *goals*: does it serve the intent and the owner's decisions, or drift past them.
   Fix the spec for every confirmed blocking finding, then move on. Notes are optional.
5. **Build.** Code against the verification plan, test first where you can. One branch and PR per piece.
6. **Verify.** Run every check the plan named (for a Fix, the reproducing test named in its Intent), and the full
   Release suite before you push. A UI change needs its
   `tests/e2e` scenario in real Flare (the factory VM when the local Flare is busy).
7. **Build review.** Run `work-loop-review` with `stage: "build"` on the PR. Lenses: correctness, security,
   consistency. Fix confirmed blocking findings. Then merge per the merge policy (CI green plus this review).
8. **Ship and learn.** Release notes and `whats-new.md` from the writer's side. Feedback that comes back starts a
   new loop at Intent.

## Where things live

- **Artifacts** go in `planning/work/<issue>-<slug>/`: `intent.md`, `research.md`, `spec.md`, `reviews.md`. The
  planning folder is private; the repo is public, so never put tester reports, names or private findings in an issue.
- **The issue** carries a short summary per stage (template `.github/ISSUE_TEMPLATE/work.md`) and links the
  artifacts by path. Tick a stage in the issue when its artifact exists and its review passed.
- **Security findings with an exploit path** go to a private advisory, never an issue (CLAUDE.md).

## The roadmap

- There are no phases or milestones. **Themes** are the unit: a parent issue labeled `theme`, one outcome a writer
  would recognize, with its issues as sub-issues.
- **Order is the board's `When` field: Now, Next or Later.** Set it on themes and on single issues that belong to no
  theme, never on sub-issues. Reprioritizing is changing that one field. Nothing else moves.
- **Every issue gets one origin label:** `from:tester` (a tester or user asked), `from:owner` (the owner asked or
  decided), `from:research` (our own reviews, plans or research).
- A new idea or request: an issue with its origin label, attached to a theme if one fits, otherwise its own `When`.
  A new outcome gets a new theme (`planning/roadmap/themes.json`, then `create_themes.py`).
- **Tester reports** arrive in the private `abmorrill4/house-style-reports` repo (Report a Problem). Triage each one:
  a public `from:tester` issue in your own words (never the report text or the tester's name), attached to a theme
  or given a `When`, then a comment on the report linking the issue. The roadmap page lists open reports above the
  board until they are triaged.
- The roadmap page is rebuilt with `python planning/roadmap/build.py` and republished to the same artifact URL
  (`planning/roadmap.md` has the link).

## Talking to the owner

- Name every issue, theme or PR in plain words first, with the number second as a link.
- Status is the stage names above, not "in progress" or "mid-design".
- Ask only at the Intent check, for a copy deck (CLAUDE.md), and for outward actions (a release to testers).
  Everything else proceeds.
