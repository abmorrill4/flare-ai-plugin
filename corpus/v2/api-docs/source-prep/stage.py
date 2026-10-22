#!/usr/bin/env python3
"""Stage the api-docs corpus project source (Markdown) from the Apache-2.0 Zulip API documentation.

Usage: build_api_stage.py <zulip-checkout> <stage-dir>

Reads api_docs/*.md, zerver/openapi/zulip.yaml, python_examples.py and javascript_examples.js and writes Markdown the CorpusKit
importer can take. Product and company names become a fictional product ("Acme Relay"); the importer turns them into variables.
Everything generated here is deterministic (no randomness, sorted iteration).
"""
import os, re, sys, json, textwrap
import yaml

SRC = sys.argv[1]
OUT = sys.argv[2]
MAX_OPS = int(os.environ.get("MAX_OPS", "88"))

PRODUCT = "Acme Relay"
os.makedirs(OUT, exist_ok=True)

spec = yaml.safe_load(open(os.path.join(SRC, "zerver/openapi/zulip.yaml"), encoding="utf-8"))
comp = spec["components"]


# ---------------------------------------------------------------------------------------------- text scrubbing
THIRD = [  # real third-party names that would read as "real company or product names" in the corpus
    (r"\bZapier\b", "an automation service"), (r"\bIFTTT\b", "another automation service"),
    (r"\bGitHub\b", "a code host"), (r"\bGitLab\b", "a code host"), (r"\bSlack\b", "another chat service"),
    (r"\bMattermost\b", "another chat service"), (r"\bDiscord\b", "another chat service"),
    (r"\bGoogle\b", "a search provider"), (r"\bApple\b", "a device vendor"), (r"\bFirebase\b", "a push service"),
    (r"\bAndroid\b", "mobile"), (r"\biOS\b", "mobile"), (r"\bJira\b", "an issue tracker"),
    (r"\bGitter\b", "another chat service"), (r"\bTwitter\b", "a social network"), (r"\bFacebook\b", "a social network"),
    (r"\bPostgreSQL\b", "the database"), (r"\bJenkins\b", "a build server"), (r"\bSentry\b", "an error tracker"),
    (r"\bStripe\b", "a payment provider"), (r"\bOkta\b", "an identity provider"), (r"\bKeycloak\b", "an identity provider"),
    (r"\bLDAP\b", "directory"), (r"\bSAML\b", "single sign-on"), (r"\bGravatar\b", "avatar service"),
    (r"\bYouTube\b", "a video site"), (r"\bGiphy\b", "an animated-image service"), (r"\bTenor\b", "an animated-image service"),
    (r"\bDropbox\b", "Acme Software"), (r"\bKandra Labs\b", "Acme Software"), (r"\bWikipedia\b", "an online encyclopedia"),
    (r"\bShakespeare\b", "the playwright"), (r"\bHamlet\b", "Ada"), (r"\bOthello\b", "Grace"), (r"\bIago\b", "Alan"),
    (r"\bCordelia\b", "Edsger"), (r"\bPolonius\b", "Barbara"), (r"\bDenmark\b", "general"), (r"\bVerona\b", "design"),
    (r"\bScotland\b", "support"), (r"\bRome\b", "sales"), (r"\bVenice\b", "ops"), (r"\bCastle\b", "Launch"),
    (r"\bGIPHY\b", "an animated-image service"), (r"\bWebex\b", "a meeting service"), (r"\bNextcloud Talk\b", "a meeting service"),
    (r"\bThumbor\b", "the thumbnailer"), (r"\bFCM\b", "the push gateway"), (r"\bAPNs\b", "the push gateway"),
    (r"\bWelcome Bot\b", "Onboarding Bot"), (r"\bNextcloud\b", "a file service"),
    (r"\bPyPI\b", "the package index"), (r"\bnpm\b", "the package manager"),
]


