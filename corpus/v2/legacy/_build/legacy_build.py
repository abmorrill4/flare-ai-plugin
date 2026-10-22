"""Stage 1 and 2 for the legacy project: generate topics (with plant markers) and the Flare project skeleton."""
import copy, json, os, random, re, shutil, sys
import os as _os; sys.path.insert(0, _os.path.dirname(_os.path.abspath(__file__)))
from legacy_plan import *


def moji(s):
    return s.replace('\u2019', '\u00e2\u20ac\u2122').replace('\u201c', '\u00e2\u20ac\u0153').replace('\u201d', '\u00e2\u20ac\ufffd').replace('\u2018', '\u00e2\u20ac\u02dc')


def mutate(c, secs):
    secs = copy.deepcopy(secs)
    for i, s in enumerate(secs):
        if s[0] == 'steps':
            st = list(s[1])
            r = c.rng.random()
            if r < 0.4 and len(st) > 3: st.pop(c.rng.randrange(1, len(st) - 1))
            elif r < 0.7 and len(st) > 3:
                j = c.rng.randrange(1, len(st) - 2); st[j], st[j + 1] = st[j + 1], st[j]
            st = [re.sub(r'\bSave\b', 'Apply', x) if c.chance(0.5) else x for x in st]
            st = [x.replace('Click ', 'Press ') if c.chance(0.3) else x for x in st]
            secs[i] = ('steps', st)
    return secs


def generate():
    clean_out(OUT)
    rng = random.Random(SEED)
    used = set()
    topics = []
    by_mod = {}

    def new_ctx(era): return Ctx(random.Random(rng.randrange(1 << 30)), era, '')

    for mk, m in MODS.items():
        titles = []
        for verb in m['tasks']:
            era = rng.choice(ERAS)
            c = new_ctx(era)
            mm = dict(m); mm['key'] = mk
            ov = OBJ_OVERRIDE.get((mk, verb))
            if ov: mm['obj'], mm['objs'] = ov
            title = title_of(c, mm, verb)
            file = fname(c, m, title, era, used)
            t = build_task(c, mk, verb, file, title)
            t.update(file=file, era=era, ctx=c, mod=mk, verb=verb)
            topics.append(t); by_mod.setdefault(mk, []).append(t)
            titles.append(title)
        era = rng.choice(['C', 'D', 'B'])
        c = new_ctx(era)
        file = fname(c, m, 'About ' + m['name'], era, used)
        t = overview(c, mk, titles, file)
        t.update(file=file, era=era, ctx=c, mod=mk, verb='overview')
        topics.append(t); by_mod.setdefault(mk, []).append(t)

    # special topics
    for sp in special_topics(lambda era: new_ctx(era)):
        c = new_ctx(sp['era'])
        file = 'Content/%s/%s.htm' % (sp['folder'], sp['name'])
        if file.lower() in used: file = file.replace('.htm', '2.htm')
        used.add(file.lower())
        t = dict(title=sp['title'], sections=sp['sections'], file=file, era=sp['era'], ctx=c, mod=None, verb='special', menu='Main')
        topics.append(t)
    for name, era in [('home_page', 'B'), ('Dashboard guide.doc', 'A'), ('quick-reference', 'C')]:
        c = new_ctx(era)
        inner = nested_layout(c, era, name)
        inner = inner.replace('<table border="1" cellpadding="2">', '<table border="1" cellpadding="2" x-plant="table_header">', 1)
        secs = [('p', 'The main screens are listed below.'), ('raw', inner), ('p', 'Choose a screen to begin.')]
        folder = 'WordImport' if era == 'A' else 'Reference'
        file = 'Content/%s/%s.htm' % (folder, name)
        used.add(file.lower())
        topics.append(dict(title=name.replace('_', ' ').replace('.doc', '').title(), sections=secs, file=file, era=era, ctx=c, mod=None, verb='special', menu='Main'))

    # drifted duplicates
    base = [t for t in topics if t['verb'] not in ('overview', 'special') and t['mod'] in ('wo', 'cust', 'inv', 'sched', 'adm', 'tech', 'parts', 'route')]
    picks = rng.sample(base, 8)
    for i, b in enumerate(picks):
        for j, (era, prefix, folder) in enumerate([('B', 'Copy of ', 'Archive'), ('A', '', 'WordImport')]):
            c = new_ctx(era)
            title = b['title'] + c.pick([' (old)', ' v2', ' - draft', ' (copy)']) if j == 0 else b['title']
            name = (prefix + slugify(b['title'], '_') + ('_old' if j == 1 else '')) if era == 'B' else camel(b['title']) + 'v%d' % (i + 2)
            file = 'Content/%s/%s.htm' % (folder, name)
            n = 2
            while file.lower() in used: file = 'Content/%s/%s%d.htm' % (folder, name, n); n += 1
            used.add(file.lower())
            topics.append(dict(title=title, sections=mutate(c, b['sections']), file=file, era=era, ctx=c, mod=None, verb='drift', menu=b.get('menu'), drift_of=b['file']))

    # related links (C and D topics): siblings in the same module; some point at topics that no longer exist
    for t in topics:
        if t['era'] in 'CD' and t.get('mod') and t['verb'] != 'overview':
            sibs = [s for s in by_mod[t['mod']] if s is not t and s['verb'] != 'overview']
            pick = t['ctx'].rng.sample(sibs, min(len(sibs), t['ctx'].rng.choice([1, 2, 3])))
            items = [(s['title'], s['file']) for s in pick]
            if t['ctx'].chance(0.18):
                gone = 'Content/Archive/Old %s.htm' % t['ctx'].pick(['Setup Guide', 'Work Order Entry', 'Dispatch Board', 'Invoice Wizard', 'Price Import'])
                items.append(('the old topic', gone))
                KNOWN['broken_xrefs'].append({'from': t['file'], 'to': gone})
            t['sections'].append(('related', items))

    # render and write
    moji_files = set(rng.sample([t['file'] for t in topics if t['era'] == 'A'], 6))
    texts = {}
    for t in topics:
        html = render_topic(t['ctx'], t, t['era'])
        if t['file'] in moji_files:
            html = moji(html)
        texts[t['file']] = html
        full = OUT + '/' + t['file']
        os.makedirs(os.path.dirname(full), exist_ok=True)
        open(full, 'w', encoding='utf-8', newline='\n').write(html)
    # snippets
    sn = OUT + '/Content/Resources/Snippets/'
    os.makedirs(sn, exist_ok=True)
    open(sn + 'BackupWarning.flsnp', 'w', encoding='utf-8', newline='\n').write('<?xml version="1.0" encoding="utf-8"?>\n<html %s>\n<head />\n<body>\n<p class="warning">Back up the database before you continue. A backup is the only way to undo this change.</p>\n</body>\n</html>\n' % NS_MC)
    open(sn + 'ProductName.flsnp', 'w', encoding='utf-8', newline='\n').write('<?xml version="1.0" encoding="utf-8"?>\n<html %s>\n<head />\n<body>\n<p>Marlowe Dispatch</p>\n</body>\n</html>\n' % NS_MC)
    # images
    for i, n in enumerate(IMAGES):
        write_png(OUT + '/Content/Resources/Images/' + n, 48, 32, rgb=(60 + 25 * i, 110, 170 - 20 * i), text_seed=i)
    json.dump({'topics': [dict(file=t['file'], era=t['era'], title=t['title'], mod=t.get('mod'), verb=t.get('verb'), drift_of=t.get('drift_of')) for t in topics]}, open(SP + '/legacy-plan.json', 'w'), indent=1)
    print('topics', len(topics), {e: sum(1 for t in topics if t['era'] == e) for e in 'ABCD'}, 'moji', len(moji_files))
    return topics


