# /// script
# requires-python = ">=3.10"
# dependencies = ["mcp>=1.2,<2", "vale>=3.9"]
# ///
"""docstyle: layered style guide + linter for technical docs, as an MCP stdio server.

Layers, applied in order (later wins on conflict):
  1. google   - Google developer documentation style guide (built in: layers/google)
  2. --layer PATH ...  - company layers (default: layers/company, itself a small Flare project)
  3. project  - optional, per call, from the open MadCap Flare project

A layer is a folder or a Flare project:
  - folder: guide.md, rules/*.yml (Vale), accept.txt, glossary.json, overrides.ini, *.htm topics
  - Flare project: the same files in Content/Resources/StyleGuide/, plus the project's
    variable sets (Project/VariableSets/*.flvar) and glossaries (Project/Glossaries/*.flglo)

Topics (.htm) are written in Flare like any other topic:
  - headings + bullets = rules for people and the AI; each bullet gets a citable id
  - "Bad: …" / "Good: …" lines under a bullet = examples (used to verify the AI)
  - a table whose header has "Avoid" and "Use" = deterministic lint rules, one per row
    (optional columns: Severity, Why, Example; Use "(delete)" = delete, empty = flag only)
  - a table whose header has "Rule" and "Setting" = turn rules of earlier layers on/off

Run:   uv run --script server.py [--layer PATH ...]
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
from pathlib import Path

HERE = Path(__file__).resolve().parent
BUILTIN = HERE / "layers"
SEVERITY = {"error": 0, "warning": 1, "suggestion": 2}
SENTENCE_START = re.compile(r"(^|[.!?:]\s+)$")
CACHE = Path(os.environ.get("LOCALAPPDATA") or tempfile.gettempdir()) / "docstyle" / "cache"
FLAG_ONLY = {"", "-", "—", "(needs rewrite)", "needs rewrite", "(rewrite)", "rewrite", "n/a"}
DELETE = {"(delete)", "delete", "(remove)", "remove"}


def slug(s):
    return re.sub(r"[^a-z0-9]+", "-", (s or "").lower()).strip("-") or "x"


def camel(s):
    return "".join(w.capitalize() for w in re.findall(r"[A-Za-z0-9]+", s or "")) or "X"


def yq(s):
    return "'" + str(s).replace("'", "''") + "'"


def read(p):
    return p.read_text(encoding="utf-8-sig") if p.is_file() else ""  # tolerate a BOM (Windows editors)


# ---- layers ----------------------------------------------------------------------------------------

class Layer:
    """One style layer: guide text with citable items, Vale rules, vocabulary, glossary, overrides."""

    def __init__(self, name, path=None):
        self.name, self.path = name, path
        self.sections = []       # (title, source file, [lines]) -> guide text
        self.items = []          # citable rules: {id, layer, section, text, link, bad, good, source}
        self.rules = {}          # Vale rule file name -> yml text
        self.vale_ids = {}       # Vale rule name (no style prefix) -> item id
        self.table_rows = []     # rule-table rows, kept for validate_guide
        self.accept, self.glossary, self.overrides, self.variables = [], [], [], []
        self.problems = []       # parse problems (reported by validate_guide)

    # -- loading --------------------------------------------------------------------------------------
    @staticmethod
    def load(path, name=None):
        path = Path(path)
        root = path if path.is_dir() and any(path.glob("*.flprj")) else None
        # Rule ids start with the layer name, and team feedback is keyed on them: a guide names itself in
        # FlareAI/layer.txt so every writer's clone gets the same ids, wherever the folder lives.
        declared = path / "FlareAI" / "layer.txt"
        if not name and declared.is_file():
            lines = [l.split("#")[0].strip() for l in declared.read_text(encoding="utf-8-sig").splitlines()]
            name = next((l for l in lines if l), None)
        layer = Layer(name or (root.name if root else path.name), path)
        folder = root / "Content" / "Resources" / "StyleGuide" if root else path
        if folder.is_dir():
            layer._load_folder(folder)
        if root:
            layer._load_flare_project(root)
        return layer

    def _load_folder(self, folder):
        rules_dir = folder / "rules"
        if rules_dir.is_dir():
            self.rules.update({p.name: read(p) for p in sorted(rules_dir.glob("*.yml"))})
        self.accept += [w.strip() for w in read(folder / "accept.txt").splitlines() if w.strip()]
        self.glossary += json.loads(read(folder / "glossary.json") or '{"terms": []}')["terms"]
        self.overrides += [l.strip() for l in read(folder / "overrides.ini").splitlines() if l.strip() and not l.strip().startswith("#")]
        if (folder / "guide.md").is_file():
            self._load_markdown(folder / "guide.md")
        for topic in sorted(list(folder.rglob("*.htm")) + list(folder.rglob("*.html"))):
            self._load_topic(topic)

    def _load_markdown(self, path):
        section, lines, n = None, [], {}
        for raw in read(path).splitlines():
            if raw.startswith("## "):
                section = raw[3:].strip()
                lines = []
                self.sections.append((section, path.name, lines))
            elif section is None:
                continue
            elif re.match(r"^- ", raw):
                n[section] = n.get(section, 0) + 1
                text = raw[2:].strip()
                link = (re.search(r"https?://\S+", text) or [None])[0]
                item = self._item(f"{slug(section)}/{n[section]}", section, re.sub(r"\s*https?://\S+", "", text).strip(), link, path)
                lines.append(f"- [{item['id']}] {item['text']}")
            elif re.match(r"^\s+- (Bad|Good):", raw, re.I) and self.items:
                kind, text = re.match(r"^\s+- (Bad|Good):\s*(.*)$", raw, re.I).groups()
                self.items[-1][kind.lower()] = text.strip()
                lines.append(f"    {kind.capitalize()}: {text.strip()}")
            elif raw.strip():
                lines.append(raw)

    def _load_topic(self, path):
        try:
            root = ET.parse(path).getroot()
        except ET.ParseError as e:
            self.problems.append({"level": "error", "file": str(path), "location": "", "message": f"Not well-formed XML: {e}"})
            return
        for e in root.iter():
            if isinstance(e.tag, str) and "}" in e.tag:
                e.tag = e.tag.split("}", 1)[1]
        body = root.find("body") if root.tag != "body" else root
        if body is None:
            return
        title = next((text_of(h) for h in body.iter("h1")), "") or path.stem
        topic = slug(path.stem)
        state = {"n": 0, "section": title, "lines": []}
        self.sections.append((title, path.name, state["lines"]))
        link = path.resolve().as_uri()

        def walk(el):
            for child in el:
                tag = child.tag
                if tag in ("h1", "h2", "h3", "h4"):
                    if tag != "h1":
                        state["section"] = text_of(child)
                        state["lines"].append(f"### {state['section']}")
                elif tag in ("ul", "ol"):
                    for li in child.findall("li"):
                        state["n"] += 1
                        own, bad, good = bullet_parts(li)
                        item = self._item(f"{topic}/{state['n']}", state["section"], own, link, path, bad, good)
                        state["lines"].append(f"- [{item['id']}] {own}" + (f"\n    Bad: {bad}" if bad else "") + (f"\n    Good: {good}" if good else ""))
                elif tag == "p":
                    t = text_of(child)
                    m = re.match(r"^(Bad|Good):\s*(.*)$", t, re.I)
                    if m and self.items and self.items[-1]["source"] == str(path):
                        self.items[-1][m.group(1).lower()] = m.group(2).strip()
                        state["lines"].append(f"    {m.group(1).capitalize()}: {m.group(2).strip()}")
                    elif t:
                        state["lines"].append(t)
                elif tag == "table":
                    self._load_table(child, topic, state, link, path)
                else:
                    walk(child)  # div, dropDown, …

        walk(body)

    def _load_table(self, table, topic, state, link, path):
        rows = [[text_of(c) for c in tr if c.tag in ("th", "td")] for tr in table.iter("tr")]
        rows = [r for r in rows if any(r)]
        if not rows:
            return
        header = [h.lower() for h in rows[0]]

        def col(*names):
            return next((i for i, h in enumerate(header) for n in names if h.startswith(n)), None)

        avoid_c, use_c = col("avoid"), col("use", "prefer", "write")
        rule_c, setting_c = col("rule"), col("setting", "level", "status")
        where = f"{path.name}"
        if avoid_c is not None and use_c is not None:
            sev_c, why_c, ex_c = col("severity", "level"), col("why", "reason", "note"), col("example")
            for r_i, row in enumerate(rows[1:], start=2):
                cell = lambda c: row[c].strip() if c is not None and c < len(row) else ""
                avoid = [a.strip() for a in re.split(r"[,;\n]+", cell(avoid_c)) if a.strip()]
                use = cell(use_c)
                if not avoid:
                    self.problems.append({"level": "error", "file": where, "location": f"table row {r_i}", "message": "Row has no Avoid text."})
                    continue
                state["n"] += 1
                sev = (cell(sev_c) or "warning").lower()
                if sev not in SEVERITY:
                    self.problems.append({"level": "warning", "file": where, "location": f"table row {r_i}", "message": f"Unknown severity '{sev}'; using warning."})
                    sev = "warning"
                why = cell(why_c)
                text = f"Avoid {', '.join(repr(a) for a in avoid)}" + (
                    " (delete it)." if use.lower() in DELETE else f" → use {use!r}." if use.lower() not in FLAG_ONLY else " (rewrite).") + (f" {why}" if why else "")
                item = self._item(f"{topic}/{state['n']}", state["section"], text, link, path)
                rule_name = f"{camel(topic)}{state['n']}"
                self.vale_ids[rule_name] = item["id"]
                self.rules[rule_name + ".yml"] = vale_rule(avoid, use, sev, why, link)
                self.table_rows.append({"id": item["id"], "file": where, "row": r_i, "avoid": avoid, "use": use,
                                        "example": cell(ex_c), "rule": rule_name})
                state["lines"].append(f"- [{item['id']}] {text}")
        elif rule_c is not None and setting_c is not None:
            for r_i, row in enumerate(rows[1:], start=2):
                rule = row[rule_c].strip() if rule_c < len(row) else ""
                setting = (row[setting_c].strip().lower() if setting_c < len(row) else "")
                if not rule:
                    continue
                if "." not in rule:
                    rule = "Google." + rule
                value = "NO" if setting in ("off", "no", "disabled", "disable", "false") else setting if setting in SEVERITY else None
                if value is None:
                    self.problems.append({"level": "error", "file": where, "location": f"table row {r_i}", "message": f"Setting '{setting}' should be off, error, warning, or suggestion."})
                    continue
                self.overrides.append(f"{rule} = {value}")
                state["lines"].append(f"- {rule}: {'off' if value == 'NO' else value} in this layer")

    def _load_flare_project(self, root):
        for f in sorted((root / "Project" / "VariableSets").glob("*.flvar")):
            try:
                for v in ET.parse(f).getroot().iter("Variable"):
                    value = (v.text or "").strip()
                    if len(value) >= 3 and re.search(r"[A-Za-z]", value):
                        self.variables.append({"name": f"{f.stem}.{v.get('Name')}", "value": value})
            except ET.ParseError:
                pass
        for f in sorted((root / "Project" / "Glossaries").glob("*.flglo")):
            try:
                for e in ET.parse(f).getroot().iter("GlossaryEntry"):
                    d = e.find("Definition")
                    definition = "".join(d.itertext()).strip() if d is not None else ""
                    for t in e.iter("Term"):
                        if (t.text or "").strip():
                            self.glossary.append({"term": t.text.strip(), "note": definition, "abbr": None, "variants": []})
            except ET.ParseError:
                pass
        # No vocabulary entries from variables or glossary: Vale treats accepted words as exceptions in every
        # rule, which would silence the very term checks generated here. Spelling stays Flare's job.
        if self.variables:
            tokens = "\n".join(f"  - {yq(re.escape(v['value']))}" for v in self.variables)
            self.rules["Variables.yml"] = ("extends: existence\nmessage: \"'%s' is a project variable: insert the variable instead of typing it.\"\n"
                                           "level: warning\nignorecase: false\nnonword: true\ntokens:\n" + tokens + "\n")
            self.sections.append(("Variables (type the variable, not the text)", "VariableSets",
                                  [f"- {v['name']}: {v['value']}" for v in self.variables]))
        swaps = {}
        for t in self.glossary:
            c = t["term"]
            for variant in {c.title(), c.upper(), c.lower()} - {c}:
                # Skip variants that differ only by a capital first letter: that is just a sentence start.
                if len(variant) > 3 and variant[:1].upper() + variant[1:] != c[:1].upper() + c[1:]:
                    swaps[variant] = c
        if swaps:
            self.rules["GlossaryTerms.yml"] = ("extends: substitution\nmessage: \"Glossary term: use '%s' instead of '%s'.\"\nlevel: warning\n"
                                               "ignorecase: false\naction:\n  name: replace\nswap:\n" +
                                               "".join(f"  {yq(re.escape(k))}: {yq(v)}\n" for k, v in sorted(swaps.items())))

    def _item(self, local_id, section, text, link, source, bad="", good=""):
        item = {"id": f"{self.name}:{local_id}", "layer": self.name, "section": section, "text": text,
                "link": link, "bad": bad, "good": good, "source": str(source)}
        self.items.append(item)
        return item

    # -- output --------------------------------------------------------------------------------------
    def guide(self):
        parts = []
        for title, source, lines in self.sections:
            body = "\n".join(lines).strip()
            if body:
                parts.append(f"## {title}  ({source})\n{body}")
        return "\n\n".join(parts)

    def stamp(self):
        return hashlib.sha1(json.dumps([self.guide(), self.rules, self.accept, self.glossary, self.overrides],
                                       sort_keys=True, default=str).encode()).hexdigest()


def text_of(el):
    """Visible text of an element, whitespace collapsed (MadCap variables have no text and are skipped)."""
    return re.sub(r"\s+", " ", "".join(el.itertext())).strip()


def bullet_parts(li):
    """(own text, bad, good) of a list item: nested 'Bad:'/'Good:' lines become examples."""
    bad = good = ""
    extra = []
    own = re.sub(r"\s+", " ", (li.text or "")).strip()
    for c in li:
        t = text_of(c) if c.tag not in ("ul", "ol") else ""
        if c.tag in ("ul", "ol"):
            for sub in c.findall("li"):
                st = text_of(sub)
                m = re.match(r"^(Bad|Good):\s*(.*)$", st, re.I)
                if m:
                    bad, good = (m.group(2), good) if m.group(1).lower() == "bad" else (bad, m.group(2))
                elif st:
                    extra.append(st)
        else:
            m = re.match(r"^(Bad|Good):\s*(.*)$", t, re.I)
            if m:
                bad, good = (m.group(2), good) if m.group(1).lower() == "bad" else (bad, m.group(2))
            elif t:
                own = (own + " " + t).strip()
        if c.tail and c.tail.strip():
            own = (own + " " + c.tail.strip()).strip()
    if extra:
        own += " (" + "; ".join(extra) + ")"
    return re.sub(r"\s+", " ", own).strip(), bad.strip(), good.strip()


def vale_rule(avoid, use, severity, why, link):
    """One rule-table row as a Vale rule: replace, delete, or flag only."""
    exact_case = any(a != a.lower() for a in avoid)
    why_text = (" " + why.replace("%", "%%")) if why else ""
    head = f"level: {severity}\nignorecase: {'false' if exact_case else 'true'}\nlink: {yq(link)}\n"
    if use.lower() in DELETE:
        return ("extends: existence\n" + f"message: {yq('Delete %s.' + why_text)}\n" + head +
                "action:\n  name: remove\ntokens:\n" + "".join(f"  - {yq(re.escape(a))}\n" for a in avoid))
    if use.lower() in FLAG_ONLY:
        return ("extends: existence\n" + f"message: {yq('Avoid %s.' + why_text)}\n" + head +
                "tokens:\n" + "".join(f"  - {yq(re.escape(a))}\n" for a in avoid))
    uses = [u.strip() for u in re.split(r"[,;\n]+", use) if u.strip()]
    pairs = list(zip(avoid, uses)) if len(uses) == len(avoid) else [(a, use) for a in avoid]
    message = "Use '%s' instead of '%s'." + why_text
    return ("extends: substitution\n" + f"message: {yq(message)}\n" + head +
            "action:\n  name: replace\nswap:\n" + "".join(f"  {yq(re.escape(a))}: {yq(u)}\n" for a, u in pairs))


def style_name(layer_name, taken):
    base = re.sub(r"[^A-Za-z0-9]", "", layer_name.title()) or "Layer"
    name, i = base, 2
    while name in taken:
        name, i = f"{base}{i}", i + 1
    taken.add(name)
    return name


def flare_project_root(path):
    """Folder containing the .flprj for any path inside a Flare project, or None."""
    p = Path(path).resolve()
    for d in [p] + list(p.parents):
        if d.is_dir() and any(d.glob("*.flprj")):
            return d
    return None


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

    def layer_for(self, check):
        style, _, rule = check.partition(".")
        return next((l for l, s in self.styles if s == style), None), rule

    def guide(self):
        parts = []
        for i, (l, _) in enumerate(self.styles):
            g = l.guide().strip()
            if g:
                note = "baseline" if i == 0 else "overrides the layers above"
                parts.append(f"# Layer: {l.name} ({note})\n\n{g}")
        return "\n\n---\n\n".join(parts) + "\n"

    def items(self):
        return [it for l in self.layers for it in l.items]


class Docstyle:
    def __init__(self, layer_paths):
        self.layer_paths = [Path(d) for d in layer_paths]

    def compose(self, project=None):
        layers = [Layer.load(BUILTIN / "google", "google")] + [Layer.load(d) for d in self.layer_paths]
        root = flare_project_root(project) if project else None
        if root and not any(Path(d).resolve() == root for d in self.layer_paths):
            layers.append(Layer.load(root, "project"))
        return Composition(layers)

    def lint(self, texts, headings=(), project=None, comp=None):
        comp = comp or self.compose(project)
        headings = set(headings or ())
        lines = [("# " if i in headings else "") + (t or "").replace("\r", " ").replace("\n", " ") for i, t in enumerate(texts)]
        proc = subprocess.run([_vale_cmd(), "--config", str(comp.dir / ".vale.ini"), "--output=JSON", "--ext=.md", "--no-exit"],
                              input=("\n\n".join(lines) + "\n").encode("utf-8"), capture_output=True, cwd=str(comp.dir), timeout=120)
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
                layer, rule_name = comp.layer_for(a["Check"])
                message, variable = a.get("Message", ""), None
                if rule_name == "Variables" and text[start:end] in variables:
                    variable = variables[text[start:end]]
                    message = f"'{text[start:end]}' is the value of variable {variable}: insert the variable instead of typing it."
                hits.append({
                    "index": i, "start": start, "length": end - start, "match": text[start:end],
                    "replacement": fix[2] if fix else None, "rule": a["Check"],
                    "rule_id": layer.vale_ids.get(rule_name) if layer else None,
                    "layer": layer.name if layer else "google", "variable": variable,
                    "severity": a.get("Severity", "suggestion"), "message": message, "link": a.get("Link", ""),
                })
        # Same span flagged by several layers: keep the latest layer (it overrides), then the fixable one.
        rank = {l.name: n for n, (l, _) in enumerate(comp.styles)}
        # An explicit guide-table rule (citable id) beats a generated one (glossary casing) on the same span.
        hits.sort(key=lambda h: (h["index"], h["start"], h["length"], -rank.get(h["layer"], 0),
                                 h["rule_id"] is None, h["replacement"] is None, SEVERITY.get(h["severity"], 3)))
        kept, seen = [], set()
        for h in hits:
            key = (h["index"], h["start"], h["length"])
            if key not in seen:
                seen.add(key)
                kept.append(h)
        return kept

    def mine(self, project):
        """Candidate rules mined from the project's content, minus what the composed guide already flags."""
        sys.path.insert(0, str(HERE))
        import mine as miner
        root = flare_project_root(project)
        if root is None:
            raise ValueError(f"not inside a Flare project: {project}")
        result = miner.mine(root)
        comp = self.compose(project)
        forms = sorted({f["text"] for c in result["candidates"] for f in c["forms"]})
        flagged = {forms[h["index"]] for h in self.lint(forms, comp=comp)} if forms else set()
        keep = []
        for c in result["candidates"]:
            others = [f["text"] for f in c["forms"] if f["text"] != c["prefer"]]
            if c["kind"] == "heading" or not all(o in flagged for o in others):
                keep.append(c)
        result["candidates"] = keep
        result["project"] = str(root)
        return result

    def glossary(self, project=None):
        return [dict(e, layer=l.name) for l in self.compose(project).layers for e in l.glossary]

    # -- validation ----------------------------------------------------------------------------------
    def validate(self, project=None):
        """Every rule-table row must be triggered by its Example, its fix must resolve it, and its 'Use'
        wording must not trip another rule. Also reports parse problems and contradictory rows."""
        comp = self.compose(project)
        issues = [dict(p, layer=l.name) for l in comp.layers for p in l.problems]
        rows = [(l, r) for l in comp.layers for r in l.table_rows]

        with_ex = [(l, r) for l, r in rows if r["example"]]
        for l, r in rows:
            if not r["example"]:
                issues.append(_issue("warning", l, r, "No Example: add one so the rule can be tested."))
        if with_ex:
            hits = self.lint([r["example"] for _, r in with_ex], comp=comp)
            fixed_texts, fixed_rows = [], []
            for i, (l, r) in enumerate(with_ex):
                mine = [h for h in hits if h["index"] == i and h["rule_id"] == r["id"]]
                if not mine:
                    issues.append(_issue("error", l, r, f"The Example doesn't trigger this rule: {r['example']!r}."))
                    continue
                if all(h["replacement"] is not None for h in mine):
                    t = r["example"]
                    for h in sorted(mine, key=lambda h: -h["start"]):
                        t = t[:h["start"]] + h["replacement"] + t[h["start"] + h["length"]:]
                    fixed_texts.append(t)
                    fixed_rows.append((l, r))
            if fixed_texts:
                again = self.lint(fixed_texts, comp=comp)
                for i, (l, r) in enumerate(fixed_rows):
                    if any(h["index"] == i and h["rule_id"] == r["id"] for h in again):
                        issues.append(_issue("error", l, r, f"Applying the fix doesn't resolve the rule: {fixed_texts[i]!r}."))

        uses = [(l, r) for l, r in rows if r["use"].lower() not in FLAG_ONLY | DELETE]
        if uses:
            hits = self.lint([r["use"] for _, r in uses], comp=comp)
            for i, (l, r) in enumerate(uses):
                for h in hits:
                    if h["index"] == i and h["replacement"] is not None:
                        what = "this same rule" if h["rule_id"] == r["id"] else (h["rule_id"] or h["rule"])
                        issues.append(_issue("error" if h["rule_id"] == r["id"] else "warning", l, r,
                                             f"The 'Use' wording {r['use']!r} is itself flagged by {what}: {h['message']}"))
        # Contradictions: one row's Use is another row's Avoid, and vice versa.
        for i, (la, a) in enumerate(rows):
            for lb, b in rows[i + 1:]:
                if any(u.lower() == x.lower() for u in [a["use"]] for x in b["avoid"]) and \
                   any(u.lower() == x.lower() for u in [b["use"]] for x in a["avoid"]):
                    issues.append(_issue("error", la, a, f"Contradicts {b['id']} ({b['file']} row {b['row']}): each replaces the other."))

        prose = [it for it in comp.items() if it["bad"] or it["good"]]
        return {"issues": issues,
                "summary": {"layers": [l.name for l in comp.layers], "table_rules": len(rows),
                            "tested_by_example": len(with_ex), "prose_rules": len(comp.items()) - len(rows),
                            "prose_rules_with_examples": len(prose),
                            "errors": sum(1 for x in issues if x["level"] == "error"),
                            "warnings": sum(1 for x in issues if x["level"] == "warning")}}


