#!/usr/bin/env python3
"""Builds HTML topics for the policy corpus project from public-domain sources (dev tool, not shipped):
  * 45 CFR Parts 160 and 164 from the eCFR versioner API XML (US federal regulation, public domain);
  * NIST SP 800-53 Rev. 5 controls and SP 800-53A assessment procedures from the OSCAL catalog JSON (public domain / CC0).
Usage: policy_build.py OUT_DIR
Writes OUT_DIR/src/<folder>/<slug>.html and OUT_DIR/topics.tsv (folder, slug, title, source, words).
"""
import html
import json
import os
import re
import shutil
import sys
import xml.etree.ElementTree as ET

OUT = sys.argv[1]
SRC = '%TEMP%/corpus-src/pol-src'
OSCAL = '%TEMP%/corpus-src/pol-oscal/nist.gov/SP800-53/rev5/json/NIST_SP-800-53_rev5_catalog.json'
NL = chr(10)
TEMPLATE = ('<!DOCTYPE html>' + NL + '<html><head><meta charset="utf-8"/><title>%s</title></head><body>' + NL +
            '%s' + NL + '</body></html>' + NL)

# ------------------------------------------------------------------------------------------------ topic registry
topics = []   # dict(key, folder, title, body_fn)
by_key = {}


def slugify(t):
    return re.sub(r"-+", "-", re.sub(r"[^a-z0-9]+", "-", t.lower())).strip("-")[:70]


class Topic:
    def __init__(self, key, folder, title, source):
        self.key, self.folder, self.title, self.source = key, folder, title, source
        self.slug = None
        self.html = None
        self.words = 0


def register(key, folder, title, source):
    t = Topic(key, folder, title, source)
    topics.append(t)
    by_key[key] = t
    return t


def assign_slugs():
    used = set()
    for t in topics:
        s = slugify(t.title) or "topic"
        base, k = s, 2
        while (t.folder, s) in used:
            s = "%s-%d" % (base, k)
            k += 1
        used.add((t.folder, s))
        t.slug = s


def href(frm, to):
    return os.path.relpath("%s/%s.html" % (to.folder, to.slug), os.path.dirname("%s/%s.html" % (frm.folder, frm.slug))).replace("\\", "/")


# ------------------------------------------------------------------------------------------------ eCFR
SECTIONS = {}   # "164.308" -> dict(head, node, part)
for part in ("part160.xml", "part164.xml"):
    root = ET.parse(os.path.join(SRC, part)).getroot()
    for d8 in root.iter("DIV8"):
        n = d8.get("N")
        SECTIONS[n] = d8
    for d9 in root.iter("DIV9"):
        SECTIONS[d9.get("N")] = d9

# keys of the selected regulation sections and where they go
CFR_PICK = [
    # (section, folder)
    ("164.103", "definitions"), ("164.105", "overview"), ("164.302", "security"), ("164.304", "definitions"), ("164.306", "security"),
    ("164.308", "security"), ("164.310", "acceptable-use"), ("164.312", "security"), ("164.314", "security"), ("164.316", "retention"),
    ("164.318", "overview"), ("Appendix A to Subpart C of Part 164", "security"),
    ("164.400", "breach"), ("164.402", "definitions"), ("164.404", "breach"), ("164.406", "breach"), ("164.408", "breach"),
    ("164.410", "breach"), ("164.412", "breach"), ("164.414", "breach"),
    ("164.500", "privacy"), ("164.501", "definitions"), ("164.502", "privacy"), ("164.504", "privacy"), ("164.506", "privacy"),
    ("164.508", "privacy"), ("164.510", "privacy"), ("164.512", "privacy"), ("164.514", "privacy"), ("164.520", "privacy"),
    ("164.522", "privacy"), ("164.524", "privacy"), ("164.526", "privacy"), ("164.528", "privacy"), ("164.530", "retention"),
    ("160.103", "definitions"), ("160.306", "audit"), ("160.308", "audit"), ("160.310", "audit"), ("160.312", "audit"),
    ("160.316", "audit"), ("160.404", "audit"),
]
TOPIC_OF_SECTION = {}


def sect_title(el):
    h = el.find("HEAD")
    t = "".join(h.itertext()).strip()
    t = re.sub(r"\s+", " ", t)
    return t


