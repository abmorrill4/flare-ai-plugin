"""Shared helpers for building the v2 corpus projects (generators, labeller).  Dev-only, never shipped."""
import os, re, json, zlib, struct, subprocess, io
import xml.etree.ElementTree as ET

SP = os.environ.get('CORPUS_SCRATCH') or os.path.join(os.path.dirname(os.path.abspath(__file__)), '_scratch')
WT = os.environ.get('FLAREAI_REPO') or os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..', '..'))
KIT = WT + "/tools/FlareAI.CorpusKit/bin/Release/net48/FlareAI.CorpusKit.exe"
SCORING = WT + "/tools/FlareAI.Scoring/bin/Release/net48/FlareAI.Scoring.exe"
os.makedirs(SP, exist_ok=True)
MC = 'http://www.madcapsoftware.com/Schemas/MadCap.xsd'

BLOCKS = {'p', 'li', 'td', 'th', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6', 'dt', 'dd', 'caption', 'blockquote', 'pre', 'MadCap:dropDownHead'}
CONTAINERS = {'ol', 'ul', 'dl', 'table', 'thead', 'tbody', 'tfoot', 'tr', 'div', 'MadCap:dropDown', 'MadCap:dropDownBody', 'MadCap:snippetBlock'}


def kit(*args):
    r = subprocess.run([KIT] + list(args), capture_output=True, text=True, encoding='utf-8')
    print('> corpuskit ' + ' '.join(args))
    print(r.stdout.strip()[-3000:])
    if r.stderr.strip(): print(r.stderr.strip()[-2000:])
    return r.returncode


def bucket(s, seed=0):
    h = (14695981039346656037 ^ seed) & 0xFFFFFFFFFFFFFFFF
    for c in [ord(ch) for ch in s]:
        h ^= c
        h = (h * 1099511628211) & 0xFFFFFFFFFFFFFFFF
    return h % 100


TEST = set()


def init_split(files, share=0.30):
    """Test files: bucket >= 70, topped up with the highest-bucket remaining files until at least `share` of the files are test."""
    import math
    TEST.clear()
    need = int(math.ceil(share * len(files)))
    ranked = sorted(files, key=lambda f: (-bucket(f), f))
    for f in ranked:
        if bucket(f) >= 70 or len(TEST) < need: TEST.add(f)
    return sorted(TEST)


def split_of(rel):
    return 'test' if rel in TEST else 'dev'


def pref(tag):
    if tag.startswith('{'):
        ns, local = tag[1:].split('}')
        return 'MadCap:' + local if ns == MC else local
    return tag


def read_text(path):
    b = open(path, 'rb').read()
    return b


def parse_file(path):
    return ET.parse(path).getroot()


def load_vars(root):
    vd = os.path.join(root, 'Project', 'VariableSets')
    out = {}
    if os.path.isdir(vd):
        for f in sorted(os.listdir(vd)):
            if f.endswith('.flvar'):
                setn = f[:-6]
                for v in ET.parse(os.path.join(vd, f)).getroot().iter('Variable'):
                    out[setn + '.' + v.get('Name')] = v.get('EvaluatedDefinition') or v.get('Definition') or (v.text or '')
    return out


class Seg(object):
    __slots__ = ('start', 'end', 'anc', 'var', 'el')

    def __init__(self, start, end, anc, var=None, el=None):
        self.start, self.end, self.anc, self.var, self.el = start, end, anc, var, el


class Block(object):
    def __init__(self, el, xpath, text, segs, file):
        self.el, self.xpath, self.text, self.segs, self.file = el, xpath, text, segs, file

    def anchors_at(self, start, end):
        """Elements (inline ancestors up to the block) of the segments overlapping [start,end)."""
        res = []
        for s in self.segs:
            if s.start < end and s.end > start:
                res.append(s)
        return res


def xpath_of(el, parents):
    steps = []
    cur = el
    while cur is not None:
        name = pref(cur.tag)
        par = parents.get(cur)
        if par is None or name == 'body':
            steps.append(name)
        else:
            idx = 1 + sum(1 for sib in par if sib is not cur and sib.tag == cur.tag and list(par).index(sib) < list(par).index(cur))
            steps.append('%s[%d]' % (name, idx))
        cur = par
    return '/' + '/'.join(reversed(steps))


def xpath_fast(el, parents, cache):
    steps = []
    cur = el
    while cur is not None:
        name = pref(cur.tag)
        par = parents.get(cur)
        if par is None or name == 'body':
            steps.append(name)
        else:
            idx = 1
            for sib in par:
                if sib is cur: break
                if sib.tag == cur.tag: idx += 1
            steps.append('%s[%d]' % (name, idx))
        cur = par
    return '/' + '/'.join(reversed(steps))


def blocks_of(root, file, vars_):
    parents = {c: p for p in root.iter() for c in p}
    body = root.find('body')
    out = []
    if body is None: return out, parents
    for e in body.iter():
        if e is body: continue
        if pref(e.tag) not in BLOCKS: continue
        sb = []
        segs = []
        collect(e, e, [], sb, segs, vars_)
        text = ''.join(sb)
        t2 = text.rstrip()
        if not t2.strip(): continue
        for s in segs:
            if s.end > len(t2): s.end = len(t2)
        out.append(Block(e, xpath_fast(e, parents, None), t2, [s for s in segs if s.end > s.start], file))
    return out, parents


def _addtext(v, sb, segs, anc, el):
    if not v: return
    start = len(sb)
    for ch in v:
        if ch.isspace() or ch == ' ':
            if sb and sb[-1] != ' ': sb.append(' ')
        else:
            sb.append(ch)
    if len(sb) > start: segs.append(Seg(start, len(sb), list(anc), None, el))


def collect(e, block, anc, sb, segs, vars_):
    _addtext(e.text, sb, segs, anc + [e], e)
    for c in e:
        name = pref(c.tag)
        if name == 'MadCap:variable':
            val = vars_.get(c.get('name') or '', '{' + (c.get('name') or '') + '}')
            start = len(sb)
            sb.extend(list(val))
            segs.append(Seg(start, len(sb), list(anc) + [e], c.get('name'), c))
        elif name == 'br':
            if sb and sb[-1] != ' ': sb.append(' ')
        elif (name in BLOCKS and name != 'MadCap:dropDownHead') or name in CONTAINERS or name == 'MadCap:snippetText':
            pass
        else:
            collect(c, block, anc + [e], sb, segs, vars_)
        _addtext(c.tail, sb, segs, anc + [e], e)


def quote_ctx(text, start, ln, bad_ranges):
    """Prefix/suffix like CorpusKit (grow by 12 until unique), but never cross a variable or no-break space."""
    quote = text[start:start + ln]
    lo, hi = start, start + ln
    # limits: nearest variable / nbsp before and after
    lim_lo = 0
    for i in range(start - 1, -1, -1):
        if text[i] == '\u00a0' or any(a <= i < b for a, b in bad_ranges):
            lim_lo = i + 1; break
    lim_hi = len(text)
    for i in range(start + ln, len(text)):
        if text[i] == '\u00a0' or any(a <= i < b for a, b in bad_ranges):
            lim_hi = i; break
    n = 12
    while True:
        ps = max(lim_lo, start - n); se = min(lim_hi, start + ln + n)
        prefix = text[ps:start]; suffix = text[start + ln:se]
        probe = prefix + quote + suffix
        if count_of(text, probe) == 1: return prefix, suffix
        if ps == lim_lo and se == lim_hi: return None
        n += 12


def count_of(text, needle):
    if not needle: return 0
    c = 0; i = 0
    while True:
        i = text.find(needle, i)
        if i < 0: return c
        c += 1; i += 1


def jl(d):
    order = ['id', 'file', 'block', 'quote', 'prefix', 'suffix', 'label', 'kind', 'rules', 'fix', 'reason', 'context', 'origin', 'seed', 'status', 'by', 'split']
    o = {k: d[k] for k in order if k in d and d[k] is not None}
    return json.dumps(o, ensure_ascii=False, separators=(',', ':'))


def write_png(path, w=32, h=24, rgb=(70, 110, 170), text_seed=0):
    rows = []
    for y in range(h):
        row = bytearray([0])
        for x in range(w):
            if (x + y + text_seed) % 8 < 2: row += bytes((255, 255, 255))
            elif y < 3 or y >= h - 3 or x < 3 or x >= w - 3: row += bytes((40, 60, 90))
            else: row += bytes(rgb)
        rows.append(bytes(row))
    raw = b''.join(rows)

    def chunk(t, d):
        c = struct.pack('>I', len(d)) + t + d
        return c + struct.pack('>I', zlib.crc32(t + d) & 0xffffffff)
    png = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 2, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(raw, 9)) + chunk(b'IEND', b'')
    os.makedirs(os.path.dirname(path), exist_ok=True)
    open(path, 'wb').write(png)


def esc(s):
    return s.replace('&', '&amp;').replace('<', '&lt;').replace('>', '&gt;')


def content_files(root):
    out = []
    for dp, dn, fn in os.walk(os.path.join(root, 'Content')):
        for f in fn:
            if f.lower().endswith(('.htm', '.html', '.flsnp')):
                full = os.path.join(dp, f)
                out.append(os.path.relpath(full, root).replace('\\', '/'))
    return sorted(out)


def clean_out(path):
    """Empty a project folder for regeneration, keeping the _build folder that holds the generators."""
    import shutil
    if not os.path.isdir(path):
        os.makedirs(path)
        return
    for n in os.listdir(path):
        if n == '_build': continue
        p = os.path.join(path, n)
        if os.path.isdir(p): shutil.rmtree(p)
        else: os.remove(p)