def scrub(s):
    s = s.replace("zulipchat.com", "chat.example.com").replace("zulip.com", "example.com")
    s = s.replace("chat.zulip.org", "chat.example.com").replace("zulip.readthedocs.io", "docs.example.com")
    s = s.replace("github.com/zulip/zulip", "code.example.com/acme/relay").replace("github.com/zulip", "code.example.com/acme")
    s = s.replace("Zulip", PRODUCT).replace("ZULIP", "ACME_RELAY").replace("zulip", "relay").replace("zuliprc", "relayrc")
    s = s.replace("relayrc", "relayrc").replace("relay-send", "relay-send")
    for pat, rep in THIRD:
        s = re.sub(pat, rep, s)
    s = s.replace("Acme Relay Cloud", "Acme Relay Cloud")
    return s


def slug(s):
    return re.sub(r"[^a-z0-9]+", "-", s.lower()).strip("-")


# ---------------------------------------------------------------------------------------------- spec helpers
def resolve(node):
    seen = 0
    while isinstance(node, dict) and "$ref" in node and seen < 20:
        ref = node["$ref"]
        parts = ref.lstrip("#/").split("/")
        cur = spec
        for p in parts:
            cur = cur[p]
        node = cur
        seen += 1
    return node


def type_of(sc):
    sc = resolve(sc) or {}
    if "oneOf" in sc:
        return " or ".join(dict.fromkeys(type_of(x) for x in sc["oneOf"]))
    if "allOf" in sc:
        for x in sc["allOf"]:
            t = type_of(x)
            if t != "any":
                return t
    t = sc.get("type")
    if t == "array":
        return "array of " + type_of(sc.get("items", {})) + "s" if type_of(sc.get("items", {})) != "any" else "array"
    if t == "object":
        return "object"
    if t:
        if "enum" in sc and t == "string":
            return "string"
        return t
    return "any"


def params_of(op):
    out = []
    for p in op.get("parameters", []) or []:
        p = resolve(p)
        sch = resolve(p.get("schema", {})) or {}
        out.append(dict(name=p["name"], where=p.get("in", "query"), required=bool(p.get("required")),
                        type=type_of(p.get("schema", {})), desc=p.get("description") or sch.get("description") or "",
                        example=p.get("example", sch.get("example")), enum=sch.get("enum")))
    rb = resolve(op.get("requestBody", {})) or {}
    for ctype, c in (rb.get("content") or {}).items():
        sch = resolve(c.get("schema", {})) or {}
        req = set(sch.get("required", []) or [])
        for name, ps in (sch.get("properties") or {}).items():
            ps = resolve(ps) or {}
            out.append(dict(name=name, where="body", required=name in req, type=type_of(ps), desc=ps.get("description") or "",
                            example=ps.get("example"), enum=ps.get("enum")))
        break
    return out


def response_props(op):
    r = (op.get("responses") or {}).get("200")
    r = resolve(r) or {}
    c = (r.get("content") or {}).get("application/json") or {}
    sch = resolve(c.get("schema", {})) or {}
    props, req = {}, set()
    for part in (sch.get("allOf") or [sch]):
        part = resolve(part) or {}
        req |= set(part.get("required", []) or [])
        for k, v in (part.get("properties") or {}).items():
            if v == {} or v is None:
                continue
            props[k] = resolve(v) or {}
    return props, req, r.get("description")


def collect_examples(node, depth=0, acc=None, desc=None):
    """Find every example object that looks like an error (has code and msg) anywhere under a response schema."""
    acc = [] if acc is None else acc
    if depth > 8:
        return acc
    node = resolve(node)
    if isinstance(node, dict):
        ex = node.get("example")
        if isinstance(ex, dict) and "code" in ex and "msg" in ex:
            acc.append((ex, node.get("description") or desc))
        for k, v in node.items():
            if k in ("allOf", "oneOf", "anyOf") and isinstance(v, list):
                for x in v:
                    collect_examples(x, depth + 1, acc, node.get("description") or desc)
            elif k in ("schema", "content", "application/json"):
                collect_examples(v, depth + 1, acc, desc)
    return acc


