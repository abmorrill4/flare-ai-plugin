# /// script
# requires-python = ">=3.10"
# dependencies = ["mcp>=1.2,<2", "vale>=3.9"]
# ///
"""docstyle: layered style guide + linter for technical docs, as an MCP stdio server.

Layers, applied in order (later wins on conflict):
  1. google   - Google developer documentation style guide (built in: layers/google)
  2. --layer DIR ...  - any number of layer folders (default: layers/company)
  3. project  - optional, generated per call from a MadCap Flare project (variables, glossary,
                Content/Resources/StyleGuide/ if present)

A layer folder holds any of:
  guide.md        prose rules (fed to the AI and to style_lookup)
  rules/*.yml     Vale rules (deterministic lint with exact fixes)
  accept.txt      words the spell checker must accept, one per line
  glossary.json   {"terms": [{"term", "abbr", "variants", "note"}]}
  overrides.ini   extra Vale lines for the [*] section, e.g. "Google.Will = NO"

Run:   uv run --script server.py [--layer DIR ...]
Test:  uv run --script server.py --selftest
"""
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
from html.parser import HTMLParser
from pathlib import Path

HERE = Path(__file__).resolve().parent
BUILTIN = HERE / "layers"
SEVERITY = {"error": 0, "warning": 1, "suggestion": 2}
SENTENCE_START = re.compile(r"(^|[.!?:]\s+)$")
CACHE = Path(os.environ.get("LOCALAPPDATA") or tempfile.gettempdir()) / "docstyle" / "cache"


# ---- layers --------------------------------------------------------------------------------------

class Layer:
    def __init__(self, name, path=None, guide="", rules=None, accept=(), glossary=(), overrides=()):
        self.name, self.path, self.guide = name, path, guide
        self.rules = rules or {}          # file name -> yml text
        self.accept, self.glossary, self.overrides = list(accept), list(glossary), list(overrides)
        self.variables = []

    @staticmethod
    def from_dir(path, name=None):
        path = Path(path)
        read = lambda p: p.read_text(encoding="utf-8") if p.is_file() else ""
        rules_dir = path / "rules"
        rules = {p.name: read(p) for p in sorted(rules_dir.glob("*.yml"))} if rules_dir.is_dir() else {}
        gl = json.loads(read(path / "glossary.json") or '{"terms": []}')["terms"]
        return Layer(name or path.name, path, read(path / "guide.md"), rules,
                     [w.strip() for w in read(path / "accept.txt").splitlines() if w.strip()],
                     gl, [l.strip() for l in read(path / "overrides.ini").splitlines() if l.strip() and not l.startswith("#")])

    def stamp(self):
        """Changes whenever the layer's content changes (cache key)."""
        return hashlib.sha1(json.dumps([self.guide, self.rules, self.accept, self.glossary, self.overrides],
                                       sort_keys=True).encode()).hexdigest()


def style_name(layer_name, taken):
    base = re.sub(r"[^A-Za-z0-9]", "", layer_name.title()) or "Layer"
    name, i = base, 2
    while name in taken:
        name, i = f"{base}{i}", i + 1
    taken.add(name)
    return name


# ---- project layer (MadCap Flare) --------------------------------------------------------------------

class _Text(HTMLParser):
    def __init__(self):
        super().__init__()
        self.out = []

    def handle_starttag(self, tag, attrs):
        if tag in ("p", "li", "h1", "h2", "h3", "h4", "br", "tr"):
            self.out.append("\n" + ("## " if tag in ("h1", "h2") else "- " if tag == "li" else ""))

    def handle_data(self, data):
        self.out.append(data)


def flare_project_root(path):
    """Folder containing the .flprj for any path inside a Flare project, or None."""
    p = Path(path).resolve()
    for d in [p] + list(p.parents):
        if d.is_dir() and any(d.glob("*.flprj")):
            return d
    return None