def project_files(topics):
    kit('scaffold', '--project', OUT, '--name', PID, '--conditions', 'Print,ScreenOnly,Obsolete,V2Only',
        '--glossary', 'Dispatcher=A person who schedules jobs and assigns them.;Depot=The place where a technician starts the day.;Stop=One visit on a route.;Sync=The exchange of data between the mobile app and the server.;Resolution=The description of the work that was done.')
    flv = ('<?xml version="1.0" encoding="utf-8"?>\n<CatapultVariableSet>\n    <Variable Name="CompanyName" EvaluatedDefinition="Marlowe Systems">Marlowe Systems</Variable>\n'
           '    <Variable Name="ProductName" EvaluatedDefinition="Marlowe Dispatch">Marlowe Dispatch</Variable>\n'
           '    <Variable Name="SupportPhone" EvaluatedDefinition="1-800-555-0142">1-800-555-0142</Variable>\n'
           '    <Variable Name="SupportUrl" EvaluatedDefinition="http://support.marlowe-dispatch.example.com/help">http://support.marlowe-dispatch.example.com/help</Variable>\n</CatapultVariableSet>\n')
    open(OUT + '/Project/VariableSets/General.flvar', 'w', encoding='utf-8', newline='\n').write(flv)
    # TOC: folder entries per module; about 15% of topics are left out (orphans) and some entries point at topics that were deleted
    rng = random.Random(SEED + 5)
    inc = [t for t in topics if t['verb'] not in ('drift',)]
    groups = {}
    for t in inc:
        folder = t['file'].split('/')[1]
        groups.setdefault(folder, []).append(t)
    toc = ['<?xml version="1.0" encoding="utf-8"?>', '<CatapultToc Version="1">']
    orphans = []
    for g in sorted(groups):
        toc.append('    <TocEntry Title="%s">' % esc(g if g != 'WordImport' else 'Imported from Word'))
        for t in sorted(groups[g], key=lambda x: x['file']):
            if rng.random() < 0.12:
                orphans.append(t['file']); continue
            lbl = '[%%=System.LinkedTitle%%]' if rng.random() < 0.8 else esc(t['title'])
            toc.append('        <TocEntry Title="%s" Link="/%s" />' % (lbl, urllib.parse.quote(t['file'], safe='/.-_~')))
        toc.append('    </TocEntry>')
    gone = ['Content/Archive/Old Setup Guide.htm', 'Content/Archive/Old Work Order Entry.htm', 'Content/Workflow/Dispatch Board.htm', 'Content/Archive/Price Import.htm', 'Content/Workflow/Invoice Wizard.htm', 'Content/Reports/Weekly Summary.htm']
    toc.append('    <TocEntry Title="Old topics">')
    for g in gone:
        toc.append('        <TocEntry Title="%s" Link="/%s" />' % (esc(os.path.basename(g)[:-4]), urllib.parse.quote(g, safe='/.-_~')))
        KNOWN['orphan_toc'].append(g)
    toc.append('    </TocEntry>')
    toc.append('</CatapultToc>')
    open(OUT + '/Project/TOCs/Main.fltoc', 'w', encoding='utf-8', newline='\n').write('\n'.join(toc) + '\n')
    KNOWN['topics_not_in_toc'] = orphans
    json.dump(KNOWN, open(OUT + '/known-breaks.json', 'w'), indent=1)
    print('toc orphans (topics not in TOC)', len(orphans), 'toc entries to missing files', len(gone), 'broken xrefs', len(KNOWN['broken_xrefs']))


if __name__ == '__main__':
    tp = generate()
    project_files(tp)
