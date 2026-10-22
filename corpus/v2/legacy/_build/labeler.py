"""Natural/hand-authored label generation over a built project (dev tool).  Labels are about text; rules come from a fixed table, never from the linter."""
import json, os, re, sys
import os as _os; sys.path.insert(0, _os.path.dirname(_os.path.abspath(__file__)))
from corplib import *

RULES_BY_KIND = {
    'exclaim': ['google:*', 'company:*'], 'please': ['google:*', 'company:*'], 'please_note': ['google:*', 'company:*'], 'simply': ['google:*', 'company:*'],
    'we': ['google:*', 'company:*'], 'will': ['google:*', 'company:*'], 'utilize': ['google:*', 'company:*'], 'in_order_to': ['google:*', 'company:*'],
    'prior_to': ['google:*', 'company:*'], 'is_able_to': ['google:*', 'company:*'], 'the_user': ['google:*', 'company:*'], 'latin': ['google:*', 'company:*'],
    'blacklist': ['google:*', 'company:*'], 'whitelist': ['google:*', 'company:*'],
    'claims_soon': ['company:*', 'ai:claims_soon'], 'claims_guaranteed': ['company:*', 'ai:claims_guaranteed'], 'claims_free': ['company:*', 'ai:claims_free'],
    'marketing': ['company:*', 'ai:marketing'], 'timeline': ['company:*', 'ai:timeline'],
    'passive': ['ai:passive', 'google:*'], 'wordy': ['ai:wordy', 'google:*'], 'vague': ['ai:vague', 'google:*'],
    'undefined_abbr': ['ai:undefined_abbr', 'google:*'], 'ui_not_bold': ['ai:ui_not_bold', 'google:*', 'company:*'],
    'alt_missing': ['structure:AltText'], 'heading_skip': ['structure:HeadingOrder'], 'second_h1': ['structure:OneH1'], 'table_header': ['structure:TableHeader'],
    'click_here': ['structure:LinkText', 'google:*', 'company:*'], 'phone': ['structure:HardCoded', 'google:phone-numbers/*', 'project:variables', 'project:variables/*'], 'url': ['structure:HardCoded', 'project:variables', 'project:variables/*'],
    'variable_product': ['project:variables', 'project:variables/*'], 'variable_company': ['project:variables', 'project:variables/*'],
}


class Proj(object):
    def __init__(self, root, pid):
        self.root = root; self.pid = pid
        self.vars = load_vars(root)
        self.docs = {}
        self.blocks = {}
        init_split([f for f in content_files(root) if '/StyleGuide/' not in f])
        for rel in content_files(root):
            try:
                r = ET.parse(os.path.join(root, rel)).getroot()
            except ET.ParseError as e:
                print('parse error', rel, e); continue
            self.docs[rel] = r
            bl, parents = blocks_of(r, rel, self.vars)
            self.blocks[rel] = bl

    def labels_file(self):
        return os.path.join(self.root, 'labels.jsonl')


