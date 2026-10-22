#!/usr/bin/env python3
"""Post-import enrichment of the hardware-install Flare project (dev tool, not shipped).

Adds what a real hardware guide has and the converter cannot produce from the source text:
  * figure caption class on "Figure N. ..." paragraphs;
  * shared snippets (power-off warning, static caution, print notice) used across many topics;
  * condition tags: Print on the print notice, ScreenOnly on "Related topic" paragraphs;
  * the CSS rules those need.
Usage: enrich_hw.py PROJECT_DIR TOPICS_TSV
"""
import json
import os
import re
import sys

proj = sys.argv[1]
tsv = sys.argv[2]

# topic html path -> adoc source file and category
src_of = {}
cat_of = {}
title_of = {}
for line in open(tsv, encoding="utf-8").read().split("\n"):
    if not line.strip():
        continue
    folder, slug, title, src, words = line.split("\t")
    key = "Content/%s/%s.htm" % (folder, slug)
    src_of[key] = src
    cat_of[key] = folder
    title_of[key] = title

SNIP_DIR = os.path.join(proj, "Content", "Resources", "Snippets")
os.makedirs(SNIP_DIR, exist_ok=True)

HEAD = ('<?xml version="1.0" encoding="utf-8"?>\n<html xmlns:MadCap="http://www.madcapsoftware.com/Schemas/MadCap.xsd">\n'
        '    <head />\n    <body>\n')
TAIL = '    </body>\n</html>\n'
SNIPPETS = {
    "disconnect-power.flsnp": '        <p class="warning">Disconnect the <MadCap:variable name="General.ProductName" /> board from power before you start. Don\'t connect or disconnect cables while the board is powered.</p>\n',
    "static-discharge.flsnp": '        <p class="caution">Static discharge can damage electronic components. Touch a grounded metal object before you handle a board, and hold boards by their edges.</p>\n',
    "print-notice.flsnp": '        <p>Printed copies of this guide can be out of date. Check the online version for the latest information.</p>\n',
}
for name, body in SNIPPETS.items():
    with open(os.path.join(SNIP_DIR, name), "w", encoding="utf-8", newline="\n") as fh:
        fh.write(HEAD + body + TAIL)

stats = {"captions": 0, "power": 0, "static": 0, "print": 0, "related": 0}
spec_by_src = {}
for k, c in cat_of.items():
    if c == "specifications":
        spec_by_src.setdefault(src_of[k], []).append(k)


def rel(from_key, to_key):
    return os.path.relpath(to_key, os.path.dirname(from_key)).replace("\\", "/")


for key in sorted(cat_of):
    path = os.path.join(proj, key.replace("/", os.sep))
    if not os.path.exists(path):
        continue
    text = open(path, encoding="utf-8").read()
    cat = cat_of[key]
    # captions
    text, n = re.subn(r'<p>(Figure \d+\. [^\n]*?)</p>', r'<p class="caption">\1</p>', text)
    stats["captions"] += n
    ins = ""
    if cat in ("mounting", "cabling", "maintenance"):
        ins += '        <MadCap:snippetBlock src="%s" />\n' % rel(key, "Content/Resources/Snippets/disconnect-power.flsnp")
        stats["power"] += 1
    if cat in ("mounting", "cabling"):
        ins += '        <MadCap:snippetBlock src="%s" />\n' % rel(key, "Content/Resources/Snippets/static-discharge.flsnp")
        stats["static"] += 1
    if ins:
        text = re.sub(r'(</h1>\n)', lambda m: m.group(1) + ins, text, count=1)
    tail = ""
    if cat != "specifications":
        specs = [s for s in spec_by_src.get(src_of[key], []) if s != key]
        if specs:
            s = specs[0]
            tail += ('        <p MadCap:conditions="Default.ScreenOnly">Related topic: <MadCap:xref href="%s">%s</MadCap:xref></p>\n'
                     % (rel(key, s), title_of[s]))
            stats["related"] += 1
    tail += '        <MadCap:snippetBlock src="%s" MadCap:conditions="Default.Print" />\n' % rel(key, "Content/Resources/Snippets/print-notice.flsnp")
    stats["print"] += 1
    text = text.replace("    </body>", tail + "    </body>", 1)
    with open(path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(text)

css = os.path.join(proj, "Content", "Resources", "Stylesheets", "Default.css")
c = open(css, encoding="utf-8").read()
if "p.caption" not in c:
    c += "p.caption { font-size: 90%; font-style: italic; margin-top: 2px; color: #444; }\n"
    c += "p.warning:before, p.caution:before { text-transform: uppercase; }\n"
    with open(css, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(c)
print(json.dumps(stats))
