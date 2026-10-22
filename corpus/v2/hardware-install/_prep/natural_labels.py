#!/usr/bin/env python3
"""Adds natural labels to a corpus project's labels.jsonl and normalises the generator's rule hints (dev tool, not shipped).

Usage: natural_labels.py PROJECT_DIR PROJECT_ID PROFILE   (PROFILE: hardware | policy)
Needs PROJECT_DIR/contexts.jsonl (corpuskit contexts) and the seeded labels written by corpuskit inject.

Natural labels written (all origin "natural"):
  * verified, by dom-check (a fact of the markup, no judgment): images without alt text and tables without header cells
    (violations), code blocks, inline code, and quoted regulation blocks (exempt for lint);
  * proposed, by generator (a rule-based reading that still needs the two-labeller pass): device labels and numbers with
    units (clean), repeated boilerplate (exempt for lint).
"""
import json
import os
import re
import sys
import xml.etree.ElementTree as ET

proj, pid, profile = sys.argv[1], sys.argv[2], sys.argv[3]
MC = "http://www.madcapsoftware.com/Schemas/MadCap.xsd"

# the generator's rule hints, aligned to the ids the engine and corpus/DigitalBanking use
RULES = {
    "alt_missing": ["structure:AltText"],
    "click_here": ["structure:LinkText", "company:plain-language/*"],
    "heading_skip": ["structure:HeadingOrder"],
    "url": ["structure:HardCoded"],
    "phone": ["structure:HardCoded", "google:phone-numbers/*"],
    "exclaim": ["google:*"], "latin": ["google:*"], "we": ["google:*"], "will": ["google:*"],
    "passive": ["google:*", "ai:passive"],
    "marketing": ["company:claims/*", "ai:marketing"],
    "claims_free": ["company:claims/*"], "claims_guaranteed": ["company:claims/*"], "claims_soon": ["company:claims/*"],
    "wordy": ["company:plain-language/*", "ai:wordy"], "vague": ["ai:vague"],
    "ui_not_bold": ["company:ui-references/*", "ai:ui_not_bold"],
    "undefined_abbr": ["company:terminology/*", "ai:undefined_abbr"],
    "utilize": ["company:plain-language/*"], "in_order_to": ["company:plain-language/*"], "prior_to": ["company:plain-language/*"],
    "is_able_to": ["company:plain-language/*"], "please": ["company:plain-language/*"], "please_note": ["company:plain-language/*"],
    "simply": ["company:plain-language/*"], "the_user": ["company:plain-language/*"],
    "whitelist": ["company:plain-language/*"], "blacklist": ["company:plain-language/*"],
    "unit_space": ["google:units-of-measure/*"],
}


def bucket(s, seed=0):
    h = (14695981039346656037 ^ seed) & 0xFFFFFFFFFFFFFFFF
    for ch in s:
        for u in (ord(ch),) if ord(ch) < 0x10000 else ((0xD800 + ((ord(ch) - 0x10000) >> 10)), (0xDC00 + ((ord(ch) - 0x10000) & 0x3FF))):
            h ^= u
            h = (h * 1099511628211) & 0xFFFFFFFFFFFFFFFF
    return h % 100


def split_of(rel):
    return "dev" if bucket(rel) < 70 else "test"


def qname(tag):
    if tag.startswith("{"):
        ns, name = tag[1:].split("}")
        return ("MadCap:" + name) if ns == MC else name
    return tag


def load(path):
    return ET.parse(path).getroot()


def xpath(el, parents):
    parts = []
    cur = el
    while cur is not None:
        par = parents.get(cur)
        name = qname(cur.tag)
        if par is None:
            parts.append(name)
        else:
            if name in ("html", "head", "body"):
                parts.append(name)
            else:
                same = [c for c in par if qname(c.tag) == name]
                parts.append("%s[%d]" % (name, same.index(cur) + 1))
        cur = par
    return "/" + "/".join(reversed(parts))