def project_layer(root: Path):
    """Variables, glossary and an optional Content/Resources/StyleGuide/ folder, as one layer."""
    variables, terms = [], []
    for f in sorted((root / "Project" / "VariableSets").glob("*.flvar")):
        try:
            for v in ET.parse(f).getroot().iter("Variable"):
                value = (v.text or "").strip()
                if len(value) >= 3 and re.search(r"[A-Za-z]", value):
                    variables.append({"name": f"{f.stem}.{v.get('Name')}", "value": value})
        except ET.ParseError:
            pass
    for f in sorted((root / "Project" / "Glossaries").glob("*.flglo")):
        try:
            for e in ET.parse(f).getroot().iter("GlossaryEntry"):
                d = e.find("Definition")
                definition = "".join(d.itertext()).strip() if d is not None else ""
                for t in e.iter("Term"):
                    if (t.text or "").strip():
                        terms.append({"term": t.text.strip(), "note": definition, "abbr": None, "variants": []})
        except ET.ParseError:
            pass

    # No vocabulary entries from variables or glossary: Vale treats accepted words as exceptions in every
    # rule, which would silence the very term checks generated here. Spelling stays Flare's job.
    rules, accept = {}, []
    if variables:
        tokens = "\n".join(f"  - '{re.escape(v['value'])}'" for v in variables)
        rules["Variables.yml"] = ("extends: existence\nmessage: \"'%s' is a project variable: insert the variable instead of typing it.\"\n"
                                  "level: warning\nignorecase: false\nnonword: true\ntokens:\n" + tokens + "\n")
    if terms:
        swaps = {}
        for t in terms:
            c = t["term"]
            for variant in {c.title(), c.upper(), c.lower()} - {c}:
                # Skip variants that differ only by a capital first letter: that is just a sentence start.
                if len(variant) > 3 and variant[:1].upper() + variant[1:] != c[:1].upper() + c[1:]:
                    swaps[variant] = c
        if swaps:
            rules["Terms.yml"] = ("extends: substitution\nmessage: \"Glossary term: use '%s' instead of '%s'.\"\nlevel: warning\n"
                                  "ignorecase: false\naction:\n  name: replace\nswap:\n" +
                                  "".join(f"  '{re.escape(k)}': '{v}'\n" for k, v in sorted(swaps.items())))

    guide = ""
    sg = root / "Content" / "Resources" / "StyleGuide"
    if sg.is_dir():
        inner = Layer.from_dir(sg)
        rules.update({"Guide" + k: v for k, v in inner.rules.items()})
        accept += inner.accept
        guide = inner.guide
        for topic in sorted(sg.glob("*.htm")):
            p = _Text()
            p.feed(topic.read_text(encoding="utf-8", errors="replace"))
            guide += "\n\n" + re.sub(r"\n\s*\n+", "\n", "".join(p.out)).strip()
    if variables:
        guide += "\n\n## Project variables (type the variable, not the text)\n" + "\n".join(f"- {v['name']}: {v['value']}" for v in variables)
    layer = Layer("project", root, guide.strip(), rules, accept, terms)
    layer.variables = variables
    return layer


# ---- composition ------------------------------------------------------------------------------------

