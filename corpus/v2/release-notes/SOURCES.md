# Sources: release-notes

## Imported text

| | |
|---|---|
| Project | Kong Gateway (open-source edition), repository `Kong/kong` |
| Files | `CHANGELOG.md` (releases 3.x) and `CHANGELOG-OLD.md` (releases 1.x and 2.x) |
| URL | https://github.com/Kong/kong/blob/21032122ab80c973f368f6bc539f53985e7164e8/CHANGELOG.md and https://github.com/Kong/kong/blob/21032122ab80c973f368f6bc539f53985e7164e8/CHANGELOG-OLD.md |
| Pinned commit | `21032122ab80c973f368f6bc539f53985e7164e8` (default branch `master`, committed 2026-10-19T02:44:32Z, fetched 2026-10-21) |
| Licence | Apache License 2.0. Verified by reading `LICENSE` at the pinned commit ("Apache License, Version 2.0, January 2004", "Copyright 2016-2026 Kong Inc."); GitHub's licence API reports `Apache-2.0`. The repository has no `NOTICE` file at that commit. |
| Licence text | `LICENSE` in this folder is the pinned commit's `LICENSE` verbatim. |
| Attribution | Changelog text © Kong Inc. and the contributors of the changelog entries, Apache-2.0. See `NOTICE.md`. |
| Release dates | 3.x dates (except 3.0.0) come from the GitHub releases API (`published_at`) for tags 3.9.1, 3.9.0, 3.8.0, 3.7.0, 3.6.0, 3.5.0, 3.4.0, 3.3.0, 3.2.0, 3.1.0, 2.8.0, 2.7.0 and 2.5.0; other dates come from the changelog. Dates are facts, not expression. |

## What was used

24 releases (the table below). From each, the Breaking changes, Deprecations, Changes, Features, Performance and Fixes entries. Of 1076 entries considered, 398 were dropped by a filter that removes entries naming third-party products, platforms or services (operating systems, databases, language runtimes, cloud and observability vendors, libraries, CVE ids) so that no real product name appears as the "our product" or as a neighbour of it, and each section was capped to keep topics a readable size. `Dependencies` sections were not used.

## What was changed (modifications, as Apache-2.0 section 4(b) asks)

- Converted from Markdown to Flare XHTML topics with CorpusKit `import`; one overview topic for each release, plus separate "New in", "Fixes in" and "Breaking changes" topics for some.
- The product name "Kong" (and "Kong Gateway") is replaced by the variable `General.ProductName` ("Acme Orbit"); command and header names such as `kong`, `KONG_`, `X-Kong-...` are renamed to `orbit`, `ORBIT_`, `X-Orbit-...`.
- Pull request and issue links are removed. The numbers are kept as issue ids written `ORB-n`.
- Thanks and credit sentences, user handles and Jira ids are removed. No person's name appears in the text.
- Emoji shortcodes and "Back to TOC" links are removed; wrapped lines are joined; the sentence "Release dates use UTC ..." and the intro sentences about counts and time words are original text added by the corpus authors.
- Defects were then injected (`corpuskit inject`, seed 20261021, density 0.10; see `LABELS.md`). The injected defects are not in the upstream text.

The 12 topics under `Content/index/` and the three "Before you upgrade" and boilerplate snippets are original text written for this corpus (not from the changelog). The glossary entries are original.

## Releases

| Version | Date | Source file | Topic |
|---|---|---|---|
| 3.9.1 | June 4, 2025 | CHANGELOG.md | Content/releases/release-3-9-1.htm |
| 3.9.0 | December 13, 2024 | CHANGELOG.md | Content/releases/release-3-9-0.htm |
| 3.8.0 | September 11, 2024 | CHANGELOG.md | Content/releases/release-3-8-0.htm |
| 3.7.0 | May 28, 2024 | CHANGELOG.md | Content/releases/release-3-7-0.htm |
| 3.6.0 | February 9, 2024 | CHANGELOG.md | Content/releases/release-3-6-0.htm |
| 3.5.0 | November 8, 2023 | CHANGELOG.md | Content/releases/release-3-5-0.htm |
| 3.4.0 | August 9, 2023 | CHANGELOG.md | Content/releases/release-3-4-0.htm |
| 3.3.0 | May 18, 2023 | CHANGELOG.md | Content/releases/release-3-3-0.htm |
| 3.2.0 | February 21, 2023 | CHANGELOG.md | Content/releases/release-3-2-0.htm |
| 3.1.0 | December 6, 2022 | CHANGELOG.md | Content/releases/release-3-1-0.htm |
| 3.0.0 | September 12, 2022 | CHANGELOG.md | Content/releases/release-3-0-0.htm |
| 2.8.0 | March 1, 2022 | CHANGELOG-OLD.md | Content/releases/release-2-8-0.htm |
| 2.7.0 | December 15, 2021 | CHANGELOG-OLD.md | Content/releases/release-2-7-0.htm |
| 2.5.0 | July 13, 2021 | CHANGELOG-OLD.md | Content/releases/release-2-5-0.htm |
| 2.4.0 | April 6, 2021 | CHANGELOG-OLD.md | Content/releases/release-2-4-0.htm |
| 2.3.0 | January 8, 2021 | CHANGELOG-OLD.md | Content/releases/release-2-3-0.htm |
| 2.2.0 | October 23, 2020 | CHANGELOG-OLD.md | Content/releases/release-2-2-0.htm |
| 2.1.0 | July 16, 2020 | CHANGELOG-OLD.md | Content/releases/release-2-1-0.htm |
| 2.0.5 | June 30, 2020 | CHANGELOG-OLD.md | Content/releases/release-2-0-5.htm |
| 2.0.0 | January 20, 2020 | CHANGELOG-OLD.md | Content/releases/release-2-0-0.htm |
| 1.3.0 | August 21, 2019 | CHANGELOG-OLD.md | Content/releases/release-1-3-0.htm |
| 1.2.0 | June 7, 2019 | CHANGELOG-OLD.md | Content/releases/release-1-2-0.htm |
| 1.1.0 | March 27, 2019 | CHANGELOG-OLD.md | Content/releases/release-1-1-0.htm |
| 1.0.0 | December 18, 2018 | CHANGELOG-OLD.md | Content/releases/release-1-0-0.htm |
