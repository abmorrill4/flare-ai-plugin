#!/usr/bin/env python3
"""Writes SOURCES.md, NOTICE.md and (for share-alike sources) LICENSE for a corpus project (dev tool, not shipped).
Usage: make_sources.py PROFILE PROJECT_DIR     (PROFILE: hardware | policy)"""
import collections
import os
import sys

profile, proj = sys.argv[1], sys.argv[2]
NL = "\n"
TSV = {"hardware": "C:/Users/arthu/AppData/Local/Temp/corpus-src/hw-md/topics.tsv", "policy": "C:/Users/arthu/AppData/Local/Temp/corpus-src/pol-md/topics.tsv"}[profile]
src_count = collections.OrderedDict()
cats = collections.Counter()
for line in open(TSV, encoding="utf-8").read().split(NL):
    if line.strip():
        f = line.split("\t")
        src_count[f[3]] = src_count.get(f[3], 0) + 1
        cats[f[0]] += 1


def w(name, text):
    with open(os.path.join(proj, name), "w", encoding="utf-8", newline="\n") as fh:
        fh.write(text)


if profile == "hardware":
    files = "\n".join("| `%s` | %d |" % (k, v) for k, v in src_count.items())
    w("SOURCES.md", """# Sources: hardware-install

All text and images come from one public source, listed here with the version pinned and the licence checked.

## Raspberry Pi Documentation

| | |
|---|---|
| Repository | https://github.com/raspberrypi/documentation |
| Pinned commit | `ddea98e98dafe9419feb4fd82710c2fbdef7dc44` (merge of pull request 781, committed 2026-10-13T13:44:27+01:00) |
| Retrieved | 2026-10-21, by a shallow, sparse clone of `documentation/asciidoc/accessories` and `documentation/asciidoc/computers` |
| Licence | Creative Commons Attribution-ShareAlike 4.0 International (CC BY-SA 4.0) |
| Licence file | `LICENSE.md` at the pinned commit. Its first line says "Attribution 4.0 International" but links to the `by-sa` deed, and the legal text that follows is the Attribution-ShareAlike text. The stricter reading (BY-SA) is the one applied here. The text is kept in `LICENSE` in this folder. |
| Scope | Only files under `documentation/asciidoc/` (text and the images next to it) were used. No build tooling or code from the repository was copied. The licence file carries no non-commercial or no-derivatives term. |

Required attribution: see `NOTICE.md`. Because the source is share-alike, this whole folder is published under CC BY-SA 4.0 and is never mixed into code.

### Source files and the topics taken from them

| Source file (under `documentation/asciidoc/`) | Topics |
|---|---|
%s

%d topics in all, in these folders: %s.

## What was changed

The source is AsciiDoc; the project is Flare XHTML. The conversion is `_prep/adoc2html.py` (written for this corpus), followed by `corpuskit import`.

- Sections of the source files became topics (a few large sections were split one level further). Sections about software setup, boot configuration, programming and imaging were left out; the topic list is `_prep/hw-curated.json`.
- Brand, company and third-party vendor names were replaced by fictional ones (the table is the `names` entry in `_prep/hw-curated.json`). The product family name is the variable `General.ProductName`; the company name is `General.CompanyName`. Descriptive hardware terms (Compute Module, Touch Display, HAT, Active Cooler) and standards (USB, HDMI, PCIe, Ethernet) were kept.
- Every external link now points at `https://www.example.com/...`; link text was kept.
- Images were copied only for the topics kept, scaled to at most 720 pixels wide and recompressed. The photographs and drawings still show the source product's own marks; alt text and file names were made fictional.
- Spec tables: neighbouring cells with identical values (for example the same size for two camera models) were merged with `colspan`.
- Added by the corpus build, not in the source: figure caption paragraphs ("Figure N."), three snippets (`disconnect-power`, `static-discharge`, `print-notice`) placed in the topics where they fit, the condition tags `Default.Print` (the print notice) and `Default.ScreenOnly` ("Related topic" paragraphs), a glossary, the Flare project files and the stylesheet.
- Seeded defects were inserted by `corpuskit inject` and are listed with their fixes in `labels.jsonl`.
""" % (files, sum(src_count.values()), ", ".join("%s (%d)" % (k, v) for k, v in sorted(cats.items()))))
    w("NOTICE.md", """# Notice: hardware-install

This project is an adaptation of the Raspberry Pi Documentation.

- Original work: "Raspberry Pi Documentation", copyright Raspberry Pi Ltd and contributors, https://github.com/raspberrypi/documentation, commit `ddea98e98dafe9419feb4fd82710c2fbdef7dc44`.
- Licence: Creative Commons Attribution-ShareAlike 4.0 International, https://creativecommons.org/licenses/by-sa/4.0/. The full text is in `LICENSE`.
- Changes: the text was converted from AsciiDoc to Flare XHTML, divided into topics, shortened by leaving out software-only sections, and edited so that brand, company and vendor names are fictional; images were scaled and recompressed; defects were inserted on purpose and are labelled in `labels.jsonl`. Details are in `SOURCES.md`.
- This adaptation is licensed under the same licence (CC BY-SA 4.0). It is test data for a documentation tool. It is not a Raspberry Pi publication and does not imply any endorsement by Raspberry Pi Ltd.
- Trademarks: the photographs and drawings show the original product marks. Those marks belong to their owners and are not licensed by this notice.
""")
    lic = open("C:/Users/arthu/AppData/Local/Temp/corpus-src/hw-rpi/LICENSE.md", encoding="utf-8").read().split("\n")
    w("LICENSE", "\n".join(lic[4:]).rstrip("\n") + "\n")