for sec, folder in CFR_PICK:
    el = SECTIONS[sec]
    title = sect_title(el)
    title = re.sub(r"\.$", "", title)
    if el.tag == "DIV9":
        title = "Appendix A to Subpart C: Security standards matrix"
    tp = register("cfr:" + sec, folder, title, "45 CFR " + (sec if el.tag != "DIV9" else "Part 164, Subpart C, Appendix A"))
    TOPIC_OF_SECTION[sec] = tp


def esc(s):
    return html.escape(s, quote=False)


def inline(el, ctx):
    """Inline HTML of an element's mixed content."""
    out = [esc(el.text or "")]
    for ch in el:
        tag = ch.tag
        inner = inline(ch, ctx)
        if tag in ("I", "E", "FR"):
            out.append("<i>%s</i>" % inner)
        elif tag == "B":
            out.append("<b>%s</b>" % inner)
        elif tag == "SU":
            out.append("<sup>%s</sup>" % inner)
        elif tag == "SUB":
            out.append("<sub>%s</sub>" % inner)
        else:
            out.append(inner)
        out.append(esc(ch.tail or ""))
    return "".join(out)


XREF = re.compile(r"(?<![\d.])(16[04]\.\d{3})(?![\d])")


def link_citations(h, cur):
    """Turns references such as 164.308(a)(1) into links to the topic for that section, outside tags."""
    parts = re.split(r"(<[^>]+>)", h)
    out = []
    in_a = 0
    for p in parts:
        if p.startswith("<"):
            if p.startswith("<a "):
                in_a += 1
            elif p == "</a>":
                in_a -= 1
            out.append(p)
        elif in_a:
            out.append(p)
        else:
            def sub(m):
                tgt = TOPIC_OF_SECTION.get(m.group(1))
                if tgt is None or tgt is cur:
                    return m.group(0)
                return '<a href="%s">%s</a>' % (href(cur, tgt), m.group(1))
            out.append(XREF.sub(sub, p))
    return "".join(out)


ROMAN = re.compile(r"^(?=[ivxlc]+$)(?:x{0,3})(?:ix|iv|v?i{0,3})$|^(?:x{1,3})(?:ix|iv|v?i{0,3})$")
MARK = re.compile(r"^\(([A-Za-z0-9]+)\)")


def marker_level(tok, italic, prev_alpha):
    """Level of a paragraph marker: 1 (a), 2 (1), 3 (i), 4 (A), 5 (1) italic, 6 (i) italic."""
    if tok.isdigit():
        return 5 if italic else 2
    if tok.isupper():
        return 4
    if re.fullmatch(r"[ivx]+", tok) and tok not in ("i", "v", "x"):
        return 6 if italic else 3
    if tok in ("i", "v", "x"):
        # alphabetic when it follows h, u, w; otherwise roman
        prev = {"i": "h", "v": "u", "x": "w"}[tok]
        if prev_alpha == prev:
            return 1
        return 6 if italic else 3
    return 1


def split_marker_chain(p_el):
    """Returns (chain, rest_html) for a paragraph that starts with (a)(1)..."""
    h = inline(p_el, None)
    chain = []
    rest = h
    while True:
        m = re.match(r"^\s*(<i>)?\(([A-Za-z0-9]+)\)(</i>)?", rest)
        if not m:
            break
        italic = bool(m.group(1))
        chain.append((m.group(2), italic))
        rest = rest[m.end():]
    return chain, rest.strip()


DEF_START = re.compile(r"^<i>([^<]{1,90})</i>(?=\s+(?:means|is|are|has|have|includes|refers|shall|may|must|\(|—|:)|:|\s*$)")


