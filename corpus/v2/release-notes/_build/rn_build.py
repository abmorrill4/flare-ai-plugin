"""Step 1 for release-notes: turn the pinned Kong changelog entries + synthetic hub topics into Markdown sources for CorpusKit import."""
import json, os, re, shutil, sys
import os as _os; sys.path.insert(0, _os.path.dirname(_os.path.abspath(__file__)))
from corplib import SP
from kparse import BLOCK
import calendar

SRC = SP + '/rn-src'
SELECT = "3.9.1 3.9.0 3.8.0 3.7.0 3.6.0 3.5.0 3.4.0 3.3.0 3.2.0 3.1.0 3.0.0 2.8.0 2.7.0 2.5.0 2.4.0 2.3.0 2.2.0 2.1.0 2.0.5 2.0.0 1.3.0 1.2.0 1.1.0 1.0.0".split()
CAP = {'Features': 14, 'Fixes': 12, 'Performance': 6, 'Breaking changes': 10, 'Deprecations': 6, 'Changes': 6}
allr = {r['ver']: r for r in json.load(open(SP + '/kall.json'))}
dates = json.load(open(SP + '/dates.json'))
MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December']


def pretty_date(iso):
    y, m, d = iso.split('-')
    return '%s %d, %s' % (MONTHS[int(m) - 1], int(d), y)


def brand(t):
    t = re.sub(r'Kong Gateway', 'Kong', t)
    t = re.sub(r'Kong(?=-)|(?<=-)Kong', 'Orbit', t)
    t = t.replace('KONG_', 'ORBIT_').replace('kong.', 'orbit.').replace('kong-', 'orbit-')
    t = re.sub(r'\bkong\b', 'orbit', t)
    t = re.sub(r'\bKONG\b', 'ORBIT', t)
    return t


def clean(t):
    t = re.sub(r':[a-z_]+:\s*', '', t)
    t = re.sub(r'\s*Thank(?:s| you)\b[^.!]*[.!]?', '', t)
    t = re.sub(r'\[Back to TOC\]\([^)]*\)', '', t)
    t = re.sub(r'\s+,', ',', t)
    t = re.sub(r'\s+', ' ', t).strip()
    t = t.rstrip(',')
    return t


VENDOR_KEYS = re.compile(r'kuma|emmylua|\binc\b|mesh|sidecar|nginx|opentelemetry|openresty|luajit|resty|zipkin|datadog|prometheus|kong_|lua_|ngx_|postgres|cassandra|redis', re.I)


def keep(it):
    return '@' not in it['text'] and not VENDOR_KEYS.search(it['text'] + ' '.join(it['sub'])) and not BLOCK.search(it['text'] + ' ' + ' '.join(it['sub'])) and len(it['text']) > 12


def fmt_item(it, area=None):
    t = clean(brand(it['text']))
    ids = ', '.join('ORB-%s' % i for i in dict.fromkeys(it['ids'][:2]))
    if ids:
        t = re.sub(r'[.\s]+$', '', t) + ' (' + ids + ').'
    elif not re.search(r'[.!?:]$', t):
        t += '.'
    lines = ['- ' + t]
    for s in it['sub'][:4]:
        s = clean(brand(s))
        if s and not BLOCK.search(s): lines.append('  - ' + s)
    return '\n'.join(lines)


def gather(rel):
    out = {}
    for sec, areas in rel['secs'].items():
        if sec == 'Dependencies': continue
        for area, items in areas.items():
            ks = [it for it in items if keep(it)]
            if ks: out.setdefault(sec, {}).setdefault(area, []).extend(ks)
    return out


def capped(areas, cap):
    """Take up to `cap` items round-robin across areas, keep area grouping."""
    res = {}; n = 0
    queues = {a: list(v) for a, v in areas.items()}
    while n < cap and any(queues.values()):
        for a in list(queues):
            if queues[a] and n < cap:
                res.setdefault(a, []).append(queues[a].pop(0)); n += 1
    return res


