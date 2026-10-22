# Sources: hardware-install

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
| `accessories/ai-hat-plus/about.adoc` | 10 |
| `accessories/ai-kit/about.adoc` | 3 |
| `accessories/bumper/about.adoc` | 1 |
| `accessories/camera/install.adoc` | 4 |
| `accessories/camera/hardware_specification.adoc` | 1 |
| `accessories/camera/lens.adoc` | 1 |
| `accessories/camera/filters.adoc` | 1 |
| `accessories/camera/gs.adoc` | 1 |
| `accessories/camera/external_trigger.adoc` | 1 |
| `accessories/cm5-programming-jig/about.adoc` | 10 |
| `accessories/display/display_intro.adoc` | 3 |
| `accessories/m2-hat-plus/about.adoc` | 7 |
| `accessories/monitor/monitor_intro.adoc` | 6 |
| `accessories/sd-cards/about.adoc` | 1 |
| `accessories/smart-display-module/about.adoc` | 4 |
| `accessories/ssd-kit/about.adoc` | 1 |
| `accessories/ssds/about.adoc` | 1 |
| `accessories/touch-display-2/about.adoc` | 7 |
| `accessories/tv-hat/about-tv-hat.adoc` | 2 |
| `accessories/usb/about.adoc` | 1 |
| `accessories/audio/getting_started.adoc` | 1 |
| `accessories/build-hat/preparing-build-hat.adoc` | 1 |
| `accessories/build-hat/compat.adoc` | 1 |
| `computers/raspberry-pi/power-supplies.adoc` | 6 |
| `computers/raspberry-pi/raspberry-pi-compliance.adoc` | 1 |
| `computers/raspberry-pi/rtc.adoc` | 1 |
| `computers/raspberry-pi/pcie.adoc` | 1 |
| `computers/raspberry-pi/power-button.adoc` | 1 |
| `computers/compute-module/introduction.adoc` | 8 |
| `computers/compute-module/datasheet.adoc` | 1 |
| `computers/compute-module/cmio-camera.adoc` | 3 |

91 topics in all, in these folders: cabling (15), indicators (3), maintenance (5), mounting (21), overview (12), power-on (7), safety (3), specifications (20), unpacking (5).

## What was changed

The source is AsciiDoc; the project is Flare XHTML. The conversion is `_prep/adoc2html.py` (written for this corpus), followed by `corpuskit import`.

- Sections of the source files became topics (a few large sections were split one level further). Sections about software setup, boot configuration, programming and imaging were left out; the topic list is `_prep/hw-curated.json`.
- Brand, company and third-party vendor names were replaced by fictional ones (the table is the `names` entry in `_prep/hw-curated.json`). The product family name is the variable `General.ProductName`; the company name is `General.CompanyName`. Descriptive hardware terms (Compute Module, Touch Display, HAT, Active Cooler) and standards (USB, HDMI, PCIe, Ethernet) were kept.
- Every external link now points at `https://www.example.com/...`; link text was kept.
- Images were copied only for the topics kept, scaled to at most 720 pixels wide and recompressed. The photographs and drawings still show the source product's own marks; alt text and file names were made fictional.
- Spec tables: neighbouring cells with identical values (for example the same size for two camera models) were merged with `colspan`.
- Added by the corpus build, not in the source: figure caption paragraphs ("Figure N."), three snippets (`disconnect-power`, `static-discharge`, `print-notice`) placed in the topics where they fit, the condition tags `Default.Print` (the print notice) and `Default.ScreenOnly` ("Related topic" paragraphs), a glossary, the Flare project files and the stylesheet.
- Seeded defects were inserted by `corpuskit inject` and are listed with their fixes in `labels.jsonl`.
