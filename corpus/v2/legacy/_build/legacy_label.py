"""Labelling passes for the legacy project: pass A resolves planted defects, inject adds seeded ones, pass B adds clean/exempt/block-complete."""
import json, os, random, re, sys
import os as _os; sys.path.insert(0, _os.path.dirname(_os.path.abspath(__file__)))
from corplib import *
from labeler import *

OUT = WT + '/corpus/v2/legacy'
PID = 'legacy'
SEED = 20261023
PLANT = re.compile('\ue000([a-z_0-9]+)\ue001([^\ue002]*)\ue002(.*?)\ue003', re.S)


def read_vars(): return load_vars(OUT)


def pass_a():
    vars_ = read_vars()
    files = content_files(OUT)
    init_split(files)
    labels = []
    n = [0]

    def lid():
        n[0] += 1
        return '%s/G%04d' % (PID, n[0])

    planted = 0
    for rel in files:
        path = OUT + '/' + rel
        raw = open(path, encoding='utf-8').read()
        if '\ue000' not in raw and 'x-plant' not in raw: continue
        root = ET.fromstring(raw.encode('utf-8'))
        blocks, parents = blocks_of(root, rel, vars_)
        era = 'legacy'
        for b in blocks:
            text = b.text
            ms = list(PLANT.finditer(text))
            if not ms: continue
            out = []; pos = 0; spans = []
            removed_at = []   # (marked_pos, cumulative removed)
            for m in ms:
                out.append(text[pos:m.start()])
                start = sum(len(x) for x in out)
                out.append(m.group(3))
                spans.append((start, start + len(m.group(3)), m.group(1), m.group(2)))
                pos = m.end()
            out.append(text[pos:])
            cleaned = ''.join(out)
            assert '\ue000' not in cleaned and '\ue003' not in cleaned, (rel, cleaned[:80])

            def to_clean(p):
                rem = 0
                for m in ms:
                    if m.end() <= p: rem += (m.end() - m.start()) - len(m.group(3))
                    elif m.start() < p: break
                return p - rem
            var_ranges = [(to_clean(s.start), to_clean(s.end)) for s in b.segs if s.var]
            for st, en, kind, fix in spans:
                ctx = quote_ctx(cleaned, st, en - st, var_ranges)
                if ctx is None:
                    print('no unique anchor', rel, kind, cleaned[st:en]); continue
                prefix, suffix = ctx
                d = dict(id=lid(), file=rel, block=b.xpath, quote=cleaned[st:en], label='violation', kind=kind, rules=RULES_BY_KIND.get(kind, ['*']),
                         origin='seeded', seed={'generator': 'legacy-gen@1', 'variant': 'authored'}, status='verified', by=['generator'], split=split_of(rel))
                if prefix: d['prefix'] = prefix
                if suffix: d['suffix'] = suffix
                if fix: d['fix'] = fix
                labels.append(d); planted += 1
        parents_all = {c: p for p in root.iter() for c in p}
        for el in root.iter():
            k = el.get('x-plant')
            if k == 'alt_junk':
                labels.append(dict(id=lid(), file=rel, block=xpath_fast(el, parents_all, None), label='ambiguous', rules=['structure:AltText', 'ai'], origin='seeded',
                                   reason='The alt text is a placeholder ("image"). Whether that counts as missing is a judgment call.', seed={'generator': 'legacy-gen@1', 'variant': 'authored'}, status='verified', by=['generator'], split=split_of(rel)))
            elif k:
                labels.append(dict(id=lid(), file=rel, block=xpath_fast(el, parents_all, None), label='violation', kind=k, rules=RULES_BY_KIND[k], origin='seeded',
                                   seed={'generator': 'legacy-gen@1', 'variant': 'authored'}, status='verified', by=['generator'], split=split_of(rel)))
                planted += 1
        raw2 = re.sub('\ue000[^\ue002]*\ue002', '', raw).replace('\ue003', '')
        raw2 = re.sub(r' x-plant="[^"]*"', '', raw2)
        assert '\ue000' not in raw2
        open(path, 'w', encoding='utf-8', newline='\n').write(raw2)
    write_labels(OUT + '/labels.jsonl', labels)
    print('pass A: planted labels', planted)


def inject():
    kinds = 'utilize,in_order_to,prior_to,is_able_to,please,simply,please_note,whitelist,blacklist,the_user,latin,exclaim,will,we,claims_guaranteed,claims_free,passive,wordy,vague,undefined_abbr,click_here,phone,url,alt_missing'
    kit('inject', '--project', OUT, '--seed', str(SEED), '--density', '0.14', '--project-id', PID, '--kinds', kinds)


