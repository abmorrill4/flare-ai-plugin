#!/usr/bin/env python3
"""Writes project.json and LABELS.md for a corpus project from its labels.jsonl (dev tool, not shipped).
Usage: make_docs.py PROJECT_DIR META_JSON"""
import collections
import json
import os
import sys

proj, meta_path = sys.argv[1], sys.argv[2]
meta = json.load(open(meta_path, encoding="utf-8"))
pid = meta["name"]
if meta.get("topicsTsv"):
    srcs = collections.OrderedDict()
    for line in open(meta["topicsTsv"], encoding="utf-8").read().split("\n"):
        if line.strip():
            f = line.split("\t")
            srcs[f[3]] = srcs.get(f[3], 0) + 1
    for s in meta["sources"]:
        pre = s.get("sourcePrefix")
        if pre is not None:
            s["files"] = [{"path": k, "topics": v} for k, v in srcs.items() if k.startswith(pre)]
            del s["sourcePrefix"]


def bucket(s, seed=0):
    h = (14695981039346656037 ^ seed) & 0xFFFFFFFFFFFFFFFF
    for ch in s:
        h ^= ord(ch)
        h = (h * 1099511628211) & 0xFFFFFFFFFFFFFFFF
    return h % 100


files = []
for dirpath, _, names in os.walk(os.path.join(proj, "Content")):
    for n in names:
        if n.lower().endswith((".htm", ".flsnp")):
            files.append(os.path.relpath(os.path.join(dirpath, n), proj).replace("\\", "/"))
files.sort()
test_files = [f for f in files if bucket(f) >= 70]
topics = [f for f in files if f.endswith(".htm")]
snippets = [f for f in files if f.endswith(".flsnp")]
labels = [json.loads(l) for l in open(os.path.join(proj, "labels.jsonl"), encoding="utf-8").read().split("\n") if l.strip()]

# ---- project.json ----------------------------------------------------------------------------------------------------
pj = collections.OrderedDict()
pj["name"] = pid
pj["title"] = meta["title"]
pj["domain"] = meta["domain"]
pj["flareProject"] = pid + ".flprj"
pj["targets"] = meta["targets"]
pj["topics"] = len(topics)
pj["snippets"] = len(snippets)
pj["seed"] = meta["seed"]
pj["seedNotes"] = meta["seedNotes"]
pj["licence"] = meta["licence"]
pj["sources"] = meta["sources"]
pj["authors"] = meta["authors"]
pj["split"] = collections.OrderedDict([
    ("rule", "FNV-1a 64-bit hash of the project-relative path with forward slashes, modulo 100; below 70 is dev, 70 and above is test (CorpusKit's rule)"),
    ("files", len(files)),
    ("testFiles", len(test_files)),
    ("testShare", round(len(test_files) / len(files), 3)),
    ("test", test_files),
])
with open(os.path.join(proj, "project.json"), "w", encoding="utf-8", newline="\n") as fh:
    fh.write(json.dumps(pj, indent=2, ensure_ascii=False) + "\n")

# ---- LABELS.md -------------------------------------------------------------------------------------------------------
C = collections.Counter
by_verdict = C((l["label"], l["origin"], l["status"], ",".join(l["by"])) for l in labels)
kinds = C(l.get("kind") for l in labels if l["label"] == "violation" and l["origin"] == "seeded")
nat_kinds = C(l.get("kind") for l in labels if l["label"] == "violation" and l["origin"] == "natural")
lines_ctx = C(c for l in labels for c in l.get("context", {}).get("line", []))
span_ctx = C(c for l in labels for c in l.get("context", {}).get("span", []))
variants = C(l.get("seed", {}).get("variant") for l in labels if l["origin"] == "seeded")
split = C(l["split"] for l in labels)
blocks_total = meta.get("blocks")

out = []
w = out.append
w("# Labels: %s\n" % pid)
w("Labels for this project are in `labels.jsonl` (schema: `corpus/schema/labels.schema.json`, explained in `docs/testing/CORPUS.md`). "
  "`project.json` lists the sources, the seed and the test split.\n")
w("## What is in the file\n")
w("| Label | Origin | Status | By | Count |\n|---|---|---|---|---|")
for (lab, org, st, by), n in sorted(by_verdict.items()):
    w("| %s | %s | %s | %s | %d |" % (lab, org, st, by, n))
w("\n%d labels over %d topics and %d snippets (%d blocks in all)." % (len(labels), len(topics), len(snippets), blocks_total or 0))
w("Split of labels: %d dev, %d test. Split of files: %d of %d files (%.0f%%) are in `test`.\n" % (split["dev"], split["test"], len(test_files), len(files), 100.0 * len(test_files) / len(files)))
w("## How each group was made\n")
for para in meta["howMade"]:
    w(para + "\n")
w("### Seeded violations by kind\n")
w("| Kind | Count |\n|---|---|")
for k, n in sorted(kinds.items(), key=lambda kv: (-kv[1], kv[0])):
    w("| %s | %d |" % (k, n))
w("\nSurface variants (case and position): " + ", ".join("%s=%d" % (k, n) for k, n in sorted(variants.items())) + ".\n")
w("### Natural violations by kind\n")
for k, n in sorted(nat_kinds.items()):
    w("- %s: %d" % (k, n))
w("\n### Contexts covered by the labels\n")
w("Line contexts: " + ", ".join("%s=%d" % (k, n) for k, n in sorted(lines_ctx.items())) + ".\n")
w("Span contexts: " + ", ".join("%s=%d" % (k, n) for k, n in sorted(span_ctx.items())) + ".\n")
w("## Density\n")
for para in meta["density"]:
    w(para + "\n")
w("## Not done yet\n")
for item in meta["pending"]:
    w("- " + item)
w("\n## Rebuilding\n")
w(meta["rebuild"])
with open(os.path.join(proj, "LABELS.md"), "w", encoding="utf-8", newline="\n") as fh:
    fh.write("\n".join(out) + "\n")
print(json.dumps({"labels": len(labels), "files": len(files), "test": len(test_files), "share": round(len(test_files) / len(files), 3)}))
