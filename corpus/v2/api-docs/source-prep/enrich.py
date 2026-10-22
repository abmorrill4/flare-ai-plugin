#!/usr/bin/env python3
"""api-docs: attribute-level enrichment after `corpuskit import` and before `scaffold` (conditions, target expressions).

Usage: enrich.py <project-dir> content    (after import, before scaffold)
       enrich.py <project-dir> targets    (after scaffold: HTML5 target excludes OnPrem, PDF target excludes Cloud)
Only attributes change, so XPaths of labelled blocks never move. Deterministic.
"""
import os, re, sys
from lxml import etree
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import enrich_common

MC = "http://www.madcapsoftware.com/Schemas/MadCap.xsd"
CONDITIONS = "{%s}conditions" % MC
root = sys.argv[1]
mode = sys.argv[2] if len(sys.argv) > 2 else "content"


def save(tree, path):
    data = etree.tostring(tree, encoding="utf-8", xml_declaration=False)
    with open(path, "wb") as f:
        f.write(b'<?xml version="1.0" encoding="utf-8"?>\n' + data + b"\n")


def text_of(e):
    return "".join(e.itertext())


if mode == "targets":
    for name, tag in (("HTML5", "Default.OnPrem"), ("PDF", "Default.Cloud")):
        p = os.path.join(root, "Project", "Targets", name + ".fltar")
        t = etree.parse(p)
        t.getroot().set("ConditionTagExpression", "exclude[%s]" % tag)
        save(t, p)
    print("targets patched")
    sys.exit(0)

counts = {"cloud": 0, "onprem": 0, "natural": 0, "files": 0}
for dp, dn, fn in os.walk(os.path.join(root, "Content")):
    for f in sorted(fn):
        if not f.endswith((".htm", ".flsnp")):
            continue
        path = os.path.join(dp, f)
        tree = etree.parse(path, etree.XMLParser(remove_blank_text=False))
        changed = False
        for e in tree.iter("p", "li"):
            t = text_of(e).lstrip()
            m = re.match(r"\[\[(cloud|onprem)\]\]\s*", t)
            if m:
                # drop the marker from the first text node
                node = e
                if e.text and e.text.lstrip().startswith("[["):
                    e.text = re.sub(r"^\s*\[\[(cloud|onprem)\]\]\s*", "", e.text)
                e.set(CONDITIONS, "Default.Cloud" if m.group(1) == "cloud" else "Default.OnPrem")
                counts[m.group(1)] += 1
                changed = True
                continue
            if CONDITIONS in e.attrib:
                continue
            cloud = re.search(r"\bCloud\b", t) and not re.search(r"self-host", t, re.I)
            onprem = re.search(r"self-host", t, re.I) and not re.search(r"\bCloud\b", t)
            # natural conditional text: only paragraphs that are about exactly one deployment model
            if (cloud or onprem) and len(t) < 600 and e.getparent() is not None and e.getparent().tag in ("body", "li"):
                e.set(CONDITIONS, "Default.Cloud" if cloud else "Default.OnPrem")
                counts["natural"] += 1
                changed = True
        if changed:
            save(tree, path)
            counts["files"] += 1
print(counts)
print('snippet references rebased:', enrich_common.rebase_snippets(root))