QUOTEDMSG = re.compile('(?:message|see) [\\u201c"]([^\\u201d"\\n]{4,60})[\\u201d"]')
UNIT = re.compile(r'^\d+(?:\.\d+)? ?(?:GB|MB|Mbps|in|px|s|min)$|^\d+ x \d+$')


def pass_b():
    labels = fix_injector_labels(read_labels(OUT + '/labels.jsonl'))
    P = Proj(OUT, PID)
    S = LabelSet(P, labels)
    S.counter = {}
    for rel in sorted(P.blocks):
        par = {c: p for p in P.docs[rel].iter() for c in p}
        for b in P.blocks[rel]:
            varr = [(s.start, s.end) for s in b.segs if s.var]
            for st, en in inline_spans(b, ('code', 'kbd')):
                if not S.overlaps_seeded(b, st, en):
                    S.add_span(b, st, en, 'exempt', ['*'], reason='Key name or code is a literal; prose style rules do not apply.')
            for st, en in inline_spans(b, ('b', 'strong')):
                in_ol = any(pref(a.tag) == 'ol' for a in anc(par, b.el))
                if in_ol and en - st <= 40 and not S.overlaps_seeded(b, st, en) and not any(st < v1 and en > v0 for v0, v1 in varr):
                    S.add_span(b, st, en, 'clean', ['*'], reason='UI label in a step, written in bold as the guide requires.')
            for m in QUOTEDMSG.finditer(b.text):
                if not S.overlaps_seeded(b, m.start(1), m.end(1)) and not any(m.start(1) < v1 and m.end(1) > v0 for v0, v1 in varr):
                    S.add_span(b, m.start(1), m.end(1), 'exempt', ['*'], reason='Message that the program shows, quoted verbatim.')
            label_curly(S, b)
            if pref(b.el.tag) == 'td' and UNIT.match(b.text):
                S.add_span(b, 0, len(b.text), 'clean', ['google:units-of-measure/*', 'google:numbers/*'], reason='Value and unit in a table cell, as the table shows them.')
            if rel == 'Content/Resources/Snippets/BackupWarning.flsnp':
                S.out.append(dict(id=S.next_id('N'), file=b.file, block=b.xpath, label='clean', rules=['*'], reason='Warning boilerplate reused in many topics; reviewed once in the snippet.', origin='natural', status='verified', by=['hand-read'], split=split_of(b.file)))
            S.block_complete(b)
    allr = labels + S.out
    for l in allr: l['split'] = split_of(l['file'])
    write_labels(OUT + '/labels.jsonl', allr)
    print('labels', len(allr), {k: sum(1 for l in allr if l['label'] == k) for k in ('violation', 'clean', 'exempt', 'block-complete')})


def anc(parents, el):
    cur = parents.get(el)
    while cur is not None:
        yield cur
        cur = parents.get(cur)


def encodings():
    """Last step: UTF-8 BOMs and Windows-1252 files, as years of different editors leave them."""
    rng = random.Random(SEED + 9)
    plan = json.load(open(SP + '/legacy-plan.json'))['topics']
    files = [t['file'] for t in plan]
    a_files = [t['file'] for t in plan if t['era'] == 'A']
    bom = rng.sample(files, 14)
    cp = []
    for f in rng.sample(a_files, len(a_files)):
        if len(cp) >= 10: break
        if f in bom: continue
        txt = open(OUT + '/' + f, encoding='utf-8').read()
        try: txt.encode('cp1252')
        except UnicodeEncodeError: continue
        cp.append(f)
    for f in bom:
        p = OUT + '/' + f
        data = open(p, 'rb').read()
        if not data.startswith(b'\xef\xbb\xbf'): open(p, 'wb').write(b'\xef\xbb\xbf' + data)
    for f in cp:
        p = OUT + '/' + f
        txt = open(p, encoding='utf-8').read().replace('encoding="utf-8"', 'encoding="windows-1252"', 1)
        open(p, 'wb').write(txt.encode('cp1252'))
    json.dump({'utf8_bom': sorted(bom), 'windows_1252': sorted(cp)}, open(OUT + '/encodings.json', 'w'), indent=1)
    print('BOM', len(bom), 'cp1252', len(cp))


if __name__ == '__main__':
    for s in sys.argv[1:]: globals()[s]()
