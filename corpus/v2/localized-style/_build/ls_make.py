import os, re, sys, shutil, json
import os as _os; sys.path.insert(0, _os.path.dirname(_os.path.abspath(__file__)))
from corplib import *
from labeler import *
import ls_content

OUT = WT + '/corpus/v2/localized-style'
PID = 'localized-style'
SEED = 20261022
XMLNS = 'xmlns:MadCap="http://www.madcapsoftware.com/Schemas/MadCap.xsd"'
GUIDE = 'Content/Resources/StyleGuide/'


def rd(p): return open(OUT + '/' + p, encoding='utf-8').read()


def wr(p, s):
    os.makedirs(os.path.dirname(OUT + '/' + p), exist_ok=True)
    open(OUT + '/' + p, 'w', encoding='utf-8', newline='\n').write(s)


def stage1():
    clean_out(OUT)
    src = SP + '/ls-src'
    if os.path.isdir(src): shutil.rmtree(src)
    for path, md, plants in ls_content.T:
        full = src + '/' + path
        os.makedirs(os.path.dirname(full), exist_ok=True)
        open(full, 'w', encoding='utf-8').write(md.replace('BOIL_LINE', ls_content.BOIL))
    kit('import', '--src', src, '--out', OUT, '--name', PID, '--seed', str(SEED), '--snippet-min', '5',
        '--var', 'VAR_PRODUCT=General.ProductName:Acme Sign-On', '--var', 'VAR_COMPANY=General.CompanyName:Acme Software')
    kit('scaffold', '--project', OUT, '--name', PID, '--conditions', 'Print,ScreenOnly',
        '--glossary', 'single sign-on=A way to log in once and reach every assigned application.;identity provider=The system that proves who a user is.;SAML 2.0=A standard for exchanging login information between systems.;OpenID Connect=A standard for logging in with a client ID and a client secret.;allowlist=A list of addresses that are allowed to log in.')


def stage2():
    # text snippet used in three places
    wr('Content/Resources/Snippets/console-address.flsnp',
       '<?xml version="1.0" encoding="utf-8"?>\n<html %s>\n    <head />\n    <body>\n        <p>the console address</p>\n    </body>\n</html>\n' % XMLNS)
    ts = '<MadCap:snippetText src="../Resources/Snippets/console-address.flsnp" />'
    f = 'Content/getting-started/log-in-to-the-console.htm'
    s = rd(f)
    for a in ['Open the console address in your browser.', 'You need the console address and the email address']:
        assert a in s, a
    s = s.replace('Open the console address in your browser.', 'Open ' + ts + ' in your browser.').replace('You need the console address and the email address', 'You need ' + ts + ' and the email address')
    wr(f, s)
    f = 'Content/users/add-a-user.htm'
    s = rd(f)
    # conditions
    f2 = 'Content/getting-started/console-tour.htm'
    s2 = rd(f2)
    a = '<p>Click <b>?</b> to open this documentation in a new tab.</p>'
    assert a in s2
    wr(f2, s2.replace(a, '<p MadCap:conditions="Default.ScreenOnly">Click <b>?</b> to open this documentation in a new tab.</p>'))
    f3 = 'Content/admin/export-reports.htm'
    s3 = rd(f3)
    a = 'Large exports can take several minutes.'
    assert a in s3
    wr(f3, s3.replace(a, '<span MadCap:conditions="Default.Print">' + a + '</span>'))