def cfr_body(topic, sec):
    el = SECTIONS[sec]
    cur = topic
    body = []
    is_def = "Definitions" in sect_title(el) if el.tag == "DIV8" else False
    heading_level = 1
    body.append("<h1>%s</h1>" % esc(topic.title))
    stack = {}                     # level -> token, for citations of time limits
    prev_alpha = None
    in_dl = False
    dd_open = False
    TL = topic.timelimits = []
    sentences_seen = set()

    def close_dl():
        nonlocal in_dl, dd_open
        if dd_open:
            body.append("</dd>")
            dd_open = False
        if in_dl:
            body.append("</dl>")
            in_dl = False

    def note_time(chain_str, text):
        plain = re.sub(r"<[^>]+>", "", text)
        for s in re.split(r"(?<=[.;:])\s+(?=[A-Z(])", plain):
            m = re.finditer(r"\b(?:\d+|one|two|three|four|five|six|seven|eight|nine|ten|twenty|thirty|forty|sixty|ninety)(?:[- ]\w+)?(?:\s+(?:calendar|business|consecutive))?\s+(?:day|days|year|years|month|months|hour|hours|week|weeks)\b|\bwithout unreasonable delay\b|\bas soon as (?:reasonably )?practicable\b|\bpromptly\b|\bannually\b|\bperiodically\b|\bat least once\b|\bon or before [A-Z][a-z]+ \d+, \d{4}\b|\b(?:January|February|March|April|May|June|July|August|September|October|November|December) \d{1,2}, \d{4}\b", s)
            found = []
            for mm in m:
                if mm.group(0) not in found:
                    found.append(mm.group(0))
            if found and len(s) < 600 and "fiscal" not in s[:20]:
                key = (chain_str, s[:60])
                if key in sentences_seen:
                    continue
                sentences_seen.add(key)
                TL.append((chain_str, "; ".join(found), s.strip()))

    children = list(el)
    for ch in children:
        tag = ch.tag
        if tag == "HEAD":
            continue
        if tag == "P" or tag == "FP":
            chain, rest = split_marker_chain(ch)
            # em-dash continuation that opens a nested marker: "(2) <i>Implementation specifications</i>—(i) <i>Name.</i> text"
            pieces = [(chain, rest)]
            m = re.search(r"</i>\s*(?:—|&#x2014;)\s*(?=\(?[A-Za-z0-9]+\)\s)", rest) if chain else None
            if chain and rest.startswith("<i>"):
                dash = re.match(r"^(<i>[^<]*</i>)\s*—\s*(\([A-Za-z0-9]+\).*)$", rest, flags=re.S)
                if dash:
                    pieces = [(chain, dash.group(1))]
                    c2 = ET.fromstring("<P>%s</P>" % dash.group(2).replace("&", "&amp;").replace("<i>", "<I>").replace("</i>", "</I>"))
                    chain2, rest2 = split_marker_chain(c2)
                    pieces.append((chain2, rest2))
            for chain_p, rest_p in pieces:
                if is_def and not chain_p:
                    dm = DEF_START.match(rest_p)
                    if dm:
                        if not in_dl:
                            body.append("<dl>")
                            in_dl = True
                        if dd_open:
                            body.append("</dd>")
                            dd_open = False
                        term = dm.group(1).strip().rstrip(".:")
                        text = rest_p[dm.end():].strip()
                        text = re.sub(r"^[:—\-]\s*", "", text)
                        body.append("<dt>%s</dt><dd>" % term)
                        dd_open = True
                        if text:
                            body.append("<p>%s</p>" % link_citations(text, cur))
                        continue
                if is_def and chain_p and dd_open:
                    body.append("<p>%s</p>" % link_citations(("(%s) " % ")(".join(t for t, _ in chain_p)) + rest_p, cur))
                    continue
                close_dl()
                if chain_p:
                    first_lv = None
                    for tok, it in chain_p:
                        lv = marker_level(tok, it, prev_alpha)
                        if first_lv is None:
                            first_lv = lv
                        stack[lv] = tok
                        for k in [k for k in stack if k > lv]:
                            del stack[k]
                        if lv == 1:
                            prev_alpha = tok
                    mk = "".join("(%s)" % t for t, _ in chain_p)
                    cite = "".join("(%s)" % stack[k] for k in sorted(stack))
                    last_lv = first_lv
                    ital = re.match(r"^<i>([^<]+)</i>(.*)$", rest_p, flags=re.S)
                    if ital and len(ital.group(1).split()) <= 16:
                        desired = {1: 2, 2: 3, 3: 4, 4: 5, 5: 6, 6: 6}[last_lv]
                        lvl = min(desired, heading_level + 1)
                        heading_level = lvl
                        body.append("<h%d>%s %s</h%d>" % (lvl, mk, ital.group(1).strip(), lvl))
                        tail = ital.group(2).strip()
                        if tail:
                            body.append("<p>%s</p>" % link_citations(tail, cur))
                            note_time("%s%s" % (sec, cite), tail)
                    else:
                        txt = "%s %s" % (mk, rest_p)
                        body.append("<p>%s</p>" % link_citations(txt, cur))
                        note_time("%s%s" % (sec, cite), rest_p)
                else:
                    body.append("<p>%s</p>" % link_citations(rest_p, cur))
                    note_time(sec, rest_p)
        elif tag == "CITA":
            close_dl()
            body.append("<p>%s</p>" % esc(re.sub(r"\s+", " ", "".join(ch.itertext()).strip())))
        elif tag == "EXTRACT":
            close_dl()
            body.append("<blockquote>%s</blockquote>" % "".join("<p>%s</p>" % link_citations(inline(p, None), cur) for p in ch.iter("P")))
        elif tag == "TABLE":
            close_dl()
            body.append(cfr_table(ch))
        elif tag == "DIV":
            close_dl()
            for sub in ch.iter():
                if sub.tag == "TABLE":
                    body.append(cfr_table(sub))
                elif sub.tag in ("P", "FP"):
                    body.append("<p>%s</p>" % link_citations(inline(sub, None), cur))
        else:
            continue
    close_dl()
    return NL.join(body)


