#!/usr/bin/env python3
"""Converts a subset of AsciiDoc (as used in the Raspberry Pi documentation) to simple HTML topics.

Usage: adoc2html.py CONFIG.json
CONFIG: {"src": "<doc root>", "out": "<output dir>", "files": [{"path": "accessories/camera/install.adoc", "title": "optional preamble title"}],
         "names": {"Real Name": "Fictional Name"}, "skip_sections": ["regex of section titles to drop"],
         "categories": [["regex", "folder"]...], "min_words": 40}
Writes <out>/<category>/<slug>.html and copies referenced images to <out>/images/. Prints a manifest (topics.tsv).
Dev tool for the corpus; not shipped.
"""
import html
import json
import os
import re
import shutil
import sys

NL = chr(10)
TEMPLATE = ('<!DOCTYPE html>' + NL + '<html><head><meta charset="utf-8"/><title>%s</title></head><body>' + NL +
            '<h1>%s</h1>' + NL + '%s' + NL + '</body></html>' + NL)
ATTRS = {"plus": "+", "sym-mu": "\u00b5", "cpp": "C++", "i": "I"}


class Topic:
    def __init__(self, src, title, lines, level, anchors):
        self.src = src            # relative source path
        self.title = title
        self.orig = title
        self.lines = lines
        self.level = level        # asciidoc level of the heading that opened it (1 = '==')
        self.anchors = anchors
        self.slug = None
        self.folder = None


def read_lines(path, seen=None):
    seen = seen or set()
    out = []
    with open(path, encoding="utf-8") as f:
        raw = f.read().replace("\r\n", "\n").split("\n")
    in_comment = False
    for ln in raw:
        if ln.strip() == "////":
            in_comment = not in_comment
            continue
        if in_comment:
            continue
        if ln.startswith("//") and not ln.startswith("///"):
            continue
        m = re.match(r"^include::(.+?)\[.*\]$", ln)
        if m:
            inc = os.path.normpath(os.path.join(os.path.dirname(path), m.group(1)))
            if os.path.exists(inc) and inc not in seen and inc.endswith(".adoc"):
                out.extend(read_lines(inc, seen | {path}))
            continue
        out.append(ln)
    return out


DELIMS = {"----": "listing", "....": "literal", "====": "example", "____": "quote", "--": "open", "|===": "table", "++++": "pass", "****": "sidebar"}


def split_topics(src_rel, lines, title_override):
    """Split a file into topics at level-1 sections (==). Returns list of Topic."""
    segs = []
    cur = []
    cur_title = None
    cur_level = 0
    pending_anchors = []
    block = None
    pre = []
    for ln in lines:
        s = ln.rstrip()
        if block:
            cur.append(ln)
            if s == block:
                block = None
            continue
        if s in ("----", "....", "++++", "****") or s == "|===" :
            block = s
            cur.append(ln)
            continue
        m = re.match(r"^==\s+(.+)$", s)
        if m:
            # anchors directly before this heading move with it
            moved = []
            while cur and (re.match(r"^\[\[[^\]]+\]\]$", cur[-1].strip()) or cur[-1].strip() == ""):
                moved.insert(0, cur.pop())
            segs.append((cur_title, cur_level, cur))
            cur = [x for x in moved if x.strip()] + []
            cur_title = m.group(1).strip()
            cur_level = 1
            continue
        m = re.match(r"^=\s+(.+)$", s)
        if m and cur_title is None:
            cur_title = m.group(1).strip()
            continue
        cur.append(ln)
    segs.append((cur_title, cur_level, cur))
    topics = []
    first = True
    for title, level, body in segs:
        text = "\n".join(body)
        anchors = re.findall(r"^\[\[([^\]]+)\]\]\s*$", text, flags=re.M)
        if title is None:
            if not text.strip():
                continue
            title = title_override or humanize(os.path.splitext(os.path.basename(src_rel))[0])
        topics.append(Topic(src_rel, title, body, level, anchors))
    return topics


