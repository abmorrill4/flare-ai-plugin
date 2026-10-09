"""Style mining: what a Flare project's own content says its style is.

Deterministic and stdlib-only. Reads every topic and snippet (not the StyleGuide folder) and finds:
  - spelling variants of one term     ("e-mail" vs "email", "sign-in" vs "sign in")
  - competing words for one concept   ("sign in" vs "log in", "member" vs "customer", click vs select vs tap)
  - inconsistent capitalization        ("Online Banking" vs "online banking" mid-sentence)
  - heading case                       (sentence case vs title case)
Each finding is a candidate rule with counts, real example sentences, and a ready-to-write guide entry
(an Avoid/Use row, or a bullet with Bad/Good). The owner picks the preferred form; nothing is written here.
"""
import collections
import re
import xml.etree.ElementTree as ET
from pathlib import Path

BLOCKS = {"p", "li", "h1", "h2", "h3", "h4", "h5", "h6", "td", "th", "dt", "dd", "caption", "title"}
HEADINGS = {"h1", "h2", "h3", "h4", "h5", "h6"}

# Concepts writers commonly name several ways. Each group: the forms, matched as whole words, case-insensitive.
SYNONYMS = [
    ("signing in", ["sign in", "log in", "log on", "login"]),
    ("signing out", ["sign out", "log out", "log off", "logout"]),
    ("UI action verb", ["click", "select", "tap", "choose"]),
    ("the people who use the product", ["member/members", "customer/customers", "user/users"]),
    ("the bank or credit union", ["financial institution/financial institutions", "bank/banks", "credit union/credit unions", "FI/FIs"]),
    ("administrator", ["administrator/administrators", "admin/admins"]),
    ("the product's software", ["app/apps", "application/applications"]),
    ("sign-in name", ["username", "user name", "user ID", "login ID"]),
    ("addressing the reader", ["the user", "you"]),
]


# Closed-up words that are not variants of their spaced form ("in to" and "into" mean different things).
NOT_VARIANTS = {"into", "onto", "maybe", "everyday", "anyone", "cannot", "someone", "sometime", "sometimes", "without",
                "within", "another", "already", "altogether", "anyway", "everyone", "nobody", "overall", "upon"}


def local(tag):
    return tag.split("}")[-1] if isinstance(tag, str) else ""


def block_text(el):
    """Text of a block, without the text of nested blocks (a list inside a list item is its own block)."""
    parts = [el.text or ""]
    for c in el:
        if local(c.tag) not in BLOCKS and local(c.tag) not in ("ul", "ol", "table"):
            parts.append(block_text(c))
        parts.append(c.tail or "")
    return re.sub(r"\s+", " ", "".join(parts)).strip()


def project_blocks(root, limit=3000):
    """(file name, tag, text) for every text block in the project's topics and snippets."""
    content = Path(root) / "Content"
    guide = (content / "Resources" / "StyleGuide").resolve()
    files = sorted(p for ext in ("*.htm", "*.html", "*.flsnp") for p in content.rglob(ext) if guide not in p.resolve().parents)
    out = []
    for f in files[:limit]:
        try:
            tree = ET.parse(f)
        except Exception:
            continue
        body = next((e for e in tree.iter() if local(e.tag) == "body"), None)
        if body is None:
            continue
        for el in body.iter():
            tag = local(el.tag)
            if tag in BLOCKS:
                text = block_text(el)
                if text:
                    out.append((f.name, tag, text))
    return out, len(files)


def _count(pattern, texts, flags=re.I):
    rx = re.compile(r"(?<![\w-])" + pattern + r"(?![\w-])", flags)
    n, examples = 0, []
    for file, _, text in texts:
        found = rx.findall(text)
        if found:
            n += len(found)
            if len(examples) < 2:
                examples.append({"text": _sentence(text, rx), "file": file})
    return n, examples


def _sentence(text, rx):
    m = rx.search(text)
    dot = text.rfind(". ", 0, m.start()) if m else -1
    start = dot + 2 if dot >= 0 else 0
    end = text.find(". ", m.end()) if m else -1
    s = text[start:end + 1 if end > 0 else len(text)]
    if len(s) <= 220 or not m:
        return s
    at = m.start() - start          # long sentence: keep a window around the matched words
    lo = max(0, at - 100)
    return ("…" if lo else "") + s[lo:lo + 210].strip() + "…"


def _phrase(form):
    return r"\s+".join(re.escape(w) for w in form.split())


