#!/usr/bin/env python3
"""Post-import enrichment of the policy Flare project (dev tool, not shipped).

Adds what a real policy set has and the source text does not carry:
  * a Policy variable set (owner, approver, effective and review dates) and a document-control snippet that uses it;
  * a print notice snippet under a Print condition, and ScreenOnly "Related topic" paragraphs on control topics;
  * the CSS rules the topics need (quotations, notes, definition lists).
Usage: enrich_pol.py PROJECT_DIR TOPICS_TSV
"""
import json
import os
import re
import sys

proj, tsv = sys.argv[1], sys.argv[2]
NL = chr(10)
folder_of = {}
src_of = {}
title_of = {}
for line in open(tsv, encoding="utf-8").read().split(NL):
    if not line.strip():
        continue
    folder, slug, title, src, words = line.split(chr(9))
    key = "Content/%s/%s.htm" % (folder, slug)
    folder_of[key] = folder
    src_of[key] = src
    title_of[key] = title

# variables
vdir = os.path.join(proj, "Project", "VariableSets")
os.makedirs(vdir, exist_ok=True)
vars_ = [("Owner", "Office of Compliance and Privacy"), ("ApprovedBy", "Governance Board"),
         ("EffectiveDate", "1 July 2026"), ("NextReview", "1 July 2027"), ("RetentionYears", "6")]
with open(os.path.join(vdir, "Policy.flvar"), "w", encoding="utf-8", newline="\n") as fh:
    fh.write('<?xml version="1.0" encoding="utf-8"?>' + NL + "<CatapultVariableSet>" + NL)
    for n, v in vars_:
        fh.write('  <Variable Name="%s" EvaluatedDefinition="%s">%s</Variable>%s' % (n, v, v, NL))
    fh.write("</CatapultVariableSet>" + NL)

SNIP = os.path.join(proj, "Content", "Resources", "Snippets")
os.makedirs(SNIP, exist_ok=True)
HEAD = ('<?xml version="1.0" encoding="utf-8"?>' + NL + '<html xmlns:MadCap="http://www.madcapsoftware.com/Schemas/MadCap.xsd">' + NL +
        "    <head />" + NL + "    <body>" + NL)
TAIL = "    </body>" + NL + "</html>" + NL
V = lambda n: '<MadCap:variable name="%s" />' % n
doc_control = (
    "        <p>This topic applies to " + V("General.CompanyName") + ".</p>" + NL +
    "        <table>" + NL +
    "            <tr><th>Document control</th><th>Detail</th></tr>" + NL +
    "            <tr><td>Policy owner</td><td>" + V("Policy.Owner") + "</td></tr>" + NL +
    "            <tr><td>Approved by</td><td>" + V("Policy.ApprovedBy") + "</td></tr>" + NL +
    "            <tr><td>Effective date</td><td>" + V("Policy.EffectiveDate") + "</td></tr>" + NL +
    "            <tr><td>Next review</td><td>" + V("Policy.NextReview") + "</td></tr>" + NL +
    "        </table>" + NL)
print_notice = "        <p>Printed copies of this policy can be out of date. Check the online version for the current text.</p>" + NL
for name, body in (("document-control.flsnp", doc_control), ("print-notice.flsnp", print_notice)):
    with open(os.path.join(SNIP, name), "w", encoding="utf-8", newline="\n") as fh:
        fh.write(HEAD + body + TAIL)

family_of_control = {}
for key, t in title_of.items():
    m = re.match(r"^([A-Z]{2})-\d+", t)
    if m:
        family_of_control[key] = m.group(1).lower()
family_topic = {}
for key, t in title_of.items():
    if t.endswith("controls: index"):
        pass
FAM = {"ac": "Access control controls: index", "au": "Audit and accountability controls: index", "ia": "Identification and authentication controls: index",
       "sc": "System and communications protection controls: index", "si": "System and information integrity controls: index"}
by_title = {t: k for k, t in title_of.items()}


def rel(frm, to):
    return os.path.relpath(to, os.path.dirname(frm)).replace(chr(92), "/")


stats = {"doc-control": 0, "print": 0, "related": 0}
for key in sorted(folder_of):
    path = os.path.join(proj, key.replace("/", os.sep))
    if not os.path.exists(path):
        continue
    text = open(path, encoding="utf-8").read()
    is_index = title_of[key].endswith("controls: index")
    ins = ""
    if not is_index and not title_of[key].startswith("About this") and not title_of[key].startswith("Source documents"):
        ins = '        <MadCap:snippetBlock src="%s" />%s' % (rel(key, "Content/Resources/Snippets/document-control.flsnp"), NL)
        stats["doc-control"] += 1
        text = re.sub(r"(</h1>" + NL + ")", lambda m: m.group(1) + ins, text, count=1)
    tail = ""
    fam = family_of_control.get(key)
    if fam in FAM and FAM[fam] in by_title:
        tgt = by_title[FAM[fam]]
        tail += ('        <p MadCap:conditions="Default.ScreenOnly">Related topic: <MadCap:xref href="%s">%s</MadCap:xref></p>%s' % (rel(key, tgt), title_of[tgt], NL))
        stats["related"] += 1
    tail += '        <MadCap:snippetBlock src="%s" MadCap:conditions="Default.Print" />%s' % (rel(key, "Content/Resources/Snippets/print-notice.flsnp"), NL)
    stats["print"] += 1
    text = text.replace("    </body>", tail + "    </body>", 1)
    with open(path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(text)

css = os.path.join(proj, "Content", "Resources", "Stylesheets", "Default.css")
c = open(css, encoding="utf-8").read()
if "blockquote" not in c:
    c += ("blockquote { margin: 8px 0 8px 1.5em; padding-left: 10px; border-left: 3px solid #bbb; color: #333; }" + NL +
          "dt { font-weight: bold; margin-top: 6px; }" + NL +
          "h5, h6 { font-size: 11pt; }" + NL)
    with open(css, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(c)
print(json.dumps(stats))
