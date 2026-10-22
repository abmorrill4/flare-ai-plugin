#!/usr/bin/env python3
"""Write project.json for a corpus project from project.meta.json plus counts taken from the project itself.

Usage: project_json.py <project-dir>
The dev/test split is by file: FNV-1a (64 bit, over the UTF-16 code units of the forward-slash path) modulo 100, 70 and above is `test`.
CorpusKit's injector and natural_labels.py use the same rule; the test files are listed here so a consumer does not need the hash.
"""
import collections, json, os, sys
from lxml import etree

root = sys.argv[1]
MC = "http://www.madcapsoftware.com/Schemas/MadCap.xsd"
meta = json.load(open(os.path.join(root, "source-prep", "project.meta.json"), encoding="utf-8"))


def bucket(s):
    h = 14695981039346656037
    for ch in s:
        h ^= ord(ch)
        h = (h * 1099511628211) & 0xFFFFFFFFFFFFFFFF
    return h % 100


files = []
for dp, dn, fn in os.walk(os.path.join(root, "Content")):
    for f in fn:
        if f.endswith((".htm", ".flsnp")):
            files.append(os.path.relpath(os.path.join(dp, f), root).replace("\\", "/"))
files.sort()
topics = [f for f in files if f.endswith(".htm")]
snips = [f for f in files if f.endswith(".flsnp")]
test = [f for f in files if bucket(f) >= 70]

c = collections.Counter()
text_snippets = set()
for f in files:
    t = etree.parse(os.path.join(root, f))
    for e in t.iter():
        if not isinstance(e.tag, str):
            continue
        q = etree.QName(e)
        if q.namespace == MC:
            c["MadCap:" + q.localname] += 1
        elif q.localname in ("table", "pre", "kbd", "img"):
            c[q.localname] += 1
        if q.localname == "img" and not e.get("alt"):
            c["img without alt"] += 1
        if e.get("{%s}conditions" % MC):
            c["conditional elements"] += 1

labels = [json.loads(l) for l in open(os.path.join(root, "labels.jsonl"), encoding="utf-8") if l.strip()]
by = collections.Counter("%s/%s/%s" % (l["origin"], l["label"], l["status"]) for l in labels)
kinds = collections.Counter(l["kind"] for l in labels if l["label"] == "violation")

project = collections.OrderedDict()
project["id"] = meta["id"]
project["title"] = meta["title"]
project["description"] = meta["description"]
project["fictionalNames"] = meta["fictionalNames"]
project["licence"] = meta["licence"]
project["sources"] = meta["sources"]
project["build"] = meta["build"]
project["split"] = collections.OrderedDict([
    ("rule", "FNV-1a 64-bit over the file path, modulo 100; 70 and above is test (about 30% of files)"),
    ("files", len(files)), ("testFiles", len(test)), ("testFraction", round(len(test) / float(len(files)), 3)),
    ("topics", len(topics)), ("testTopics", sum(1 for f in topics if f in set(test))),
    ("test", test)])
project["counts"] = collections.OrderedDict([
    ("topics", len(topics)), ("snippetFiles", len(snips)), ("snippetBlocks", c["MadCap:snippetBlock"]), ("snippetTexts", c["MadCap:snippetText"]),
    ("variables", c["MadCap:variable"]), ("xrefs", c["MadCap:xref"]), ("dropDowns", c["MadCap:dropDown"]), ("togglers", c["MadCap:toggler"]),
    ("glossaryTerms", c["MadCap:glossaryTerm"]), ("conditionalElements", c["conditional elements"]), ("tables", c["table"]), ("codeBlocks", c["pre"]),
    ("kbd", c["kbd"]), ("images", c["img"]), ("imagesWithoutAlt", c["img without alt"])])
project["labels"] = collections.OrderedDict([("total", len(labels)), ("byOriginLabelStatus", dict(sorted(by.items()))), ("violationKinds", dict(sorted(kinds.items())))])
with open(os.path.join(root, "project.json"), "w", encoding="utf-8", newline="\n") as f:
    json.dump(project, f, indent=1, ensure_ascii=False)
    f.write("\n")
print("project.json:", len(topics), "topics,", len(test), "test files of", len(files))