else:
    cfr = "\n".join("| `%s` | %d |" % (k, v) for k, v in src_count.items() if k.startswith("45 CFR"))
    nist = "\n".join("| `%s` | %d |" % (k, v) for k, v in src_count.items() if k.startswith("NIST"))
    w("SOURCES.md", """# Sources: policy

Two public-domain sources supply every regulation and control topic. Three framing topics are generated from the sources' own metadata.

## 1. Electronic Code of Federal Regulations, Title 45, Parts 160 and 164

| | |
|---|---|
| Publisher | Office of the Federal Register, via the eCFR versioner API |
| URLs | `https://www.ecfr.gov/api/versioner/v1/full/2026-10-06/title-45.xml?part=164` and `...?part=160` |
| Version | The text in force on 2026-10-06 (latest issue date for Title 45; the API reported it up to date as of 2026-10-20) |
| Retrieved | 2026-10-21 |
| Licence | United States federal government work, public domain (17 U.S.C. 105). No licence file exists for the API, so the version is pinned by the issue date in the URL |

### Sections used

| Section | Topics |
|---|---|
%s

## 2. NIST SP 800-53 Rev. 5.2.0 controls and SP 800-53A Rev. 5.2.0 assessment procedures

| | |
|---|---|
| Repository | https://github.com/usnistgov/oscal-content |
| Pinned commit | `78650f02ad9321bb7b817846f8fbd4f2bcd620de` (committed 2026-05-13T05:13:20Z) |
| File | `nist.gov/SP800-53/rev5/json/NIST_SP-800-53_rev5_catalog.json` (catalog version 5.2.0, OSCAL 1.2.2, last modified 2026-05-11) |
| Retrieved | 2026-10-21, by a shallow, sparse clone |
| Licence | Public domain in the United States, and the project waives rights worldwide under CC0 1.0 (`LICENSE.md` at the pinned commit) |

### Controls used

| Control | Topics |
|---|---|
%s

## What was changed

The sources are XML and JSON; the project is Flare XHTML. The conversion is `_prep/policy_build.py` (written for this corpus), followed by `corpuskit import`.

- Regulation: each section became a topic. A paragraph that starts with a marker and an italic title, such as "(a)(1) Standard: Access control.", became a heading whose level follows the paragraph's place in the hierarchy (h2 to h6), with the marker kept in the heading text. Definitions sections became definition lists. The Appendix A matrix is kept as a table with its merged cells. Section numbers in the text (for example 164.308) became links to the topic for that section when it is in the set.
- Controls: each control became a topic. The control text is a `blockquote`; the discussion is a `p.note`; parameters and assessment objectives are definition lists; assessment methods and objects are headings and lists; enhancements are nested sections. NIST's `{{ insert: param, ... }}` placeholders became the bracketed form NIST prints, `[Assignment: organization-defined ...]`. Withdrawn controls were left out.
- Family index topics list every control and enhancement in a family as a table. The time-limits topic collects, by a text search, the sentences in the selected regulation sections that set a period or a date; each row links back to its section.
- Added by the corpus build, not in the sources: the three framing topics (About this policy set, Source documents and revision dates, Time limits and retention periods), the document-control and print-notice snippets, the `Policy.*` variables and `General.CompanyName` (a fictional organization), the condition tags `Default.Print` and `Default.ScreenOnly`, a glossary, the Flare project files and the stylesheet.
- Seeded defects were inserted by `corpuskit inject` and are listed with their fixes in `labels.jsonl`.
- Names: the sources name public bodies (the Secretary, HHS, NIST, NARA, OMB) and public documents. They are part of the regulation and standard and were kept; no company, product or person from outside the sources appears.
""" % (cfr, nist))
    w("NOTICE.md", """# Notice: policy

This project is an adaptation of two public-domain works.

- "Code of Federal Regulations, Title 45, Parts 160 and 164", published by the Office of the Federal Register through the eCFR (https://www.ecfr.gov/), text in force on 2026-10-06. United States federal government work, public domain.
- "NIST Special Publication 800-53 Revision 5.2.0, Security and Privacy Controls for Information Systems and Organizations" and "NIST Special Publication 800-53A Revision 5.2.0, Assessing Security and Privacy Controls", in the OSCAL catalog of https://github.com/usnistgov/oscal-content (commit `78650f02ad9321bb7b817846f8fbd4f2bcd620de`), by the National Institute of Standards and Technology. Public domain in the United States; rights waived worldwide under CC0 1.0 (https://creativecommons.org/publicdomain/zero/1.0/).
- Changes: converted to Flare XHTML, divided into topics, headings and lists rebuilt from the paragraph markers, parameters written in NIST's bracketed form, defects inserted on purpose (labelled in `labels.jsonl`). Details are in `SOURCES.md`.
- The text added by the corpus build (framing topics, snippets, variables, glossary) is released under CC0 1.0. This is test data for a documentation tool; it is not an official publication of the Federal Register or NIST and does not imply their endorsement.
- No share-alike licence applies, so there is no `LICENSE` file in this folder.
""")
print("ok", len(src_count))