def mine(root, covered=lambda form: False, limit=3000):
    """Candidate rules for the project at `root`. `covered(form)` says whether the guide already flags a form."""
    blocks, nfiles = project_blocks(root, limit)
    prose = [b for b in blocks if b[1] not in HEADINGS]
    candidates = []

    # 1. Spelling variants: the same letters written with a hyphen, a space, or closed up.
    grams = collections.Counter()
    for _, _, text in prose:
        words = re.findall(r"[A-Za-z]+(?:-[A-Za-z]+)?", text)
        for i, w in enumerate(words):
            grams[w.lower()] += 1
            if i + 1 < len(words) and "-" not in w + words[i + 1]:
                grams[(w + " " + words[i + 1]).lower()] += 1
    by_key = collections.defaultdict(dict)
    for g, n in grams.items():
        key = re.sub(r"[\s-]", "", g)
        if n >= 2 and len(key) >= 5 and key not in NOT_VARIANTS:
            by_key[key][g] = n
    for key, forms in by_key.items():
        if len(forms) >= 2:
            candidates.append(_term_candidate("spelling", "Spell “" + max(forms, key=forms.get) + "” one way", forms, prose, covered))

    # 2. Competing words for one concept.
    for concept, members in SYNONYMS:
        # "member/members": singular and plural count as one form, and the rule pairs them (users → members).
        plurals = {m.split("/")[0]: (m.split("/")[1] if "/" in m else None) for m in members}
        forms = {w: _count(_forms_rx(w, pl), prose)[0] for w, pl in plurals.items()}
        forms = {w: n for w, n in forms.items() if n > 0}
        if len(forms) >= 2 and sum(forms.values()) >= 5:
            candidates.append(_term_candidate("synonym", "One word for " + concept, forms, prose, covered, plurals=plurals))

    # 3. Capitalization: a term capitalized mid-sentence in some places and lowercase in others.
    mid = r"(?<=[a-z,;:] )"
    caps = collections.Counter(m.group(1) for _, _, t in prose for m in re.finditer(mid + r"([A-Z][a-z]+(?: [A-Z][a-z]+)+)", t))
    for term, n in caps.items():
        if n < 2:
            continue
        low = sum(len(re.findall(mid + re.escape(term.lower()) + r"(?!\w)", t)) for _, _, t in prose)
        if low >= 2:
            forms = {term: n, term.lower(): low}
            candidates.append(_term_candidate("capitalization", "Capitalize “" + max(forms, key=forms.get) + "” one way", forms, prose, covered, case=True))

    # 4. Heading case.
    heads = [t for _, tag, t in blocks if tag in HEADINGS and len(t.split()) >= 3]
    if len(heads) >= 5:
        title = [h for h in heads if sum(1 for w in h.split()[1:] if w[:1].isupper() and len(w) > 3) >= max(1, len([w for w in h.split()[1:] if len(w) > 3]) * 0.6)]
        share = len(title) / len(heads)
        sentence = [h for h in heads if h not in title]
        prefer_title = share >= 0.5
        good, bad = (title, sentence) if prefer_title else (sentence, title)
        if bad:
            rule = "Use title case for headings." if prefer_title else "Use sentence case for headings: capitalize only the first word and proper nouns."
            candidates.append({
                "id": "heading-case", "kind": "heading", "title": rule.split(":")[0],
                "forms": [{"text": "title case", "count": len(title)}, {"text": "sentence case", "count": len(sentence)}],
                "prefer": "title case" if prefer_title else "sentence case",
                "examples": [{"text": h, "file": ""} for h in bad[:3]],
                "bullet": {"text": rule, "bad": bad[0], "good": good[0] if good else ""},
                "impact": len(bad), "covered": False,
            })

    candidates = [c for c in candidates if not c["covered"]]
    candidates.sort(key=lambda c: -c["impact"])
    return {"files": nfiles, "blocks": len(blocks), "candidates": candidates[:40]}


def _forms_rx(word, plural=None):
    return "(?:" + _phrase(plural) + "|" + _phrase(word) + ")" if plural else _phrase(word)


def pair_row(prefer, others, plurals):
    """Avoid/Use lists paired one to one: each avoided singular (and its plural) maps to the preferred singular (plural)."""
    avoid, use = [], []
    for o in others:
        avoid.append(o); use.append(prefer)
        if plurals.get(o):
            avoid.append(plurals[o]); use.append(plurals.get(prefer) or prefer)
    return ", ".join(avoid), ", ".join(use)


def _term_candidate(kind, title, forms, prose, covered, case=False, plurals=None):
    plurals = plurals or {}
    ordered = sorted(forms.items(), key=lambda kv: -kv[1])
    prefer = ordered[0][0]
    examples = []
    for form, _ in ordered:     # every form: the owner may pick a different preferred form
        _, ex = _count(_forms_rx(form, plurals.get(form)), prose, 0 if case else re.I)
        examples += [dict(e, form=form) for e in ex]
    others = [f for f, _ in ordered[1:]]
    avoid, use = pair_row(prefer, others, plurals)
    row = {
        "avoid": avoid, "use": use, "severity": "warning",
        "why": "Team usage: " + ", ".join(f"“{f}” {n}×" for f, n in ordered) + ".",
        "example": next((e["text"] for e in examples if e["form"] != prefer), ""),
    }
    return {
        "id": kind + ":" + re.sub(r"\W+", "-", prefer.lower()), "kind": kind, "title": title,
        "forms": [{"text": f, "count": n, "plural": plurals.get(f)} for f, n in ordered], "prefer": prefer, "examples": examples[:8],
        "row": row, "impact": sum(n for _, n in ordered[1:]), "covered": all(covered(f) for f in others),
    }