def error_responses(op):
    errs = []
    for code, r in sorted((op.get("responses") or {}).items()):
        if str(code) == "200":
            continue
        r = resolve(r) or {}
        desc = r.get("description") or ""
        c = (r.get("content") or {}).get("application/json") or {}
        exs = collect_examples(c)
        if not exs and c.get("example"):
            exs = [(c["example"], desc)]
        errs.append((str(code), desc, exs))
    return errs


# ---------------------------------------------------------------------------------------------- operations
OPS = []
for path, item in spec["paths"].items():
    for m in ("get", "post", "put", "patch", "delete"):
        if m in item:
            op = item[m]
            OPS.append(dict(path=path, method=m, op=op, key="%s:%s" % (path, m), id=op.get("operationId") or slug(path + m),
                            tag=(op.get("tags") or ["general"])[0], summary=op.get("summary") or path))
OPS.sort(key=lambda o: (o["tag"], o["path"], o["method"]))

# python / javascript examples keyed by "path:method"
py_examples, js_examples = {}, {}
cur = None
cap = None
for line in open(os.path.join(SRC, "zerver/openapi/python_examples.py"), encoding="utf-8"):
    m = re.match(r'@openapi_test_function\("([^"]+)"\)', line)
    if m:
        cur = m.group(1)
    if "{code_example|start}" in line:
        cap = []
        continue
    if "{code_example|end}" in line:
        if cur and cap:
            py_examples.setdefault(cur, []).append(textwrap.dedent("".join(cap)).rstrip())
        cap = None
        continue
    if cap is not None:
        cap.append(line)
cur = None
cap = None
for line in open(os.path.join(SRC, "zerver/openapi/javascript_examples.js"), encoding="utf-8"):
    m = re.match(r'add_example\("[^"]+", "([^"]+)"', line)
    if m:
        cur = m.group(1)
    if "{code_example|start}" in line:
        cap = []
        continue
    if "{code_example|end}" in line:
        if cur and cap:
            js_examples.setdefault(cur, []).append(textwrap.dedent("".join(cap)).rstrip())
        cap = None
        continue
    if cap is not None:
        cap.append(line)

# choose operations: prefer those with a python example, spread over tags
with_py = [o for o in OPS if o["key"] in py_examples]
chosen = []
bytag = {}
for o in with_py:
    bytag.setdefault(o["tag"], []).append(o)
i = 0
while len(chosen) < MAX_OPS and any(bytag.values()):
    for t in sorted(bytag):
        if bytag[t] and len(chosen) < MAX_OPS:
            chosen.append(bytag[t].pop(0))
chosen.sort(key=lambda o: (o["tag"], o["path"], o["method"]))
TAGSEC = {"messages": "messages", "users": "users", "streams": "channels", "channels": "channels"}
for o in chosen:
    o["section"] = "reference/" + slug(o["tag"])
    o["file"] = "%s/%s" % (o["section"], slug(o["id"]))

# page index for link rewriting: api page slug -> staged relative path (no extension)
PAGES = {o["id"]: o["file"] for o in chosen}
HAND = {}  # filled below: api_docs stem -> staged path

HANDWRITTEN = [
    # stem, section, new name, title override
    ("index", "", "index", None),
    ("rest", "getting-started", "rest-overview", None),
    ("installation-instructions", "getting-started", "install-client-bindings", None),
    ("api-keys", "getting-started", "api-keys", None),
    ("configuring-python-bindings", "getting-started", "configure-python-bindings", None),
    ("client-libraries", "getting-started", "client-libraries", None),
    ("http-headers", "getting-started", "http-headers", None),
    ("send-message", "getting-started", "send-your-first-message", "Send your first message"),
    ("create-scheduled-message", "getting-started", "schedule-a-message", None),
    ("real-time-events", "concepts", "real-time-events", None),
    ("message-formatting", "concepts", "message-formatting", None),
    ("roles-and-permissions", "concepts", "roles-and-permissions", None),
    ("group-setting-values", "concepts", "group-setting-values", None),
    ("construct-narrow", "concepts", "construct-a-narrow", None),
    ("zulip-urls", "concepts", "urls-and-ids", None),
    ("outgoing-webhook-payload", "concepts", "outgoing-webhook-payload", None),
    ("mobile-notifications", "concepts", "mobile-notifications", None),
    ("rest-error-handling", "errors", "error-handling", None),
    ("changelog", "concepts", "api-changelog", None),
]
for stem, sec, name, _ in HANDWRITTEN:
    HAND[stem] = (sec + "/" if sec else "") + name