def cfr_table(t):
    rows = []
    head_cells = None
    out = ["<table>"]
    th = t.find("THEAD")
    if th is not None:
        out.append("<tr>%s</tr>" % "".join("<th>%s</th>" % esc(re.sub(r"\s+", " ", "".join(c.itertext()).strip())) for c in th.iter() if c.tag in ("TH", "TD")))
    for tr in t.iter("TR"):
        if th is not None and tr in list(th):
            continue
        cells = []
        for c in tr:
            if c.tag in ("TD", "TH"):
                txt = esc(re.sub(r"\s+", " ", "".join(c.itertext()).strip()))
                a = ""
                for nm in ("colspan", "rowspan"):
                    v = c.get(nm) or c.get(nm.upper())
                    if v and v != "1":
                        a += ' %s="%s"' % (nm, v)
                cells.append("<%s%s>%s</%s>" % ("th" if c.tag == "TH" else "td", a, txt, "th" if c.tag == "TH" else "td"))
        if cells:
            out.append("<tr>%s</tr>" % "".join(cells))
    out.append("</table>")
    return "".join(out)


# ------------------------------------------------------------------------------------------------ OSCAL
cat = json.load(open(OSCAL, encoding="utf-8"))["catalog"]
CONTROLS = {}     # id -> (control, group)
PARAMS = {}
RES = {}
for r in cat.get("back-matter", {}).get("resources", []):
    RES[r["uuid"]] = r


def walk_controls(ctrls, grp):
    for c in ctrls:
        CONTROLS[c["id"]] = (c, grp)
        for p in c.get("params", []):
            PARAMS[p["id"]] = p
        walk_controls(c.get("controls", []), grp)


for g in cat["groups"]:
    walk_controls(g["controls"], g)


def prop(obj, name, cls=None, default=None):
    for p in obj.get("props", []):
        if p["name"] == name and (cls is None or p.get("class") == cls):
            return p["value"]
    return default


def label_of(c):
    for p in c.get("props", []):
        if p["name"] == "label" and "class" not in p:
            return p["value"]
    return c["id"].upper()


def is_withdrawn(c):
    return prop(c, "status") == "withdrawn"


NIST_PICK = [
    ("au-1", "audit"), ("au-2", "audit"), ("au-3", "audit"), ("au-6", "audit"), ("au-9", "audit"), ("au-12", "audit"),
    ("ca-2", "audit"), ("ca-7", "audit"),
    ("au-11", "retention"), ("si-12", "retention"), ("mp-6", "retention"), ("cp-9", "retention"),
    ("ac-8", "acceptable-use"), ("ac-20", "acceptable-use"), ("pl-4", "acceptable-use"), ("ps-6", "acceptable-use"),
    ("ps-8", "acceptable-use"), ("at-2", "acceptable-use"),
    ("pt-2", "privacy"), ("pt-3", "privacy"), ("pt-4", "privacy"), ("pt-5", "privacy"), ("pm-18", "privacy"),
    ("ac-2", "security"), ("ac-3", "security"), ("ia-2", "security"), ("ia-5", "security"), ("sc-8", "security"),
    ("sc-12", "security"), ("sc-28", "security"), ("ir-4", "security"), ("ir-6", "security"), ("cm-6", "security"), ("ra-3", "security"),
]
FAMILIES = [("ac", "Access control"), ("au", "Audit and accountability"), ("ia", "Identification and authentication"),
            ("sc", "System and communications protection"),
            ("si", "System and information integrity")]
