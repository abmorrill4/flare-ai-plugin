"""Writes project.json, LABELS.md, SOURCES.md, NOTICE.md, terms.json for the three corpus projects from the built files and labels."""
import json, os, re, sys, shutil, math
import os as _os; sys.path.insert(0, _os.path.dirname(_os.path.abspath(__file__)))
from corplib import *
from labeler import *

ROOTS = {'release-notes': WT + '/corpus/v2/release-notes', 'localized-style': WT + '/corpus/v2/localized-style', 'legacy': WT + '/corpus/v2/legacy'}


def read_any(path):
    b = open(path, 'rb').read()
    if b.startswith(b'\xef\xbb\xbf'): return b[3:].decode('utf-8')
    head = b[:200].decode('ascii', 'replace')
    if 'windows-1252' in head: return b.decode('cp1252')
    return b.decode('utf-8')


def stats(pid):
    root = ROOTS[pid]
    P = Proj(root, pid)
    labels = read_labels(root + '/labels.jsonl')
    files = sorted(f for f in P.docs if '/StyleGuide/' not in f)
    init_split(files)
    topics = [f for f in files if f.endswith(('.htm', '.html')) and '/StyleGuide/' not in f]
    nblocks = sum(len(v) for f, v in P.blocks.items() if '/StyleGuide/' not in f)
    by = {}
    for l in labels: by[l['label']] = by.get(l['label'], 0) + 1
    kinds = {}
    for l in labels:
        if l['label'] == 'violation': kinds[l['kind']] = kinds.get(l['kind'], 0) + 1
    scored = [f for f in files if '/StyleGuide/' not in f]
    test_files = [f for f in scored if split_of(f) == 'test']
    seeded = sum(1 for l in labels if l['label'] == 'violation' and l['seed'].get('generator', '').startswith('corpuskit'))
    return dict(P=P, labels=labels, files=scored, topics=topics, blocks=nblocks, by=by, kinds=dict(sorted(kinds.items())), test_files=test_files,
                proposed=sum(1 for l in labels if l['status'] != 'verified'), injected=seeded)


def write(root, name, text):
    open(root + '/' + name, 'w', encoding='utf-8', newline='\n').write(text)


def table(rows, head):
    out = ['| ' + ' | '.join(head) + ' |', '|' + '|'.join('---' for _ in head) + '|']
    for r in rows: out.append('| ' + ' | '.join(str(x) for x in r) + ' |')
    return '\n'.join(out)


