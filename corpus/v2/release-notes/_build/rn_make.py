import os, re, sys, shutil, json, subprocess
import os as _os; sys.path.insert(0, _os.path.dirname(_os.path.abspath(__file__)))
from corplib import *
from labeler import *
import rn_build, rn_hubs

OUT = WT + '/corpus/v2/release-notes'
PID = 'release-notes'
SEED = 20261021
XMLNS = 'xmlns:MadCap="http://www.madcapsoftware.com/Schemas/MadCap.xsd"'


def sh(*a):
    return kit(*a)


def stage1():
    clean_out(OUT)
    rn_build.main(); rn_hubs.main()
    sh('import', '--src', SP + '/rn-src', '--out', OUT, '--name', PID, '--seed', str(SEED), '--snippet-min', '5',
       '--var', 'Kong=General.ProductName:Acme Orbit', '--var', 'VAR_PRODUCT=General.ProductName:Acme Orbit',
       '--var', 'VAR_CURRENT_RELEASE=Versions.CurrentRelease:3.9.1', '--var', 'VAR_OLDEST_SUPPORTED=Versions.OldestSupported:3.6.0')
    sh('scaffold', '--project', OUT, '--name', PID, '--conditions', 'Print,ScreenOnly',
       '--glossary', 'Control plane=The part of a deployment that manages configuration.;Data plane=The part of a deployment that proxies traffic.;Plugin=An extension that adds behavior to a route or service.')


def rd(p): return open(OUT + '/' + p, encoding='utf-8').read()


def wr(p, s):
    os.makedirs(os.path.dirname(OUT + '/' + p), exist_ok=True)
    open(OUT + '/' + p, 'w', encoding='utf-8', newline='\n').write(s)


def stage2():
    # text snippet used by a few hub topics
    wr('Content/Resources/Snippets/support-contact.flsnp',
       '<?xml version="1.0" encoding="utf-8"?>\n<html %s>\n    <head />\n    <body>\n        <p>the <MadCap:variable name="General.ProductName" /> support portal</p>\n    </body>\n</html>\n' % XMLNS)
    ts = '<MadCap:snippetText src="../Resources/Snippets/support-contact.flsnp" />'
    for f, a, b in [('Content/index/upgrade-guide.htm', 'then contact support.', 'then contact ' + ts + '.'),
                    ('Content/index/roll-back.htm', 'Contact support before', 'Contact ' + ts + ' before'),
                    ('Content/index/issue-ids.htm', 'ask support.', 'ask ' + ts + '.'),
                    ('Content/index/faq.htm', 'available from support.', 'available from ' + ts + '.')]:
        s = rd(f)
        assert a in s, (f, a)
        wr(f, s.replace(a, b))
    # CorpusKit import leaves xrefs inside snippets relative to the topic folder; rebase them to the snippet folder
    sp = 'Content/Resources/Snippets/back-up-your-configuration.flsnp'
    t = rd(sp)
    wr(sp, t.replace('href="../index/upgrade-guide.htm"', 'href="../../index/upgrade-guide.htm"'))
    # conditions
    s = rd('Content/index/subscribe.htm')
    a = '<p>Join the releases channel'
    assert a in s
    wr('Content/index/subscribe.htm', s.replace(a, '<p MadCap:conditions="Default.ScreenOnly">Join the releases channel'))
    s = rd('Content/index/versioning-policy.htm')
    a = 'usually within 2 weeks of the report'
    assert a in s
    wr('Content/index/versioning-policy.htm', s.replace(a, '<span MadCap:conditions="Default.Print">' + a + '</span>'))
    # TOC: About, Upgrade, then one folder per release (newest first)
    sm = json.load(open(SP + '/rn-summary.json'))
    toc = ['<?xml version="1.0" encoding="utf-8"?>', '<CatapultToc Version="1">']
    L = lambda t, l, ind: '%s<TocEntry Title="%s" Link="/Content/%s" />' % (ind, t, l)
    toc.append('    <TocEntry Title="About the release notes">')
    for n, t in [('about-release-notes', 'About these release notes'), ('release-history', 'Release history'), ('change-types', 'Types of change'), ('issue-ids', 'Issue ids'),
                 ('versioning-policy', 'Versioning policy'), ('supported-versions', 'Supported versions'), ('deprecation-policy', 'Deprecation policy'),
                 ('known-issues', 'Known issues'), ('faq', 'Release notes FAQ'), ('subscribe', 'Get release announcements')]:
        toc.append(L(t, 'index/%s.htm' % n, '        '))
    toc.append('    </TocEntry>')
    toc.append('    <TocEntry Title="Upgrade">')
    toc.append(L('Upgrade guide', 'index/upgrade-guide.htm', '        '))
    toc.append(L('Roll back an upgrade', 'index/roll-back.htm', '        '))
    toc.append('    </TocEntry>')
    for r in sm:
        v = r['v']; sl = v.replace('.', '-')
        toc.append('    <TocEntry Title="Release %s" Link="/Content/releases/release-%s.htm">' % (v, sl))
        if r['new']: toc.append(L('New in %s' % v, 'releases/new-%s.htm' % sl, '        '))
        if r['fix']: toc.append(L('Fixes in %s' % v, 'releases/fixes-%s.htm' % sl, '        '))
        if r['bc']: toc.append(L('Breaking changes in %s' % v, 'upgrade/breaking-%s.htm' % sl, '        '))
        toc.append('    </TocEntry>')
    toc.append('</CatapultToc>')
    wr('Project/TOCs/Main.fltoc', '\n'.join(toc) + '\n')
    # Project override: the Timeless-Documentation rows are off here
    ini = ['# House Style project overrides for the release notes.', '# Release notes are time-stamped content, so the Timeless documentation rows do not apply.', '']
    for n in range(1, 21):
        ini.append('# by docs-team on 2026-10-21: Release notes describe change over time, so time words such as now and new are correct here.')
        ini.append('google:timeless-documentation/%d = off' % n)
    wr('Content/Resources/StyleGuide/overrides.ini', '\n'.join(ini) + '\n')