class Composition:
    """A Vale config built from an ordered list of layers, cached on disk until any layer's content changes."""

    def __init__(self, layers):
        self.layers = layers
        taken = set()
        self.styles = [(l, style_name(l.name, taken)) for l in layers]
        key = hashlib.sha1("|".join(s + l.stamp() for l, s in self.styles).encode()).hexdigest()[:16]
        self.dir = CACHE / key
        if not (self.dir / ".vale.ini").is_file():
            self._build()

    def _build(self):
        tmp = self.dir.with_name(self.dir.name + "." + str(os.getpid()) + ".tmp")
        shutil.rmtree(tmp, ignore_errors=True)
        (tmp / "styles" / "config" / "vocabularies" / "Docstyle").mkdir(parents=True)
        accept, overrides = [], []
        for layer, style in self.styles:
            d = tmp / "styles" / style
            d.mkdir()
            for fname, text in layer.rules.items():
                (d / fname).write_text(text, encoding="utf-8")
            accept += layer.accept
            overrides += layer.overrides
        (tmp / "styles" / "config" / "vocabularies" / "Docstyle" / "accept.txt").write_text(
            "\n".join(sorted(set(accept))) + "\n", encoding="utf-8")
        (tmp / ".vale.ini").write_text(
            "StylesPath = styles\nMinAlertLevel = suggestion\nVocab = Docstyle\n\n[*]\nBasedOnStyles = " +
            ", ".join(s for _, s in self.styles) + "\n" + "\n".join(overrides) + "\n", encoding="utf-8")
        try:
            tmp.rename(self.dir)
        except OSError:  # another process built it first
            shutil.rmtree(tmp, ignore_errors=True)

    def layer_of(self, check):
        style = check.split(".", 1)[0]
        return next((l.name for l, s in self.styles if s == style), "google")

    def guide(self):
        parts = [f"# Layer: {l.name}\n\n{l.guide.strip()}" for l, _ in self.styles if l.guide.strip()]
        return "\n\n---\n\n".join(parts) + "\n"


class Docstyle:
    def __init__(self, layer_dirs):
        self.layer_dirs = [Path(d) for d in layer_dirs]

    def compose(self, project=None):
        layers = [Layer.from_dir(BUILTIN / "google", "google")] + [Layer.from_dir(d) for d in self.layer_dirs]
        root = flare_project_root(project) if project else None
        if root:
            layers.append(project_layer(root))
        return Composition(layers)

    def lint(self, texts, headings=(), project=None):
        comp = self.compose(project)
        headings = set(headings or ())
        lines = [("# " if i in headings else "") + (t or "").replace("\r", " ").replace("\n", " ") for i, t in enumerate(texts)]
        proc = subprocess.run([_vale_cmd(), "--config", str(comp.dir / ".vale.ini"), "--output=JSON", "--ext=.md", "--no-exit"],
                              input=("\n\n".join(lines) + "\n").encode("utf-8"), capture_output=True, cwd=str(comp.dir), timeout=60)
        out = proc.stdout.decode("utf-8", "replace").strip()
        if not out.startswith("{"):
            raise RuntimeError("vale failed: " + (proc.stderr.decode("utf-8", "replace") or out)[:500])

        variables = {v["value"]: v["name"] for l in comp.layers for v in l.variables}
        hits = []
        for alerts in json.loads(out).values():
            for a in alerts:
                line = a["Line"] - 1
                if line % 2 or line // 2 >= len(texts):
                    continue
                i = line // 2
                shift = 2 if i in headings else 0
                text = texts[i] or ""
                start, end = max(0, a["Span"][0] - 1 - shift), a["Span"][1] - shift
                fix = _fix(a, text, start, end)
                if fix:
                    start, end = fix[0], fix[1]
                message = a.get("Message", "")
                if a["Check"].endswith(".Variables") and text[start:end] in variables:
                    message = f"'{text[start:end]}' is the value of variable {variables[text[start:end]]}: insert the variable instead of typing it."
                hits.append({
                    "index": i, "start": start, "length": end - start, "match": text[start:end],
                    "replacement": fix[2] if fix else None, "rule": a["Check"], "layer": comp.layer_of(a["Check"]),
                    "severity": a.get("Severity", "suggestion"), "message": message, "link": a.get("Link", ""),
                })
        # Same span flagged by several layers: keep the latest layer (it overrides), then the fixable one.
        rank = {l.name: n for n, (l, _) in enumerate(comp.styles)}
        hits.sort(key=lambda h: (h["index"], h["start"], h["length"], -rank.get(h["layer"], 0),
                                 h["replacement"] is None, SEVERITY.get(h["severity"], 3)))
        kept, seen = [], set()
        for h in hits:
            key = (h["index"], h["start"], h["length"])
            if key not in seen:
                seen.add(key)
                kept.append(h)
        return kept

    def glossary(self, project=None):
        return [dict(e, layer=l.name) for l in self.compose(project).layers for e in l.glossary]