class LabelSet(object):
    def __init__(self, proj, existing):
        self.p = proj
        self.out = []
        self.existing = existing
        self.counter = {}
        # spans claimed by seeded violations: (file, block) -> list of (start,end)
        self.claimed = {}
        for l in existing:
            if l.get('label') == 'violation' and l.get('quote'):
                key = (l['file'], l['block'])
                self.claimed.setdefault(key, []).append(l)

    def next_id(self, prefix):
        n = self.counter.get(prefix, 0) + 1
        self.counter[prefix] = n
        return '%s/%s%04d' % (self.p.pid, prefix, n)

    def overlaps_seeded(self, b, start, end):
        for l in self.claimed.get((b.file, b.xpath), []):
            q = l['quote']; pre = l.get('prefix', ''); suf = l.get('suffix', '')
            probe = pre + q + suf
            at = b.text.find(probe)
            if at < 0: continue
            s0 = at + len(pre); e0 = s0 + len(q)
            if start < e0 and end > s0: return True
        return False

    def add_span(self, b, start, end, label, rules, reason=None, kind=None, fix=None, origin='natural', by=('hand-read',), status='verified', seed=None):
        ln = end - start
        bad = [(s.start, s.end) for s in b.segs if s.var]
        ctx = quote_ctx(b.text, start, ln, bad)
        if ctx is None: return False
        prefix, suffix = ctx
        d = dict(id=self.next_id('N' if origin == 'natural' else 'G'), file=b.file, block=b.xpath, quote=b.text[start:end], label=label, rules=list(rules),
                 origin=origin, status=status, by=list(by), split=split_of(b.file))
        if prefix: d['prefix'] = prefix
        if suffix: d['suffix'] = suffix
        if kind: d['kind'] = kind
        if reason: d['reason'] = reason
        if fix is not None: d['fix'] = fix
        if seed: d['seed'] = seed
        self.out.append(d)
        return True

    def block_complete(self, b, by=('generator',), status='proposed'):
        self.out.append(dict(id=self.next_id('B'), file=b.file, block=b.xpath, label='block-complete', rules=['lint'], origin='natural', status=status, by=list(by), split=split_of(b.file)))

    def element_label(self, file, el, parents_root, label, rules, kind=None, reason=None, fix=None, origin='seeded', by=('generator',), status='verified', seed=None):
        parents = {c: p for p in parents_root.iter() for c in p}
        d = dict(id=self.next_id('G' if origin == 'seeded' else 'N'), file=file, block=xpath_fast(el, parents, None), label=label, rules=list(rules), origin=origin, status=status, by=list(by), split=split_of(file))
        if kind: d['kind'] = kind
        if reason: d['reason'] = reason
        if fix is not None: d['fix'] = fix
        if seed: d['seed'] = seed
        self.out.append(d)


CURLY = '\u201c\u201d\u2018\u2019'


def label_curly(S, b, par=None):
    """Curly quotation marks and apostrophes are a real finding (google:quotation-marks/2) that the taxonomy has no kind for: ambiguous."""
    codes = inline_spans(b, ('code', 'kbd'))
    for i, ch in enumerate(b.text):
        if ch in CURLY and not any(c0 <= i < c1 for c0, c1 in codes) and not S.overlaps_seeded(b, i, i + 1):
            S.add_span(b, i, i + 1, 'ambiguous', ['google:quotation-marks/*'], reason='Curly quotation mark or apostrophe: a real finding of google:quotation-marks/2, but corpus/schema/defects.yaml has no kind for it yet.', by=('hand-read',))


def read_labels(path):
    out = []
    if os.path.exists(path):
        for line in open(path, encoding='utf-8'):
            line = line.strip()
            if line: out.append(json.loads(line))
    return out


def write_labels(path, labels):
    with open(path, 'w', encoding='utf-8', newline='\n') as f:
        for l in labels: f.write(jl(l) + '\n')


def fix_injector_labels(labels, extra_by=None):
    """Make injector (generator-exact) labels scorer-compatible: real rule specs, verified."""
    for l in labels:
        if l.get('origin') == 'seeded' and l.get('label') == 'violation' and 'seed' in l and l['seed'].get('generator', '').startswith('corpuskit/inject'):
            k = l.get('kind')
            if k in RULES_BY_KIND: l['rules'] = RULES_BY_KIND[k]
            l['status'] = 'verified'
    return labels


def inline_spans(b, tags=('code',), classes=()):
    """Yield (start,end) of block-text ranges that sit inside the given inline tags (merging contiguous segments of one element)."""
    res = []
    cur = None
    for s in b.segs:
        hit = None
        for a in s.anc:
            n = pref(a.tag)
            if n in tags: hit = a; break
            if n == 'span' and any(c in (a.get('class') or '').split() for c in classes): hit = a; break
        if hit is not None and cur is not None and cur[2] is hit and cur[1] == s.start:
            cur[1] = s.end
        elif hit is not None:
            if cur: res.append((cur[0], cur[1]))
            cur = [s.start, s.end, hit]
        else:
            if cur: res.append((cur[0], cur[1])); cur = None
    if cur: res.append((cur[0], cur[1]))
    return res