def relpath(frm, to):
    return os.path.relpath(to + ".md", os.path.dirname(frm + ".md")).replace("\\", "/")


def fix_links(text, here):
    """Rewrite [text](/api/x) to relative links between staged pages; drop links that point outside the corpus."""
    def sub(m):
        label, target = m.group(1), m.group(2)
        frag = ""
        if "#" in target:
            target, frag = target.split("#", 1)
        if target.startswith("/api/"):
            stem = target[len("/api/"):]
            dest = PAGES.get(stem) or HAND.get(stem)
            if dest and dest != here:
                return "[%s](%s)" % (label, relpath(here, dest))
            return label
        if target.startswith(("http://", "https://", "mailto:")):
            if re.search(r"(zulip|github\.com/zulip)", target):
                return label
            return m.group(0)
        if target.startswith("/") or target.startswith("#") or target == "":
            return label
        dest = HAND.get(target.replace(".md", ""))
        if dest and dest != here:
            return "[%s](%s)" % (label, relpath(here, dest))
        return label
    text = re.sub(r"\[([^\]]+)\]\(([^)\s]+)(?:\s+\"[^\"]*\")?\)", sub, text)
    text = re.sub(r"\[([^\]]+)\]\[[^\]]*\]", r"\1", text)  # reference-style links
    text = re.sub(r"^\[[^\]]+\]:\s+\S+.*$", "", text, flags=re.M)  # their definitions
    return text


def clean_desc(d, here):
    d = (d or "").replace("\r", "")
    d = re.sub(r"\{[a-z_]+\|[^}]*\}", "", d)
    d = fix_links(d, here)
    d = scrub(d)
    d = re.sub(r"\n{3,}", "\n\n", d).strip()
    return d


def first_para(d):
    paras = [p for p in re.split(r"\n\s*\n", d) if p.strip()]
    return (re.sub(r"\s+", " ", paras[0]).strip() if paras else ""), paras[1:]


def esc_cell(s):
    return s.replace("|", "\\|")


# ---------------------------------------------------------------------------------------------- endpoint pages
def curl_example(o, ps):
    path = o["path"]
    vals = {}
    for p in ps:
        ex = p["example"]
        if ex is None and p["enum"]:
            ex = p["enum"][0]
        if ex is None:
            ex = {"integer": 1, "boolean": True}.get(p["type"], "example")
        vals[p["name"]] = ex
    url_path = path
    for p in ps:
        if p["where"] == "path":
            url_path = url_path.replace("{%s}" % p["name"], str(vals[p["name"]]))
    lines = ["# %s" % o["summary"], "curl -sS -X %s https://chat.example.com/api/v1%s \\" % (o["method"].upper(), url_path),
             "    -u EMAIL_ADDRESS:API_KEY"]
    use = [p for p in ps if p["where"] in ("query", "body") and (p["required"] or len([q for q in ps if q["required"]]) == 0)][:4]
    for p in use:
        v = vals[p["name"]]
        if isinstance(v, (list, dict, bool)):
            v = json.dumps(v)
            lines[-1] += " \\"
            lines.append("    --data-urlencode '%s=%s'" % (p["name"], v))
        else:
            lines[-1] += " \\"
            lines.append("    --data-urlencode %s=%s" % (p["name"], str(v).replace(" ", "\\ ")))
    return "\n".join(lines)


