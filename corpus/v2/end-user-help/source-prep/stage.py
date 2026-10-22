#!/usr/bin/env python3
"""Stage the end-user-help corpus project source (HTML) from the CC BY 3.0 Nextcloud user manual.

Usage: build_help_stage.py <nextcloud-checkout> <stage-dir>

Reads user_manual/**/*.rst with docutils (Sphinx roles and directives re-implemented minimally), splits large pages into
topics, replaces the product and vendor names with a fictional product, replaces screenshots with generated placeholder
images (alt text kept as authored), appends the shared footer and the markers that `enrich_help.py` turns into snippets
and conditions. Deterministic.
"""
import os, re, sys, html, struct, zlib, posixpath, hashlib, copy
from docutils import nodes, utils
from docutils.core import publish_doctree, publish_from_doctree
from docutils.parsers.rst import roles, directives, Directive
from docutils.parsers.rst.directives import images as dimages

SRC = sys.argv[1]
OUT = sys.argv[2]
UM = os.path.join(SRC, "user_manual")
PRODUCT = "Acme Hub"
os.makedirs(OUT, exist_ok=True)

DROP_FILES = {"contents.rst", "index.rst_"}  # top-level toctree page is rebuilt as the landing page
DROP_PAGES = {"talk/matterbridge.rst", "groupware/sync_thunderbird.rst", "groupware/sync_gnome.rst", "groupware/sync_kde.rst",
              "collectives/markdown_files.rst"}  # pages about other vendors' products


# ---------------------------------------------------------------------------------------------- roles and directives
def role_ui(name):
    def _r(role, rawtext, text, lineno, inliner, options=None, content=None):
        return [nodes.inline(rawtext, utils.unescape(text), classes=["guilabel"])], []
    return _r


def role_menu(role, rawtext, text, lineno, inliner, options=None, content=None):
    parts = [p.strip() for p in utils.unescape(text).split("-->")]
    return [nodes.inline(rawtext, " > ".join(parts), classes=["guilabel"])], []


def role_kbd(role, rawtext, text, lineno, inliner, options=None, content=None):
    return [nodes.inline(rawtext, utils.unescape(text), classes=["kbd"])], []


def role_code(role, rawtext, text, lineno, inliner, options=None, content=None):
    return [nodes.literal(rawtext, utils.unescape(text))], []


def split_title(text):
    m = re.match(r"^(.*?)\s*<([^<>]+)>$", utils.unescape(text), re.S)
    return (m.group(1), m.group(2)) if m else (None, utils.unescape(text))


def role_ref(role, rawtext, text, lineno, inliner, options=None, content=None):
    title, target = split_title(text)
    n = nodes.reference(rawtext, title or "", refuri="@@ref:" + target.strip().lower())
    n["needs_title"] = title is None
    return [n], []


def role_doc(role, rawtext, text, lineno, inliner, options=None, content=None):
    title, target = split_title(text)
    n = nodes.reference(rawtext, title or "", refuri="@@doc:" + target.strip())
    n["needs_title"] = title is None
    return [n], []


for r in ("guilabel", "menuselection_"):
    roles.register_local_role(r, role_ui(r))
roles.register_local_role("menuselection", role_menu)
roles.register_local_role("kbd", role_kbd)
for r in ("file", "command", "samp", "envvar", "program", "option", "mimetype", "manpage", "regexp", "makevar", "dfn"):
    roles.register_local_role(r, role_code)
roles.register_local_role("ref", role_ref)
roles.register_local_role("doc", role_doc)
roles.register_local_role("term", role_code)
roles.register_local_role("abbr", role_code)


class Versioned(Directive):
    has_content = True
    required_arguments = 1
    optional_arguments = 1
    final_argument_whitespace = True
    label = "New in version"

    def run(self):
        text = "%s %s" % (self.label, self.arguments[0])
        p = nodes.paragraph()
        p += nodes.strong(text=text + ":")
        p += nodes.Text(" ")
        if self.content:
            inner = nodes.paragraph()
            self.state.nested_parse(self.content, self.content_offset, inner)
            for c in inner.children:
                for cc in c.children:
                    p += cc.deepcopy() if hasattr(cc, "deepcopy") else cc
        return [p]


class VersionAdded(Versioned):
    label = "New in version"


class VersionChanged(Versioned):
    label = "Changed in version"


