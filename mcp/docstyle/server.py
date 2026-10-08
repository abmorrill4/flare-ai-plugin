# /// script
# requires-python = ">=3.10"
# dependencies = ["mcp>=1.2,<2", "vale>=3.9"]
# ///
"""docstyle: layered style guide + linter for technical docs, as an MCP stdio server.

Layers (later wins on conflict):
  1. Google developer documentation style guide: guides/google.md + Vale package styles/Google (vendored, MIT)
  2. Company style:                            guides/company.md + Vale style styles/Company

Run:   uv run --script server.py               (stdio MCP server)
Test:  uv run --script server.py --selftest

Tools (all read-only, safe to auto-execute):
  lint(texts, headings)  -> exact-span findings with fixes, per text run
  style_lookup(query)    -> matching guide sections per layer ("" = both guides in full)
  glossary_lookup(term)  -> canonical term, abbreviation, usage note
"""
import json
import re
import shutil
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
VALE_INI = HERE / ".vale.ini"
LAYERS = [("google", HERE / "guides" / "google.md"), ("company", HERE / "guides" / "company.md")]
GLOSSARY = json.loads((HERE / "glossary.json").read_text(encoding="utf-8"))["terms"]

# Existence rules whose fix is "delete these words" (Vale has no empty replacement).
DELETE_RULES = {"Company.Filler"}
SENTENCE_START = re.compile(r"(^|[.!?:]\s+)$")
SEVERITY = {"error": 0, "warning": 1, "suggestion": 2}


def _vale_cmd():
    exe = shutil.which("vale", path=str(Path(sys.executable).parent)) or shutil.which("vale")
    if not exe:
        raise RuntimeError("Vale not found. Run this server with: uv run --script server.py")
    return exe


def _match_case(text: str, start: int, repl: str) -> str:
    """Capitalize the replacement only where a sentence starts; elsewhere the rule's spelling is the point."""
    if repl and repl[:1].islower() and SENTENCE_START.search(text[:start]):
        return repl[0].upper() + repl[1:]
    return repl


def _fix(alert, text, start, end):
    """(start, end, replacement) for a mechanical fix, or None when it needs judgment."""
    match = text[start:end]
    action = alert.get("Action") or {}
    name, params = action.get("Name"), action.get("Params") or []
    if alert["Check"] in DELETE_RULES:
        if end < len(text) and text[end] == " ":
            end += 1  # take the following space with the words
        if SENTENCE_START.search(text[:start]) and end < len(text):
            return start, end + 1, text[end].upper()  # re-capitalize what now starts the sentence
        return start, end, ""
    if name == "replace" and params:
        repl = _match_case(text, start, params[0].split("|")[0])
        if alert["Check"] == "Google.Latin" and not text[end:end + 1] in (",", ";", ":", "") and not repl.endswith(","):
            repl += ","  # "for example, …" / "that is, …" take a comma when used as an introductory phrase
        return start, end, repl
    if name == "edit" and len(params) >= 2 and params[0] == "trim_right":
        return start, end, match.rstrip(params[1])
    if name == "remove":
        return start, end, ""
    return None


def lint_texts(texts, headings=()):
    """Lint plain-text runs with Vale (Google + Company). Offsets are per run, in characters."""
    headings = set(headings or ())
    # One markdown paragraph per run (so sentence rules never cross runs); headings get "# " so
    # heading-scoped rules (sentence case, punctuation) apply. Newlines inside a run become spaces.
    lines = []
    for i, t in enumerate(texts):
        flat = (t or "").replace("\r", " ").replace("\n", " ")
        lines.append(("# " if i in headings else "") + flat)
    doc = "\n\n".join(lines) + "\n"
    proc = subprocess.run(
        [_vale_cmd(), "--config", str(VALE_INI), "--output=JSON", "--ext=.md", "--no-exit"],
        input=doc.encode("utf-8"), capture_output=True, cwd=str(HERE), timeout=60,
    )
    out = proc.stdout.decode("utf-8", "replace").strip()
    if proc.returncode not in (0, 1) or not out.startswith("{"):
        raise RuntimeError("vale failed: " + (proc.stderr.decode("utf-8", "replace") or out)[:500])

    hits = []
    for alerts in json.loads(out).values():
        for a in alerts:
            line = a["Line"] - 1
            if line % 2:  # blank separator line; cannot happen, but never mis-map
                continue
            i = line // 2
            if i >= len(texts):
                continue
            shift = 2 if i in headings else 0
            text = texts[i] or ""
            start = a["Span"][0] - 1 - shift
            end = a["Span"][1] - shift
            if start < 0:  # a whole-heading finding includes the "# " we added
                start = 0
            fix = _fix(a, text, start, end)
            if fix:
                start, end = fix[0], fix[1]
            check = a["Check"]
            hits.append({
                "index": i, "start": start, "length": end - start, "match": text[start:end],
                "replacement": fix[2] if fix else None,
                "rule": check, "layer": "company" if check.startswith("Company.") else "google",
                "severity": a.get("Severity", "suggestion"), "message": a.get("Message", ""), "link": a.get("Link", ""),
            })

    # Same span flagged by both layers: keep one, company first (it overrides Google), then the fixable one.
    hits.sort(key=lambda h: (h["index"], h["start"], h["length"], h["layer"] != "company",
                             h["replacement"] is None, SEVERITY.get(h["severity"], 3)))
    kept, seen = [], set()
    for h in hits:
        key = (h["index"], h["start"], h["length"])
        if key not in seen:
            seen.add(key)
            kept.append(h)
    return kept