def company_pages():
    head = lambda t: '<?xml version="1.0" encoding="utf-8"?>\n<html xmlns:MadCap="http://www.madcapsoftware.com/Schemas/MadCap.xsd">\n    <head><title>%s</title></head>\n    <body>\n' % t
    foot = '    </body>\n</html>\n'
    about = head('About this style guide') + """        <h1>About this style guide</h1>
        <p>This Flare project is the style guide that the Acme Software documentation team checks topics against. The Company folder holds the team's rules. Where a company rule disagrees with the Google developer documentation style guide, the company rule wins.</p>
        <h2>How to write rules</h2>
        <p>Only bullets and table rows are rules; paragraphs like this one are context. Keep one rule per bullet. Under a bullet, add a nested list with Bad: and Good: lines so the AI's use of the rule can be tested.</p>
        <p>Tables with the headers Avoid, Use, Severity, Why, Example become automatic lint rules. A table with the headers Rule, Setting, Reason turns Google rules off or changes their level.</p>
""" + foot
    over = head('Changes to the Google guide') + """        <h1>Changes to the Google guide</h1>
        <p>Rows in this table turn rules from the Google developer documentation style guide off, or change their level, for this team.</p>
        <table>
            <tr><th>Rule</th><th>Setting</th><th>Reason</th></tr>
            <tr id="rule-1"><td>google:word-list/443</td><td>off</td><td>The console says Log in, so the documentation says log in.</td></tr>
            <tr id="rule-2"><td>google:word-list/637</td><td>off</td><td>The console says Log in and Log out; the guide does not switch to sign in and sign out.</td></tr>
            <tr id="rule-3"><td>google:word-list/638</td><td>off</td><td>The console says Log in and Log out, so these words are correct here.</td></tr>
            <tr id="rule-4"><td>google:word-list/53</td><td>off</td><td>Our audience manages applications in an enterprise console; we write application, not app.</td></tr>
            <tr id="rule-5"><td>google:word-list/211</td><td>suggestion</td><td>Disable is a button label in the console. Prose can still use a more precise verb.</td></tr>
        </table>
""" + foot
    term = head('Terminology') + """        <h1>Terminology</h1>
        <h2>People and organizations</h2>
        <ul>
            <li id="rule-1">Refer to the people who use the console as "users", not "customers" or "end users".<ul><li>Bad: An end user can reset the password.</li><li>Good: A user can reset the password.</li></ul></li>
            <li id="rule-2">Refer to the customer's company as "the organization", not "the tenant".<ul><li>Bad: Each tenant has its own login page.</li><li>Good: Each organization has its own login page.</li></ul></li>
        </ul>
        <table>
            <tr><th>Avoid</th><th>Use</th><th>Severity</th><th>Why</th><th>Example</th></tr>
            <tr id="rule-3"><td>customer, customers, end user, end users</td><td>user, users</td><td>warning</td><td>We write for users.</td><td>An end user can reset the password.</td></tr>
            <tr id="rule-4"><td>tenant, tenants</td><td>organization, organizations</td><td>warning</td><td>The console says organization.</td><td>Each tenant has a login page.</td></tr>
        </table>
        <h2>Logging in</h2>
        <ul>
            <li id="rule-5">Use "log in" as the verb and "login" as the noun or adjective. Do not use "sign in".<ul><li>Bad: Sign in to the console.</li><li>Good: Log in to the console.</li></ul></li>
        </ul>
        <table>
            <tr><th>Avoid</th><th>Use</th><th>Severity</th><th>Why</th><th>Example</th></tr>
            <tr id="rule-6"><td>sign in, Sign in, signs in, signed in</td><td>log in, Log in, logs in, logged in</td><td>warning</td><td>The console says Log in.</td><td>Sign in to the console.</td></tr>
            <tr id="rule-7"><td>sign-in page, sign-in screen</td><td>login page, login screen</td><td>warning</td><td>The console says Log in.</td><td>Open the sign-in page.</td></tr>
        </table>
        <h2>Product and feature names</h2>
        <ul>
            <li id="rule-8">Use the exact spelling from the glossary, and spell out abbreviations on first use, for example "single sign-on (SSO)".<ul><li>Bad: Turn on SSO for your users.</li><li>Good: Turn on single sign-on (SSO) for your users.</li></ul></li>
        </ul>
        <table>
            <tr><th>Avoid</th><th>Use</th><th>Severity</th><th>Why</th><th>Example</th></tr>
            <tr id="rule-9"><td>single sign on, Single Sign-On, Single Sign On, single-sign-on</td><td>single sign-on</td><td>warning</td><td>Glossary spelling.</td><td>Turn on Single Sign-On first.</td></tr>
            <tr id="rule-10"><td>Identity Provider, identity-provider</td><td>identity provider</td><td>warning</td><td>Glossary spelling.</td><td>Ask your Identity Provider for the metadata.</td></tr>
            <tr id="rule-11"><td>OpenId Connect, Open ID Connect, openID connect</td><td>OpenID Connect</td><td>warning</td><td>Glossary spelling.</td><td>It supports OpenId Connect.</td></tr>
            <tr id="rule-12"><td>SAML2</td><td>SAML 2.0</td><td>warning</td><td>Include the version.</td><td>It supports SAML2.</td></tr>
        </table>
""" + foot
    ui = head('UI references') + """        <h1>UI references</h1>
        <h2>Labels</h2>
        <ul>
            <li id="rule-1">The console is the source of truth for button, tab and column names. Write each name exactly as the console shows it, in bold, even when the label breaks another rule in this guide.<ul><li>Bad: Click the save button.</li><li>Good: Click <b>Save</b>.</li></ul></li>
            <li id="rule-2">Labels such as Whitelist, E-mail address, Kill session, Master key and Sign-On Settings are part of the product. Do not correct them in running text that quotes the label; write the preferred word everywhere else.<ul><li>Bad: Select Security > Allowlist.</li><li>Good: Select <b>Security</b> > <b>Whitelist</b>.</li></ul></li>
        </ul>
""" + foot
    for name, text in [('About', about), ('Google-overrides', over), ('Terminology', term), ('UI-references', ui)]:
        wr(GUIDE + 'Company/%s.htm' % name, text)
    wr('Content/Resources/StyleGuide/owners.txt', '# Owners of this style guide\ndocs-team\n')