class Noop(Directive):
    has_content = True
    optional_arguments = 10
    final_argument_whitespace = True
    option_spec = None

    def run(self):
        return []


class Toctree(Directive):
    has_content = True
    optional_arguments = 1
    final_argument_whitespace = True
    option_spec = {"maxdepth": directives.unchanged, "hidden": directives.flag, "caption": directives.unchanged, "titlesonly": directives.flag,
                   "glob": directives.flag, "numbered": directives.unchanged, "name": directives.unchanged}

    def run(self):
        n = nodes.container(classes=["toctree-stub"])
        n["entries"] = [l.strip() for l in self.content if l.strip() and not l.strip().startswith(":")]
        return [n]


class CodeBlock(Directive):
    has_content = True
    optional_arguments = 1
    final_argument_whitespace = True
    option_spec = {"linenos": directives.flag, "caption": directives.unchanged, "emphasize-lines": directives.unchanged, "name": directives.unchanged,
                   "dedent": directives.unchanged, "force": directives.flag, "lineno-start": directives.unchanged}

    def run(self):
        lit = nodes.literal_block("\n".join(self.content), "\n".join(self.content))
        if self.arguments:
            lit["classes"].append("language-" + re.sub(r"[^a-z0-9]", "", self.arguments[0].lower()))
        return [lit]


class OnlyDir(Directive):
    has_content = True
    required_arguments = 1
    final_argument_whitespace = True

    def run(self):
        c = nodes.container()
        self.state.nested_parse(self.content, self.content_offset, c)
        return c.children


directives.register_directive("versionadded", VersionAdded)
directives.register_directive("versionchanged", VersionChanged)
directives.register_directive("deprecated", VersionChanged)
directives.register_directive("toctree", Toctree)
directives.register_directive("code-block", CodeBlock)
directives.register_directive("sourcecode", CodeBlock)
directives.register_directive("only", OnlyDir)
for d in ("raw", "contents", "highlight", "index", "meta", "sectionauthor", "tabularcolumns", "cssclass", "tab", "tabs"):
    directives.register_directive(d, Noop)


# ---------------------------------------------------------------------------------------------- source scrubbing
THIRD = [
    (r"\bNextcloud Talk\b", PRODUCT + " Talk"), (r"\bNextcloud\b", PRODUCT), (r"\bnextcloud\b", "acmehub"), (r"\bNEXTCLOUD\b", "ACMEHUB"),
    (r"\bownCloud\b", "another file service"), (r"\bCollabora( Online)?\b", "the document server"), (r"\bOnlyOffice\b", "the document server"),
    (r"\bGoogle\b", "a web provider"), (r"\bFacebook\b", "a social network"), (r"\bTwitter\b", "a social network"),
    (r"\bGitHub\b", "a code host"), (r"\bF-Droid\b", "an app repository"), (r"\bMatterbridge\b", "a bridge"),
    (r"\bSlack\b", "another chat service"), (r"\bTelegram\b", "another chat service"), (r"\bWhatsApp\b", "another chat service"),
    (r"\bZoom\b", "a meeting service"), (r"\bMicrosoft\b", "a vendor"), (r"\bApple\b", "a vendor"), (r"\bOutlook\b", "a mail client"),
    (r"\bThunderbird\b", "a mail client"), (r"\bGmail\b", "a web mail service"), (r"\bYahoo\b", "a web mail service"),
    (r"\bDropbox\b", "another file service"), (r"\bAmazon( S3)?\b", "object storage"), (r"\bS3\b", "object storage"),
    (r"\bTransifex\b", "a translation service"), (r"\bEtherpad\b", "a shared editor"), (r"\bCODE\b", "the document server"),
    (r"\bFirefox\b", "a browser"), (r"\bChrome\b", "a browser"), (r"\bSafari\b", "a browser"), (r"\bEdge\b", "a browser"),
    (r"\bKeePass\w*\b", "a password manager"), (r"\bBitwarden\b", "a password manager"), (r"\bYubiKey\b", "a hardware key"),
    (r"\bGPG\b", "key"), (r"\bPGP\b", "key"), (r"\bOpenStreetMap\b", "a map service"), (r"\bGnome\b|\bGNOME\b", "the desktop"), (r"\bKDE\b", "the desktop"),
    (r"\bKarlitschek\b", "Smith"), (r"OWNCLOUD", "ACMEHUB"), (r"[Oo]wn[Cc]loud", "acmehub"), (r"\bDAVx\S*", "a sync app"), (r"\bOpenProject\b", "a project tool"),
    (r"\bWinSCP\b", "a file transfer tool"), (r"\bCyberduck\b", "a file transfer tool"), (r"\bPlay Store\b", "app store"), (r"\bUbuntu\b", "Linux"),
    (r"\bAndrew\b", "Alex"), (r"\bNitrokey\b", "a hardware key vendor"), (r"\bLet's Encrypt\b", "a certificate authority"), (r"\bReddit\b", "a forum"),
    (r"\bLibreOffice\b", "an office suite"), (r"\bWine\b", "a compatibility layer"), (r"\bRoundcube\b", "a web mail client"),
]