def quote_ctx(text, start, ln):
    n = 12
    while True:
        ps = max(0, start - n)
        se = min(len(text), start + ln + n)
        prefix, suffix = text[ps:start], text[start + ln:se]
        probe = prefix + text[start:start + ln] + suffix
        if text.count(probe) == 1 or (ps == 0 and se == len(text)):
            return prefix, suffix
        n += 12


labels_path = os.path.join(proj, "labels.jsonl")
labels = [json.loads(l) for l in open(labels_path, encoding="utf-8").read().split("\n") if l.strip()]
for l in labels:
    if l.get("origin") == "seeded" and l.get("kind") in RULES:
        l["rules"] = RULES[l["kind"]]
    if l.get("origin") == "seeded" and l.get("kind") == "heading_skip":
        # the defect is the whole heading element (as in corpus/DigitalBanking); a quote would have to span a variable value
        for k in ("quote", "prefix", "suffix"):
            l.pop(k, None)

ctx_rows = [json.loads(l) for l in open(os.path.join(proj, "contexts.jsonl"), encoding="utf-8").read().split("\n") if l.strip()]
by_block = {}
for r in ctx_rows:
    by_block[(r["file"], r["block"])] = r
taken = {}
for l in labels:
    if "quote" in l:
        taken.setdefault((l["file"], l["block"]), []).append((l["prefix"] + l["quote"] + l["suffix"]))

next_id = max([int(re.search(r"(\d+)$", l["id"]).group(1)) for l in labels] + [0]) + 1
new = []
counts = {}


def add(file, block, label, by, status, kind=None, rules=None, quote=None, text=None, start=None, reason=None, fix=None):
    global next_id
    o = {"id": "%s/N%04d" % (pid, next_id), "file": file, "block": block}
    next_id += 1
    if quote is not None:
        prefix, suffix = quote_ctx(text, start, len(quote))
        o["quote"], o["prefix"], o["suffix"] = quote, prefix, suffix
    o["label"] = label
    if kind:
        o["kind"] = kind
    o["rules"] = rules
    if fix:
        o["fix"] = fix
    if reason:
        o["reason"] = reason
    o["origin"] = "natural"
    o["status"] = status
    o["by"] = by
    o["split"] = split_of(file)
    new.append(o)
    key = "%s/%s" % (label, by[0])
    counts[key] = counts.get(key, 0) + 1


def overlaps_seeded(file, block, text, start, end):
    for l in labels:
        if l["file"] == file and l["block"] == block and "quote" in l and l.get("label") == "violation":
            p = text.find(l["prefix"] + l["quote"] + l["suffix"])
            if p >= 0:
                s = p + len(l["prefix"])
                if s < end and s + len(l["quote"]) > start:
                    return True
    return False


# ---- DOM facts ------------------------------------------------------------------------------------------------------
files = []
for dirpath, _, names in os.walk(os.path.join(proj, "Content")):
    for n in names:
        if n.lower().endswith((".htm", ".flsnp")):
            files.append(os.path.relpath(os.path.join(dirpath, n), proj).replace("\\", "/"))
files.sort()
for rel in files:
    root = load(os.path.join(proj, rel))
    parents = {c: p for p in root.iter() for c in p}
    for el in root.iter():
        n = qname(el.tag)
        if n == "img" and not (el.get("alt") or "").strip():
            add(rel, xpath(el, parents), "violation", ["dom-check"], "verified", kind="alt_missing", rules=["structure:AltText"])
        elif n == "table" and not any(qname(c.tag) == "th" for c in el.iter()):
            add(rel, xpath(el, parents), "violation", ["dom-check"], "verified", kind="table_header", rules=["structure:TableHeader"])
        elif n == "pre":
            add(rel, xpath(el, parents), "exempt", ["dom-check"], "verified", rules=["lint"], reason="Code or command listing; style rules for prose do not apply")
        elif n == "blockquote" and profile == "policy":
            add(rel, xpath(el, parents), "exempt", ["dom-check"], "verified", rules=["lint"], reason="Quoted source text; kept verbatim")