NIST_TOPIC = {}
for cid, folder in NIST_PICK:
    c, g = CONTROLS[cid]
    tp = register("nist:" + cid, folder, "%s %s" % (label_of(c), c["title"]), "NIST SP 800-53 Rev. 5.2.0 control " + label_of(c))
    NIST_TOPIC[cid] = tp
FAMILY_TOPIC = {}
for gid, name in FAMILIES:
    tp = register("family:" + gid, "catalogue", "%s controls: index" % name, "NIST SP 800-53 Rev. 5.2.0 family " + gid.upper())
    FAMILY_TOPIC[gid] = tp


def md_inline(s, cur):
    s = html.escape(s, quote=False)
    s = re.sub(r"\*\*(.+?)\*\*", r"<b>\1</b>", s)
    s = re.sub(r"(?<![\w*])\*(?=\S)(.+?)(?<=\S)\*(?![\w*])", r"<i>\1</i>", s)

    def link(m):
        text, target = m.group(1), m.group(2)
        if target.startswith("#"):
            cid = target[1:]
            tp = NIST_TOPIC.get(cid)
            if tp is not None and tp is not cur:
                return '<a href="%s">%s</a>' % (href(cur, tp), text)
        return text
    s = re.sub(r"\[([^\]]+)\]\(([^)]+)\)", link, s)
    return s


def param_text(pid, cur):
    p = PARAMS.get(pid)
    if p is None:
        return "[Assignment: organization-defined value]"
    if "select" in p:
        sel = p["select"]
        how = sel.get("how-many", "one")
        choices = [subst(c, cur) for c in sel.get("choice", [])]
        kind = "Selection (one or more)" if how == "one-or-more" else "Selection"
        return "[%s: %s]" % (kind, "; ".join(choices))
    lab = p.get("label") or prop(p, "label") or "value"
    if not lab.startswith("organization"):
        lab = "organization-defined " + lab
    return "[Assignment: %s]" % lab


def subst(s, cur):
    return re.sub(r"\{\{\s*insert:\s*param,\s*([^\s}]+)\s*\}\}", lambda m: param_text(m.group(1), cur), s)


def prose_html(txt, cur, tag="p", cls=None):
    txt = subst(txt, cur)
    out = []
    for para in re.split(r"\n\s*\n", txt.strip()):
        para = para.strip()
        if not para:
            continue
        a = ' class="%s"' % cls if cls else ""
        lines = para.split("\n")
        if all(re.match(r"^[-*]\s+", l) for l in lines) and len(lines) > 0:
            out.append("<ul>%s</ul>" % "".join("<li>%s</li>" % md_inline(re.sub(r"^[-*]\s+", "", l), cur) for l in lines))
        else:
            out.append("<%s%s>%s</%s>" % (tag, a, md_inline(" ".join(l.strip() for l in lines), cur), tag))
    return "".join(out)


def parts_named(c, name):
    return [p for p in c.get("parts", []) if p["name"] == name]


def statement_html(part, cur):
    out = []
    if part.get("prose"):
        out.append(prose_html(part["prose"], cur))
    items = [p for p in part.get("parts", []) if p["name"] in ("item", "statement")]
    if items:
        out.append("<ol>")
        for it in items:
            li = []
            if it.get("prose"):
                li.append(prose_html(it["prose"], cur))
            sub = statement_html({"parts": it.get("parts", [])}, cur) if it.get("parts") else ""
            out.append("<li>%s%s</li>" % ("".join(li), sub))
        out.append("</ol>")
    return "".join(out)


