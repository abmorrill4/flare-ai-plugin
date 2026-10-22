#!/usr/bin/env python3
"""Remove natural labels that FlareAI.Scoring validate cannot anchor (text that depends on a variable's value, which the CLI does not expand).

Usage: drop_unanchorable.py <labels.jsonl> <validate-output.txt>
Only `origin: natural` labels are dropped; a seeded label in the list is reported and the exit code is 1.
"""
import json, re, sys

labels_path, out_path = sys.argv[1], sys.argv[2]
raw = open(out_path, "rb").read()
text = raw.decode("utf-16") if b"\x00" in raw else raw.decode("utf-8-sig")  # Windows PowerShell writes UTF-16 through Tee-Object
bad = set(re.findall(r"^\s*([A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+):", text, flags=re.M))
rows = [json.loads(l) for l in open(labels_path, encoding="utf-8") if l.strip()]
keep, dropped, seeded = [], [], []
for r in rows:
    if r["id"] in bad:
        (dropped if r.get("origin") == "natural" else seeded).append(r["id"])
        if r.get("origin") == "natural":
            continue
    keep.append(r)
with open(labels_path, "w", encoding="utf-8", newline="\n") as f:
    for r in keep:
        f.write(json.dumps(r, ensure_ascii=False, separators=(",", ":")) + "\n")
print("dropped %d natural labels that depend on variable text; seeded failing: %s" % (len(dropped), seeded or "none"))
sys.exit(1 if seeded else 0)
