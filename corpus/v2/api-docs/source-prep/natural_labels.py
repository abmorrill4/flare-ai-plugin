#!/usr/bin/env python3
"""Derive explicit natural clean/exempt labels from the project's own markup and promote generator labels.

Usage: natural_labels.py <project-dir> <project-id> <contexts.jsonl>

contexts.jsonl comes from `corpuskit contexts --project DIR --out FILE` (CorpusKit's own DOM walker; no linter involved).
Never reads linter output. Deterministic: same project, same labels.

What it writes (appended to labels.jsonl after the seeded labels):
  - clean   (rules "*")   every UI label the source marked up as one (span.uicontrol, or bold inside a step / after "select", "click" ...)
  - exempt  (lint layers) inline code and kbd spans; every code block
  - exempt  (google:*)    quoted text that reproduces a message or input verbatim          [status proposed: pattern match]
  - exempt  (units)       number plus unit written with a plain space in a table cell        [status proposed: pattern match]
Seeded labels become status "verified" by "generator" (the injector knows exactly what it changed).
"""
import json, re, sys, os, hashlib

root, pid, ctx_path = sys.argv[1], sys.argv[2], sys.argv[3]
labels_path = os.path.join(root, "labels.jsonl")
labels = [json.loads(l) for l in open(labels_path, encoding="utf-8") if l.strip()]
labels = [l for l in labels if l.get("origin") != "natural"]  # idempotent re-run
for l in labels:
    if l.get("origin") == "seeded":
        l["status"] = "verified"
        l["by"] = ["generator"]


def bucket(s):
    h = 14695981039346656037 ^ 0
    for c in s:
        h ^= ord(c)
        h = (h * 1099511628211) & 0xFFFFFFFFFFFFFFFF
    return h % 100


def split_of(f):
    return "dev" if bucket(f) < 70 else "test"


records = [json.loads(l) for l in open(ctx_path, encoding="utf-8") if l.strip()]
n = 0


def count(hay, needle):
    c, i = 0, 0
    while True:
        i = hay.find(needle, i)
        if i < 0:
            return c
        c += 1
        i += 1


def anchor(text, start, end):
    """prefix/suffix of up to 24 characters; widen (up to 24) until the probe is unique, else give up."""
    q = text[start:end]
    for k in (12, 24):
        pre, suf = text[max(0, start - k):start], text[end:end + k]
        if count(text, pre + q + suf) == 1:
            return pre, suf
    return None


out = []


def add(rec, label, quote, rules, reason, status, by, line, span, start=None, end=None):
    global n
    n += 1
    d = {"id": "%s/N%04d" % (pid, n), "file": rec["file"], "block": rec["block"]}
    if quote is not None:
        a = anchor(rec["text"], start, end)
        if a is None:
            n -= 1
            return
        d.update({"quote": quote, "prefix": a[0], "suffix": a[1]})
    d.update({"label": label, "rules": rules, "reason": reason, "context": {"line": line, "span": span},
              "origin": "natural", "status": status, "by": by, "split": split_of(rec["file"])})
    out.append(d)


UNIT = re.compile(r"(?<![\w.])(\d+(?:\.\d+)?) (KB|MB|GB|TB|kB|ms|s|px|%|MHz|GHz|kbps|Mbps)\b")
QUOTED = re.compile(r"“([^”]{4,80})”|\"([^\"]{4,80})\"")
markup = ["source-markup"]

for rec in records:
    text, line = rec["text"], rec["line"]
    if "code-block" in line:
        add(rec, "exempt", None, ["google:*", "company:*"], "Code block: literal code and sample output, not prose.", "verified", markup, line, [])
        continue
    for sp in rec.get("spans", []):
        s, e, kinds = sp["start"], sp["end"], sp["span"]
        quote = text[s:e]
        if not quote.strip() or len(quote.strip()) < 2:
            continue
        lead = len(quote) - len(quote.lstrip())
        trail = len(quote) - len(quote.rstrip())
        s2, e2 = s + lead, e - trail
        quote = text[s2:e2]
        if "ui-label" in kinds:
            add(rec, "clean", quote, ["*"], "UI label: must match the product text verbatim.", "verified", markup, line, kinds, s2, e2)
        elif "inline-code" in kinds:
            # a code span is labelled when it has a space in it (the shape a prose rule can match) or falls in a fixed one-in-six sample
            if " " not in quote and int(hashlib.md5((rec["file"] + rec["block"] + quote).encode()).hexdigest()[:4], 16) % 6 != 0:
                continue
            add(rec, "exempt", quote, ["google:*", "company:*"], "Inline code: a literal name, value or command, not prose.", "verified", markup, line, kinds, s2, e2)
        elif "kbd" in kinds:
            add(rec, "exempt", quote, ["google:*", "company:*"], "Key name: written as it appears on the keyboard.", "verified", markup, line, kinds, s2, e2)
    # patterns in running text (not code, not already a UI span)
    if any(k in line for k in ("table-cell",)):
        for m in UNIT.finditer(text):
            add(rec, "exempt", m.group(0), ["google:units-of-measure/*"], "Value and unit copied from a table of specifications; the table layout fixes the spacing.", "proposed", ["pattern"], line,
                [], m.start(), m.end())
    elif not any(k in line for k in ("heading:h1", "code-block")):
        for m in QUOTED.finditer(text):
            q = m.group(0)
            if re.search(r"[\w]", q) and len(q.split()) >= 2:
                add(rec, "exempt", q, ["google:*", "company:*"], "Quoted text reproduces a message, value or input verbatim.", "proposed", ["pattern"], line, [], m.start(), m.end())

with open(labels_path, "w", encoding="utf-8", newline="\n") as f:
    for l in labels + out:
        f.write(json.dumps(l, ensure_ascii=False, separators=(",", ":")) + "\n")
print("natural labels: %d (seeded promoted: %d)" % (len(out), sum(1 for l in labels if l.get("origin") == "seeded")))
