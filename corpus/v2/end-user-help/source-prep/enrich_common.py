"""Shared helpers for the corpus enrichment scripts (lxml; attribute-level or inline edits only, deterministic)."""
import os, re, posixpath
from lxml import etree

MC = "http://www.madcapsoftware.com/Schemas/MadCap.xsd"
CONDITIONS = "{%s}conditions" % MC


def mc(name):
    return "{%s}%s" % (MC, name)


def load(path):
    return etree.parse(path, etree.XMLParser(remove_blank_text=False))


def save(tree, path):
    data = etree.tostring(tree, encoding="utf-8", xml_declaration=False)
    with open(path, "wb") as f:
        f.write(b'<?xml version="1.0" encoding="utf-8"?>\n' + data + b"\n")


def content_files(root, exts=(".htm", ".flsnp")):
    for dp, dn, fn in os.walk(os.path.join(root, "Content")):
        dn.sort()
        for f in sorted(fn):
            if f.endswith(exts):
                yield os.path.join(dp, f)


def rel(root, path):
    return os.path.relpath(path, root).replace("\\", "/")


def text_of(e):
    return "".join(e.itertext())


def rebase_snippets(root):
    """CorpusKit's snippet extraction moves a block into Content/Resources/Snippets/ but keeps its topic-relative hrefs and srcs.
    Rewrite them relative to the snippet file, using the first topic that includes the snippet."""
    users = {}
    for path in content_files(root, (".htm",)):
        tree = load(path)
        for e in tree.iter(mc("snippetBlock"), mc("snippetText")):
            src = e.get("src")
            if not src:
                continue
            target = posixpath.normpath(posixpath.join(posixpath.dirname(rel(root, path)), src))
            users.setdefault(target, rel(root, path))
    fixed = 0
    for path in content_files(root, (".flsnp",)):
        r = rel(root, path)
        topic = users.get(r)
        if not topic:
            continue
        tree = load(path)
        changed = False
        for e in tree.iter():
            for attr in ("href", "src"):
                v = e.get(attr)
                if not v or re.match(r"^([a-z][a-z0-9+.-]*:|/|#)", v, re.I):
                    continue
                if attr == "href" and e.tag == "link":
                    pass
                base = v.split("#")[0]
                frag = v[len(base):]
                if e.tag == mc("snippetBlock") or e.tag == mc("snippetText"):
                    continue  # snippet in snippet: keep as is
                dest = posixpath.normpath(posixpath.join(posixpath.dirname(topic), base))
                new = posixpath.relpath(dest, posixpath.dirname(r)) + frag
                if new != v:
                    e.set(attr, new)
                    changed = True
                    fixed += 1
        if changed:
            save(tree, path)
    return fixed