def split_children(topic):
    """Split a topic at '===' headings into the intro (kept in the parent) and child topics."""
    segs = []
    cur = []
    cur_title = None
    block = None
    for ln in topic.lines:
        st = ln.rstrip()
        if block:
            cur.append(ln)
            if st == block:
                block = None
            continue
        if st in ("----", "....", "++++", "****", "|==="):
            block = st
            cur.append(ln)
            continue
        m = re.match(r"^===\s+(.+)$", st)
        if m:
            moved = []
            while cur and (re.match(r"^\[\[[^\]]+\]\]$", cur[-1].strip()) or cur[-1].strip() == ""):
                moved.insert(0, cur.pop())
            segs.append((cur_title, cur))
            cur = [x for x in moved if x.strip()]
            cur_title = m.group(1).strip()
            continue
        cur.append(ln)
    segs.append((cur_title, cur))
    out = []
    for title, body in segs:
        if title is None:
            topic.lines = body
            out.append(topic)
        else:
            anchors = re.findall(r"^\[\[([^\]]+)\]\]\s*$", "\n".join(body), flags=re.M)
            t = Topic(topic.src, title, body, 2, anchors)
            t.parent = topic
            out.append(t)
    return out


def humanize(stem):
    s = re.sub(r"[-_]+", " ", stem).strip()
    return s[:1].upper() + s[1:]


def words(lines):
    return len(re.findall(r"[A-Za-z]{2,}", " ".join(l for l in lines if not l.startswith("[[") and not l.startswith("image::"))))


def slugify(t):
    return re.sub(r"-+", "-", re.sub(r"[^a-z0-9]+", "-", t.lower())).strip("-")[:60]


# ---------------------------------------------------------------- inline

class Inline:
    def __init__(self, ctx):
        self.ctx = ctx

    def run(self, text):
        ph = self.ctx.ph

        def hold(h):
            ph.append(h)
            return "\u0001%d\u0002" % (len(ph) - 1)

        t = text
        t = re.sub(r"pass:\[(.*?)\]", lambda m: hold(m.group(1)), t)
        t = t.replace("*\\+*", hold("+"))
        t = re.sub(r"\\([*_`#+^~])", lambda m: hold(html.escape(m.group(1))), t)
        # code spans
        t = re.sub(r"`\+(.+?)\+`", lambda m: hold("<code>%s</code>" % self.run_code(m.group(1))), t)
        t = re.sub(r"(?<![\w`])`([^`]+)`(?![\w`])", lambda m: hold("<code>%s</code>" % self.run_code(m.group(1))), t)
        t = re.sub(r"\{(\w[\w-]*)\}", lambda m: ATTRS.get(m.group(1), m.group(0)), t)
        # macros
        t = re.sub(r"kbd:\[(.+?)\]", lambda m: hold("<kbd>%s</kbd>" % html.escape(m.group(1))), t)
        t = re.sub(r"btn:\[(.+?)\]", lambda m: hold("<b>%s</b>" % html.escape(m.group(1))), t)
        t = re.sub(r"menu:([^\[]+)\[(.*?)\]", lambda m: hold("<b>%s</b>" % html.escape((m.group(1) + " > " + m.group(2)).replace("&gt;", ">").strip(" >"))), t)
        t = re.sub(r"footnote:\[(.*?)\]", lambda m: hold(" (%s)" % html.escape(m.group(1))), t)
        t = re.sub(r"image:([^\s:\[][^\[]*)\[([^\]]*)\]", lambda m: hold(self.ctx.image_inline(m.group(1), m.group(2))), t)
        t = re.sub(r"xref:([^\[]+)\[([^\]]*)\]", lambda m: hold(self.ctx.xref(m.group(1), m.group(2))), t)
        t = re.sub(r"<<([^,>]+)(?:,([^>]*))?>>", lambda m: hold(self.ctx.anchor_ref(m.group(1), m.group(2))), t)
        t = re.sub(r"link:([^\[\s]+)\[([^\]]*)\]", lambda m: hold(self.ctx.link(m.group(1), m.group(2))), t)
        t = re.sub(r"(https?://[^\s\[<]+)\[([^\]]*)\]", lambda m: hold(self.ctx.link(m.group(1), m.group(2))), t)
        t = re.sub(r"(?<![\"'>=\w])(https?://[^\s\[<)]+[^\s\[<).,;])", lambda m: hold(self.ctx.link(m.group(1), "")), t)
        t = html.escape(t, quote=False)
        # constrained marks
        t = re.sub(r"(?<![\w*])\*\*(.+?)\*\*", r"<b>\1</b>", t)
        t = re.sub(r"(?<![\w*])\*(?=\S)(.+?)(?<=\S)\*(?![\w*])", r"<b>\1</b>", t)
        t = re.sub(r"(?<![\w_])_(?=\S)(.+?)(?<=\S)_(?![\w_])", r"<i>\1</i>", t)
        t = re.sub(r"\^(\S+?)\^", r"<sup>\1</sup>", t)
        t = re.sub(r"(?<=\w)~(\S+?)~", r"<sub>\1</sub>", t)
        t = re.sub(r"(?<![\w])#(?=\S)(.+?)(?<=\S)#(?![\w])", r"\1", t)
        t = t.replace("&amp;#8203;", "")
        t = re.sub(r" \+$", "<br/>", t)
        t = self.ctx.names(t)
        for _ in range(3):
            t = re.sub(r"\u0001(\d+)\u0002", lambda m: ph[int(m.group(1))], t)
        return t

    def run_code(self, s):
        return html.escape(self.ctx.alt(s))