def objective_dl(parts, cur):
    out = ["<dl>"]
    for p in parts:
        lab = prop(p, "label", "sp800-53a") or prop(p, "label") or ""
        text = prose_html(p["prose"], cur, tag="p") if p.get("prose") else ""
        sub = [q for q in p.get("parts", []) if q["name"] == "assessment-objective"]
        out.append("<dt>%s</dt><dd>%s%s</dd>" % (esc(lab), text or "<p></p>", objective_dl(sub, cur) if sub else ""))
    out.append("</dl>")
    return "".join(out)


def control_html(c, cur, base, out):
    """Appends the sections of a control or enhancement; base is the heading level of its own title."""
    L = lambda n: min(base + n, 6)
    st = parts_named(c, "statement")
    if st:
        out.append("<h%d>Control text</h%d>" % (L(1), L(1)))
        out.append("<blockquote>%s</blockquote>" % statement_html(st[0], cur))
    gd = parts_named(c, "guidance")
    if gd:
        out.append("<h%d>Discussion</h%d>" % (L(1), L(1)))
        out.append(prose_html(gd[0]["prose"], cur, cls="note"))
    params = c.get("params", [])
    if params:
        out.append("<h%d>Parameters</h%d>" % (L(1), L(1)))
        out.append("<dl>")
        for p in params:
            lab = prop(p, "label", "sp800-53a") or p["id"]
            gl = p.get("guidelines", [{}])[0].get("prose", "")
            if "select" in p:
                gl = gl or "choose from: " + "; ".join(subst(x, cur) for x in p["select"].get("choice", []))
            out.append("<dt>%s</dt><dd><p>%s</p></dd>" % (esc(lab), md_inline(subst(gl, cur), cur) or esc(p.get("label", ""))))
        out.append("</dl>")
    rel = [l for l in c.get("links", []) if l.get("rel") == "related"]
    if rel:
        out.append("<h%d>Related controls</h%d>" % (L(1), L(1)))
        items = []
        for l in rel:
            cid = l["href"][1:]
            if cid in CONTROLS:
                oc = CONTROLS[cid][0]
                label = "%s %s" % (label_of(oc), oc["title"])
                tp = NIST_TOPIC.get(cid)
                if tp is not None and tp is not cur:
                    items.append('<li><a href="%s">%s</a></li>' % (href(cur, tp), esc(label)))
                else:
                    items.append("<li>%s</li>" % esc(label))
        out.append("<ul>%s</ul>" % "".join(items))
    objs = [p for p in c.get("parts", []) if p["name"] == "assessment-objective"]
    if objs:
        out.append("<h%d>Assessment objectives</h%d>" % (L(1), L(1)))
        out.append(objective_dl(objs, cur))
    meths = parts_named(c, "assessment-method")
    if meths:
        out.append("<h%d>Assessment methods</h%d>" % (L(1), L(1)))
        for m in meths:
            name = (prop(m, "method") or "method").capitalize()
            out.append("<h%d>%s</h%d>" % (L(2), name, L(2)))
            for obj in m.get("parts", []):
                if obj["name"] == "assessment-objects":
                    out.append("<h%d>Assessment objects</h%d>" % (L(3), L(3)))
                    items = [x.strip() for x in re.split(r"\n\s*\n", obj.get("prose", "")) if x.strip()]
                    out.append("<ul>%s</ul>" % "".join("<li>%s</li>" % md_inline(x, cur) for x in items))
                elif obj["name"] == "assessment-objects" or obj.get("prose"):
                    out.append(prose_html(obj["prose"], cur))
    refs = [l for l in c.get("links", []) if l.get("rel") == "reference"]
    if refs and base == 1:
        out.append("<h%d>References</h%d>" % (L(1), L(1)))
        items = []
        for l in refs:
            r = RES.get(l["href"].lstrip("#"))
            if r:
                # the full citation names the authors; the title of the public document is enough here
                cit = r.get("title") or ""
                items.append("<li>%s</li>" % md_inline(re.sub(r"\s+", " ", cit), cur))
        if items:
            out.append("<ul>%s</ul>" % "".join(items))
    enh = [e for e in c.get("controls", []) if not is_withdrawn(e)]
    if enh and base == 1:
        out.append("<h2>Control enhancements</h2>")
        for e in enh:
            out.append("<h3>%s %s</h3>" % (esc(label_of(e)), esc(e["title"])))
            control_html(e, cur, 3, out)