def stage3():
    lp = OUT + '/labels.jsonl'
    if os.path.exists(lp): os.remove(lp)
    kinds = 'utilize,in_order_to,prior_to,is_able_to,please,simply,please_note,whitelist,blacklist,the_user,latin,exclaim,will,we,claims_guaranteed,claims_free,passive,wordy,vague,undefined_abbr,click_here,phone,url'
    sh('inject', '--project', OUT, '--seed', str(SEED), '--density', '0.10', '--project-id', PID, '--kinds', kinds)


TIME = re.compile(r"\b(now|currently|soon|latest|newer|new|newly|no longer|previously|future|eventually|recently|planned|coming weeks)\b", re.I)
QUOTED = re.compile(r'"[^"\n]{6,80}"')
UNIT = re.compile(r'^\d+(\.\d+)? ?(GB|MB|s|months?)$')


def stage4():
    lp = OUT + '/labels.jsonl'
    existing = fix_injector_labels(read_labels(lp))
    P = Proj(OUT, PID)
    S = LabelSet(P, existing)
    snippet_boiler = 'Content/Resources/Snippets/release-dates-use-utc.flsnp'
    for rel in sorted(P.blocks):
        par = {c: p for p in P.docs[rel].iter() for c in p}
        for b in P.blocks[rel]:
            codes = inline_spans(b, ('code',))
            varr = [(s.start, s.end) for s in b.segs if s.var]
            def free(st, en):
                return not S.overlaps_seeded(b, st, en) and not any(st < c1 and en > c0 for c0, c1 in codes) and not any(st < v1 and en > v0 for v0, v1 in varr)
            # exempt: time words in release notes
            for m in TIME.finditer(b.text):
                if free(m.start(), m.end()):
                    S.add_span(b, m.start(), m.end(), 'exempt', ['google:timeless-documentation/*'], reason='Release notes are time-stamped content; the project turns the Timeless documentation rows off.')
            label_curly(S, b)
            # exempt: inline code
            for c0, c1 in codes:
                if c1 - c0 >= 3 and not S.overlaps_seeded(b, c0, c1):
                    S.add_span(b, c0, c1, 'exempt', ['*'], reason='Inline code is a literal value; prose style rules do not apply.')
            # clean: bold UI labels in numbered steps
            for el_start, el_end in inline_spans(b, ('b', 'strong')):
                anc_ol = any(pref(a.tag) == 'ol' for a in anc(par, b.el))
                if anc_ol and not S.overlaps_seeded(b, el_start, el_end):
                    S.add_span(b, el_start, el_end, 'clean', ['*'], reason='UI label in a step; it must match the product verbatim.')
            # exempt: quoted text
            for m in QUOTED.finditer(b.text):
                if free(m.start(), m.end()):
                    S.add_span(b, m.start(), m.end(), 'exempt', ['*'], reason='Quoted message or value that is reproduced verbatim.')
            # clean: units in table cells
            if pref(b.el.tag) == 'td' and UNIT.match(b.text):
                S.add_span(b, 0, len(b.text), 'clean', ['google:units-of-measure/*', 'google:numbers/*'], reason='Unit written with a space in a table column, as the guide requires.')
            # clean: boilerplate snippet
            if rel == snippet_boiler:
                S.out.append(dict(id=S.next_id('N'), file=b.file, block=b.xpath, label='clean', rules=['*'], reason='Boilerplate repeated in every release topic; reviewed once in the snippet.', origin='natural', status='verified', by=['hand-read'], split=split_of(b.file)))
            S.block_complete(b)
    # seeded violations that the author wrote into the hub topics (hard-coded phone, url)
    for rel, quote, kind in [('Content/index/subscribe.htm', '1-800-555-0100', 'phone'), ('Content/index/subscribe.htm', 'https://docs.example.com/releases/feed.xml', 'url'), ('Content/index/subscribe.htm', 'releases@example.com', 'url')]:
        for b in P.blocks[rel]:
            at = b.text.find(quote)
            if at >= 0:
                S.add_span(b, at, at + len(quote), 'violation', RULES_BY_KIND[kind], kind=kind, origin='seeded', by=('generator',), status='verified',
                           seed={'generator': 'release-notes-author@1', 'variant': 'hard-coded'}, fix='a variable or a link to the support page')
    allr = existing + S.out
    for l in allr: l['split'] = split_of(l['file'])
    write_labels(lp, allr)
    print('labels', len(allr), {k: sum(1 for l in allr if l['label'] == k) for k in ('violation', 'clean', 'exempt', 'block-complete')})