NEWSET = set('3.9.0 3.8.0 3.6.0 3.5.0 3.3.0 3.1.0 2.3.0 2.1.0 1.3.0 1.1.0'.split())
FIXSET = set('3.8.0 3.7.0 3.4.0 3.0.0 2.5.0 2.4.0 1.3.0'.split())


def slug(v): return v.replace('.', '-')


TIME_LINES = [
    "This release is now available for download.",
    "Version {v} is currently in extended support.",
    "The next minor release will bring more improvements soon.",
    "This version no longer receives fixes, so upgrade as soon as you can.",
    "At the time of writing, this is the latest release in the {m} series.",
    "Earlier releases previously treated this setting differently.",
    "A new maintenance release is planned for the coming weeks.",
    "This release adds new options for operators and recent fixes for the proxy.",
]


def main():
    if os.path.isdir(SRC): shutil.rmtree(SRC)
    os.makedirs(SRC + '/releases'); os.makedirs(SRC + '/index'); os.makedirs(SRC + '/upgrade')
    summary = []
    topics = 0
    boiler1 = "Release dates use UTC. To report a problem with this release, contact support through the VAR_PRODUCT support portal."
    boiler2 = "Issue ids such as ORB-1234 refer to entries in the public issue tracker."
    for idx, v in enumerate(SELECT):
        r = allr[v]
        date = r['date'] or dates.get(v)
        g = gather(r)
        feats = capped(g.get('Features', {}), CAP['Features'])
        fixes = capped(g.get('Fixes', {}), CAP['Fixes'])
        perf = capped(g.get('Performance', {}), CAP['Performance'])
        chg = capped(g.get('Changes', {}), CAP['Changes'])
        bc = capped(g.get('Breaking changes', {}), CAP['Breaking changes'])
        dep = capped(g.get('Deprecations', {}), CAP['Deprecations'])
        nf = sum(len(x) for x in feats.values()); nx = sum(len(x) for x in fixes.values())
        np_ = sum(len(x) for x in perf.values()); nb = sum(len(x) for x in bc.values()); nd = sum(len(x) for x in dep.values()); nc = sum(len(x) for x in chg.values())
        minor = '.'.join(v.split('.')[:2])
        has_new = v in NEWSET and nf + np_ + nc >= 4
        has_fix = v in FIXSET and nx >= 6
        has_bc = nb + nd >= 1
        s = []
        s.append('---\ntitle: Release %s\n---\n' % v)
        s.append('# Release %s\n' % v)
        s.append('Kong %s was released on %s.\n' % (v, pretty_date(date)))
        itext = clean(brand(' '.join(x for x in r['intro'] if not x.startswith(('#', '>', '|', '-', '*')))))
        sents = [x.strip() for x in re.split(r'(?<=[.!?])\s+', itext) if x.strip()]
        sents = [x for x in sents if not BLOCK.search(x) and not VENDOR_KEYS.search(x) and '@' not in x and len(x) > 25][:3]
        if sents: s.append(' '.join(sents) + '\n')
        s.append(TIME_LINES[idx % len(TIME_LINES)].format(v=v, m=minor) + '\n')
        s.append('## What is in this release\n')
        counts = []
        if nf: counts.append('- %d new %s' % (nf, 'feature' if nf == 1 else 'features'))
        if nc: counts.append('- %d %s' % (nc, 'change' if nc == 1 else 'changes'))
        if np_: counts.append('- %d performance %s' % (np_, 'improvement' if np_ == 1 else 'improvements'))
        if nx: counts.append('- %d %s' % (nx, 'fix' if nx == 1 else 'fixes'))
        if nb: counts.append('- %d breaking %s' % (nb, 'change' if nb == 1 else 'changes'))
        if nd: counts.append('- %d %s' % (nd, 'deprecation' if nd == 1 else 'deprecations'))
        s.append('\n'.join(counts) if counts else '- Maintenance changes only')
        s.append('')
        links = []
        if has_new: links.append('- [New in %s](new-%s.md)' % (v, slug(v)))
        if has_fix: links.append('- [Fixes in %s](fixes-%s.md)' % (v, slug(v)))
        if has_bc: links.append('- [Breaking changes and deprecations in %s](../upgrade/breaking-%s.md)' % (v, slug(v)))
        if links:
            s.append('For the full list, see:\n')
            s.append('\n'.join(links) + '\n')
        if not has_new and (nf or np_ or nc):
            s.append('## Changes\n')
            for area, items in list(feats.items()) + list(perf.items()) + list(chg.items()):
                for it in items: s.append(fmt_item(it))
            s.append('')
        if not has_fix and nx:
            s.append('## Fixes\n')
            for area, items in fixes.items():
                for it in items: s.append(fmt_item(it))
            s.append('')
        s.append(boiler1 + '\n')
        s.append('[Release history](../index/release-history.md) · [Upgrade guide](../index/upgrade-guide.md)\n')
        open(SRC + '/releases/release-%s.md' % slug(v), 'w', encoding='utf-8').write('\n'.join(s))
        topics += 1

        def section_topic(fname, title, intro, blocks, extra=None):
            t = ['---\ntitle: %s\n---\n' % title, '# %s\n' % title, intro + '\n']
            for heading, areas in blocks:
                if not areas: continue
                t.append('## %s\n' % heading)
                for area, items in areas.items():
                    if area != 'General' and len(areas) > 1:
                        t.append('### %s\n' % area)
                    for it in items: t.append(fmt_item(it))
                    t.append('')
            if extra: t.append(extra)
            t.append(boiler2 + '\n')
            t.append('Back to [Release %s](release-%s.md).\n' % (v, slug(v)))
            open(SRC + fname, 'w', encoding='utf-8').write('\n'.join(t))

        if has_new:
            section_topic('/releases/new-%s.md' % slug(v), 'New in %s' % v,
                          'These features and improvements arrived with Kong %s on %s.' % (v, pretty_date(date)),
                          [('Features', feats), ('Changes', chg), ('Performance', perf)])
            topics += 1
        if has_fix:
            section_topic('/releases/fixes-%s.md' % slug(v), 'Fixes in %s' % v,
                          'This release fixes the following problems.', [('Fixes', fixes)])
            topics += 1
        if has_bc:
            t = ['---\ntitle: Breaking changes and deprecations in %s\n---\n' % v, '# Breaking changes and deprecations in %s\n' % v,
                 'Review these changes before you upgrade to Kong %s.\n' % v]
            for heading, areas in [('Breaking changes', bc), ('Deprecations', dep)]:
                if not areas: continue
                t.append('## %s\n' % heading)
                for area, items in areas.items():
                    if area != 'General' and len(areas) > 1: t.append('### %s\n' % area)
                    for it in items: t.append(fmt_item(it))
                    t.append('')
            t.append('## Before you upgrade\n')
            t.append('1. Back up your configuration and your data store.')
            t.append('2. Read each change in this topic and note the settings that you use.')
            t.append('3. Upgrade a test environment first, and then check the logs for warnings.')
            t.append('4. Follow the steps in the [upgrade guide](../index/upgrade-guide.md).\n')
            t.append(boiler2 + '\n')
            t.append('Back to [Release %s](../releases/release-%s.md).\n' % (v, slug(v)))
            open(SRC + '/upgrade/breaking-%s.md' % slug(v), 'w', encoding='utf-8').write('\n'.join(t))
            topics += 1
        summary.append(dict(v=v, date=date, nf=nf, nx=nx, nb=nb, nd=nd, new=has_new, fix=has_fix, bc=has_bc))
    json.dump(summary, open(SP + '/rn-summary.json', 'w'))
    print('release topics', topics)
    return summary


if __name__ == '__main__':
    main()
