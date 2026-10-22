#!/usr/bin/env python3
"""end-user-help: enrichment around CorpusKit.

  enrich_help.py <project> content    after `import`, before `scaffold`: conditions on the marked snippets, the text snippet,
                                       glossary terms, snippet href rebasing
  enrich_help.py <project> togglers   after `inject`, before `contexts --update`: togglers on unlabelled short lead-in paragraphs
Attribute-level or inline-only edits (a paragraph keeps its XPath), so seeded labels stay valid. Deterministic.
"""
import os, re, sys, hashlib, posixpath, json
from lxml import etree
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import enrich_common as ec
from enrich_common import mc, CONDITIONS

root, mode = sys.argv[1], sys.argv[2]

ADMIN = "Your server administrator can turn this feature off."
GLOSSARY_TERMS = ["WebDAV", "CalDAV", "CardDAV", "TOTP", "E2EE", "end-to-end encryption", "two-factor authentication", "SSL", "sync client", "desktop client"]
SNIP = "Content/Resources/Snippets/admin-can-turn-off.flsnp"


def inline_ok(e):
    """True when e or an ancestor is code, a link or a UI label: leave those alone."""
    for a in e.iterancestors():
        if a.tag in ("code", "a", "b", "pre", "kbd", "h1", "h2", "h3", "h4", "h5", "h6", mc("xref"), mc("dropDownHotspot")):
            return False
        if a.tag == "span" and "uicontrol" in (a.get("class") or ""):
            return False
    return True


if mode == "content":
    counts = dict(print=0, screen=0, text_snippet=0, glossary=0)
    # snippets: conditions on the marked ones; create the text snippet file
    snip_dir = os.path.join(root, "Content", "Resources", "Snippets")
    os.makedirs(snip_dir, exist_ok=True)
    for path in ec.content_files(root, (".flsnp",)):
        tree = ec.load(path)
        ch = False
        for p in tree.iter("p"):
            t = ec.text_of(p).lstrip()
            m = re.match(r"\[\[(print|screen)\]\]\s*", t)
            if m:
                if p.text:
                    p.text = re.sub(r"^\s*\[\[(print|screen)\]\]\s*", "", p.text)
                p.set(CONDITIONS, "Default.Print" if m.group(1) == "print" else "Default.ScreenOnly")
                counts[m.group(1)] += 1
                ch = True
        if ch:
            ec.save(tree, path)
    # a marker paragraph used in fewer than 3 topics is not a snippet; condition it in the topic itself
    for path in ec.content_files(root, (".htm",)):
        tree = ec.load(path)
        ch = False
        for p in tree.iter("p"):
            t = ec.text_of(p).lstrip()
            m = re.match(r"\[\[(print|screen)\]\]\s*", t)
            if m:
                if p.text:
                    p.text = re.sub(r"^\s*\[\[(print|screen)\]\]\s*", "", p.text)
                p.set(CONDITIONS, "Default.Print" if m.group(1) == "print" else "Default.ScreenOnly")
                ch = True
        if ch:
            ec.save(tree, path)
    # text snippet: the admin sentence appended to a paragraph in some topics
    snip_root = etree.fromstring(('<html xmlns:MadCap="%s"><head/><body><p>%s</p></body></html>' % (ec.MC, ADMIN)).encode())
    ec.save(etree.ElementTree(snip_root), os.path.join(root, SNIP.replace("/", os.sep)))
    for path in ec.content_files(root, (".htm",)):
        tree = ec.load(path)
        r = ec.rel(root, path)
        ch = False
        for p in list(tree.iter("p")):
            done = False
            nodes = [(p, "text")] + [(c, "tail") for c in p]
            for owner, kind in nodes:
                val = owner.text if kind == "text" else owner.tail
                if val and ADMIN in val:
                    before, after = val.split(ADMIN, 1)
                    el = etree.Element(mc("snippetText"))
                    el.set("src", posixpath.relpath(SNIP, posixpath.dirname(r)))
                    if kind == "text":
                        p.text = before
                        p.insert(0, el)
                        el.tail = after
                    else:
                        owner.tail = before
                        owner.addnext(el)
                        el.tail = after
                    counts["text_snippet"] += 1
                    ch = done = True
                    break
            if done:
                continue
        if ch:
            ec.save(tree, path)
    # glossary terms: first occurrence of each term per topic, in running text only (not code, links, bold, headings)
    for path in ec.content_files(root, (".htm",)):
        tree = ec.load(path)
        ch = False
        used = set()
        for p in list(tree.iter("p", "li")):
            if not inline_ok(p):
                continue
            for owner in [p] + list(p):
                is_text = owner is p
                val = owner.text if is_text else owner.tail
                if not val:
                    continue
                hit = None
                for term in GLOSSARY_TERMS:
                    if term in used:
                        continue
                    m = re.search(r"(?<![\w-])" + re.escape(term) + r"(?![\w-])", val)
                    if m and (hit is None or m.start() < hit[1].start()):
                        hit = (term, m)
                if hit is None:
                    continue
                term, m = hit
                el = etree.Element(mc("glossaryTerm"))
                el.text = term
                before, after = val[:m.start()], val[m.end():]
                if is_text:
                    p.text = before
                    p.insert(0, el)
                else:
                    owner.tail = before
                    owner.addnext(el)
                el.tail = after
                used.add(term)
                counts["glossary"] += 1
                ch = True
        if ch:
            ec.save(tree, path)
    print(counts)
    print("snippet references rebased:", ec.rebase_snippets(root))

elif mode == "togglers":
    labelled = set()
    lp = os.path.join(root, "labels.jsonl")
    if os.path.exists(lp):
        for line in open(lp, encoding="utf-8"):
            if line.strip():
                d = json.loads(line)
                labelled.add(d["file"] + "|" + d["block"])
    made = 0
    for path in ec.content_files(root, (".htm",)):
        r = ec.rel(root, path)
        if made >= 26:
            break
        h = int(hashlib.md5(r.encode()).hexdigest()[:6], 16)
        if h % 7 != 0 and "faq" not in r and "known" not in r:
            continue
        tree = ec.load(path)
        body = tree.getroot().find("body")
        # XPath of body children as CorpusKit writes them: /html/body/p[2]
        counters = {}
        for child in body:
            counters[child.tag] = counters.get(child.tag, 0) + 1
            if child.tag != "p" or len(child):
                continue
            txt = (child.text or "").strip()
            nxt = child.getnext()
            xp = "/html/body/p[%d]" % counters["p"]
            if not (15 <= len(txt) <= 110 and txt.endswith(":") and nxt is not None and nxt.tag in ("ul", "ol", "pre", "table", "p")):
                continue
            if (r + "|" + xp) in labelled or nxt.get(mc("targetName")):
                continue
            name = "tgl%d" % (made + 1)
            child.text = None
            tg = etree.SubElement(child, mc("toggler"))
            tg.set("targets", name)
            tg.text = txt
            nxt.set(mc("targetName"), name)
            made += 1
            ec.save(tree, path)
            break
    print("togglers:", made)
