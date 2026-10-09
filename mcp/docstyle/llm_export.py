"""LLM-ready output: built Flare HTML (HTML5 or Clean XHTML) → one Markdown file per topic + llms.txt + llms-full.txt.

Built output already has snippets, variables and conditions resolved, so the conversion is deterministic.
Stdlib only. Usage:  server.py --llm-export OUTPUT_DIR [--to DEST] [--url BASE_URL]
Default DEST: OUTPUT_DIR (llms.txt at the site root, Markdown under llm/), so it publishes with the site.
"""
import html
import re
from html.parser import HTMLParser
from pathlib import Path

SKIP_DIRS = {"skins", "resources", "data", "llm", "search", "fonts", "scripts", "stylesheets"}
DROP = {"script", "style", "noscript", "nav", "header", "footer", "button", "form", "svg", "iframe"}
DROP_CLASSES = ("breadcrumb", "toolbar", "sidenav", "side-menu", "search", "navigation", "nav-", "topnav", "menu", "skip", "feedback")
MAIN_IDS = ("mc-main-content", "main-content", "contentBody")


class _Md(HTMLParser):
    """Small HTML → Markdown converter for documentation topics (headings, paragraphs, lists, tables, links, code)."""

    def __init__(self, link):
        super().__init__(convert_charrefs=True)
        self.link = link
        self.out, self.title = [], ""
        self.skip = 0          # depth inside dropped elements
        self.main_depth = None  # inside the main-content element (when the page has one)
        self.depth = 0
        self.lists = []         # stack of [kind, counter]
        self.table = None       # rows of cells while inside a table
        self.cell = None
        self.href = None
        self.pre = False
        self.in_title = False
        self.has_main = False

    def handle_starttag(self, tag, attrs):
        a = dict(attrs)
        self.depth += 1
        cls = (a.get("class") or "").lower()
        if self.skip or tag in DROP or any(c in cls for c in DROP_CLASSES) or a.get("role") in ("navigation", "search", "banner"):
            self.skip += 1
            return
        if a.get("id") in MAIN_IDS or a.get("role") == "main" or tag == "main":
            self.has_main = True
            if self.main_depth is None:
                self.main_depth = self.depth
        if tag == "title":
            self.in_title = True
        elif tag in ("h1", "h2", "h3", "h4", "h5", "h6"):
            self._block("#" * int(tag[1]) + " ")
        elif tag in ("p", "div", "dt", "blockquote"):
            self._block("> " if tag == "blockquote" else "")
        elif tag == "dd":
            self._block(": ")
        elif tag in ("ul", "ol"):
            self.lists.append([tag, 0])
        elif tag == "li":
            kind = self.lists[-1] if self.lists else ["ul", 0]
            kind[1] += 1
            indent = "   " * (len(self.lists) - 1)
            self._block(indent + (f"{kind[1]}. " if kind[0] == "ol" else "- "))
        elif tag == "br":
            self._emit("  \n")
        elif tag in ("b", "strong"):
            self._emit("**")
        elif tag in ("i", "em"):
            self._emit("*")
        elif tag == "code" and not self.pre:
            self._emit("`")
        elif tag == "pre":
            self.pre = True
            self._block("```\n")
        elif tag == "a":
            self.href = self.link(a.get("href") or "")
            self._emit("[")
        elif tag == "img":
            alt = (a.get("alt") or "").strip()
            if alt:
                self._emit(f"![{alt}]({a.get('src', '')})")
        elif tag == "table":
            self.table = []
        elif tag == "tr" and self.table is not None:
            self.table.append([])
        elif tag in ("td", "th") and self.table is not None:
            self.cell = []

    def handle_endtag(self, tag):
        self.depth -= 1
        if self.skip:
            self.skip -= 1
            return
        if self.main_depth is not None and self.depth < self.main_depth:
            self.main_depth = -1   # left the main element: ignore the rest of the page chrome
        if tag == "title":
            self.in_title = False
        elif tag in ("ul", "ol") and self.lists:
            self.lists.pop()
            self._block("")
        elif tag in ("b", "strong"):
            self._emit("**")
        elif tag in ("i", "em"):
            self._emit("*")
        elif tag == "code" and not self.pre:
            self._emit("`")
        elif tag == "pre":
            self._emit("\n```")
            self.pre = False
            self._block("")
        elif tag == "a":
            self._emit(f"]({self.href})" if self.href else "]")
            self.href = None
        elif tag in ("td", "th") and self.table is not None and self.cell is not None:
            self.table[-1].append(re.sub(r"\s+", " ", "".join(self.cell)).strip().replace("|", "\\|"))
            self.cell = None
        elif tag == "table" and self.table is not None:
            rows = [r for r in self.table if r]
            self.table = None
            if rows:
                width = max(len(r) for r in rows)
                rows = [r + [""] * (width - len(r)) for r in rows]
                lines = ["| " + " | ".join(rows[0]) + " |", "|" + "---|" * width] + ["| " + " | ".join(r) + " |" for r in rows[1:]]
                self._block("\n".join(lines))
                self._block("")
        elif tag in ("p", "div", "h1", "h2", "h3", "h4", "h5", "h6", "li", "dt", "dd", "blockquote"):
            self._block("")

    def handle_data(self, data):
        if self.in_title:
            self.title += data
            return
        if self.skip:
            return
        self._emit(data if self.pre else re.sub(r"\s+", " ", data))

    def _active(self):
        return not self.has_main or (self.main_depth is not None and self.main_depth > 0)

    def _emit(self, s):
        if not self._active():
            return
        if self.cell is not None:
            self.cell.append(s)
        else:
            self.out.append(s)

    def _block(self, prefix):
        if not self._active() or self.cell is not None:
            return
        self.out.append("\n\n" + prefix)

    def markdown(self):
        text = "".join(self.out)
        text = re.sub(r"[ \t]+\n", "\n", text)
        text = re.sub(r"\n{3,}", "\n\n", text)
        text = re.sub(r"\[\s*\]\([^)]*\)", "", text)      # empty links (icons)
        return text.strip() + "\n"


