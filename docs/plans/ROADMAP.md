# House Style roadmap

**Live roadmap:** the [House Style roadmap project](https://github.com/users/abmorrill4/projects/2) (Roadmap, Board and
Table views). Phases are repo milestones; the **Sprint** field is the timeline. Each issue links to the plan or review
section it comes from. This file records the decisions behind it.

Inputs, all in `docs/plans/`: gap plans `gap-1`…`gap-5`, and `ux/inventory.md`, `ux/review-deliberate.md`,
`ux/review-adversarial.md`, `ux/plan-critique.md`.

## Owner decisions (2026-10-13)

1. **Capability is welcome when it's intuitive.** "Don't drown the user" means clear homes, task names and progressive
   disclosure, not fewer features. Nothing is parked in a "v1.1": if it's worth doing, it's on the roadmap.
2. **No copy-paste round trips.** Markdown is native docs-as-code (link a repo, import, re-sync as tracked changes,
   write back), designed in #24 before it's built.
3. **Owners shouldn't learn rule-table types.** How owners create and change rules is designed in #15 before the new
   check types (#16–#19) are built. Tables stay the storage.
4. **One engine for pane and CLI** (#10), so Flare and CI never disagree.
5. **Setting the API key is initial setup** (#13), not a trust problem.
6. **Current Flare only.** Older versions can't be tested without installs, so `gap-4-flare-versions.md` is shelved.
7. **Feedback attribution is configurable** by the guide owner (#5).
8. **No day estimates.** Agentic development; the board's sprints are the timeline.

## Phases

| Milestone | Issues |
|---|---|
| Phase 1: Trust and correctness | #1 wrong-occurrence fixes · #2 project-wide apply · #3 snippet fixes · #4 AI cost visible · #5 attribution · #6 one vocabulary · #7 accessibility basics · #8 dialog fixes · #9 surface budget test |
| Phase 2: New shell | #10 foundations · #11 ribbon · #12 review pane · #13 Settings and first-run setup · #14 accessibility audit |
| Phase 3: Rules, scale and CI | #15 rule authoring (design) · #16 structural checks · #17 file names · #18 readability · #19 built-ins as rules · #20 project backlog view · #21 Validate and feedback views · #22 CLI checker · #23 AI in CI |
| Phase 4: Markdown and docs-as-code | #24 design · #25 engine · #26 link, import, re-sync |

## Standing UX rules (every issue)
One home per feature; discovery at the moment of need; defaults that work; glossary words only (#6); every write is a
tracked change in one undo step with a preview for batches; visible loading/empty/error states; keyboard and screen
reader support; the surface budget test (#9) changes only on purpose.
