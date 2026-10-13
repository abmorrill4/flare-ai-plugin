# House Style roadmap

**Live roadmap:** the [House Style roadmap project](https://github.com/users/abmorrill4/projects/2) (Roadmap, Board and
Table views). Phases are repo milestones; the **Sprint** field is the timeline. Each issue links to the plan or review
section it comes from. This file records the decisions behind it.

Inputs, all in `docs/plans/`: gap plans `gap-1`…`gap-5`, and `ux/inventory.md`, `ux/review-deliberate.md`,
`ux/review-adversarial.md`, `ux/plan-critique.md`.

## Owner decisions (2026-10-13)

1. **Everything worth doing is in 1.0.** Items the plans had parked as "later" or "v1.1" are 1.0 issues (#18, #19, #22,
   #23, #28–#33). No day estimates: agentic development, and the board's sprints are the timeline.
2. **Easy for a writer who has never read the style guide.** Every suggestion explains itself (what, why, change,
   example, certainty, source) through one output contract (#27), checked by an eval.
3. **Rules are created and managed by describing them** (#15): AI proposes the rule; the engine shows conflicts,
   impact on the project and examples before anything is saved. Tables stay the storage; nobody has to learn them.
4. **Markdown is a first-class format, in and out** (#24): paste, open, import, copy, export, with honest fidelity
   reports. No copy-paste round trips through external chatbots.
5. **Consistency and clarity over caps.** Wide capability is welcome when it's intuitive: deliberate layout, task
   names, one command table and one vocabulary (#6, #9, #11). No numeric limits on buttons or settings.
6. **One engine for pane and CLI** (#10). **Setting the API key is initial setup** (#13). **Current Flare only**
   (`gap-4-flare-versions.md` shelved). **Feedback attribution is configurable** (#5).

## Phases

| Milestone | Issues |
|---|---|
| Phase 1: Trust and correctness | #1 wrong-occurrence fixes · #2 project-wide apply · #3 snippet fixes · #4 AI cost visible · #5 attribution · #6 one vocabulary · #7 accessibility basics · #8 dialog fixes · #9 UI consistency test |
| Phase 2: New shell | #10 foundations · #27 self-explaining suggestions · #11 ribbon and menus · #12 review pane · #13 Settings and first-run setup · #14 accessibility audit |
| Phase 3: Rules, scale and CI | #15 rule authoring (design) · #16 structural checks · #17 file names · #18 readability · #19 built-ins as rules · #20 project backlog view · #21 Validate and feedback views · #22 CLI checker · #23 AI in CI · #28 structural fixes · #29 exempt one place · #30 image/snippet names · #31 CLI config · #32 cross-platform CLI and GitHub Action · #33 progress over time |
| Phase 4: Markdown in and out | #24 design · #25 engine · #26 paste, open, import, copy, export |

## Standing UX rules (every issue)
One home per feature; discovery at the moment of need; defaults that work; glossary words only (#6); every write is a
tracked change in one undo step with a preview for batches; visible loading/empty/error states; keyboard and screen
reader support; every suggestion meets the output contract (#27).
