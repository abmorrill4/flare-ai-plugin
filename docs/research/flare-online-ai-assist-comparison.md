# MadCap Flare Online "AI Assist" vs. House Style

Research report, 2026-10-23.

> **Outcome (2026-10-23):** Flare Desktop has no AI Assist (confirmed by the owner). Everything in §4.1 is on the
> roadmap: #181 Ask answers, #182 Ask conversation, #183 insert at the cursor, #184 Write an Introduction, #185
> one-click rewrites, plus the existing #164 team AI policy and #172 providers. Translation is a maybe (#186). The
> decisions are recorded in `docs/plans/ROADMAP.md`.
Source: *MadCap Flare Online — AI Assist Guide* (61 pages, © 2026). Our side: `PLAN.md`, `docs/plans/ROADMAP.md`, the
`docs/plans/` gap plans, open GitHub issues (#1–#174), the user guide (`docs/user-guide`), and the shipped commands in
`src/FlareAI.Flare/Commands.cs`.

---

## 1. Summary

- **AI Assist is a ChatGPT chat window inside the Flare Online editor.** You select text (or place the cursor), type a
  prompt, refine over several turns, look at a diff, edit the reply in a rich-text toolbar, and then click **Replace**
  or **Insert**. Everything else in the guide, all 12 "use cases", is prompt advice for that one window. The product
  has no style engine, no project awareness, no validation, no tracked changes and no persistence.
- **We're far ahead on what we chose to do**: style enforcement, safe in-place edits, team governance, project scale,
  accessibility, metadata assists and LLM export. Their style-guide use case (paste your rules into every thread and
  hope) is exactly the problem House Style solves.
- **They have one interaction we don't**, and it's the one a buyer will try first: *"I can just ask it anything about
  this text, go back and forth, and drop the result in where my cursor is."* Our Ask is a single instruction that
  always rewrites. It has no conversation, can't answer without editing, and can't insert at the cursor.
- **About a third of their use cases we shouldn't copy.** General research, video summaries, fact checking against
  the internet and writing JavaScript all fall outside the "trustworthy house-style editor" position, and the guide
  itself shows several of them failing.
- **The guide is stale.** It cites a "September 2021" knowledge cutoff next to GPT-5 in the model list, and its
  screenshots date from 2023–2025. MadCap built a thin pass-through and hasn't invested much since. That's an
  opening, not a threat. Flare Desktop has no AI Assist.

---

## 2. What AI Assist is, decomposed

### A. Platform, account and governance

| ID | Capability | Detail from the guide |
|---|---|---|
| A1 | Single provider: OpenAI | ChatGPT only. The API key needs paid OpenAI billing. Model picker: GPT-3.5, 4, 4o, 5, 5 Mini. |
| A2 | Org-level key | An admin pastes one key in **License Settings > Integrations > AI Assist** for the whole Flare Online license. Writers never see or enter a key. |
| A3 | Permission gates | **Manage Integrations** (who can connect the key) and **Edit Files With AI Assist** (who can use it), on top of the normal Author permissions (Create/Edit Files, Edit Code). |
| A4 | Admin kill switch | Turn the permission off or don't connect a key. The guide's advice: "If your company has strict policies against AI… simply do not use it." |
| A5 | Data-posture statement | Only the prompt text is sent, and "ChatGPT does not scan anything on your computer". The guide also warns that OpenAI may log conversations. |
| A6 | Scope | Topics and snippets opened from the Projects page. **Not** available in topic reviews. |

### B. Interaction model

| ID | Capability | Detail |
|---|---|---|
| B1 | Editor toolbar button | An AI icon in the XML editor toolbar opens a modal dialog. |
| B2 | Two contexts | **Selection** (rewrite or replace it) or **cursor only** (generate new content to insert). |
| B3 | Prompt box | Shows the selected text, a free-form prompt, Shift+Enter for new lines, and a small star when the model can't parse the prompt. |
| B4 | **Multi-turn thread** | Keep refining ("simplify this for a fifth-grade reading level"). Every reply stays in the thread. Opening the dialog again starts a new thread, so nothing persists across sessions. |
| B5 | **Editable reply** | Each reply sits in a mini editor with the full toolbar: styles, lists, links, images, tables. |
| B6 | Diff tab | Red for removed text and green for added or changed text, compared with the original selection. |
| B7 | **Insert / Replace any reply** | One click puts *any* reply from the thread into the topic or snippet. |
| B8 | Commit | The writer commits through Flare Online's built-in Git afterwards. Changes aren't tracked; review happens through commits. |

### C. Use cases (prompt patterns, all through B)

| ID | Use case | What the guide shows | Guide's own verdict |
|---|---|---|---|
| C1 | Create new content | "Brief explanation of accrued interest" → insert | Works for generic, pre-cutoff knowledge. Can't know your product. |
| C2 | Regular expressions | Ask for a regex, copy it into Flare Desktop's Find and Replace | Works. Manual copy. |
| C3 | Enforce style rules | Paste a few rules into the first prompt, then "Edit the following to comply" | Partly works. Naming "Microsoft Manual of Style" instead is less reliable, and rules have to be pasted again in every thread. |
| C4 | Fact checking | Check claims about Zilker Park | **Fails**: the model kept a wrong figure (the park's size) until the writer corrected it. "Cross-check with other sources." |
| C5 | Feedback | "Give us feedback on this paragraph" | Works. Generic suggestions with no rule citations. |
| C6 | Rewrite | Simplify, expand, shorten, change tone, fix grammar, reading level | Works. The main use case. |
| C7 | Search and SEO | Conversational research, SEO ideas, related keywords | Research fails on recent facts. Keywords are copied by hand into the topic. |
| C8 | Summarize | Draft an intro from the topic body. Collate customer feedback | Intro: **copy and paste by hand**, because Replace would overwrite the body. Feedback: "use the OpenAI website". |
| C9 | Summarize videos | Paste a YouTube link and title | Shown as working. The API model can't watch video, so it's probably working from the title alone. |
| C10 | Translate | Select the whole topic → French → Replace, or paste next to the English with conditions | "MadCap Lingo with an experienced translator is always the preferred method." |
| C11 | Use your voice | Paste a voice sample at the start of every thread: "apply that exact tone…" | Works for each thread. Nothing persists, so the guide suggests "save sample text in a separate document". |
| C12 | Scripts and code | A JavaScript countdown clock, copied into the Code Editor | Works for simple cases. Manual copy. |

### D. Gaps their guide admits (useful for our messaging)
No project context; style rules and voice must be pasted again in every thread; stale knowledge; replies aren't
validated against Flare markup; Replace overwrites the selection (so the guide falls back to copy and paste for
intros, regexes, code and keywords); no tracked changes; no cost visibility; fact checking is unreliable; not
available during reviews.

---

## 3. Side by side

Legend: ✅ built · 🟡 partly built · 📋 planned (issue or plan) · ⛔ out of scope by decision · ❌ neither built nor planned

| ID | Their capability | House Style today | Plan | Status |
|---|---|---|---|---|
| A1 | OpenAI, model picker | Anthropic direct or Bifrost gateway. Routing tiers with per-task models, writer- or team-set (`models.json`) | #172 more providers (OpenAI-compatible, Bedrock, Vertex, Azure, local) | 🟡 / 📋 |
| A2 | One org key, writers never see it | Per-writer DPAPI key, or a company gateway (virtual key) | #173 secret managers and interactive gateway sign-in; #163 key asked for at first real use | 🟡 (the gateway is our org-key equivalent) |
| A3 | Per-role permission to use AI | Guide owners (`owners.txt`) govern **rules**, not AI use | #164 team AI policy: features on or off, gateway required, cost cap | 📋 |
| A4 | Admin kill switch | Lint-only mode works with no key | #164 ("Your team turned this off") | 🟡 / 📋 |
| A5 | Data-posture statement | Privacy docs and site privacy page | #164 "What's Sent" on every AI action and a usage log | 🟡 / 📋 (ours will be more specific) |
| A6 | Topics and snippets, not reviews | Topics and snippets. Results are tracked changes that flow into Flare review packages | n/a | ✅ (reviews included, unlike theirs) |
| — | Flare Online itself | Desktop plugin only | "Won't fix: Flare Online" (PLAN §2c) | ⛔ |
| B1 | Toolbar button | Ribbon tab, Tool Strip buttons, right-click menu, margin markers, live check | — | ✅ (wider) |
| B2a | Selection context | Selection, or the whole topic with no selection. Finer blocks for list items and table cells | — | ✅ |
| B2b | **Cursor-only: generate and insert** | ❌ No selection means "whole topic", never "insert here" | #169 New Topic from Notes creates a *new topic*, not an insert at the cursor | ❌ |
| B3 | Prompt box | `AskForm`: a single multi-line box | — | ✅ |
| B4 | **Multi-turn thread** | ❌ Ask is one shot. **Refine…** re-asks one proposal with an instruction, which is narrow and per card | — | ❌ (Refine is the nearest thing) |
| B5 | Rich-text edit of the reply | **Edit…** on a proposal card (text). After applying, the writer edits in Flare itself, tracked | — | 🟡 (different by design) |
| B6 | Diff | Readable word diff per block, list items numbered, XHTML behind a toggle | — | ✅ (better: tracked changes in the real topic too) |
| B7 | Insert or Replace any reply | Apply as word-level tracked changes in one undo step, validated, linted, repaired once | — | ✅ for Replace · ❌ for Insert |
| B8 | Commit | Flare's own source control. Guide-version notice, "N behind origin" | — | n/a |
| C1 | New content from general knowledge | ❌ on purpose: we don't invent facts | #169 draft from *the writer's notes*, gaps marked rather than filled | 📋 (the safer version) |
| C2 | Regex generation | Guide Pattern rows (regex rules), Build style guide mining | #168 Describe a Rule (AI drafts the rule and shows impact and conflicts) | 🟡 / 📋 (for rules, not for Find and Replace) |
| C3 | Style enforcement by pasted rules | **The core product**: 2,023 compiled Google rules plus company layer plus project variables and glossary, deterministic lint, cited AI review, Validate, Tune, Build | Phase 3 rules work (#15–#33, #165–#168) | ✅✅ |
| C4 | Fact checking | ❌ We flag risky claims ("promises about timelines") as a rule | #165 Audit Project: outdated and missing content, repeated text, against *your own project* | 🟡 / 📋 (internal consistency, not the internet) |
| C5 | Free-form feedback | AI review: cited findings (rule, why, Bad/Good example). Explain on a card | #27 self-explaining suggestions | 🟡 (structured, cited) · ❌ free-form "critique this" without edits |
| C6 | Rewrite: simplify, shorten, tone, reading level | Rewrite selection, Ask with any instruction, Restructure. Output linted against the guide | #18 readability (scores and targets) | ✅ (no one-click presets) |
| C7a | Conversational research | ❌ | — | ❌ |
| C7b | SEO ideas and keywords | Topic assists: meta description, index keywords with subentries, search synonyms, micro content, glossary candidates (Copy buttons) | — | ✅ (copy, not write, due to API limits) |
| C8a | Summarize into an intro | ❌ The original PLAN listed a Summarize action. It never shipped as a command | — | ❌ |
| C8b | Collate external feedback | ❌ (their guide also says use the website) | — | ❌ |
| C9 | Summarize videos | ❌ | — | ❌ |
| C10 | Translate | ❌ | — | ❌ |
| C11 | Voice | Voice *as rules*: the company guide, Build style guide mines conventions from your best content, team prompt overrides (`FlareAI/prompts/*.md`) | #167 house rules from good writing | ✅ / 📋 (persistent and shared, unlike theirs) |
| C12 | Scripts and code | ❌ The validator rejects new markup it doesn't recognize | — | ❌ |

**Ahead of them, with no counterpart in their product:** deterministic instant lint; project-wide check; snippet and
variable awareness; accessibility and structure checks; tracked changes everywhere; persistent review sessions;
team feedback loop with rule tuning; style-guide validator and builder; alt text from vision; LLM export
(`llms.txt`); cost routing; check on save and build; CLI, CI and Markdown on the roadmap.

---

## 4. The gaps, classified

Each gap gets **Can we build it?** (technical feasibility in a Flare Desktop plugin) and **Should we?** (fit with the
direction in `ROADMAP.md`, the planning brief and memory: easy for new writers, wide capability without overload,
everything in place with no copy-paste round trips, every write tracked and validated, no feature gating).

### 4.1 Can build, and probably should

| Gap | Feasibility | Why it fits | Watch-outs |
|---|---|---|---|
| **B2b Insert at the cursor** (generate content where the caret is) | Good. `InsertDocumentNode` exists. The risk is the known Flare 22 **Redo crash** with plugin inserts (seen with variables and annotations). Plain `<p>`/list inserts with no MadCap elements need a spike to confirm. | Removes the biggest copy-paste round trip in their guide (intros, explanations, keywords) and matches "no copy-paste round trips". | Must stay a tracked change in one undo step. Validate and lint the inserted fragment like a restructure. |
| **B4 A conversation on the selection** (multi-turn Ask, with Apply on any turn) | Good. The Messages API is multi-turn by nature, and the review pane already hosts rich state. | This is the one behavior buyers coming from AI Assist or ChatGPT will expect. It also lets the house-style context (rules, variables, glossary) carry through a whole conversation, which is what their users have to paste in by hand each time. | Risk of becoming a generic chatbot (see §5). Keep it anchored to the selection or cursor, and keep "Apply" going through the existing validate, lint and repair path. Cost per turn should be visible (#4, #164). |
| **C5 Ask without editing** ("What's unclear here?", "Is this consistent with the glossary?") | Trivial. The same call with an answer-only output contract. | Today Ask *always* rewrites, so a writer can't just ask. That's a small change with a large perceived capability gain. It also folds naturally into B4. | Answers should still cite rules where they apply (#27). |
| **C8a Summarize into an intro or overview** | Easy once B2b exists: summarize the topic or section and insert it at the cursor or the top. | A real writer task their guide handles badly (copy and paste by hand). Doesn't invent facts, because it summarizes the writer's own text. | Insert-at-cursor dependency. |
| **C6 one-click rewrite presets** (Simplify, Shorten, Expand from notes, Reading level) | Trivial: named instructions into the existing Ask path, or a split button. | Easier for new writers. Fits "discovery at the moment of need" if offered contextually (right-click or the Ask dialog) rather than as new ribbon buttons. | Ribbon-button discipline from the brief. #18 readability can supply the reading-level target. |
| **A2/A3/A4 org key and team AI policy** | Planned in #164, #172 and #173. The gateway already behaves like their org key. | Their admin story (one key, per-role permission, kill switch) is the thing IT asks about. We can exceed it with What's Sent and cost caps. | Flare's own role permissions aren't reachable from Desktop (see §4.3). Policy has to live in the guide project, as #164 designs it. |
| **A1 OpenAI-compatible provider** | Planned in #172. `BifrostClient` already speaks the OpenAI chat format. | Teams standardized on OpenAI or Azure won't need a second vendor. | Quality and validation tuning per model family. The bench must cover it. The memory notes a vendor preference: it's the user's call whether to go beyond Anthropic and Bifrost. |

### 4.2 Can build, but questionable or probably shouldn't

| Gap | Feasibility | Case for | Case against |
|---|---|---|---|
| **C10 Translate** | Our run-addressed rewrite (the model returns text per run, never markup) is *better suited* to markup-safe translation than AI Assist's whole-topic Replace. | Writers will try it. We'd preserve variables, snippets and conditions where they lose them. | MadCap Lingo's territory (their own guide defers to it). Quality liability in languages the writer can't read. Our style engine is English-only, so the translated text couldn't be checked. Maybe "draft translation into a copy of the topic" at most. |
| **C2 Regex for Find and Replace** | Easy: an Ask answer mode. | Their guide sells it as a highlight. | Narrow. #168 Describe a Rule already covers the high-value version (patterns that become rules with impact and conflict checks). A general regex helper would be a second, weaker home for the same thing. |
| **C1 New content from general knowledge** | Easy technically. | Their headline demo. | Conflicts with the accuracy stance (#169 cites accuracy as writers' top barrier) and with "no invented facts". The notes-grounded #169 plus insert-at-cursor covers the legitimate need. |
| **B5 Rich-text editor inside the reply** | Hard in WinForms (re-implementing Flare's editor). | Parity. | Applying as a tracked change and editing in Flare's real editor is *better*. Not worth building. |
| **C7b Write keywords and meta into the topic** (instead of Copy) | Blocked or unsafe today: head metadata, index markers, synonym and micro-content files aren't safely writable through the plugin API (PLAN §2h). | Removes copy steps. | Revisit only if a Flare API change or a safe file-level write path appears. |
| **C12 Scripts and code into topics** | Possible, but the validator rejects unknown markup by design. | — | Security and maintainability. It's outside a style tool's job, and Flare's Code view already exists. |

### 4.3 Can't build (or not as a Desktop plugin)

| Gap | Why not |
|---|---|
| **Flare Online / browser editor integration** | No plugin API for Flare Online, and it's outside our product (the decision is recorded in PLAN). The nearest reach is the MCP server, CLI and GitHub Action (#22, #32) for repo-based authoring, which works on Flare Online projects in Git without being *in* Flare Online. |
| **Hooking Flare's license, user or role permissions** ("Edit Files With AI Assist") | Flare Desktop exposes no licensing or permission API to plugins. The equivalent is a team policy in the guide project (#164), with owners governed through Git branch protection. |
| **Org key stored in the Flare license** | Same reason. The gateway (or #173 secret managers) is the substitute. |
| **Watching videos (C9)** | The API model can't fetch or watch YouTube. Feasible only with a transcript, which turns it into "summarize text" (C8). |
| **AI Assist inside Flare's own topic review UI** | We don't own that UI. Our tracked changes already flow into Flare review packages, which is arguably the better answer. |

### 4.4 Can build, but shouldn't (scope discipline)

| Gap | Reason |
|---|---|
| **C7a General research or "ChatGPT as search engine"** | Generic chatbots already do this well. It adds nothing House Style-specific, invites hallucination, and turns the plugin into a chat client. The Anthropic web-search tool *could* do it, but wouldn't work uniformly through every gateway. |
| **C4 Internet fact checking** | Their own demo fails. A wrong "verified" stamp is worse than none. The defensible version is **internal consistency** (#165: conflicting figures across topics, outdated content, repeated text). |
| **C8b Collating external documents** | Not Flare content. Their guide sends users to the OpenAI website. |
| **C11 Voice conditioning for each thread** | Already solved better: voice lives in the shared guide. Offering per-thread voice samples would create a second, unshared source of truth for voice, against the "single source of truth" decision (`confluence-sync.md`). A **"learn voice from these topics"** input to Build style guide (#167) is the right home. |

---

## 5. Things to think about

1. **What category are we in?** AI Assist is "a chatbot in the editor". House Style is "your style guide, enforced,
   with AI". Buyers who've seen ChatGPT will judge us on the chatbot axis first. A selection-anchored conversation
   (B4 + C5 + B2b) closes that gap *without* changing category, as long as every Apply still goes through
   validate → lint → tracked change. That combination is the single most important decision this report surfaces.
2. **"House context, automatically" is the whole pitch.** Every weakness their guide admits (paste rules each time,
   paste voice each time, no project knowledge, copy by hand afterwards, no tracked changes) is something House
   Style does by default. If a conversation mode ships, every turn carries the guide, the project's variables and
   glossary, and the topic's structure. That's a strong demo: the same prompt in AI Assist and in House Style, side
   by side.
3. **Insert-at-cursor is the hidden prerequisite.** Three of the "should" items (generate here, summarize into an
   intro, keywords and snippets) need it, and it's the one with real Flare API risk (the Redo crash). A spike decides
   how much of §4.1 is cheap.
4. **Governance is their only admin story, and ours can be stronger.** Their story is one key, two permissions and
   "don't use it". #164's What's Sent, cost caps and feature switches in the guide project beat that, and they're
   what an IT reviewer reads first. Order relative to the conversation work is a choice.
5. **Translation is the one "shouldn't" with a real technical edge.** Our run-addressed, markup-preserving rewrite
   is a genuinely better translation substrate than theirs. It still competes with Lingo and can't be style-checked.
   Worth a conscious "no" or "later", not an accidental one.
6. **The provider question (#172) gets sharper.** AI Assist users already have OpenAI keys. Supporting an
   OpenAI-compatible endpoint lowers the switching cost for exactly the users this comparison targets.

---

## 6. Open questions to check before deciding

- ~~Does Flare Desktop ship AI Assist too?~~ No (owner, 2026-10-23). AI Assist exists only in MadCap Central (since
  2023-10) and Flare Online (since May 2025).
- MadCap's press material says users can "train it on their own content". Nothing in the guide supports that beyond
  pasting text into a thread. Is there an undocumented retrieval feature?
- Is the "September 2021" text just stale copy, or does Flare Online pin old models by default? (This matters for
  any head-to-head demo.)
- Insert spike: does a plugin `InsertDocumentNode` of a plain `<p>` survive Undo and Redo in current Flare?

---

## Sources
- MadCap Flare Online AI Assist Guide (PDF, the provided copy; public copy:
  [docs.madcapsoftware.com](https://docs.madcapsoftware.com/flare-online/Flare-Online-AI-Assist-Guide.pdf))
- [AI Assist — Flare Online online help](https://documentation.madcapsoftware.com/flare-online/Content/Flare-Online/Projects/Step2-Authoring/AI-Assist/AI-Assist.htm)
- [MadCap press release: Flare Online](https://www.madcapsoftware.com/press-release/madcap-flare-online/)
- [MadCap Central releases (ComponentSource)](https://www.componentsource.com/product/madcap-central/releases/2795066)
- Internal: `PLAN.md`, `docs/plans/ROADMAP.md`, `docs/plans/_brief.md`, `docs/plans/confluence-sync.md`, GitHub issues
  #4, #18, #22, #27, #32, #139, #163–#169, #172, #173, `docs/user-guide/Content/*.htm`, `src/FlareAI.Flare/Commands.cs`,
  `src/FlareAI.Flare/UI/AskForm.cs`