# ---------------------------------------------------------------- blocks

ADMON = {"NOTE": "note", "TIP": "tip", "WARNING": "warning", "CAUTION": "caution", "IMPORTANT": "important"}


class Converter:
    def __init__(self, cfg, topic_of_anchor, topic_of_file, cur_topic, images, warnings, nameset):
        self.cfg = cfg
        self.topic_of_anchor = topic_of_anchor
        self.topic_of_file = topic_of_file
        self.cur = cur_topic
        self.images = images
        self.warnings = warnings
        self.nameset = nameset
        self.ph = []
        self.inline = Inline(self)
        self.fig = 0

    # -- helpers used by Inline
    def names(self, t):
        for k, v in self.nameset:
            t = t.replace(k, v)
        return t

    def alt(self, t):
        t = self.names(t)
        for k, v in self.cfg.get("alt_names", {}).items():
            t = t.replace(k, v)
        return t

    def rel_href(self, target):
        a = os.path.relpath(target.path(), os.path.dirname(self.cur.path())).replace("\\", "/")
        return a

    def anchor_ref(self, aid, text):
        tgt = self.topic_of_anchor.get(aid)
        label = self.inline.run(text.strip()) if text else None
        if tgt is None:
            self.warnings.append("%s: unresolved anchor <<%s>>" % (self.cur.src, aid))
            return label or html.escape(aid.replace("-", " "))
        if label is None:
            label = html.escape(tgt.title)
        if tgt is self.cur:
            return label
        return '<a href="%s">%s</a>' % (self.rel_href(tgt), label)

    def xref(self, target, text):
        path, _, frag = target.partition("#")
        tgt = None
        if path.endswith(".adoc") or path == "":
            if frag and frag in self.topic_of_anchor:
                tgt = self.topic_of_anchor[frag]
            elif path:
                full = os.path.normpath(os.path.join(os.path.dirname(self.cur.src), path)).replace("\\", "/")
                tgt = self.topic_of_file.get(full)
        label = self.inline.run(text.strip()) if text else None
        if tgt is None:
            self.warnings.append("%s: xref %s not in selection" % (self.cur.src, target))
            return label or ""
        if label is None:
            label = html.escape(tgt.title)
        if tgt is self.cur:
            return label
        return '<a href="%s">%s</a>' % (self.rel_href(tgt), label)

    def link(self, url, text):
        # external addresses name real companies: point every external link at the reserved example.com domain
        label = text or "Related information"
        fake = "https://www.example.com/" + (re.sub(r"[^a-z0-9]+", "-", self.alt(label).lower()).strip("-")[:40] or "page")
        return '<a href="%s">%s</a>' % (fake, html.escape(self.names(label)))

    def image_inline(self, path, attrs):
        alt = attrs.split(",")[0] if attrs and "=" not in attrs.split(",")[0] else ""
        return self.img(path, alt)

    def img(self, path, alt):
        src = os.path.normpath(os.path.join(self.cfg["src_dir"], os.path.dirname(self.cur.src), path))
        if not os.path.exists(src):
            self.warnings.append("%s: image %s missing" % (self.cur.src, path))
            return ""
        key = os.path.relpath(src, self.cfg["src_dir"]).replace("\\", "/")
        name = self.images.get(key)
        if name is None:
            base = re.sub(r"[^a-z0-9.-]+", "-", os.path.basename(src).lower())
            for k, v in self.cfg.get("slug_names", {}).items():
                base = base.replace(k, v)
            name = base
            n = 2
            while name in self.images.values():
                stem, ext = os.path.splitext(base)
                name = "%s-%d%s" % (stem, n, ext)
                n += 1
            self.images[key] = name
        rel = os.path.relpath(os.path.join(self.cfg["out_dir"], "images", name), os.path.dirname(self.cur.path())).replace("\\", "/")
        alt_attr = ' alt="%s"' % html.escape(self.alt(alt), quote=True) if alt else ""
        return '<img src="%s"%s/>' % (rel, alt_attr)

    # -- block parsing
    def blocks(self, lines, level_shift, depth=0):
        out = []
        i = 0
        n = len(lines)
        title = None
        attrs = None
        while i < n:
            ln = lines[i]
            s = ln.rstrip()
            if s == "":
                i += 1
                continue
            m = re.match(r"^\[\[([^\]]+)\]\]$", s)
            if m or s == "+":
                i += 1
                continue
            if re.match(r"^:[\w!-]+:", s) or s.startswith("ifdef::") or s.startswith("ifndef::") or s.startswith("endif::") or s.startswith("ifeval::"):
                i += 1
                continue
            m = re.match(r"^\.([A-Za-z0-9<(\"'`*][^.].*)$", s)
            if m and not s.startswith(".."):
                title = m.group(1).strip()
                i += 1
                continue
            m = re.match(r"^\[(.*)\]$", s)
            if m and not s.startswith("[["):
                attrs = m.group(1)
                i += 1
                continue
            m = re.match(r"^(={2,6})\s+(.+)$", s)
            if m:
                lvl = len(m.group(1)) - 1
                h = min(max(lvl - level_shift + 1, 2), 6)
                out.append("<h%d>%s</h%d>" % (h, self.inline.run(m.group(2).strip()), h))
                i += 1
                attrs = None
                continue
            if attrs and attrs.startswith("discrete") or (attrs and attrs == "discrete"):
                pass
            # delimited blocks
            d = s if (s in DELIMS or re.fullmatch(r"={4,}", s)) else None
            if d:
                j = i + 1
                body = []
                while j < n and lines[j].rstrip() != d:
                    body.append(lines[j])
                    j += 1
                i = j + 1
                kind = DELIMS.get(d, "example")
                out.append(self.delimited(kind, d, body, attrs, title, level_shift, depth))
                title = None
                attrs = None
                continue
            # image block
            m = re.match(r"^image::([^\[]+)\[(.*)\]$", s)
            if m:
                a = m.group(2)
                alt = ""
                first = a.split(",")[0] if a else ""
                if first and "=" not in first:
                    alt = first
                if not alt and title:
                    alt = title
                self.fig += 1
                fig = self.img(m.group(1), alt)
                if fig:
                    out.append("<p>%s</p>" % fig)
                    if title:
                        out.append("<p>Figure %d. %s</p>" % (self.fig, self.inline.run(title).rstrip(".")))
                title = None
                attrs = None
                i += 1
                continue
            # admonition paragraph
            m = re.match(r"^(NOTE|TIP|WARNING|CAUTION|IMPORTANT):\s+(.*)$", s)
            if m:
                para = [m.group(2)]
                i += 1
                while i < n and lines[i].strip() != "":
                    para.append(lines[i].strip())
                    i += 1
                out.append('<p class="%s">%s</p>' % (ADMON[m.group(1)], self.inline.run(" ".join(para))))
                title = None
                attrs = None
                continue
            # lists
            if re.match(r"^(\*{1,5}|-|\.{1,5}|\d+\.)\s+\S", s) and not s.startswith("---"):
                j, html_list = self.list_block(lines, i, level_shift, depth)
                out.append(html_list)
                i = j
                attrs = None
                title = None
                continue
            if re.match(r"^.+?(::|;;)(\s|$)", s) and not s.startswith("|"):
                j, html_dl = self.dlist(lines, i, level_shift, depth)
                out.append(html_dl)
                i = j
                continue
            # paragraph (or literal if indented)
            para = []
            while i < n and lines[i].strip() != "" and not self.starts_block(lines[i]):
                para.append(lines[i].strip())
                i += 1
            if not para:
                i += 1
                continue
            text = " ".join(para)
            if attrs and attrs.startswith("discrete"):
                out.append("<h5>%s</h5>" % self.inline.run(text))
            else:
                out.append("<p>%s</p>" % self.inline.run(text))
            attrs = None
            title = None
        return "\n".join(x for x in out if x)

    def starts_block(self, ln):
        s = ln.rstrip()
        if s in DELIMS or re.match(r"^\[.*\]$", s) or re.match(r"^image::", s) or re.match(r"^={2,6}\s", s):
            return True
        if re.match(r"^(NOTE|TIP|WARNING|CAUTION|IMPORTANT):\s", s):
            return True
        if re.match(r"^(\*{1,5}|-|\.{1,5}|\d+\.)\s+\S", s) and not s.startswith("---"):
            return True
        return False

    def delimited(self, kind, d, body, attrs, title, level_shift, depth):
        a = (attrs or "")
        first = a.split(",")[0].strip()
        if kind == "listing" or kind == "literal":
            code = "\n".join(body)
            code = self.alt(code)
            return "<pre>%s</pre>" % html.escape(code)
        if kind == "pass":
            return ""
        if kind == "table":
            return self.table(body, a, title)
        if kind in ("example", "open", "sidebar", "quote"):
            if kind == "quote":
                return "<blockquote>%s</blockquote>" % self.blocks(body, level_shift, depth + 1)
            if first in ADMON:
                inner = self.blocks(body, level_shift, depth + 1)
                inner = re.sub(r"^<p>", '<p class="%s">' % ADMON[first], inner, count=1)
                inner = re.sub(r"\n<p>", '\n<p class="%s">' % ADMON[first], inner)
                return inner
            if first == "tabs":
                return self.tabs(body, level_shift, depth)
            return self.blocks(body, level_shift, depth + 1)
        return ""

    def tabs(self, body, level_shift, depth):
        out = []
        i = 0
        cur = None
        buf = []
        res = []
        for ln in body:
            m = re.match(r"^(.+?)::\s*$", ln)
            if m:
                if cur:
                    res.append((cur, buf))
                cur, buf = m.group(1), []
            else:
                buf.append(ln)
        if cur:
            res.append((cur, buf))
        for name, b in res:
            out.append("<h5>%s</h5>" % self.inline.run(name))
            out.append(self.blocks(b, level_shift, depth + 1))
        return "\n".join(out)

    # lists --------------------------------------------------------------
    def list_block(self, lines, i, level_shift, depth):
        n = len(lines)
        items = []   # (marker, text lines, attached blocks)
        while i < n:
            s = lines[i].rstrip()
            m = re.match(r"^(\*{1,5}|-|\.{1,5}|\d+\.)\s+(.*)$", s)
            if not m or s.startswith("---"):
                break
            marker = m.group(1)
            if marker == "-":
                marker = "*"
            if re.match(r"^\d+\.$", marker):
                marker = "."
            text = [m.group(2).strip()]
            attach = []
            i += 1
            while i < n:
                t = lines[i].rstrip()
                if t == "":
                    # blank: list may continue
                    k = i + 1
                    while k < n and lines[k].strip() == "":
                        k += 1
                    if k < n and re.match(r"^(\*{1,5}|-|\.{1,5}|\d+\.)\s+\S", lines[k].rstrip()) and not lines[k].startswith("---"):
                        i = k
                    break
                if t == "+":
                    i += 1
                    # attached block: take until blank line or next list item (delimited blocks taken whole)
                    blk = []
                    while i < n and (re.match(r"^\[.*\]$", lines[i].rstrip()) or re.match(r"^\.\w", lines[i])):
                        blk.append(lines[i]); i += 1
                    if i < n and (lines[i].rstrip() in DELIMS or re.fullmatch(r"={4,}", lines[i].rstrip())):
                        dd = lines[i].rstrip()
                        blk.append(lines[i]); i += 1
                        while i < n and lines[i].rstrip() != dd:
                            blk.append(lines[i]); i += 1
                        if i < n:
                            blk.append(lines[i]); i += 1
                    else:
                        while i < n and lines[i].strip() != "" and lines[i].rstrip() != "+" and not re.match(r"^(\*{1,5}|-|\.{1,5}|\d+\.)\s+\S", lines[i].rstrip()):
                            blk.append(lines[i]); i += 1
                    attach.append(blk)
                    continue
                if re.match(r"^(\*{1,5}|-|\.{1,5}|\d+\.)\s+\S", t) and not t.startswith("---"):
                    break
                if t.startswith("[") and t.endswith("]") and i + 1 < n:
                    break
                text.append(t.strip())
                i += 1
            items.append((marker, " ".join(text), attach))
        return i, self.render_items(items, 0, level_shift, depth)

    def render_items(self, items, pos, level_shift, depth):
        # build nested structure by marker depth/kind
        html_out, _ = self._render(items, 0, items[0][0] if items else "*", level_shift, depth)
        return html_out

    def _render(self, items, idx, marker, level_shift, depth):
        tag = "ol" if marker.startswith(".") else "ul"
        out = ["<%s>" % tag]
        while idx < len(items):
            mk, text, attach = items[idx]
            if mk != marker:
                if self._deeper(mk, marker):
                    break
                else:
                    break
            idx += 1
            li = [self.inline.run(text)]
            for blk in attach:
                li.append(self.blocks(blk, level_shift, depth + 1))
            while idx < len(items) and items[idx][0] != marker and self._deeper(items[idx][0], marker):
                sub, idx = self._render(items, idx, items[idx][0], level_shift, depth)
                li.append(sub)
            body = li[0] if len(li) == 1 else "<p>%s</p>" % li[0] + "".join(li[1:])
            out.append("<li>%s</li>" % body)
        out.append("</%s>" % tag)
        return "".join(out), idx

    @staticmethod
    def _deeper(a, b):
        return (a[0] == b[0] and len(a) > len(b)) or (a[0] != b[0])

    def dlist(self, lines, i, level_shift, depth):
        n = len(lines)
        out = ["<dl>"]
        while i < n:
            s = lines[i].rstrip()
            m = re.match(r"^(.+?)(::|;;)(?:\s+(.*))?$", s)
            if not m or s.startswith("|") or s.startswith("["):
                if s == "" and i + 1 < n and re.match(r"^(.+?)(::|;;)(\s|$)", lines[i + 1].rstrip()):
                    i += 1
                    continue
                break
            out.append("<dt>%s</dt>" % self.inline.run(m.group(1).strip()))
            desc = [m.group(3)] if m.group(3) else []
            i += 1
            while i < n and lines[i].strip() != "" and not re.match(r"^(.+?)(::|;;)(\s|$)", lines[i].rstrip()):
                desc.append(lines[i].strip())
                i += 1
            if desc:
                out.append("<dd><p>%s</p></dd>" % self.inline.run(" ".join(desc)))
            else:
                out.append("<dd><p></p></dd>")
        out.append("</dl>")
        return i, "".join(out)

    # tables -------------------------------------------------------------
    def table(self, body, attrs, title):
        opts = attrs
        header = "header" in opts
        cols = None
        m = re.search(r'cols="([^"]+)"', opts)
        styles = []
        if m:
            specs = []
            for part in m.group(1).split(","):
                part = part.strip()
                mm = re.match(r"^(\d+)\*(.*)$", part)
                rep, spec = (int(mm.group(1)), mm.group(2)) if mm else (1, part)
                specs.extend([spec] * rep)
            cols = len(specs)
            styles = [re.sub(r"[\d.<^>]", "", s) for s in specs]
        text = "\n".join(body)
        # implicit header: first line non-blank followed by blank line
        implicit_header = False
        nonblank = [l for l in body]
        if body and body[0].strip() != "" and len(body) > 1 and body[1].strip() == "" and not header:
            implicit_header = True
        # tokenise cells
        cells = []  # (spec, text)
        pos = 0
        pat = re.compile(r"(?:(?<![^\s])(?P<spec>(?:\d+\*)?(?:\d*(?:\.\d+)?\+)?[<^>]?(?:\.[<^>])?[a-z]?)\||\|)")
        marks = [(m.start(), m.end(), m.group("spec") or "") for m in pat.finditer(text) if not (m.start() > 0 and text[m.start() - 1] == "\\")]
        for k, (s, e, spec) in enumerate(marks):
            nxt = marks[k + 1][0] if k + 1 < len(marks) else len(text)
            cells.append((spec, text[e:nxt].strip("\n")))
        # first-row count for cols
        if cols is None:
            first_line = text.strip("\n").split("\n")[0]
            cols = max(1, first_line.count("|"))
            styles = [""] * cols
        rows = []
        cur = []
        col = 0
        occupied = {}   # (row, col) -> remaining
        r = 0
        pending_rowspans = {}
        for spec, content in cells:
            cs, rs, dup, style = 1, 1, 1, ""
            mm = re.match(r"^(?:(\d+)\*)?(?:(\d*)(?:\.(\d+))?\+)?[<^>]?(?:\.[<^>])?([a-z])?$", spec)
            if mm:
                if mm.group(1):
                    dup = int(mm.group(1))
                if mm.group(2):
                    cs = int(mm.group(2))
                if mm.group(3):
                    rs = int(mm.group(3))
                style = mm.group(4) or ""
            for _ in range(dup):
                # skip occupied
                while pending_rowspans.get(col, 0) > 0:
                    pending_rowspans[col] -= 1
                    col += 1
                    if col >= cols:
                        rows.append(cur); cur = []; col = 0
                cur.append((cs, rs, style or (styles[col] if col < len(styles) else ""), content))
                if rs > 1:
                    for c in range(col, col + cs):
                        pending_rowspans[c] = rs - 1
                col += cs
                if col >= cols:
                    rows.append(cur); cur = []; col = 0
                    # consume remaining rowspans at line start
                    while pending_rowspans.get(col, 0) > 0 and col < cols:
                        pending_rowspans[col] -= 1
                        col += 1
                    if col >= cols:
                        rows.append([]); col = 0
        if cur:
            rows.append(cur)
        if self.cfg.get("merge_equal_cells"):
            # spec tables repeat identical values across neighbouring models: show them as one merged cell
            for ri in range(1 if (header or implicit_header) else 0, len(rows)):
                row = rows[ri]
                if len(row) < 4:
                    continue
                merged = []
                for cell in row:
                    if merged and cell[0] == 1 and cell[1] == 1 and merged[-1][1] == 1 and merged[-1][3] == cell[3] \
                            and cell[3].strip() and len(merged) > 1 and cell[2] == merged[-1][2]:
                        prev = merged[-1]
                        merged[-1] = (prev[0] + 1, prev[1], prev[2], prev[3])
                    else:
                        merged.append(cell)
                rows[ri] = merged
        out = ["<table>"]
        if title:
            out.append("<caption>%s</caption>" % self.inline.run(title))
        for ri, row in enumerate(rows):
            is_head = ri == 0 and (header or implicit_header)
            if ri == 1 and (header or implicit_header):
                pass
            out.append("<tr>")
            for cs, rs, style, content in row:
                tag = "th" if is_head else "td"
                a = ""
                if cs > 1:
                    a += ' colspan="%d"' % cs
                if rs > 1:
                    a += ' rowspan="%d"' % rs
                if style == "a":
                    inner = self.blocks(content.strip().split("\n"), 1, 1)
                else:
                    inner = self.inline.run(" ".join(x.strip() for x in content.split("\n") if x.strip()))
                    if style == "m" and inner:
                        inner = "<code>%s</code>" % inner
                    elif style == "h" and inner:
                        inner = "<b>%s</b>" % inner
                out.append("<%s%s>%s</%s>" % (tag, a, inner, tag))
            out.append("</tr>")
        out.append("</table>")
        return "".join(out)