def endpoint_page(o):
    op, here = o["op"], o["file"]
    ps = params_of(op)
    props, req, ok_desc = response_props(op)
    md = ["# %s" % scrub(o["summary"]), "", "`%s %s`" % (o["method"].upper(), scrub(o["path"])), ""]
    desc = clean_desc(op.get("description"), here)
    if desc:
        md += [desc, ""]
    md += ["This endpoint is part of version APIVERSIONTOKEN of the REST API.", ""]
    if o["method"] != "get":
        md += ["[[cloud]] On Acme Relay Cloud, requests to this endpoint are rate limited for each organization. When you reach the limit, the server responds with HTTP status 429 and a `retry-after` value.", "",
               "[[onprem]] On a self-hosted server, an administrator sets the rate limits in the server configuration. Ask your administrator for the limits that apply to your deployment.", ""]
    # usage examples
    md += ["## Usage examples", ""]
    exs = py_examples.get(o["key"], [])
    if exs:
        md += ["### Python", ""]
        for e in exs[:2]:
            md += ["```python", scrub(e), "```", ""]
    if o["key"] in js_examples:
        md += ["### JavaScript", ""]
        for e in js_examples[o["key"]][:1]:
            md += ["```javascript", scrub(e), "```", ""]
    md += ["### curl", "", "```bash", scrub(curl_example(o, ps)), "```", ""]
    # parameters
    if ps:
        md += ["## Parameters", ""]
        md += ["| Parameter | Location | Type | Required | Description |", "|---|---|---|---|---|"]
        details = []
        for p in ps:
            fp, rest = first_para(clean_desc(p["desc"], here))
            fp = fp or "None."
            md.append("| `%s` | %s | %s | %s | %s |" % (p["name"], p["where"], esc_cell(p["type"]), "Yes" if p["required"] else "No", esc_cell(fp)))
            extra = "\n\n".join(rest)
            if p["enum"]:
                extra = ("Possible values: " + ", ".join("`%s`" % v for v in p["enum"][:12]) + ".\n\n" + extra).strip()
            if extra:
                details.append((p["name"], extra))
        md.append("")
        if details:
            md += ["### Parameter details", ""]
            for name, extra in details[:6]:
                md += ["#### `%s`" % name, "", extra, ""]
    # response
    md += ["## Response", ""]
    if props:
        md += ["| Field | Type | Description |", "|---|---|---|"]
        notes = []
        for k, v in props.items():
            fp, rest = first_para(clean_desc(v.get("description", ""), here))
            md.append("| `%s` | %s | %s |" % (k, esc_cell(type_of(v)), esc_cell(fp or "None.")))
        md.append("")
    else:
        md += [scrub(ok_desc or "A successful response has an empty body apart from the standard `result` and `msg` fields."), ""]
    md += ["!!! tip \"\"", "", "    Every successful response also contains `result` set to `\"success\"` and an empty `msg` field.", ""]
    # errors
    errs = error_responses(op)
    if errs:
        md += ["## Errors", ""]
        for code, d, exs in errs[:3]:
            md += ["### HTTP %s" % code, "", scrub(re.sub(r"\s+", " ", d)) or "An error response.", ""]
            for ex, exd in exs[:1]:
                md += ["<details><summary>Example response</summary>", "", "```json", json.dumps(ex, indent=2, ensure_ascii=False), "```", "", "</details>", ""]
    # a warning for destructive operations
    if o["method"] == "delete":
        md += ["!!! warning \"\"", "", "    This operation cannot be undone. Check the identifier before you send the request.", ""]
    return "\n".join(md)