def nist_body(topic, cid):
    c, g = CONTROLS[cid]
    out = ["<h1>%s</h1>" % esc(topic.title)]
    control_html(c, topic, 1, out)
    return NL.join(out)


def family_body(topic, gid):
    g = [x for x in cat["groups"] if x["id"] == gid][0]
    out = ["<h1>%s</h1>" % esc(topic.title)]
    out.append("<p>This index lists every control and control enhancement in the %s family of NIST SP 800-53 Rev. 5.2.0, with the number of organization-defined parameters each one carries.</p>" % esc(g["title"].lower()))
    rows = []
    for c in g["controls"]:
        for item, kind in [(c, "Control")] + [(e, "Enhancement") for e in c.get("controls", [])]:
            tp = NIST_TOPIC.get(c["id"])
            lab = label_of(item)
            title = esc(item["title"])
            status = "Withdrawn" if is_withdrawn(item) else "Active"
            if tp is not None and tp is not topic and not is_withdrawn(item):
                lab_html = '<a href="%s">%s</a>' % (href(topic, tp), esc(lab))
            else:
                lab_html = esc(lab)
            rows.append("<tr><td>%s</td><td>%s</td><td>%s</td><td>%s</td><td>%d</td></tr>" % (lab_html, title, kind, status, len(item.get("params", []))))
    out.append("<table><tr><th>Identifier</th><th>Title</th><th>Type</th><th>Status</th><th>Parameters</th></tr>%s</table>" % "".join(rows))
    topic.rowcount = len(rows)
    return NL.join(out)


# ------------------------------------------------------------------------------------------------ front matter
register("front:about", "overview", "About this policy set", "generated from source metadata")
register("front:time", "retention", "Time limits and retention periods", "derived from the selected 45 CFR sections")
register("front:sources", "overview", "Source documents and revision dates", "generated from source metadata")

assign_slugs()
for t in topics:
    if t.key.startswith("cfr:"):
        t.html = cfr_body(t, t.key[4:])
    elif t.key.startswith("nist:"):
        t.html = nist_body(t, t.key[5:])
    elif t.key.startswith("family:"):
        t.html = family_body(t, t.key[7:])


def front_about(t):
    out = ["<h1>%s</h1>" % esc(t.title)]
    out.append("<p>This policy set brings together two bodies of public requirements so that a reader can move between what the regulation says an organization must do and how a control catalogue says to do it.</p>")
    out.append("<h2>Regulation</h2>")
    out.append("<p>The regulatory text is taken from Parts 160 and 164 of Title 45 of the Code of Federal Regulations, in the version in force on 6 October 2026. A covered entity or business associate must follow the privacy, security and breach notification requirements in these parts, and the compliance and investigation procedures in Part 160 describe how the Secretary reviews that compliance.</p>")
    out.append("<h2>Control catalogue</h2>")
    out.append("<p>The control text, discussion and assessment procedures come from NIST SP 800-53 Rev. 5.2.0 and SP 800-53A Rev. 5.2.0. Each control topic quotes the control text, then lists the discussion, the organization-defined parameters, the related controls and the assessment procedures an assessor follows.</p>")
    out.append("<h2>How the topics are grouped</h2>")
    out.append("<dl>")
    groups = [("privacy", "Privacy", "Uses and disclosures of protected health information, individual rights, and privacy controls."),
              ("security", "Security", "Administrative, physical and technical safeguards, and security controls."),
              ("breach", "Breach notification", "Notification to individuals, the media, the Secretary and by business associates."),
              ("acceptable-use", "Acceptable use", "Workstation use, rules of behavior, access agreements, sanctions and awareness training."),
              ("retention", "Retention", "Documentation retention, information management and retention, and media and backup controls."),
              ("audit", "Audit", "Audit controls and records, assessments, compliance reviews and investigations."),
              ("definitions", "Definitions", "Defined terms for each part of the regulation."),
              ("catalogue", "Catalogue", "Indexes of the controls and control enhancements in each family.")]
    for key, name, desc in groups:
        members = [x for x in topics if x.folder == key]
        first = members[0] if members else None
        link = '<a href="%s">%s</a>' % (href(t, first), esc(first.title)) if first else ""
        out.append("<dt>%s</dt><dd><p>%s For example, see %s.</p></dd>" % (name, desc, link))
    out.append("</dl>")
    return NL.join(out)