def scrub(s):
    s = re.sub(r"https?://[^\s<>\"')\]`]+", lambda m: "https://example.com/" if False else m.group(0), s)
    for pat, rep in THIRD:
        s = re.sub(pat, rep, s)
    return s


def slug(s):
    return re.sub(r"[^a-z0-9]+", "-", s.lower()).strip("-")[:70].strip("-") or "topic"


# ---------------------------------------------------------------------------------------------- pass 1: parse all pages
settings = {"report_level": 5, "halt_level": 5, "warning_stream": open(os.devnull, "w"), "file_insertion_enabled": True,
            "doctitle_xform": False, "sectsubtitle_xform": False, "syntax_highlight": "none", "input_encoding": "utf-8"}

pages = []  # dict(rst=relpath, dir=, stem=, doc=)
for dp, dn, fn in os.walk(UM):
    dn[:] = sorted(d for d in dn if d not in ("images", "_templates", "locale"))
    for f in sorted(fn):
        if not f.endswith(".rst"):
            continue
        full = os.path.join(dp, f)
        rel = os.path.relpath(full, UM).replace("\\", "/")
        if rel in DROP_PAGES or rel == "contents.rst":
            continue
        text = open(full, encoding="utf-8").read()
        text = re.sub(r"\s*\|version\|", "", text)
        text = re.sub(r"\|release\|", "", text)
        text = scrub(text)
        doc = publish_doctree(text, source_path=full, settings_overrides=settings)
        d = posixpath.dirname(rel) or "general"
        pages.append(dict(rst=rel, dir=d, stem=posixpath.splitext(posixpath.basename(rel))[0], doc=doc, full=full))
print("parsed", len(pages), "pages")


def top_sections(doc):
    """The document's top-level section and its children."""
    secs = [c for c in doc.children if isinstance(c, nodes.section)]
    pre = [c for c in doc.children if not isinstance(c, nodes.section)]
    return pre, secs


# ---------------------------------------------------------------------------------------------- topic plan
class Topic:
    pass


topics = []
label_map = {}  # label -> topic path (without extension, "section/name")
doc_map = {}    # rst rel without extension -> topic path


def title_of(sec):
    t = sec.next_node(nodes.title)
    return t.astext() if t else "Untitled"


MIN_SPLIT = int(os.environ.get("MIN_SPLIT", "3200"))
for pg in pages:
    pre, secs = top_sections(pg["doc"])
    if not secs:
        continue
    root_sec = secs[0]
    subs = [c for c in root_sec.children if isinstance(c, nodes.section)]
    head = [c for c in root_sec.children if not isinstance(c, nodes.section)]
    size = len(root_sec.astext())
    is_index = pg["stem"] in ("index", "contents") or any(isinstance(n, nodes.container) and "toctree-stub" in n["classes"] for n in root_sec.traverse())
    base = pg["stem"] if pg["stem"] != "index" else "overview"
    unit_path = "%s/%s" % (pg["dir"], slug(base))
    doc_map[posixpath.splitext(pg["rst"])[0]] = unit_path
    if pg["stem"] == "index":
        doc_map[pg["dir"] + "/index" if pg["dir"] != "general" else "index"] = unit_path
    split = len(subs) >= 2 and size > MIN_SPLIT and not is_index and pg["stem"] != "faq"
    pg["toc"] = []
    if not split:
        t = Topic()
        t.path, t.title, t.nodes, t.page, t.kind = unit_path, title_of(root_sec), [root_sec], pg, "task"
        topics.append(t)
        for n in root_sec.traverse(nodes.Element):
            for nm in n.get("names", []):
                label_map.setdefault(nm.lower(), unit_path)
    else:
        # overview topic: the title, the lead-in and a link list; then one topic per sub-section
        ov = Topic()
        ov.path, ov.title, ov.page, ov.kind = unit_path, title_of(root_sec), pg, "overview"
        lead = nodes.section(ids=root_sec["ids"], names=root_sec["names"])
        lead += root_sec.children[0].deepcopy()
        for c in head[1:] if head and isinstance(head[0], nodes.title) else head:
            lead += c.deepcopy()
        ov.nodes = [lead]
        ov.children = []
        topics.append(ov)
        for nm in root_sec.get("names", []):
            label_map.setdefault(nm.lower(), unit_path)
        used = set()
        for s in subs:
            t = Topic()
            sl = slug(title_of(s))
            sl = sl[:50].strip("-") or "section"
            name = "%s-%s" % (slug(base), sl)
            k = 2
            while (pg["dir"] + "/" + name) in used:
                name = "%s-%s-%d" % (slug(base), sl, k)
                k += 1
            t.path = pg["dir"] + "/" + name
            used.add(t.path)
            t.title, t.nodes, t.page, t.kind = title_of(s), [s], pg, "task"
            topics.append(t)
            ov.children.append(t)
            for n in s.traverse(nodes.Element):
                for nm in n.get("names", []):
                    label_map.setdefault(nm.lower(), t.path)