def _topics(out):
    for p in sorted(out.rglob("*.htm*")):
        rel = p.relative_to(out)
        if any(part.lower() in SKIP_DIRS for part in rel.parts[:-1]):
            continue
        if p.suffix.lower() not in (".htm", ".html") or p.name.lower() in ("default.htm", "index.htm", "index.html", "search.htm"):
            continue
        yield p, rel


def export(output_dir, dest=None, base_url=None, name=None):
    out = Path(output_dir)
    dest = Path(dest) if dest else out
    md_root = dest / "llm"
    pages = []
    for path, rel in _topics(out):
        def link(href, rel=rel):
            if not href or href.startswith(("#", "mailto:", "javascript:")) or re.match(r"^[a-z]+://", href):
                return href
            target = href.split("#")[0]
            return re.sub(r"\.html?$", ".md", target) if target else href

        try:
            raw = path.read_text(encoding="utf-8", errors="replace")
        except OSError:
            continue
        conv = _Md(link)
        conv.feed(raw)
        body = conv.markdown()
        if len(body.strip()) < 20:
            continue
        title = html.unescape(conv.title).strip() or rel.stem
        md_rel = rel.with_suffix(".md")
        url = (base_url.rstrip("/") + "/" + rel.as_posix()) if base_url else None
        front = "---\ntitle: " + title.replace("\n", " ") + "\nsource: " + rel.as_posix() + ("\nurl: " + url if url else "") + "\n---\n\n"
        target = md_root / md_rel
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(front + body, encoding="utf-8")
        first = next((l for l in body.splitlines() if l and not l.startswith(("#", "|", "-", "!", ">")) and len(l) > 30), "")
        pages.append({"title": title, "md": ("llm/" + md_rel.as_posix()), "url": url, "summary": first[:160], "body": body})

    project = next((d for d in out.resolve().parents if any(d.glob("*.flprj"))), None)
    site = name or ((project.name + " — " + out.name) if project else out.name)
    lines = ["# " + site, "", "> Product documentation from MadCap Flare, as Markdown for LLMs and AI assistants.", "", "## Topics", ""]
    for p in pages:
        lines.append(f"- [{p['title']}]({p['md']})" + (f": {p['summary']}" if p["summary"] else ""))
    (dest / "llms.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
    full = [f"# {site}\n"] + [f"\n\n---\n\n<!-- {p['md']} -->\n\n" + p["body"] for p in pages]
    (dest / "llms-full.txt").write_text("".join(full), encoding="utf-8")
    return {"topics": len(pages), "llms_txt": str(dest / "llms.txt"), "markdown": str(md_root)}