def anc(parents, el):
    cur = parents.get(el)
    while cur is not None:
        yield cur
        cur = parents.get(cur)


def stage5():
    sh('contexts', '--project', OUT, '--update')
    sh('check', '--project', OUT)


def stats(root, pid):
    P = Proj(root, pid)
    labels = read_labels(root + '/labels.jsonl')
    nblocks = sum(len(v) for v in P.blocks.values())
    bc = sum(1 for l in labels if l['label'] == 'block-complete')
    files = sorted(P.docs)
    topics = [f for f in files if f.endswith(('.htm', '.html'))]
    init_split(files)
    test_files = [f for f in files if split_of(f) == 'test']
    by = {}
    for l in labels: by[l['label']] = by.get(l['label'], 0) + 1
    kinds = {}
    for l in labels:
        if l['label'] == 'violation': kinds[l['kind']] = kinds.get(l['kind'], 0) + 1
    return dict(blocks=nblocks, blockComplete=bc, files=len(files), topics=len(topics), snippets=len(files) - len(topics), testFiles=len(test_files), labels=len(labels), byLabel=by, violationKinds=dict(sorted(kinds.items())), proposed=sum(1 for l in labels if l['status'] != 'verified'), testFileList=test_files)


def stage6():
    st = stats(OUT, PID)
    pj = {
        'id': PID, 'name': 'Release notes', 'description': 'Release notes for the Acme Orbit API product: 24 releases, list-heavy, issue ids, breaking-change notes. Time words are correct here; the project override turns the Timeless documentation rows off.',
        'schema': 'corpus/schema/labels.schema.json', 'kind': 'imported-real-text',
        'topics': st['topics'], 'snippets': st['snippets'], 'releases': 24,
        'split': {'rule': 'FNV-1a bucket of the file path (CorpusKit Rng.Bucket, seed 0): below 70 is dev, otherwise test', 'testFiles': st['testFiles'], 'files': st['files'], 'testShare': round(st['testFiles'] / float(st['files']), 3)},
        'license': {'content': 'Apache-2.0', 'source': 'Kong/kong CHANGELOG.md and CHANGELOG-OLD.md', 'file': 'LICENSE'},
        'sources': 'SOURCES.md', 'variables': {'General.ProductName': 'Acme Orbit', 'General.CompanyName': 'Acme Software', 'Versions.CurrentRelease': '3.9.1', 'Versions.OldestSupported': '3.6.0'},
        'sharedWith': {'api-docs': ['General.ProductName', 'General.CompanyName']},
        'override': 'Content/Resources/StyleGuide/overrides.ini turns google:timeless-documentation/1..20 off',
        'labels': {'total': st['labels'], 'byLabel': st['byLabel'], 'violationKinds': st['violationKinds'], 'blocks': st['blocks'], 'blockComplete': st['blockComplete'], 'blockCompleteShare': round(st['blockComplete'] / float(st['blocks']), 3), 'proposed': st['proposed']},
        'authors': ['Kong Inc. and contributors (changelog text, Apache-2.0)', 'FlareAI corpus authors (hub topics, markup, labels)'],
        'seed': SEED,
    }
    wr('project.json', json.dumps(pj, indent=2, ensure_ascii=False) + '\n')
    print(json.dumps(pj['labels']), pj['split'])
    shutil.copy(SP + '/kong-LICENSE.txt', OUT + '/LICENSE')


if __name__ == '__main__':
    st = sys.argv[1:] or ['1', '2', '3', '4', '5', '6']
    for s in st: globals()['stage' + s]()