def common_labels_md(pid, st, intro, extra=''):
    n = st['by']
    viol = n.get('violation', 0)
    kinds = table([(k, v) for k, v in st['kinds'].items()], ['kind', 'violations'])
    return """# Labels: {pid}

{intro}

## Density

| | |
|---|---|
| Topics | {topics} (files under `Content/`, snippets included: {files}) |
| Blocks (plain-text blocks the labels anchor to) | {blocks} |
| Labels | {total} |
| `violation` | {viol} ({vper:.1f} per topic) |
| `clean` | {clean} |
| `exempt` | {exempt} |
| `ambiguous` | {amb} |
| `block-complete` | {bc} ({bcs:.0f}% of blocks; status `proposed`, see below) |
| Violations written by CorpusKit `inject` | {inj} |
| Violations written by the corpus author's generator | {auth} |
| Files in the `test` split | {tf} of {ff} ({tp:.1f}%) |

## Violations by kind

{kinds}

## How the labels were made

- **Authored violations** (`origin: seeded`, `seed.generator` names the author script: `release-notes-author@1`, `localized-style-author@1` or `legacy-gen@1`, `by: [generator]`, `status: verified`): the generator that wrote the text knows exactly which spans it made defective, so these labels are exact. The quote, prefix and suffix are computed from the final DOM.
- **Injected violations** (`seed.generator: corpuskit/inject@1`): `corpuskit inject --seed N` on the finished text, in blocks that carry no other label. CorpusKit writes `status: proposed` and rule hints that are not scorer rule ids (`company:plain-language`, `structure:hard-coded-value`); a post-step replaces `rules` with the scorer's families (`google:*`, `company:*`, `structure:AltText`, `structure:HardCoded`, ...) and sets `status: verified`, because the generator is exact. No label comes from running the linter.
- **`clean` and `exempt`** (`by: [hand-read]`, `status: verified`): derived from the markup by rule, then read as a class, not span by span: bold UI labels in numbered steps, `code` and `kbd` text, quoted program messages, values with units in table cells, and boilerplate. Each carries a `reason`.
- **`block-complete`** (`status: proposed`, `by: [generator]`): one label per block. It asserts that every lint-detectable problem in the block is labelled. That is true of the generator-authored text by construction only for the defect kinds in `corpus/schema/defects.yaml`; it has **not** been confirmed by the blind two-labeller pass of design 1.4 (no labeller tool exists yet), so the labels are `proposed` and the scorer ignores them until that pass promotes them. Do not read the {bcs:.0f}% as a verified figure.
- Contexts (`context.line`, `context.span`) are written by `corpuskit contexts --update`, never by `FlareAI.Core`.
- `split`: FNV-1a bucket of the file path (CorpusKit `Rng.Bucket`, seed 0), below 70 is `dev`; if that left fewer than 30% of the files in `test`, the remaining files with the highest bucket were added to `test`. The list is in `project.json`.
{extra}
## Known gaps

- Defects in the taxonomy only: wording problems that no kind names (for example a misspelling in the real changelog text, or a Google rule with no taxonomy kind) are not labelled, so a finding there is "unscored", not a false positive, until a block is promoted to `block-complete` after the blind pass.
- Hard-coded email addresses are labelled with kind `url` (hard-coded value) because the taxonomy has no email kind; `structure:HardCoded` flags them.
- Inter-annotator agreement (kappa) is not recorded: there was one author and no second labeller.
""".format(pid=pid, intro=intro, topics=len(st['topics']), files=len(st['files']), blocks=st['blocks'], total=len(st['labels']), viol=viol, vper=viol / float(len(st['topics'])),
           clean=n.get('clean', 0), exempt=n.get('exempt', 0), amb=n.get('ambiguous', 0), bc=n.get('block-complete', 0), bcs=100.0 * n.get('block-complete', 0) / st['blocks'],
           inj=st['injected'], auth=viol - st['injected'], tf=len(st['test_files']), ff=len(st['files']), tp=100.0 * len(st['test_files']) / len(st['files']), kinds=kinds, extra=extra)


def project_json(pid, st, extra):
    n = st['by']
    d = {'id': pid, 'schema': 'corpus/schema/labels.schema.json', 'topics': len(st['topics']), 'files': len(st['files']), 'blocks': st['blocks'],
         'split': {'rule': 'FNV-1a bucket of the file path (CorpusKit Rng.Bucket, seed 0): below 70 is dev; topped up to at least 30% of files', 'testFiles': len(st['test_files']),
                   'testShare': round(len(st['test_files']) / float(len(st['files'])), 3), 'test': st['test_files']},
         'labels': {'total': len(st['labels']), 'byLabel': n, 'violationKinds': st['kinds'], 'blockCompleteShare': round(n.get('block-complete', 0) / float(st['blocks']), 3),
                    'blockCompleteStatus': 'proposed', 'proposed': st['proposed']}}
    d.update(extra)
    return json.dumps(d, indent=2, ensure_ascii=False) + '\n'