# ---- spans from the contexts walker --------------------------------------------------------------------------------
UNIT = re.compile(r"\b\d+(?:\.\d+)?(?:[-–]\d+(?:\.\d+)?)? (?:mm|cm|kg|g|V|A|mA|mAh|W|Hz|kHz|MHz|GHz|GB|MB|Gb|Mb|°C|µm|ms|s|megapixels|pixels)\b")
LABEL = re.compile(r"^(?:[A-Z][A-Z0-9 +/_.-]{1,23}|[0-9]+-inch)$")
for r in ctx_rows:
    file, block, text = r["file"], r["block"], r["text"]
    key = (file, block)
    seeded_here = any(l["file"] == file and l["block"] == block and l.get("origin") == "seeded" for l in labels)
    if profile == "policy" and "quote" in r["line"] and "code-block" not in r["line"] and not seeded_here:
        add(file, block, "exempt", ["dom-check"], "verified", rules=["lint"], reason="Quoted control text, kept verbatim from the standard")
    for sp in r["spans"]:
        s, e = sp["start"], sp["end"]
        q = text[s:e]
        kinds = sp["span"]
        if "inline-code" in kinds and q.strip() and "code-block" not in r["line"]:
            add(file, block, "exempt", ["dom-check"], "verified", rules=["lint"], quote=q, text=text, start=s,
                reason="Code, command, file name or pin label; written exactly as the product shows it")
        elif profile == "hardware" and "emphasis" in kinds and "ui-label" not in kinds and LABEL.match(q) and q != "STATUS LED":
            add(file, block, "clean", ["generator"], "proposed", rules=["*"], quote=q, text=text, start=s,
                reason="Label printed on the device or a model variant; must match the hardware verbatim")
        elif "boilerplate" in kinds and not "xref" in kinds and q.strip() == "Related topic:":
            add(file, block, "exempt", ["generator"], "proposed", rules=["lint"], quote=q, text=text, start=s,
                reason="Boilerplate label repeated in every topic")
    if "table-cell" in r["line"] and profile == "hardware":
        for m in UNIT.finditer(text):
            if overlaps_seeded(file, block, text, m.start(), m.end()):
                continue
            add(file, block, "clean", ["generator"], "proposed", rules=["google:units-of-measure/*", "google:numbers/*"], quote=m.group(0), text=text, start=m.start(),
                reason="Value with its unit in a specification table: a space between number and unit, as the guide requires")
    if "table-cell" in r["line"] and profile == "policy":
        t2 = text.strip()
        if re.fullmatch(r"(?:\d+|[a-z]+)(?: calendar| business)? (?:days?|years?|months?|hours?)(?:; .+)?|(?:January|February|March|April|May|June|July|August|September|October|November|December) \d{1,2}, \d{4}", t2) and not overlaps_seeded(file, block, text, 0, len(text)):
            add(file, block, "clean", ["generator"], "proposed", rules=["google:numbers/*", "google:units-of-measure/*", "google:dates-and-times/*"], quote=t2, text=text, start=text.index(t2),
                reason="Period or date in the time-limits table: written as the source regulation states it")
    if file.endswith("print-notice.flsnp") and r["block"] == "/html/body/p[1]":
        add(file, block, "exempt", ["generator"], "proposed", rules=["lint"], reason="Boilerplate sentence used in every topic through a snippet")

out = labels + sorted(new, key=lambda o: (o["file"], o["block"], o.get("prefix", ""), o["id"]))
with open(labels_path, "w", encoding="utf-8", newline="\n") as fh:
    for o in out:
        fh.write(json.dumps(o, ensure_ascii=False, separators=(",", ":")) + "\n")
print(json.dumps(counts, sort_keys=True))