def front_sources(t):
    out = ["<h1>%s</h1>" % esc(t.title)]
    out.append("<p>Every topic in this set is taken from one of the following documents. The dates below are the dates of the versions used.</p>")
    out.append("<table><tr><th>Document</th><th>Version or date</th><th>Published by</th><th>Used for</th></tr>")
    out.append("<tr><td>45 CFR Part 160, General Administrative Requirements</td><td>In force on 6 October 2026</td><td>Office of the Federal Register</td><td>Definitions and compliance procedures</td></tr>")
    out.append("<tr><td>45 CFR Part 164, Security and Privacy</td><td>In force on 6 October 2026</td><td>Office of the Federal Register</td><td>Privacy, security and breach notification requirements</td></tr>")
    out.append("<tr><td>NIST SP 800-53 Rev. 5.2.0, Security and Privacy Controls</td><td>Version 5.2.0, last modified 11 May 2026</td><td>National Institute of Standards and Technology</td><td>Control text and discussion</td></tr>")
    out.append("<tr><td>NIST SP 800-53A Rev. 5.2.0, Assessment Procedures</td><td>Version 5.2.0, last modified 11 May 2026</td><td>National Institute of Standards and Technology</td><td>Assessment objectives and methods</td></tr>")
    out.append("</table>")
    out.append("<h2>Revision history of the control catalogue</h2>")
    out.append("<p>The catalogue has been revised several times since the first electronic release. These are the versions recorded in its own revision list.</p>")
    revs = cat["metadata"].get("revisions", [])
    out.append("<ol>%s</ol>" % "".join("<li>Version %s</li>" % esc(r.get("version", "")) for r in revs))
    out.append("<p>Rev. 5.2.0 is the version used here, and it was last modified on 11 May 2026. Withdrawn controls are not given a topic.</p>")
    return NL.join(out)


def front_time(t):
    rows = []
    for tp in topics:
        for cite, period, sent in getattr(tp, "timelimits", []):
            rows.append((tp, cite, period, sent))
    out = ["<h1>%s</h1>" % esc(t.title)]
    out.append("<p>This table lists the sentences in the selected regulation sections that set a time limit, a notice period or a retention period. Each row gives the paragraph that sets the period, the period itself, and the sentence.</p>")
    out.append("<table><tr><th>Paragraph</th><th>Period</th><th>Requirement</th></tr>")
    for tp, cite, period, sent in rows:
        shown = "&#167; " + cite if cite[0].isdigit() else cite
        out.append('<tr><td><a href="%s">%s</a></td><td>%s</td><td>%s</td></tr>' % (href(t, tp), esc("§ " + cite), esc(period), esc(sent)))
    out.append("</table>")
    t.rowcount = len(rows)
    return NL.join(out)


for t in topics:
    if t.key == "front:about":
        t.html = front_about(t)
    elif t.key == "front:sources":
        t.html = front_sources(t)
    elif t.key == "front:time":
        t.html = front_time(t)

shutil.rmtree(os.path.join(OUT, "src"), ignore_errors=True)
manifest = []
for t in topics:
    title_plain = re.sub(r"<[^>]+>", "", t.title)
    body = TEMPLATE % (html.escape(title_plain), t.html)
    p = os.path.join(OUT, "src", t.folder, t.slug + ".html")
    os.makedirs(os.path.dirname(p), exist_ok=True)
    with open(p, "w", encoding="utf-8", newline="\n") as fh:
        fh.write(body)
    words = len(re.findall(r"\w+", re.sub(r"<[^>]+>", " ", t.html)))
    t.words = words
    manifest.append((t.folder, t.slug, t.title, t.source, words))
with open(os.path.join(OUT, "topics.tsv"), "w", encoding="utf-8", newline="\n") as fh:
    for row in manifest:
        fh.write(chr(9).join(str(x) for x in row) + NL)
print("topics: %d, words: %d" % (len(topics), sum(t.words for t in topics)))
print("time-limit rows: %d" % sum(len(getattr(x, "timelimits", [])) for x in topics))