# FAQ / troubleshooting flags
for t in topics:
    if re.search(r"(^|/)faq", t.path):
        t.kind = "faq"
    elif re.search(r"troubleshoot|problem|known-issues|advanced-configuration", t.path + " " + t.title.lower()):
        t.kind = "troubleshooting"

# landing page from the top-level index
index_pg = [p for p in pages if p["rst"] == "index.rst"]
sections_order = ["general", "files", "desktop", "talk", "groupware", "collectives"]


# ---------------------------------------------------------------------------------------------- images
IMG_DIR = os.path.join(OUT, "images")
os.makedirs(IMG_DIR, exist_ok=True)
img_seen = {}


def png_size(path):
    with open(path, "rb") as f:
        head = f.read(32)
    if head[:8] != b"\x89PNG\r\n\x1a\n":
        return None
    return struct.unpack(">II", head[16:24])


def placeholder(path, w, h):
    w, h = max(160, w // 4), max(90, h // 4)
    w, h = min(w, 480), min(h, 300)
    shade = 225 + (int(hashlib.md5(os.path.basename(path).encode()).hexdigest()[:2], 16) % 24)
    rows = []
    for y in range(h):
        row = bytearray([0])
        for x in range(w):
            edge = x < 2 or y < 2 or x >= w - 2 or y >= h - 2
            v = 150 if edge else shade
            row += bytes((v, v, v))
        rows.append(bytes(row))
    raw = b"".join(rows)

    def chunk(t, d):
        c = struct.pack(">I", len(d)) + t + d
        return c + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(png)


def image_for(rst_full, uri):
    src = os.path.normpath(os.path.join(os.path.dirname(rst_full), uri))
    key = os.path.normpath(src)
    if key in img_seen:
        return img_seen[key]
    name = slug(os.path.splitext(os.path.basename(src))[0]) + ".png"
    k = 2
    while os.path.exists(os.path.join(IMG_DIR, name)) and img_seen.get(("name", name)) != key:
        name = slug(os.path.splitext(os.path.basename(src))[0]) + "-%d.png" % k
        k += 1
    sz = png_size(src) if os.path.exists(src) else None
    placeholder(os.path.join(IMG_DIR, name), *(sz or (640, 360)))
    img_seen[key] = name
    img_seen[("name", name)] = key
    return name


# ---------------------------------------------------------------------------------------------- render
def relpath(frm, to):
    return posixpath.relpath(to + ".html", posixpath.dirname(frm + ".html"))


def render_unit(t):
    doc = utils.new_document("unit", __import__("docutils.frontend", fromlist=["x"]).OptionParser(components=()).get_default_values())
    for n in t.nodes:
        doc += n.deepcopy()
    # resolve custom references and images
    for ref in list(doc.traverse(nodes.reference)):
        uri = ref.get("refuri", "")
        if uri.startswith("@@ref:"):
            lab = uri[6:]
            dest = label_map.get(lab)
            if dest and dest != t.path:
                if ref.get("needs_title") or not ref.astext():
                    ref.clear()
                    ref += nodes.Text(nice_title(dest))
                ref["refuri"] = "TOPICMARK/" + dest
            else:
                if ref.get("needs_title") or not ref.astext():
                    ref.replace_self(nodes.Text(nice_title(dest) if dest else lab))
                else:
                    ref.replace_self(nodes.Text(ref.astext()))
        elif uri.startswith("@@doc:"):
            target = uri[6:]
            base = posixpath.normpath(posixpath.join(posixpath.dirname(t.page["rst"]), target)) if not target.startswith("/") else target.lstrip("/")
            dest = doc_map.get(base) or doc_map.get(base + "/index")
            if dest and dest != t.path:
                if ref.get("needs_title") or not ref.astext():
                    ref.clear()
                    ref += nodes.Text(nice_title(dest))
                ref["refuri"] = "TOPICMARK/" + dest
            else:
                ref.replace_self(nodes.Text(ref.astext() or (nice_title(dest) if dest else target)))
    for fig in doc.traverse(nodes.image):
        fig["uri"] = "IMGMARK/" + image_for(t.page["full"], fig["uri"])
    for tg in list(doc.traverse(nodes.container)):
        if "toctree-stub" in tg["classes"]:
            tg.parent.remove(tg)
    out = publish_from_doctree(doc, writer_name="html5", settings_overrides={"initial_header_level": 1, "embed_stylesheet": False, "stylesheet_path": None,
                                                                               "warning_stream": open(os.devnull, "w"), "report_level": 5,
                                                                               "syntax_highlight": "none", "math_output": "html", "file_insertion_enabled": False})
    body = out.decode("utf-8")
    body = body[body.index("<body>") + 6:body.rindex("</body>")] if "<body>" in body else body
    return body


TITLES = {}


def nice_title(path):
    for t in topics:
        if t.path == path:
            return t.title
    return path


def postprocess(t, body):
    # images: drop the alt docutils invents from the file name
    def img(m):
        tag = m.group(0)
        mm = re.search(r'alt="([^"]*)"', tag)
        if mm and (re.search(r"\.(png|jpe?g|gif|svg)$", mm.group(1), re.I) or mm.group(1).startswith("IMGMARK/")):
            tag = tag.replace(mm.group(0), "")
        tag = re.sub(r'\s+(width|height|style)="[^"]*"', "", tag)
        return tag
    body = re.sub(r"<img\b[^>]*>", img, body)
    body = re.sub(r'src="IMGMARK/([^"]+)"', lambda m: 'src="%s"' % posixpath.relpath("images/" + m.group(1), posixpath.dirname(t.path)), body)
    body = re.sub(r'href="TOPICMARK/([^"]+)"', lambda m: 'href="%s"' % relpath(t.path, m.group(1)).replace(".html", ".md_") .replace(".md_", ".html"), body)
    body = re.sub(r'<span class="guilabel">', '<span class="guilabel">', body)
    body = re.sub(r'<span class="kbd">(.*?)</span>', r"<kbd>\1</kbd>", body, flags=re.S)
    body = re.sub(r"<(/?)tt\b", r"<\1code", body)
    return body


def wrap_html(title, body):
    return "<!DOCTYPE html>\n<html><head><meta charset=\"utf-8\"><title>%s</title></head><body>\n%s\n</body></html>\n" % (html.escape(title), body)


ALLOW = ("example.com", "cloud.example.com", "docs.example.com")
KEEP_HOSTS = ("acmehub.com", "docs.acmehub.com", "apps.acmehub.com", "your-acmehub-domain.com", "mycloud.com", "cloud.example.com", "example.com")


def final_html_scrub(body):
    def anchor(m):
        host = m.group(2)
        if host in KEEP_HOSTS or host.endswith(".acmehub.com") or host.endswith("example.com"):
            return m.group(0)
        return m.group(3)
    body = re.sub(r'<a([^>]*?)href="https?://([^"/]+)[^"]*"[^>]*>(.*?)</a>', anchor, body, flags=re.S)

    def host(m):
        h = m.group(2)
        if h.endswith("example.com"):
            return m.group(0)
        return m.group(1) + ("docs.example.com" if "acmehub" in h or h == "mycloud.com" else "example.com")
    body = re.sub(r"(https?://)([A-Za-z0-9.-]+[A-Za-z0-9])", host, body)
    return body


FOOTER = '<p class="footer">Need more help? Ask your %s administrator, or select <b>Help</b> in the app to open the support page.</p>' % PRODUCT
ADMIN_SENTENCE = "Your server administrator can turn this feature off."
NOTE_SHARE = '<div class="admonition note"><p class="admonition-title">Note</p><p>The sharing options you see depend on the permissions that your administrator sets for your account.</p></div>'
NOTE_RESTART = '<div class="admonition note"><p class="admonition-title">Note</p><p>Restart the desktop client after you change this setting, so that the client reads the new value.</p></div>'
PRINT_NOTE = '<p>[[print]] This page is part of the printed guide. The online help always has the latest version of these steps.</p>'
SCREEN_NOTE = '<p>[[screen]] Select the video link in the online help to watch these steps in a short clip.</p>'


def bucket(s, n):
    return int(hashlib.md5(s.encode()).hexdigest()[:8], 16) % n


def faq_to_dropdowns(body):
    """Turn each h2 section of a FAQ page into a drop-down: question as the head, answer as the body."""
    parts = re.split(r"(?=<section id=)", body)
    return body


count = 0
os.makedirs(os.path.join(OUT, "x"), exist_ok=True)
os.rmdir(os.path.join(OUT, "x"))
for t in topics:
    body = render_unit(t)
    body = postprocess(t, body)
    # overview: list the child topics
    if getattr(t, "children", None):
        items = "".join('<li><a href="%s">%s</a></li>' % (relpath(t.path, c.path), html.escape(c.title)) for c in t.children)
        body += "\n<p>This page covers the following topics:</p>\n<ul>%s</ul>" % items
    # sections become plain headings: strip section wrappers
    body = re.sub(r"</?section\b[^>]*>", "", body)
    # FAQ pages: each h2 and what follows it up to the next h2 becomes a drop-down
    if t.kind == "faq":
        chunks = re.split(r"(?=<h2\b)", body)
        pre, rest = chunks[0], chunks[1:]
        dd = []
        for c in rest:
            m = re.match(r"<h2[^>]*>(.*?)</h2>(.*)$", c, re.S)
            if not m:
                dd.append(c)
                continue
            dd.append("<details><summary>%s</summary>%s</details>" % (re.sub(r"<[^>]+>", "", m.group(1)), m.group(2)))
        body = pre + "\n".join(dd)
    # kinds of mixed authorship: ~40% of pages mark UI labels with the style class instead of bold
    h = bucket(t.path, 100)
    if h < 40:
        body = re.sub(r"<strong>([^<]{1,40})</strong>", lambda m: '<span class="guilabel">%s</span>' % m.group(1) if re.match(r"^[A-Z]", m.group(1)) else m.group(0), body)
    # markers for snippets and conditions
    if bucket(t.path + "admin", 12) == 0 and t.kind == "task":
        body = re.sub(r"(</p>)", " " + ADMIN_SENTENCE + r"\1", body, count=1) if "<p>" in body else body
    if bucket(t.path + "print", 15) == 0:
        body += "\n" + PRINT_NOTE
    if bucket(t.path + "screen", 14) == 0:
        body += "\n" + SCREEN_NOTE
    if "shar" in t.path and t.kind == "task":
        body += "\n" + NOTE_SHARE
    if t.path.startswith("desktop/") and t.kind == "task":
        body += "\n" + NOTE_RESTART
    body = re.sub(r"</?main\b[^>]*>", "", body)
    body += "\n" + FOOTER
    body = final_html_scrub(scrub(body))
    path = os.path.join(OUT, t.path + ".html")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(wrap_html(t.title, body))
    count += 1

# landing page
land = ["<h1>%s help</h1>" % PRODUCT,
        "<p>%s helps you store, share and discuss files and messages with your team. This help covers the web app, the desktop client and the chat and calendar tools.</p>" % PRODUCT,
        "<p>Pick a part of the help to start with:</p>", "<ul>"]
for d in sections_order:
    first = [t for t in topics if t.path.startswith(d + "/") and t.kind == "overview"] or [t for t in topics if t.path.startswith(d + "/")]
    if first:
        land.append('<li><a href="%s">%s</a></li>' % (relpath("about", first[0].path), html.escape(first[0].title)))
land.append("</ul>")
land.append(FOOTER)
with open(os.path.join(OUT, "about.html"), "w", encoding="utf-8", newline="\n") as f:
    f.write(wrap_html(PRODUCT + " help", scrub("\n".join(land))))
print("staged", count + 1, "topics;", len(os.listdir(IMG_DIR)), "placeholder images")