# ---------------------------------------------------------------------------------------------- hand-written pages
def md_macros(text, stem, here):
    text = text.replace("{{ api_url }}", "https://chat.example.com/api")
    text = re.sub(r"\{generate_api_header\([^)]*\)\}", "", text)
    text = re.sub(r"\{generate_(?:code_example|api_arguments_table|return_values_table|parameter_description|response_description)[^}]*\}", "", text)
    text = re.sub(r"\{!(?:rest-endpoints|api-admin-only|api-owner-only|api-pending-web|empty)\.md!\}", "", text)
    text = text.replace("{!curl-auth-credentials.md!}", "You can send your credentials with HTTP basic authentication: use the account email address as the user name and the API key as the password.")
    text = re.sub(r"\{start_tabs\}|\{end_tabs\}", "", text)
    text = re.sub(r"^\{tab\|([^}]+)\}\s*$", lambda m: "#### " + m.group(1).replace("-", " ").title(), text, flags=re.M)
    text = re.sub(r"^\{settings_tab\|([^}]+)\}\s*$", lambda m: "Open the **%s** settings." % m.group(1).replace("-", " ").title(), text, flags=re.M)
    text = re.sub(r"\{relative\|[^}]+\}", "", text)
    text = re.sub(r"\{!([^!]+)!\}", "", text)
    return text


def handwritten(stem, here, title_override):
    p = os.path.join(SRC, "api_docs", stem + ".md")
    text = open(p, encoding="utf-8").read()
    if stem == "changelog":
        parts = re.split(r"(?m)^(?=## )", text)
        text = "".join(parts[:7])  # the header and the newest few releases
    text = md_macros(text, stem, here)
    if stem == "index":
        a, b = text.index("* Next, check if"), text.index("* If you'd like to send content")
        text = text[:a] + "* Next, check if an automation service already has an integration. These services often let you connect a new service without writing any code.\n" + text[b:]
    if stem == "send-message":
        text = "# Send your first message\n\nThis guide sends one message with the API. For the full list of options, see the [Send a message](../reference/messages/send-message.md) reference.\n\n" + text
    text = fix_links(text, here)
    text = scrub(text)
    if title_override and not text.lstrip().startswith("# "):
        text = "# %s\n\n%s" % (title_override, text)
    if not re.match(r"\s*# ", text):
        text = "# %s\n\n%s" % (stem.replace("-", " ").capitalize(), text)
    # intro line carrying the API version variable on the pages people land on first
    if stem in ("rest", "index", "installation-instructions", "api-keys", "http-headers"):
        text = re.sub(r"(?m)^(# .*)\n", r"\1\n\nThis page describes version APIVERSIONTOKEN of the REST API.\n", text, count=1)
    return text


# ---------------------------------------------------------------------------------------------- error code pages
errors = {}
for o in OPS:
    for code, d, exs in error_responses(o["op"]):
        for ex, exd in exs:
            e = errors.setdefault(ex["code"], dict(http=set(), msg=ex.get("msg"), desc=set(), eps=[], extra={}))
            e["http"].add(code)
            for dd in (exd, d):
                if dd:
                    e["desc"].add(re.sub(r"\s+", " ", dd).strip())
            if o not in e["eps"]:
                e["eps"].append(o)
            e["extra"].update({k: v for k, v in ex.items() if k not in ("result", "msg", "code")})


def error_page(code, e):
    here = "errors/" + slug(code)
    md = ["# Error code %s" % code, "",
          "The `%s` error code is returned with HTTP status %s." % (code, " or ".join(sorted(e["http"])))]
    md[-1] += ""
    md += ["", "## When you see this error", ""]
    for d in sorted(e["desc"])[:2]:
        md += [scrub(d), ""]
    md += ["## Example response", "", "```json", json.dumps(dict([("result", "error"), ("msg", scrub(e["msg"] or "An error occurred.")), ("code", code)] + list(e["extra"].items())), indent=2), "```", ""]
    shown = [o for o in e["eps"] if o["id"] in PAGES][:6]
    if shown:
        md += ["## Where it appears", ""]
        for o in shown:
            md.append("- [%s](%s)" % (scrub(o["summary"]), relpath(here, o["file"])))
        md.append("")
    md += ["!!! note \"\"", "", "    Check the `code` field, not the `msg` text. The text is translated and can change between releases.", ""]
    return "\n".join(md)