def release_notes():
    pid = 'release-notes'; root = ROOTS[pid]; st = stats(pid)
    sm = json.load(open(SP + '/rn-summary.json'))
    import rn_build
    tot = drop = 0
    for v in rn_build.SELECT:
        for sec, areas in rn_build.allr[v]['secs'].items():
            if sec == 'Dependencies': continue
            for area, items in areas.items():
                for it in items:
                    tot += 1
                    if not rn_build.keep(it): drop += 1
    dates = json.load(open(SP + '/dates.json'))
    write(root, 'project.json', project_json(pid, st, {
        'name': 'Release notes', 'kind': 'imported-real-text + synthetic hubs',
        'description': 'Release notes for the Acme Orbit API product: 24 releases, 60 topics, list-heavy, with issue ids, breaking-change notes and time words. A project override turns the Timeless documentation rows of the Google guide off.',
        'releases': len(sm), 'license': {'content': 'Apache-2.0', 'file': 'LICENSE', 'attribution': 'NOTICE.md', 'sources': 'SOURCES.md'},
        'override': 'Content/Resources/StyleGuide/overrides.ini: google:timeless-documentation/1 to 20 = off',
        'variables': {'General.ProductName': 'Acme Orbit', 'General.CompanyName': 'Acme Software', 'Versions.CurrentRelease': '3.9.1', 'Versions.OldestSupported': '3.6.0'},
        'sharedWith': {'api-docs': 'Use the same General.ProductName and General.CompanyName values; Versions.* are defined here and can be reused by api-docs.'},
        'seed': 20261021}))
    rows = []
    for r in sm:
        rows.append((r['v'], pretty(r['date']), 'CHANGELOG-OLD.md' if r['v'].startswith(('1.', '2.')) else 'CHANGELOG.md',
                     'Content/releases/release-%s.htm' % r['v'].replace('.', '-')))
    write(root, 'SOURCES.md', """# Sources: release-notes

## Imported text

| | |
|---|---|
| Project | Kong Gateway (open-source edition), repository `Kong/kong` |
| Files | `CHANGELOG.md` (releases 3.x) and `CHANGELOG-OLD.md` (releases 1.x and 2.x) |
| URL | https://github.com/Kong/kong/blob/21032122ab80c973f368f6bc539f53985e7164e8/CHANGELOG.md and https://github.com/Kong/kong/blob/21032122ab80c973f368f6bc539f53985e7164e8/CHANGELOG-OLD.md |
| Pinned commit | `21032122ab80c973f368f6bc539f53985e7164e8` (default branch `master`, committed 2026-10-19T02:44:32Z, fetched 2026-10-21) |
| Licence | Apache License 2.0. Verified by reading `LICENSE` at the pinned commit ("Apache License, Version 2.0, January 2004", "Copyright 2016-2026 Kong Inc."); GitHub's licence API reports `Apache-2.0`. The repository has no `NOTICE` file at that commit. |
| Licence text | `LICENSE` in this folder is the pinned commit's `LICENSE` verbatim. |
| Attribution | Changelog text © Kong Inc. and the contributors of the changelog entries, Apache-2.0. See `NOTICE.md`. |
| Release dates | 3.x dates (except 3.0.0) come from the GitHub releases API (`published_at`) for tags 3.9.1, 3.9.0, 3.8.0, 3.7.0, 3.6.0, 3.5.0, 3.4.0, 3.3.0, 3.2.0, 3.1.0, 2.8.0, 2.7.0 and 2.5.0; other dates come from the changelog. Dates are facts, not expression. |

## What was used

24 releases (the table below). From each, the Breaking changes, Deprecations, Changes, Features, Performance and Fixes entries. Of {tot} entries considered, {drop} were dropped by a filter that removes entries naming third-party products, platforms or services (operating systems, databases, language runtimes, cloud and observability vendors, libraries, CVE ids) so that no real product name appears as the "our product" or as a neighbour of it, and each section was capped to keep topics a readable size. `Dependencies` sections were not used.

## What was changed (modifications, as Apache-2.0 section 4(b) asks)

- Converted from Markdown to Flare XHTML topics with CorpusKit `import`; one overview topic for each release, plus separate "New in", "Fixes in" and "Breaking changes" topics for some.
- The product name "Kong" (and "Kong Gateway") is replaced by the variable `General.ProductName` ("Acme Orbit"); command and header names such as `kong`, `KONG_`, `X-Kong-...` are renamed to `orbit`, `ORBIT_`, `X-Orbit-...`.
- Pull request and issue links are removed. The numbers are kept as issue ids written `ORB-n`.
- Thanks and credit sentences, user handles and Jira ids are removed. No person's name appears in the text.
- Emoji shortcodes and "Back to TOC" links are removed; wrapped lines are joined; the sentence "Release dates use UTC ..." and the intro sentences about counts and time words are original text added by the corpus authors.
- Defects were then injected (`corpuskit inject`, seed 20261021, density 0.10; see `LABELS.md`). The injected defects are not in the upstream text.

The 12 topics under `Content/index/` and the three "Before you upgrade" and boilerplate snippets are original text written for this corpus (not from the changelog). The glossary entries are original.

## Releases

{rows}
""".format(tot=tot, drop=drop, rows=table(rows, ['Version', 'Date', 'Source file', 'Topic'])))
    write(root, 'NOTICE.md', """# Notice: release-notes

The release text in `Content/releases/` and `Content/upgrade/` is adapted from the changelog of the Kong Gateway open-source project.

Copyright 2016-2026 Kong Inc. and contributors. Licensed under the Apache License, Version 2.0 (`LICENSE` in this folder). You may obtain a copy of the licence at http://www.apache.org/licenses/LICENSE-2.0.

Modified: the text was converted to Flare XHTML, filtered, rebranded to the fictional product "Acme Orbit", and seeded with defects. These files are not the upstream project's text and are not endorsed by Kong Inc. See `SOURCES.md` for the full list of changes.

Everything else in this folder (the topics under `Content/index/`, the project files, `labels.jsonl`) is original work of the FlareAI corpus authors.
""")
    extra = """
## Project-specific notes

- The imported entries are real text; the labels do not describe its natural problems (typos such as "Addded", "braking changes", doubled commas and unfinished sentences exist in the upstream text and were left in on purpose). Only the injected and authored defects are violations.
- `exempt` labels on time words ({tw} of {tot}): release notes legitimately say now, currently, soon, new, latest and no longer, and the project's `overrides.ini` turns `google:timeless-documentation/1` to `20` off. A finding from those rows anywhere in this project is a false positive; this is the fixture for the "turned off, still flagged" class of bug.
- Seeded kinds are dominated by `exclaim` because the injector's other operations need a matching phrase that list-style release notes rarely contain.
""".format(tw=sum(1 for l in st['labels'] if l['label'] == 'exempt' and 'timeless' in ' '.join(l['rules'])), tot=len(st['labels']))
    write(root, 'LABELS.md', common_labels_md(pid, st, 'Span labels for the 60-topic release-notes project (24 releases of an API gateway changelog plus 12 original hub topics).', extra))