def _sections(layer, text):
    parts = re.split(r"(?m)^## ", text)
    return [{"layer": layer, "text": ("## " + p).strip()} for p in parts[1:]]


def guides():
    return {layer: path.read_text(encoding="utf-8") for layer, path in LAYERS}


def combined_guide():
    g = guides()
    return (g["google"].strip() + "\n\n---\n\n" + g["company"].strip() + "\n")


# ---- MCP surface ------------------------------------------------------------------------

def build_server():
    from mcp.server.fastmcp import FastMCP

    mcp = FastMCP("docstyle", instructions=(
        "Style guide and linter for technical documentation: the Google developer documentation style "
        "guide as the baseline, with the company style guide layered on top (company wins). Use lint for "
        "exact, fixable findings; use style_lookup before judging style questions; use glossary_lookup for "
        "product terms."))

    @mcp.tool()
    def lint(texts: list[str], headings: list[int] | None = None) -> dict:
        """Lint plain-text runs (one per XHTML text node; never include markup).
        `headings` lists the indices of runs that are headings. Returns findings with run index,
        character span, matched text, the rule and its layer (google|company), a link to the guide,
        and an exact replacement when the fix is mechanical (null when it needs judgment)."""
        return {"findings": lint_texts(texts, headings or [])}

    @mcp.tool()
    def style_lookup(query: str = "") -> dict:
        """Return style-guide sections relevant to `query` (keywords) from both layers, or the full
        layered guide when `query` is empty. Company sections override Google sections."""
        g = guides()
        if not query.strip():
            return {"guide": combined_guide(), "google": g["google"], "company": g["company"]}
        words = [w for w in re.findall(r"\w+", query.lower()) if len(w) > 2]
        secs = [s for layer, text in g.items() for s in _sections(layer, text)
                if any(w in s["text"].lower() for w in words)]
        return {"sections": secs}

    @mcp.tool()
    def glossary_lookup(term: str) -> dict:
        """Look up a product/technical term: canonical spelling, abbreviation, usage note."""
        t = term.strip().lower()
        hits = [e for e in GLOSSARY
                if t in (e["term"].lower(), (e.get("abbr") or "").lower())
                or any(t == v.lower() for v in e.get("variants", []))]
        return {"entries": hits}

    return mcp


def _selftest():
    texts = [
        "Configure Single Sign-On",
        "In order to be able to utilize SSO, simply ask the user. Please note that we whitelist it!",
        "It supports SAML 2.0 and OIDC.",
        "Customers can contact the bank, e.g. by phone.",
    ]
    hits = lint_texts(texts, headings=[0])
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

    # Terms.yml must cover every glossary variant.
    terms = (HERE / "styles" / "Company" / "Terms.yml").read_text(encoding="utf-8")
    missing = [v for e in GLOSSARY for v in e.get("variants", []) if f"  {v}:" not in terms]
    assert not missing, f"glossary variants missing from Terms.yml: {missing}"
    assert "Google developer documentation" in combined_guide()
    print("selftest ok:", len(hits), "hits")


if __name__ == "__main__":
    if "--selftest" in sys.argv:
        _selftest()
    else:
        build_server().run()