# ---------------------------------------------------------------------------------------------- SDK how-tos
def sdk_pages():
    pages = {}
    by = {}
    for o in OPS:
        if o["key"] in py_examples or o["key"] in js_examples:
            by.setdefault(o["tag"], []).append(o)
    for tag, ops in sorted(by.items()):
        for lang, ex, fence in (("Python", py_examples, "python"), ("JavaScript", js_examples, "javascript")):
            items = [o for o in ops if o["key"] in ex][:6]
            if len(items) < 2:
                continue
            name = "sdk/%s-%s" % (lang.lower(), slug(tag))
            md = ["# %s: work with %s" % (lang, tag.replace("_", " ")), "",
                  "These recipes use the %s client library against the REST API, version APIVERSIONTOKEN. Each recipe links to the reference page for the call it makes." % lang, ""]
            md += ["## Before you start", "",
                   "1. Install the client library for %s." % lang,
                   "2. Create a bot and download its credentials file.",
                   "3. Point the client at the credentials file.", ""]
            for o in items:
                md += ["## %s" % scrub(o["summary"]), "", "Call [%s](%s) like this:" % (scrub(o["summary"]), relpath(name, o["file"]) if o["id"] in PAGES else "#"), ""]
                md += ["```%s" % fence, scrub(ex[o["key"]][0]), "```", ""]
            md = "\n".join(md)
            if not any(o["id"] in PAGES for o in items):
                md = re.sub(r"\[([^\]]+)\]\(#\)", r"\1", md)
            pages[name] = md
    return pages


# ---------------------------------------------------------------------------------------------- write
ALLOW_HOSTS = ("en.wikipedia.org", "developer.mozilla.org", "www.rfc-editor.org", "datatracker.ietf.org", "tools.ietf.org", "example.com", "chat.example.com", "docs.example.com", "code.example.com", "files.example.com")
LOWER = [("giphy", "gifsvc"), ("discord", "chatsvc"), ("nextcloud_talk", "meetings"), ("nextcloud", "filesvc"), ("slack", "chatsvc2"),
         ("hamlet", "ada"), ("othello", "grace"), ("iago", "alan"), ("cordelia", "edsger"), ("polonius", "barbara"), ("zoe", "lin"),
         ("dropbox", "files"), ("webex", "meetsvc"), ("zulip", "relay"), ("firebase", "pushsvc"), ("aioapns", "pushclient")]


def final_scrub(text):
    def link(m):
        host = re.match(r"https?://([^/\s)]+)", m.group(2)).group(1)
        return m.group(0) if host in ALLOW_HOSTS else m.group(1)
    text = re.sub(r"\[([^\]]+)\]\((https?://[^)\s]+)\)", link, text)

    def host(m):
        h = m.group(2)
        return m.group(0) if h in ALLOW_HOSTS else m.group(1) + "example.com"
    text = re.sub(r"(https?://)([A-Za-z0-9.-]+)", host, text)
    for a, b in LOWER:
        text = re.sub(r"(?i)(?<![A-Za-z])" + a + r"(?![a-z])", lambda m: b.capitalize() if m.group(0)[0].isupper() else b, text)
    return text


def write(name, text):
    text = final_scrub(text)
    text = re.sub(r"[ \t]+\n", "\n", text)
    path = os.path.join(OUT, name + ".md")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(text.rstrip() + "\n")


n = 0
for stem, sec, name, title in HANDWRITTEN:
    here = HAND[stem]
    write(here if stem != "index" else "index", handwritten(stem, here, title))
    n += 1
for o in chosen:
    write(o["file"], endpoint_page(o))
    n += 1
for code, e in sorted(errors.items()):
    if code == "BAD_REQUEST":
        pass
    write("errors/" + slug(code), error_page(code, e))
    n += 1
for name, text in sorted(sdk_pages().items()):
    write(name, text)
    n += 1
print("staged", n, "pages;", len(chosen), "endpoints;", len(errors), "error codes")