def pretty(iso):
    y, m, d = iso.split('-')
    return '%s %d, %s' % (['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'][int(m) - 1], int(d), y)


def localized_style():
    pid = 'localized-style'; root = ROOTS[pid]; st = stats(pid)
    write(root, 'project.json', project_json(pid, st, {
        'name': 'Localized style', 'kind': 'synthetic', 'description': 'Admin guide for Acme Sign-On with its own Company style guide that overrides the Google guide; used as the team-guide fixture for scenario tests.',
        'guide': {'folder': 'Content/Resources/StyleGuide/Company', 'pages': ['About', 'Google-overrides', 'Terminology', 'UI-references'],
                  'overrides': {'google:word-list/443': 'off (log in allowed)', 'google:word-list/637': 'off (sign in preferred)', 'google:word-list/638': 'off (log in / log out flagged)', 'google:word-list/53': 'off (application, not app)', 'google:word-list/211': 'suggestion (Disable is a UI label)'}},
        'variables': {'General.ProductName': 'Acme Sign-On', 'General.CompanyName': 'Acme Software'},
        'license': 'Original text; no third-party content', 'seed': 20261022}))
    write(root, 'SOURCES.md', "# Sources: localized-style\n\nAll text in this project is original, written for the FlareAI corpus. No third-party content is used, so there is no licence to record. Names (Acme Sign-On, Acme Software) are fictional and are written through the variables `General.ProductName` and `General.CompanyName`.\n\nThe company guide pages under `Content/Resources/StyleGuide/Company/` follow the layout of the plugin's own company guide (`styleguide/`) but contain original rules for this fictional team.\n")
    write(root, 'NOTICE.md', "# Notice: localized-style\n\nNo third-party material. Original work of the FlareAI corpus authors.\n")
    extra = """
## Project-specific notes

- The Company guide (`Content/Resources/StyleGuide/Company/`) is part of the fixture. Its rows, by id: `Google-overrides` turns `google:word-list/443`, `637` and `638` (the log in / sign in guidance and the lint row that flags log in and log out) and `google:word-list/53` (app) off and lowers `google:word-list/211` (disable) to a suggestion; `Terminology` rule-3 to rule-12 are lint rows (users not customers/end users, organization not tenant, log in not sign in, single sign-on, identity provider, OpenID Connect, SAML 2.0); `UI-references` says labels are verbatim. The guide pages are not labelled and are excluded from block-complete; inject was run before they were written so no defect was injected into them.
- Company-over-Google cases: every "log in", "log out" and "login" in running text has a `clean` label for `google:word-list/443`, `637` and `638` (the company turns them off). Planted company violations (kinds `bank`, `customer`, `sso_spelling`, `idp`, `oidc`, `saml2`, rules `company:terminology/*`) are "sign in", "end user", "Single sign on", "Identity Provider", "SAML2", "OpenId Connect". One vendor mention of "Single Sign-On" is `exempt`.
- UI labels that must stay verbatim although a Google rule dislikes them (Whitelist, E-mail address, Kill session, Kill all sessions, Master key, Sign-On Settings, Disable, Enforce whitelist) are `exempt` for `google:word-list/*` and `company:terminology/*`, in bold and in the reference table.
- The company taxonomy kinds are borrowed from the DigitalBanking kinds (`customer`, `bank`, ...): `bank` is used for "sign in" and "tenant" (generic term where the guide requires the product's term). A dedicated kind would describe them better.
"""
    write(root, 'LABELS.md', common_labels_md(pid, st, 'Span labels for the 30-topic localized-style project.', extra))


