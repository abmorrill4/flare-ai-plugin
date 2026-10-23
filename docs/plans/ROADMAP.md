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

## Owner decisions (2026-10-22): AI design pass

Inputs: the Flare desktop demand report and task-level research on what writers use AI for (session notes, not in the
repo). The case for House Style is governed, reviewable AI on the desktop, not linting: MadCap's AI is cloud-only, and
writers can't paste 10,000 topics through a chatbot without losing Flare structure or rule traceability.

1. **AI is organized as jobs, not prompts or models.** Four hero workflows: check a topic in passes (#162), audit the
   project (#165, #166), bring the style guide the team already has (#167), and describe a rule (#168). Writers never
   pick a model; the team policy does (#164).
2. **Add, don't replace.** Big workflows are shortcuts over the small commands. Lint Topic becomes a split button for
   checks and scope (#162); Rewrite, Ask AI, Restructure, Topic Metadata and alt text all stay.
3. **Iterative, not all or nothing.** Every check can cover a section or selection, keep partial progress, resume later
   and re-run only what changed.
4. **The writer chooses the shape of review.** In Order, By Rule or By File in both topic and project, with a walk
   through one rule at a time to keep context switching low (#161).
5. **Nobody starts from scratch.** Importing an existing guide in any format, a term list from another tool, or a body
   of good writing is the easiest step in the product (#167).
6. **Deterministic first.** Parse, count and lint locally; AI only for what's left, and labeled. Audit Project's first
   stage (#165) and the guide import's structure and corpus paths send nothing.
7. **Cost and data are shown before every AI action** (What's Sent, #164), and the first AI moment needs no key: the
   practice topic plays labeled sample results (#163).
8. **Drafting is secondary.** One issue (#169) turns notes into a topic in the team's template and marks gaps instead
   of inventing facts. Release notes and gap analysis from support logs stay out.

## Phases

| Milestone | Issues |
|---|---|
| Phase 1: Trust and correctness | #1 wrong-occurrence fixes · #2 project-wide apply · #3 snippet fixes · #4 AI cost visible · #5 attribution · #6 one vocabulary · #7 accessibility basics · #8 dialog fixes · #9 UI consistency test |
| Phase 2: New shell | #10 foundations · #27 self-explaining suggestions · #11 ribbon and menus · #12 review pane · #13 Settings and first-run setup · #14 accessibility audit · #161 review views · #162 lint checks and scope · #163 first AI moment without a key · #164 team AI policy and What's Sent |
| Phase 3: Rules, scale and CI | #15 rule authoring (design) · #16 structural checks · #17 file names · #18 readability · #19 built-ins as rules · #20 project backlog view · #21 Validate and feedback views · #28 structural fixes · #29 exempt one place · #30 image/snippet names · #33 progress over time · #165 Audit Project local scan · #166 Audit Project AI passes and fix queue · #167 bring your style guide · #168 Describe a Rule · #169 New Topic from Notes |
| Phase 4: Markdown in and out | #24 design · #25 engine · #26 paste, open, import, copy, export |
| Future: on request | #22 CLI checker · #23 AI in CI · #31 CLI config · #32 cross-platform CLI and GitHub Action |

## Standing UX rules (every issue)
One home per feature; discovery at the moment of need; defaults that work; glossary words only (#6); every write is a
tracked change in one undo step with a preview for batches; visible loading/empty/error states; keyboard and screen
reader support; every suggestion meets the output contract (#27).