def stage3():
    lp = OUT + '/labels.jsonl'
    if os.path.exists(lp): os.remove(lp)
    kinds = 'utilize,in_order_to,prior_to,is_able_to,please,simply,please_note,whitelist,blacklist,the_user,latin,exclaim,will,we,passive,wordy,vague,click_here,phone,url'
    kit('inject', '--project', OUT, '--seed', str(SEED), '--density', '0.08', '--project-id', PID, '--kinds', kinds)
    company_pages()


LOGIN = re.compile(r'\blog(?:ged)? (?:in|out)\b|\blogin\b|\blog-in\b', re.I)
UI_FLAG = re.compile(r'whitelist|e-mail|kill|master|disable|sign-on', re.I)
QUOTED = re.compile(r'"[^"\n]{6,60}"')
UNIT = re.compile(r'^(\d+)(?:,\d+)* ?(min|hr|px|s)\b.*$|^\d+ min to \d+ hr$|^\d+ hr to \d+ hr$|^\d+ hr$|^\d+ min$')
UI_TD = {'Whitelist', 'E-mail address', 'Kill session', 'Master key', 'Sign-On Settings', 'Disable'}
CO = ['company:terminology/*', 'localized-style:terminology/*']
KIND_RULES = {'bank': CO, 'customer': CO, 'sso_spelling': CO, 'idp': CO, 'oidc': CO, 'saml2': CO}


