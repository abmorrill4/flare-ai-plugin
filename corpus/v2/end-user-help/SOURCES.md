# end-user-help: sources

End-user help for a fictional file-sharing, chat and calendar product, **Acme Hub** (company: Acme Software), with a web app and a desktop client, built from a permissively licensed open-source user manual written by many authors. Issue #73, epic #66. Design: `dist/audit/TEST-STRATEGY.md` section 1.2.

## Source

| | |
|---|---|
| Project | Nextcloud documentation repository, `user_manual` folder (reStructuredText) |
| URL | https://github.com/nextcloud/documentation |
| Commit | `1aa36a3d332c85ca9951a365e0c53e9a2cef8628`, committed 2026-10-21T17:04:04Z (sparse shallow clone of the default branch, retrieved 2026-10-21) |
| Licence | Creative Commons Attribution 3.0 Unported. `README.rst` at that commit: "All documentation in this repository is licensed under the Creative Commons Attribution 3.0 Unported license"; `COPYING` (the legal code) is copied here as `LICENSE` |
| Attribution | "Nextcloud GmbH and Nextcloud contributors" (the copyright line in the upstream `conf.py`), see `NOTICE.md` |
| Files used | 81 of the 87 `.rst` pages under `user_manual/` (not used: the table-of-contents page, and five pages about other vendors' products: the Matterbridge bridge, the Thunderbird, GNOME and KDE sync guides, and the Markdown-files page of Collectives) |
| Not used | Screenshots (see below), the `locale` folder, the rest of the repository (which has other licences), anything outside `user_manual/` |

CC BY needs attribution, the licence and a statement of changes. All three are in this folder. No NC, ND or SA source, no customer or pilot content, so no share-alike `LICENSE` obligation arises; the legal code is included because CC BY asks for the licence text or its URI.

## What the topics are

207 topics. Pages that are long and have at least two sections (3,200 characters or more) are split into an overview topic with a link list and one topic per second-level section; the FAQ stays one topic.

| Folder | Topics | Content |
|---|---|---|
| `general` | 29 | the landing page; web interface tour; activity; two-factor sign-in; sessions; user preferences; AI assistant; universal access |
| `files` | 57 | tasks: access, share, version, tag, encrypt, transfer ownership, WebDAV, external storage, quota |
| `desktop` | 35 | installing and using the desktop client; conflicts; command line; update channels; the FAQ (6 questions as drop-downs) |
| `talk` | 41 | chat, calls, conversations, moderation, bots, webinars |
| `groupware` | 42 | calendar, contacts, mail, absence, and syncing from Android, iOS, macOS and Windows |
| `collectives` | 3 | getting started and onboarding |

By kind (approximate): tasks about 170, UI tours and overviews about 25 (web interface, appearance, call views, the six overview topics and the landing page), FAQ 2 (one topic, one section page), troubleshooting and known-issues 6. The upstream manual has few FAQ and troubleshooting pages; the project does not invent more.

## What we changed (CC BY asks for this)

- Converted reStructuredText to Flare XHTML: `stage.py` reads the pages with docutils (the Sphinx roles `:ref:`, `:doc:`, `:kbd:`, `:guilabel:` and the directives used are re-implemented minimally), then `corpuskit import` writes topics, `MadCap:xref`s, block snippets and variables.
- **Names.** The product, company, vendors, outside services, example people and hosts are replaced by fictional names (`stage.py`, table `THIRD`); the product and company are `MadCap:variable`s (`General.ProductName` = Acme Hub, `General.CompanyName` = Acme Software). Links to outside sites are removed; links to the product's own sites point to `example.com`. Names of operating systems and file formats and protocols that the tasks cannot do without (Windows, macOS, Linux, Android, iOS, Finder, WebDAV, CalDAV) are kept; they are not the documented product's or its maker's names.
- **Screenshots.** 344 images are referenced. The originals show the vendor's branding and are large; each is replaced by a generated grey placeholder PNG of a quarter of the size, and the upstream `alt` text is kept as authored (265 of the 348 image elements have it, 83 do not, as upstream; plus 6 injected `alt_missing` defects).
- **Added by us** (listed in `project.json` under `build.generatedByUs`): the landing page; the "Need more help?" footer (a block snippet in all 207 topics); two notes that repeat on sharing and desktop topics; a print note and a screen note (conditions `Default.Print`, `Default.ScreenOnly`); a sentence about the administrator that becomes a text snippet; the glossary and its markers; a mix of `b` and `span.uicontrol` for UI labels (about 40% of pages use the style class, to imitate authors who differ).
- **Targets and TOCs.** `HTML5.fltar` (WebHelp2, TOC `Main`, excludes `Default.Print`) and `PDF.fltar` (TOC `Print`, excludes `Default.ScreenOnly`).
- **Defects inserted** by `corpuskit inject` with a fixed seed (see `LABELS.md`).

## What is in the project (counts from `project.json`)

207 topics; 7 snippet files (the footer, a block snippet used by all 207 topics; a restart note used by 24 and a sharing note used by 11; a print note and a screen note, 13 uses each, each with a condition; one natural repeated block used by 3; one text snippet used by 11); 334 variable uses; 196 xrefs; 6 drop-downs; 16 togglers; 52 glossary term markers (10 glossary entries); 19 tables (55 header cells); 54 code blocks; 37 `kbd`; about 300 `b` and 109 `span.uicontrol` UI labels; 107 notes (`p.note`) and 16 other admonitions; numbered procedures with sub-steps and paragraphs inside list items; images with and without alt; headings h1 to h4; two targets, two TOCs.

## Rebuild

`source-prep/build.ps1 -Upstream <sparse clone of the repository at the commit above, with user_manual checked out>` rebuilds the project byte for byte (needs `dotnet`, `uv`). Seed 73001, injection density 0.09. The scripts in `source-prep/` are corpus-construction tools, not part of the Flare project.

## CorpusKit notes (found while building this project; tool code was not changed)

1. `--defects corpus/schema/defects.yaml` fails: `defects.yaml needs a top-level 'defects' list` (the shared file is a taxonomy, the injector reads its own ops format). The built-in catalogue was used; the `rules` it writes are family hints, not rule ids.
2. A single injector pass is dominated by one cheap kind (a period turned into `!`); the build runs five passes with `--kinds`.
3. Snippet extraction keeps topic-relative `href`/`src` after moving a block into `Content/Resources/Snippets/`; `source-prep/enrich_common.py` rebases them.
4. `import` has no conditions, text snippets, togglers or glossary terms; they are added by `enrich.py`. `BlockWalker` does not treat `MadCap:toggler` as a block (the Scoring `Xhtml` does), so togglers are added after injection on paragraphs without a label, and no label carries the `toggler` context.
5. `--alt-drop-rate` removes `alt` without a label (it only lists the images in `corpus-kit.json`), so it was not used; the `alt_missing` kind was used instead.
6. `FlareAI.Scoring validate --root` does not expand variables, `corpuskit` does: 6 natural labels whose quote, prefix or suffix included a variable's value (for example a bold UI label that is the product name) could not be anchored by `validate` and were dropped by `source-prep/drop_unanchorable.py`.
7. Injected labels are `status: proposed` and there is no `project.json` writer; scripts in `source-prep/` do both.

## Not done

- Opening the project in Flare 22 and building the HTML5 target once by hand (shared acceptance) was not done in this run; the files pass `corpuskit check` and `FlareAI.Scoring validate`, which is not the same thing. A screenshot is still needed for the PR.
- `NOTICE.md` at the repository root has no entry for this folder yet (outside this work package's owned paths): add "Corpus v2 `end-user-help`: derived from the Nextcloud user manual, CC BY 3.0, see `corpus/v2/end-user-help/NOTICE.md`".