def _vale_cmd():
    exe = shutil.which("vale", path=str(Path(sys.executable).parent)) or shutil.which("vale")
    if not exe:
        raise RuntimeError("Vale not found. Run this server with: uv run --script server.py")
    return exe


def _match_case(text, start, repl):
    """Capitalize the replacement only where a sentence starts; elsewhere the rule's spelling is the point."""
    if repl and repl[:1].islower() and SENTENCE_START.search(text[:start]):
        return repl[0].upper() + repl[1:]
    return repl


def _fix(alert, text, start, end):
    """(start, end, replacement) for a mechanical fix, or None when it needs judgment."""
    match = text[start:end]
    action = alert.get("Action") or {}
    name, params = action.get("Name"), action.get("Params") or []
    if name == "remove":
        if end < len(text) and text[end] == " ":
            end += 1  # take the following space with the words
        if SENTENCE_START.search(text[:start]) and end < len(text):
            return start, end + 1, text[end].upper()  # re-capitalize what now starts the sentence
        return start, end, ""
    if name == "replace" and params:
        repl = _match_case(text, start, params[0].split("|")[0])
        if alert["Check"].endswith(".Latin") and text[end:end + 1] not in (",", ";", ":", "") and not repl.endswith(","):
            repl += ","  # "for example, …" / "that is, …" take a comma as an introductory phrase
        return start, end, repl
    if name == "edit" and len(params) >= 2 and params[0] == "trim_right":
        return start, end, match.rstrip(params[1])
    return None


# ---- MCP surface ------------------------------------------------------------------------------------

def build_server(ds: Docstyle):
    from mcp.server.fastmcp import FastMCP

    mcp = FastMCP("docstyle", instructions=(
        "Style guide and linter for technical documentation. Layers: the Google developer documentation "
        "style guide, then the configured company layers, then (when `project` is given) the open MadCap "
        "Flare project's variables, glossary and StyleGuide folder; later layers win. Use lint for exact, "
        "fixable findings; style_lookup before judging style; glossary_lookup for terms."))

    @mcp.tool()
    def lint(texts: list[str], headings: list[int] | None = None, project: str | None = None) -> dict:
        """Lint plain-text runs (one per XHTML text node; never include markup). `headings` = indices of
        heading runs. `project` = any path inside a Flare project to add its project layer. Returns
        findings with run index, character span, matched text, rule, layer, guide link, and an exact
        `replacement` when the fix is mechanical (null when it needs judgment)."""
        return {"findings": ds.lint(texts, headings or [], project)}

    @mcp.tool()
    def style_lookup(query: str = "", project: str | None = None) -> dict:
        """The layered guide (all layers, in order) when `query` is empty; otherwise the sections that
        match the keywords, each tagged with its layer. Later layers override earlier ones."""
        comp = ds.compose(project)
        if not query.strip():
            return {"guide": comp.guide(), "layers": [l.name for l in comp.layers]}
        words = [w for w in re.findall(r"\w+", query.lower()) if len(w) > 2]
        secs = [{"layer": l.name, "text": ("## " + p).strip()}
                for l in comp.layers for p in re.split(r"(?m)^## ", l.guide)[1:]
                if any(w in p.lower() for w in words)]
        return {"sections": secs}

    @mcp.tool()
    def glossary_lookup(term: str, project: str | None = None) -> dict:
        """Look up a term in every layer's glossary (company and project): canonical spelling,
        abbreviation, definition or usage note."""
        t = term.strip().lower()
        return {"entries": [e for e in ds.glossary(project)
                            if t in (e["term"].lower(), (e.get("abbr") or "").lower())
                            or any(t == v.lower() for v in e.get("variants", []))]}

    return mcp


# ---- self test ----------------------------------------------------------------------------------------

