"""Parse Kong CHANGELOG.md / CHANGELOG-OLD.md into releases -> sections -> areas -> items (cleaned)."""
import re, json, sys, os
SP = os.environ.get('CORPUS_SCRATCH') or os.path.join(os.path.dirname(os.path.abspath(__file__)), '_scratch')

BLOCK = re.compile(r"""\b(lua|luajit|luarocks|openresty|resty|nginx|ngx|openssl|libexpat|expat|wasmtime|wasmer|v8|ubi|rpm|apk|alpine|debian|ubuntu|centos|rhel|redhat|amazon\s+linux|redis|cassandra|postgres|postgresql|pgmoon|prometheus|zipkin|datadog|statsd|vault|hashicorp|aws|azure|gcp|google|gemini|openai|anthropic|cohere|mistral|llama|bedrock|hugging\s*face|ollama|kubernetes|k8s|docker|helm|github|splunk|loggly|opentelemetry|otel|jaeger|grafana|sentry|okta|auth0|keycloak|ldap|konnect|kong[- ]build|kong/|luaossl|jwt-keycloak|brotli|libyaml|lyaml|lpeg|pcre|zlib|lmdb|ljsonschema|libcrypto|boringssl|openid|hmac-auth|bintray|travis|circleci|jenkins|windows|macos|osx|homebrew|brew|macOS|centos|fedora|sles|suse|arm64|aarch64|musl|glibc|gcc|clang|bazel|cmake|rust|go\s+plugin|golang|python|node\.?js|javascript|java|ruby|erlang|haproxy|envoy|istio|linkerd|consul|etcd|zookeeper|mongodb|mysql|sqlite|elasticsearch|kafka|rabbitmq|newrelic|new\s+relic|honeycomb|lightstep|libc|ngx_|grpc-web|swagger|ldap|saml|mtls|cert-manager|letsencrypt|acme|wasm|proxy-wasm|ada\b|whatwg|libcurl|curl)\b""", re.I)

HEAD_SECS = {'breaking changes': 'Breaking changes', 'deprecations': 'Deprecations', 'dependencies': 'Dependencies', 'features': 'Features', 'fixes': 'Fixes',
             'additions': 'Features', 'added': 'Features', 'changes': 'Changes', 'performance': 'Performance'}


def clean_item(txt):
    t = txt
    t = re.sub(r'\[#?(?:[A-Za-z\-/\.]+ )?#?(\d+)\]\(https?://[^)]*\)', lambda m: '', t)  # PR links dropped, ids taken separately
    t = re.sub(r'\[FTI-\d+\]\([^)]*\)', '', t)
    t = re.sub(r'\[([^\]]+)\]\(https?://[^)]*\)', r'\1', t)  # other links keep text only
    t = re.sub(r'\[([^\]]+)\]\(\(https?://[^)]*\)\)', r'\1', t)
    t = re.sub(r'\s+', ' ', t).strip()
    return t


def parse(path, vers_filter=None):
    lines = open(path, encoding='utf-8').read().split('\n')
    rels = []; cur = None
    i = 0
    for ln in lines:
        m = re.match(r'^## \[?(\d+\.\d+\.\d+)\]?\s*$', ln)
        if m:
            cur = dict(ver=m.group(1), date=None, intro=[], raw=[]); rels.append(cur); continue
        if re.match(r'^## ', ln) or re.match(r'^\[\d', ln):
            cur = None if re.match(r'^## ', ln) else cur
            continue
        if cur is not None:
            cur['raw'].append(ln)
    out = []
    for r in rels:
        sec = None; area = None; secs = {}; item = None; date = None; intro = []
        raw = r['raw']
        for ln in raw:
            m = re.match(r'^>\s*Released(?: on)?:?\s*(\d{4})/(\d{2})/(\d{2})', ln)
            if m: date = '%s-%s-%s' % m.groups(); continue
            m = re.match(r'^(#{3,5})\s+(.*?)\s*$', ln)
            if m:
                level, title = len(m.group(1)), m.group(2).strip()
                key = title.lower()
                if key in HEAD_SECS:
                    sec = HEAD_SECS[key]; area = None; item = None; continue
                if key == 'kong':  # container
                    continue
                if sec is not None:
                    area = title; item = None
                continue
            if sec is None:
                if ln.strip() and not ln.startswith('['): intro.append(ln.strip())
                continue
            m = re.match(r'^\s*[-*]\s+(.*)$', ln)
            if m:
                indent = len(ln) - len(ln.lstrip())
                if indent >= 2 and item is not None and not re.match(r'^\s*-\s+\*\*', ln):
                    item['sub'].append(m.group(1)); continue
                item = dict(text=m.group(1), sub=[], ids=[])
                secs.setdefault(sec, {}).setdefault(area or 'General', []).append(item)
                continue
            if item is not None and ln.strip():
                # continuation or id line
                ids = re.findall(r'\[#(\d+)\]\(https?://github\.com/Kong/kong/(?:pull|issues)/\d+\)', ln)
                if ids:
                    item['ids'] += ids
                    rest = re.sub(r'\[#\d+\]\([^)]*\)', '', ln).strip()
                    if rest: item['text'] += ' ' + rest
                elif ln.startswith('  ') and item['sub'] and not ids:
                    item['sub'][-1] += ' ' + ln.strip()
                else:
                    item['text'] += ' ' + ln.strip()
        for s in secs.values():
            for a in s.values():
                for it in a:
                    ids = re.findall(r'\[#(\d+)\]\(https?://github\.com/Kong/kong/(?:pull|issues)/\d+\)', it['text'])
                    it['ids'] += ids
                    it['text'] = clean_item(it['text'])
                    it['sub'] = [clean_item(x) for x in it['sub']]
        out.append(dict(ver=r['ver'], date=date, intro=[clean_item(x) for x in intro], secs=secs))
    return out


if __name__ == '__main__':
    new = parse(SP + '/kong.md')
    old = parse(SP + '/kong-old.md')
    allr = new + old
    json.dump(allr, open(SP + '/kall.json', 'w'))
    for r in allr:
        tot = kept = 0
        for s in r['secs'].values():
            for a in s.values():
                for it in a:
                    tot += 1
                    if not BLOCK.search(it['text'] + ' ' + ' '.join(it['sub'])): kept += 1
        print(r['ver'], r['date'], {k: sum(len(a) for a in v.values()) for k, v in r['secs'].items()}, 'kept', kept, '/', tot, 'intro', len(r['intro']))