def _issue(level, layer, row, message):
    return {"level": level, "layer": layer.name, "file": row["file"], "location": f"table row {row['row']} ({row['id']})", "message": message}


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
        "Flare project; later layers win. Every guide rule has an id like 'company:terminology/3' — cite it. "
        "Use lint for exact, fixable findings; style_lookup before judging style; glossary_lookup for terms; "
        "validate_guide after editing the guide."))

    @mcp.tool()
    def lint(texts: list[str], headings: list[int] | None = None, project: str | None = None) -> dict:
        """Lint plain-text runs (one per paragraph; never include markup). `headings` = indices of
        heading runs. `project` = any path inside a Flare project to add its project layer. Findings have
        run index, character span, matched text, rule, `rule_id` (citable guide id when the rule comes from
        a guide table), layer, link, an exact `replacement` when the fix is mechanical (null otherwise), and
        `variable` when the text is a typed copy of a project variable's value."""
        return {"findings": ds.lint(texts, headings or [], project)}

    @mcp.tool()
    def style_lookup(query: str = "", project: str | None = None) -> dict:
        """The layered guide when `query` is empty (with every citable rule: id, layer, section, text,
        link); otherwise the rules matching the keywords. Later layers override earlier ones."""
        comp = ds.compose(project)
        items = [{k: it[k] for k in ("id", "layer", "section", "text", "link")} for it in comp.items()]
        if not query.strip():
            return {"guide": comp.guide(), "layers": [l.name for l in comp.layers], "rules": items}
        words = [w for w in re.findall(r"\w+", query.lower()) if len(w) > 2]
        return {"rules": [it for it in items if any(w in (it["section"] + " " + it["text"]).lower() for w in words)]}

    @mcp.tool()
    def glossary_lookup(term: str, project: str | None = None) -> dict:
        """Look up a term in every layer's glossary (company and project): canonical spelling,
        abbreviation, definition or usage note."""
        t = term.strip().lower()
        return {"entries": [e for e in ds.glossary(project)
                            if t in (e["term"].lower(), (e.get("abbr") or "").lower())
                            or any(t == v.lower() for v in e.get("variants", []))]}

    @mcp.tool()
    def validate_guide(project: str | None = None) -> dict:
        """Test the guide itself: each rule-table row against its Example (it must trigger, the fix must
        resolve it, the 'Use' wording must be clean), contradictory rows, unreadable topics, bad settings."""
        return ds.validate(project)

    @mcp.tool()
    def guide_examples(project: str | None = None) -> dict:
        """Prose rules that carry Bad/Good examples, for checking that an AI reviewer applies them."""
        return {"examples": [{k: it[k] for k in ("id", "layer", "section", "text", "bad", "good")}
                             for it in ds.compose(project).items() if it["bad"] or it["good"]]}

    @mcp.tool()
    def mine_style(project: str) -> dict:
        """What a Flare project's own content says its style is: spelling variants, competing words for one
        concept, inconsistent capitalization, heading case. Each candidate has counts, example sentences, the
        most-used form, and a ready guide entry (row: Avoid/Use/Severity/Why/Example, or bullet: text/bad/good).
        Rules the layered guide already enforces are left out."""
        return ds.mine(project)

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
    assert by[(3, "Customers")]["rule_id"].startswith("company:"), by[(3, "Customers")]

    # The bundled company layer must pass its own validation.
    report = ds.validate()
    errors = [i for i in report["issues"] if i["level"] == "error"]
    assert not errors, errors
    assert report["summary"]["table_rules"] >= 8 and report["summary"]["tested_by_example"] == report["summary"]["table_rules"], report["summary"]

    comp = ds.compose()
    guide = comp.guide()
    assert "[google:" in guide and "[company:" in guide and "overrides the layers above" in guide
    ids = {it["id"] for it in comp.items()}
    assert len(ids) == len(comp.items()), "rule ids must be unique"

    # A Flare project as a layer: topic tables, prose with examples, overrides table, variables, glossary.
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
        sg.mkdir(parents=True)
        ns = 'xmlns:MadCap="http://www.madcapsoftware.com/Schemas/MadCap.xsd"'
        (sg / "Mobile.htm").write_text(f"""<html {ns}><body><h1>Mobile</h1>
            <h2>Gestures</h2>
            <ul><li>Say "tap", not "click", in mobile app topics.<ul><li>Bad: Click the button.</li><li>Good: Tap the button.</li></ul></li></ul>
            <table><tr><th>Avoid</th><th>Use</th><th>Severity</th><th>Why</th><th>Example</th></tr>
              <tr><td>click the button</td><td>tap the button</td><td>warning</td><td>Mobile.</td><td>Then click the button.</td></tr>
              <tr><td>hit</td><td>(needs rewrite)</td><td>suggestion</td><td>Violent.</td><td>Hit Save.</td></tr>
              <tr><td>swipe</td><td>swipe</td><td>bogus</td><td></td><td>Swipe left.</td></tr>
            </table>
            <table><tr><th>Rule</th><th>Setting</th><th>Reason</th></tr><tr><td>Will</td><td>off</td><td>Release notes.</td></tr></table>
            </body></html>""", encoding="utf-8")
        topic = root / "Content" / "Topic.htm"
        topic.write_text("<html/>", encoding="utf-8")

        ph = ds.lint(["Open Acme Online Banking and click the button.", "Send a Zelle Transfer now.", "It will work."], project=str(topic))
        pby = {h["match"]: h for h in ph}
        assert pby["Acme Online Banking"]["variable"] == "General.ProductName"
        assert pby["click the button"]["replacement"] == "tap the button"
        assert pby["click the button"]["rule_id"] == "project:mobile/2", pby["click the button"]
        assert pby["Zelle Transfer"]["replacement"] == "Zelle transfer"
        assert not [h for h in ph if h["rule"] == "Google.Will"], "override table turns Google.Will off"
        comp = ds.compose(str(topic))
        assert [l.name for l in comp.layers] == ["google", "company", "project"]
        g = comp.guide()
        assert "[project:mobile/1] Say \"tap\"" in g and "Bad: Click the button." in g, g
        report = ds.validate(str(topic))
        msgs = [i["message"] for i in report["issues"] if i["layer"] == "project"]
        assert any("flagged by this same rule" in m or "doesn't resolve" in m for m in msgs), msgs   # swipe -> swipe
        assert any("Unknown severity" in m for m in msgs), msgs
        ex = [it for it in comp.items() if it["bad"] and it["layer"] == "project"]
        assert ex and ex[0]["good"] == "Tap the button."
    # A guide cloned into a differently named folder keeps its declared name, so rule ids match across writers.
    with tempfile.TemporaryDirectory() as tmp:
        clone = Path(tmp) / "Some-Writers-Clone"
        shutil.copytree(HERE / "layers" / "company", clone)
        moved = Layer.load(clone)
        assert moved.name == "company", moved.name
        assert any(r["id"].startswith("company:") for r in moved.items), "declared layer name not used in rule ids"

    # Mining a project's own conventions.
    with tempfile.TemporaryDirectory() as tmp:
        proj = Path(tmp) / "Docs"
        (proj / "Content").mkdir(parents=True)
        (proj / "Docs.flprj").write_text("<CatapultProject />", encoding="utf-8")
        topic = ('<?xml version="1.0" encoding="utf-8"?><html><body><h1>Set up alerts for your account</h1>'
                 "<p>Turn on autopay for bills. Autopay runs monthly. Autopay stops when you cancel.</p>"
                 "<p>You can also set up auto-pay here. Edit auto-pay later.</p>"
                 "<p>To start, sign in to the app. Then sign in again and log in to the console. Sign in daily. Log in to see statements.</p>"
                 "<p>Open the app and go to Online Banking now. From there, go to Online Banking settings and then online banking help, and online banking alerts.</p>"
                 "<h2>Change Your Alert Settings</h2><h2>View Your Account History</h2><h2>Delete an alert from the list</h2><h2>Reset Your Online Password</h2></body></html>")
        (proj / "Content" / "Alerts.htm").write_text(topic, encoding="utf-8")
        mined = ds.mine(str(proj / "Content" / "Alerts.htm"))
        kinds = {c["kind"]: c for c in mined["candidates"]}
        assert mined["files"] == 1, mined
        spelling = next(c for c in mined["candidates"] if c["kind"] == "spelling" and "autopay" in c["prefer"])
        assert spelling["prefer"] == "autopay" and spelling["row"]["avoid"] == "auto-pay", spelling
        assert "auto-pay" in spelling["row"]["example"], spelling["row"]
        signin = next((c for c in mined["candidates"] if c["kind"] == "synonym" and "sign" in c["prefer"]), None)
        assert signin and signin["prefer"] == "sign in" and signin["row"]["avoid"] == "log in", signin
        assert "capitalization" in kinds, [c["title"] for c in mined["candidates"]]
        assert "heading" in kinds and kinds["heading"]["prefer"] == "title case", kinds.get("heading")

    print("selftest ok:", len(hits), "hits; layers, tables, validation, mining ok")


if __name__ == "__main__":
    args = sys.argv[1:]
    layers = [args[i + 1] for i, a in enumerate(args) if a == "--layer" and i + 1 < len(args)]
    if "--selftest" in args:
        _selftest()
    else:
        build_server(Docstyle(layers or [BUILTIN / "company"])).run()