def _selftest():
    ds = Docstyle([BUILTIN / "company"])
    texts = [
        "Configure Single Sign-On",
        "In order to be able to utilize SSO, simply ask the user. Please note that we whitelist it!",
        "It supports SAML 2.0 and OIDC.",
        "Customers can contact the bank, e.g. by phone.",
    ]
    hits = ds.lint(texts, headings=[0])
    by = {(h["index"], h["match"]): h for h in hits}

    def fix(i, match):
        h = by.get((i, match))
        assert h, f"no hit for {match!r} in run {i}: {[(x['index'], x['match'], x['rule']) for x in hits]}"
        return h["replacement"]

    assert fix(0, "Single Sign-On") == "single sign-on"
    assert fix(1, "In order to") == "To" and by[(1, "In order to")]["layer"] == "company"
    assert fix(1, "utilize") == "use"
    assert fix(1, "simply ") == ""
    assert fix(1, "Please note that w") == "W"
    assert fix(1, "whitelist") == "allowlist"
    assert fix(1, "the user") is None
    assert fix(3, "Customers") == "Members"
    assert fix(3, "e.g.") == "for example,"
    assert not [h for h in hits if h["index"] == 2], "clean sentence must not be flagged"

    company = Layer.from_dir(BUILTIN / "company")
    missing = [v for e in company.glossary for v in e.get("variants", []) if f"  {v}:" not in company.rules.get("Terms.yml", "")]
    assert not missing, f"glossary variants missing from Terms.yml: {missing}"

    # Project layer from a minimal Flare project.
    with tempfile.TemporaryDirectory() as d:
        root = Path(d)
        (root / "Demo.flprj").write_text("<CatapultProject />", encoding="utf-8")
        (root / "Project" / "VariableSets").mkdir(parents=True)
        (root / "Project" / "VariableSets" / "General.flvar").write_text(
            '<CatapultVariableSet><Variable Name="ProductName">Acme Online Banking</Variable>'
            '<Variable Name="Empty" /></CatapultVariableSet>', encoding="utf-8")
        (root / "Project" / "Glossaries").mkdir(parents=True)
        (root / "Project" / "Glossaries" / "Main.flglo").write_text(
            "<CatapultGlossary><GlossaryEntry><Terms><Term>Zelle transfer</Term></Terms>"
            "<Definition>A person-to-person payment.</Definition></GlossaryEntry></CatapultGlossary>", encoding="utf-8")
        sg = root / "Content" / "Resources" / "StyleGuide"
        (sg / "rules").mkdir(parents=True)
        (sg / "guide.md").write_text("## Voice\n- Say \"tap\" on mobile.\n", encoding="utf-8")
        (sg / "rules" / "Tap.yml").write_text(
            "extends: substitution\nmessage: \"Use '%s' instead of '%s'.\"\nlevel: warning\nignorecase: true\n"
            "action:\n  name: replace\nswap:\n  click the button: tap the button\n", encoding="utf-8")
        topic = root / "Content" / "Topic.htm"
        topic.write_text("<html/>", encoding="utf-8")

        ph = ds.lint(["Open Acme Online Banking and click the button.", "Send a Zelle Transfer now."], project=str(topic))
        pby = {h["match"]: h for h in ph}
        assert pby["Acme Online Banking"]["layer"] == "project", ph
        assert "General.ProductName" in pby["Acme Online Banking"]["message"]
        assert pby["click the button"]["replacement"] == "tap the button"
        assert pby["Zelle Transfer"]["replacement"] == "Zelle transfer"
        comp = ds.compose(str(topic))
        assert [l.name for l in comp.layers] == ["google", "company", "project"]
        assert "Say \"tap\"" in comp.guide() and "General.ProductName" in comp.guide()
        assert any(e["term"] == "Zelle transfer" for e in ds.glossary(str(topic)))
        assert not [h for h in ds.lint(["Open Acme Online Banking."]) if h["layer"] == "project"]
    print("selftest ok:", len(hits), "hits; project layer ok")


if __name__ == "__main__":
    args = sys.argv[1:]
    layers = [args[i + 1] for i, a in enumerate(args) if a == "--layer" and i + 1 < len(args)]
    if "--selftest" in args:
        _selftest()
    else:
        build_server(Docstyle(layers or [BUILTIN / "company"])).run()