def count_regex(blocks, rx):
    return sum(len(re.findall(rx, b.text)) for b in blocks)


def legacy():
    pid = 'legacy'; root = ROOTS[pid]; st = stats(pid)
    P = st['P']
    allblocks = [b for f in st['files'] for b in P.blocks.get(f, [])]
    # prose blocks: text outside b/i/strong/code/kbd/font
    def prose(b):
        keep = []
        for s in b.segs:
            if s.var or any(pref(a.tag) in ('b', 'i', 'strong', 'em', 'code', 'kbd') for a in s.anc): continue
            keep.append(b.text[s.start:s.end])
        return ' '.join(keep)
    prosetexts = [prose(b) for b in allblocks]
    groups = {
        'work order': {'work order': r'\bwork orders?\b', 'Work Order': r'\bWork Orders?\b', 'WO': r'\bWOs?\b', 'job ticket': r'\bjob tickets?\b', 'service call': r'\bservice calls?\b'},
        'technician': {'technician': r'\btechnicians?\b', 'Technician': r'\bTechnicians?\b', 'tech': r'\btechs?\b', 'field engineer': r'\bfield engineers?\b', 'field rep': r'\bfield reps?\b'},
        'customer': {'customer': r'\bcustomers?\b', 'Customer': r'\bCustomers?\b', 'client': r'\bclients?\b'},
        'log in': {'log in': r'\blog in\b', 'sign in': r'\bsign in\b', 'log on': r'\blog on\b', 'login': r'\blogin\b'},
        'email': {'email': r'\bemail\b', 'e-mail': r'\be-mail\b', 'E-mail': r'\bE-mail\b', 'Email': r'\bEmail\b'},
        'product name': {'Marlowe Dispatch': r'\bMarlowe Dispatch\b', 'MarloweDispatch': r'\bMarloweDispatch\b', 'Marlowe-Dispatch': r'\bMarlowe-Dispatch\b', 'MD': r'\bMD\b'},
    }
    out = {'method': 'Case-sensitive regular expressions over the plain text of every block in Content/ (the BlockWalker definition: variables expanded to their values, nested blocks separate). "all" counts every block; "prose" ignores text inside b, strong, i, em, code and kbd, so UI labels written in bold (Work Orders, Technicians, Customers) are out of it. Counts are exact for the delivered files; they change only if the files change.',
           'groups': {}}
    for g, forms in groups.items():
        out['groups'][g] = {}
        for form, rx in forms.items():
            out['groups'][g][form] = {'regex': rx, 'all': count_regex(allblocks, rx), 'prose': sum(len(re.findall(rx, t)) for t in prosetexts)}
    # variable usage and literals
    varuse = 0
    for f in st['files']:
        raw = read_any(root + '/' + f)
        varuse += len(re.findall(r'<MadCap:variable name="General\.ProductName"', raw))
    out['groups']['product name']['General.ProductName variable'] = {'regex': '<MadCap:variable name="General.ProductName" />', 'all': varuse, 'prose': varuse}
    write(root, 'terms.json', json.dumps(out, indent=2, ensure_ascii=False) + '\n')
    # feature census
    census = {}
    raws = {f: read_any(root + '/' + f) for f in st['files']}
    def nfiles(rx, flags=0): return sum(1 for r in raws.values() if re.search(rx, r, flags))
    census['pasted Word markup (MsoNormal)'] = nfiles(r'class="Mso')
    census['o:p elements'] = nfiles(r'<o:p>')
    census['inline style attributes'] = nfiles(r'style=')
    census['runs of three or more no-break spaces'] = nfiles(r'(&#160;){3,}')
    census['br used as a paragraph break'] = nfiles(r'<br />')
    census['empty paragraphs'] = nfiles(r'<p>&#160;</p>|<p></p>|<p class="MsoNormal"><o:p>&#160;</o:p></p>')
    census['bold or underlined paragraph used as a heading'] = nfiles(r'<p[^>]*><(?:u><)?b>[^<]+</b>(?:</u>)?</p>|<p><font[^>]*><b>[^<]+</b></font></p>|<center><b>') + nfiles(r'<p class="MsoNormal"><b><span[^>]*>[^<]+<o:p>')
    census['deprecated HTML (font, center, u, align)'] = nfiles(r'<font |<center>|<u>|align="center"')
    census['tables with 100 or more rows'] = sum(1 for r in raws.values() if r.count('<tr>') >= 100)
    census['nested tables'] = nfiles(r'<table[^>]*>(?:(?!</table>).)*<table', re.S)
    census['lists nested 7 levels'] = nfiles(r'\(level 7\)')
    census['MadCap:snippetText'] = nfiles(r'<MadCap:snippetText')
    census['MadCap:snippetBlock'] = nfiles(r'<MadCap:snippetBlock')
    census['inline conditions (MadCap:conditions on spans, list items)'] = nfiles(r'MadCap:conditions=')
    census['deprecated MadCap elements (MadCap:HelpControl)'] = nfiles(r'<MadCap:HelpControl')
    census['MadCap:dropDown'] = nfiles(r'<MadCap:dropDown>')
    census['MadCap:variable'] = nfiles(r'<MadCap:variable')
    census['images without alt attribute or with empty alt'] = sum(len(re.findall(r'<img (?![^>]*\balt="[^"]+")[^>]*>', r)) for r in raws.values())
    census['curly quotes or apostrophes'] = nfiles('[\u2018\u2019\u201c\u201d]')
    census['straight apostrophes or quotes in text'] = nfiles(r">[^<]*['\"][^<]*<")
    census['mojibake (UTF-8 read as Windows-1252)'] = nfiles('\u00e2\u20ac')
    enc = json.load(open(root + '/encodings.json'))
    census['UTF-8 with BOM'] = len(enc['utf8_bom']); census['saved as Windows-1252'] = len(enc['windows_1252'])
    census['.doc.htm file names'] = sum(1 for f in st['files'] if f.endswith('.doc.htm'))
    census['file names with spaces'] = sum(1 for f in st['files'] if ' ' in f)
    kb = json.load(open(root + '/known-breaks.json'))
    census['xrefs to topics that do not exist'] = len(kb['broken_xrefs'])
    census['TOC entries to topics that do not exist'] = len(kb['orphan_toc'])
    census['topics missing from the TOC (orphans)'] = len(kb['topics_not_in_toc'])
    plan = json.load(open(SP + '/legacy-plan.json'))['topics']
    census['drifted duplicate topics'] = sum(1 for t in plan if t['verb'] == 'drift')
    eras = {e: sum(1 for t in plan if t['era'] == e) for e in 'ABCD'}
    write(root, 'project.json', project_json(pid, st, {
        'name': 'Messy legacy', 'kind': 'synthetic', 'description': 'Help for the fictional Marlowe Dispatch field-service app after ten years, many authors and several migrations.',
        'eras': {'A': 'Word paste (%d topics)' % eras['A'], 'B': 'copy-paste web (%d)' % eras['B'], 'C': 'Flare-native but inconsistent (%d)' % eras['C'], 'D': 'modern but verbose (%d)' % eras['D']},
        'variables': {'General.ProductName': 'Marlowe Dispatch', 'General.CompanyName': 'Marlowe Systems', 'General.SupportPhone': '1-800-555-0142', 'General.SupportUrl': 'http://support.marlowe-dispatch.example.com/help'},
        'termForms': 'terms.json', 'knownBreaks': 'known-breaks.json', 'encodings': 'encodings.json', 'census': census, 'license': 'Original text; no third-party content', 'seed': 20261023}))
    write(root, 'SOURCES.md', "# Sources: legacy\n\nAll text and markup in this project is original, written for the FlareAI corpus to look like what long-lived help projects contain. No third-party content is used, so there is no licence to record. Names (Marlowe Dispatch, Marlowe Systems), phone numbers (555) and addresses (`example.com`) are fictional.\n")
    write(root, 'NOTICE.md', "# Notice: legacy\n\nNo third-party material. Original work of the FlareAI corpus authors.\n")
    write(root, '.gitattributes', "# Keep the byte order marks, Windows-1252 files and line endings exactly as delivered: they are test data.\n* -text\n")
    cen = table([(k, v) for k, v in census.items()], ['Feature', 'Files (or count)'])
    tg = out['groups']
    termtab = table([(g, form, d['all'], d['prose']) for g, forms in tg.items() for form, d in forms.items()], ['Concept', 'Form', 'Count (all blocks)', 'Count (prose only)'])
    extra = """
## Project-specific notes

### Eras

The {nt2} topics are written in four voices, assigned at random: A (Word paste, {a} topics), B (copy and paste from the web, {b}), C (Flare-native but inconsistent, {c}), D (modern but verbose, {d}). Defects are written into the sentences by the generator at a rate that falls from A to D, so density differs by era as in a real project.

### Legacy features present (census of the delivered files)

{cen}

### Competing term forms (Mine scenario)

`terms.json` holds the exact counts of competing forms (regex, count over all text and over prose only). Summary:

{termtab}

The product name is written four ways (the variable, the literal `Marlowe Dispatch`, `MarloweDispatch`, `Marlowe-Dispatch`, `MD`). Every literal `Marlowe Dispatch` and `Marlowe Systems` outside a variable is a `variable_product` / `variable_company` violation (`project:variables`), because `General.ProductName` and `General.CompanyName` exist. The other forms are the rival spellings the mining scenario must surface; they are not labelled as violations because this project has no glossary that picks a winner.

### Broken on purpose

`CorpusKit check` exits 1 on this project with exactly {nb} problems, listed in `known-breaks.json`: {nx} xrefs to topics that do not exist and {nt} TOC entries that link to missing files. Every other check (well-formed XML, snippets, images, variables, condition tags, label anchors) passes. `FlareAI.Scoring validate` passes with 0 problems.

### Encoding

{bom} files start with a UTF-8 byte order mark, {cp} are saved as Windows-1252 with a matching XML declaration, and {moj} contain mojibake (UTF-8 read as Windows-1252: `a`-circumflex followed by Euro and a quote). The Word topics also carry a `meta` that says `charset=windows-1252` although the file is UTF-8, the way Word exports do. `.gitattributes` in this folder (`* -text`) stops git from changing line endings or encodings.

### Labelled and not labelled

Labelled: hard-coded phone numbers, URLs and email addresses, the product and company names where a variable exists, images without alt text, a skipped heading level, a second h1, tables without header cells (including layout tables), vague link text, UI labels that are not bold (quoted, italic, coloured), and the wording kinds in the taxonomy (utilize, in order to, prior to, is able to, please, please note, simply, we, will, the user, e.g., exclamation marks, free of charge, guaranteed, coming soon, passive, wordy, vague, undefined abbreviation, whitelist). Not labelled because the taxonomy has no kind: mojibake characters, no-break space runs, empty paragraphs, `br` breaks, bold paragraphs used as headings, deprecated HTML and MadCap elements, conditions on spans, drifted duplicates, competing term forms. These are markup and consistency features for the extractor and mining tests, not lint findings.
""".format(nt2=len(st['topics']), a=eras['A'], b=eras['B'], c=eras['C'], d=eras['D'], cen=cen, termtab=termtab, nb=len(kb['broken_xrefs']) + len(kb['orphan_toc']), nx=len(kb['broken_xrefs']), nt=len(kb['orphan_toc']), bom=len(enc['utf8_bom']), cp=len(enc['windows_1252']), moj=census['mojibake (UTF-8 read as Windows-1252)'])
    write(root, 'LABELS.md', common_labels_md(pid, st, 'Span labels for the 149-topic messy legacy project.', extra))
    return census, out


if __name__ == '__main__':
    which = sys.argv[1:] or ['release_notes', 'localized_style', 'legacy']
    for w in which:
        r = globals()[w]()
        print(w, 'done')
        if w == 'legacy':
            print(json.dumps(r[0], indent=1))