def stage4():
    lp = OUT + '/labels.jsonl'
    existing = fix_injector_labels(read_labels(lp))
    P = Proj(OUT, PID)
    for rel in list(P.blocks):
        if rel.startswith(GUIDE): P.blocks.pop(rel)
    S = LabelSet(P, existing)
    plants = {}
    for path, md, pl in ls_content.T:
        slug = os.path.splitext(os.path.basename(path))[0]
        plants[slug] = pl
    boiler = 'Content/Resources/Snippets/if-you-cannot-log.flsnp'
    done_plants = 0
    for rel in sorted(P.blocks):
        if rel.startswith(GUIDE): continue
        slug = os.path.splitext(os.path.basename(rel))[0]
        par = {c: p for p in P.docs[rel].iter() for c in p}
        for b in P.blocks[rel]:
            codes = inline_spans(b, ('code', 'kbd'))
            bolds = inline_spans(b, ('b', 'strong'))
            varr = [(s.start, s.end) for s in b.segs if s.var]
            def free(st, en, skip_bold=True):
                return (not S.overlaps_seeded(b, st, en) and not any(st < c1 and en > c0 for c0, c1 in codes)
                        and not any(st < v1 and en > v0 for v0, v1 in varr) and not any(st < c1 and en > c0 for c0, c1 in bolds))
            # planted company-guide violations
            for quote, kind, fix in plants.get(slug, []):
                at = b.text.find(quote)
                if at >= 0 and b.text.count(quote) == 1:
                    if S.add_span(b, at, at + len(quote), 'violation', KIND_RULES[kind], kind=kind, origin='seeded', by=('generator',), status='verified',
                                  seed={'generator': 'localized-style-author@1', 'variant': 'company-term'}, fix=fix):
                        done_plants += 1
            # the mention of a vendor's capitalised term is not a violation
            if 'Single Sign-On or SSO' in b.text:
                at = b.text.find('Single Sign-On')
                S.add_span(b, at, at + len('Single Sign-On'), 'exempt', CO, reason="Mentions another vendor's term in order to say what this guide uses instead.")
            # company over Google: log in
            for m in LOGIN.finditer(b.text):
                if free(m.start(), m.end()):
                    S.add_span(b, m.start(), m.end(), 'clean', ['google:word-list/443', 'google:word-list/637', 'google:word-list/638'], reason='The company guide turns the Google log in / sign in rules off: the console says Log in, so the documentation says log in.')
            # UI labels: bold
            for st, en in bolds:
                if S.overlaps_seeded(b, st, en): continue
                text = b.text[st:en]
                if UI_FLAG.search(text):
                    S.add_span(b, st, en, 'exempt', ['google:word-list/*'] + CO, reason='UI label; it must match the console exactly even when the word is not preferred in prose.')
                else:
                    S.add_span(b, st, en, 'clean', ['*'], reason='UI label; it must match the console verbatim.')
            # inline code and keys
            for st, en in codes:
                if en - st >= 1 and not S.overlaps_seeded(b, st, en):
                    S.add_span(b, st, en, 'exempt', ['*'], reason='Inline code or a key name is a literal; prose style rules do not apply.')
            for dt in ('04/03/2026', '03.04.2026'):
                at = b.text.find(dt)
                if at >= 0 and rel.endswith('languages-and-localization.htm'):
                    S.add_span(b, at, at + len(dt), 'exempt', ['google:dates-times/*'], reason='Example of how the console formats a date in another language; the format is the point of the sentence.')
            label_curly(S, b)
            for m in QUOTED.finditer(b.text):
                if free(m.start(), m.end()):
                    S.add_span(b, m.start(), m.end(), 'exempt', ['*'], reason='Message shown by the console, quoted verbatim.')
            if pref(b.el.tag) == 'td' and UNIT.match(b.text):
                S.add_span(b, 0, len(b.text), 'clean', ['google:units-of-measure/*', 'google:numbers/*'], reason='Value and unit in a table cell, written as the console shows them.')
            if pref(b.el.tag) == 'td' and b.text in UI_TD:
                S.add_span(b, 0, len(b.text), 'exempt', ['google:word-list/*'] + CO, reason='UI label in the reference table; it must match the console exactly.')
            if rel == boiler:
                S.out.append(dict(id=S.next_id('N'), file=b.file, block=b.xpath, label='clean', rules=['*'], reason='Boilerplate repeated in 6 topics; reviewed once in the snippet.', origin='natural', status='verified', by=['hand-read'], split=split_of(b.file)))
            S.block_complete(b)
    allr = existing + S.out
    for l in allr: l['split'] = split_of(l['file'])
    write_labels(lp, allr)
    print('planted', done_plants, 'of', sum(len(v) for v in plants.values()))
    print('labels', len(allr), {k: sum(1 for l in allr if l['label'] == k) for k in ('violation', 'clean', 'exempt', 'block-complete')})


def stage5():
    kit('contexts', '--project', OUT, '--update')
    kit('check', '--project', OUT)


if __name__ == '__main__':
    st = sys.argv[1:] or ['1', '2', '3', '4', '5']
    for s in st: globals()['stage' + s]()