def main():
    cfg = json.load(open(sys.argv[1], encoding="utf-8"))
    src_dir = cfg["src"]
    out_dir = cfg["out"]
    cfg["src_dir"] = src_dir
    cfg["out_dir"] = out_dir
    names = sorted(cfg.get("names", {}).items(), key=lambda kv: -len(kv[0]))
    for sub in ("src", "images"):
        shutil.rmtree(os.path.join(out_dir, sub), ignore_errors=True)
    tmap = cfg.get("topics", {})
    split2 = set(cfg.get("split2", []))
    topics = []
    for f in cfg["files"]:
        path = os.path.join(src_dir, f["path"])
        lines = read_lines(path)
        ts = split_topics(f["path"], lines, f.get("title"))
        for t in ts:
            t.parent = None
            key = f["path"] + "|" + t.title
            if key in split2:
                for c in split_children(t):
                    topics.append(c)
            else:
                topics.append(t)
    topics_all = list(topics)
    keep = []
    seen_keys = set()
    for t in topics:
        key = t.src + "|" + (t.parent.orig + ">" if t.parent else "") + t.orig
        n_seen = sum(1 for x in seen_keys if x == key or x.startswith(key + "#"))
        if n_seen:
            key = key + "#%d" % (n_seen + 1)
        seen_keys.add(key)
        spec = tmap.get(key)
        if spec is None:
            continue
        t.key = key
        t.folder = spec["cat"]
        t.title = spec.get("title", t.title)
        keep.append(t)
    if cfg.get("dump"):
        words_of = {}
        for t in topics_all:
            words_of[t.src + "|" + (t.parent.orig + ">" if t.parent else "") + t.orig] = words(t.lines)
        for k in sorted(seen_keys):
            print("KEY	%s	%d" % (k, words_of.get(k, 0)))
    for k in tmap:
        if k not in seen_keys:
            print("WARNING: topic map key not found:", k)
    topics = keep
    used = set()
    for t in topics:
        _t = t.title
        for _k, _v in names:
            _t = _t.replace(_k, _v)
        for _k, _v in cfg.get("alt_names", {}).items():
            _t = _t.replace(_k, _v)
        slug = slugify(_t) or "topic"
        base = slug
        k = 2
        while (t.folder, slug) in used:
            slug = "%s-%d" % (base, k)
            k += 1
        used.add((t.folder, slug))
        t.slug = slug
        t.path = (lambda tt=t: os.path.join(out_dir, "src", tt.folder, tt.slug + ".html"))
    topic_of_anchor = {}
    topic_of_file = {}
    for t in topics:
        for a in t.anchors:
            topic_of_anchor[a] = t
        topic_of_file.setdefault(t.src, t)
    images = {}
    warnings = []
    manifest = []
    for t in topics:
        c = Converter(cfg, topic_of_anchor, topic_of_file, t, images, warnings, names)
        body = c.blocks(t.lines, t.level)
        h1 = c.inline.run(t.title)
        page = TEMPLATE % (html.escape(re.sub(r"<[^>]+>", "", c.alt(t.title))), h1, body)
        p = t.path()
        os.makedirs(os.path.dirname(p), exist_ok=True)
        with open(p, "w", encoding="utf-8", newline=NL) as fh:
            fh.write(page)
        manifest.append((t.folder, t.slug, t.title, t.src, words(t.lines)))
    for key, name in images.items():
        dst = os.path.join(out_dir, "images", name)
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        shutil.copyfile(os.path.join(src_dir, key), dst)
    with open(os.path.join(out_dir, "topics.tsv"), "w", encoding="utf-8") as fh:
        for row in manifest:
            fh.write(chr(9).join(str(x) for x in row) + NL)
    with open(os.path.join(out_dir, "warnings.txt"), "w", encoding="utf-8") as fh:
        fh.write(NL.join(warnings))
    print("topics: %d, images: %d, warnings: %d" % (len(topics), len(images), len(warnings)))


if __name__ == "__main__":
    main()
