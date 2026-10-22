"""Topic plan for the synthetic legacy project: base tasks, special topics, drifted duplicates, project files."""
import copy, json, os, re, shutil, sys
import os as _os; sys.path.insert(0, _os.path.dirname(_os.path.abspath(__file__)))
from legacy_render import *

ERAS = ['A'] * 28 + ['B'] * 25 + ['C'] * 27 + ['D'] * 20
FIELD_DESC = ['Required. Type the value exactly as it appears on the paper form.', 'Choose a value from the list.', 'Optional. The value appears on printed documents.',
              'The program fills this in for you.', 'Required. The value must be unique.', 'You can leave this box empty.']
SNIP_BACKUP = 'Content/Resources/Snippets/BackupWarning.flsnp'
SNIP_PRODUCT = 'Content/Resources/Snippets/ProductName.flsnp'
KNOWN = {'broken_xrefs': [], 'orphan_toc': [], 'toc_missing_topics': []}


def slugify(s, sep='-'):
    s = re.sub(r"[^A-Za-z0-9]+", sep, s).strip(sep)
    return s.lower() if sep == '-' else s


def camel(s): return ''.join(w.capitalize() for w in re.split(r'[^A-Za-z0-9]+', s) if w)


def fname(c, mod, title, era, used):
    folder = mod['folder']
    if era == 'A':
        pat = c.pick([0, 1, 2, 3])
        if pat == 0: name, folder = '%s - %s.doc' % (mod['name'], title), 'WordImport'
        elif pat == 1: name = 'WO_%s' % camel(title)
        elif pat == 2: name, folder = camel(title) + 'v2', 'WordImport'
        else: name = camel(title)
    elif era == 'B':
        name = c.pick([slugify(title, '_'), camel(title), slugify(title, '_') + '_new', 'topic_' + str(c.rng.randint(100, 999))])
    else:
        name = slugify(title)
    base = 'Content/%s/%s.htm' % (folder, name)
    n = 2
    while base.lower() in used:
        base = 'Content/%s/%s%d.htm' % (folder, name, n); n += 1
    used.add(base.lower())
    return base


def title_of(c, m, verb):
    key = 'restore_db' if (m['key'] == 'adm' and verb == 'restore') else verb
    t = VERB_TITLE[key]
    a = 'an' if (m['obj'][0] in 'aeiou' and not m['obj'].startswith('user')) else 'a'
    o = c.term('wo') if m['key'] == 'wo' else m['obj']
    os_ = c.term('wo', True) if m['key'] == 'wo' else m['objs']
    t = t.format(a=a, o=o, os=os_)
    if c.era in 'AB' and c.chance(0.3): t = t.title()
    return t


def build_task(c, mk, verb, file, title):
    m = dict(MODS[mk]); m['key'] = mk
    ov = OBJ_OVERRIDE.get((mk, verb))
    if ov: m['obj'], m['objs'] = ov
    key = 'restore_db' if (mk == 'adm' and verb == 'restore') else verb
    secs = [('p', intro_text(c, m, key if key in ('create',) else verb, title))]
    if c.chance(0.55):
        pr = PRODUCT(c)
        secs.append(('p', '%s keeps a history of every change to the %s.' % (pr, c.term('wo') if mk == 'wo' else m['obj'])))
    if c.chance(0.7): secs.append(('p', subst(c, c.pick(CONTEXT), m)))
    secs.append(('h2', c.pick(['Procedure', 'Steps', 'How to do it'])))
    secs.append(('steps', step_list(c, m, key)))
    if c.chance(0.45): secs.append(('img', c.pick(IMAGES)))
    for _ in range(c.rng.choice([0, 1, 1, 2])): secs.append(('note', note_text(c, m)))
    if c.chance(0.35):
        rows = [[f, c.pick(FIELD_DESC)] for f in m['fields']]
        secs.append(('h2', 'Fields'))
        secs.append(('table', dict(header=['Field', 'Description'], rows=rows, widths=[140, 300])))
    cl = contact_line(c)
    if cl: secs.append(('p', cl))
    cl2 = claim_line(c)
    if cl2: secs.append(('p', cl2))
    if c.era in 'CD' and c.chance(0.5): secs.append(('snippet', relhref(file, SNIP_BACKUP)))
    if c.era in 'AB' and c.chance(0.7):
        secs.append(('p', 'Copyright 2014 %s. All rights reserved.' % P(COMPANY_LITERAL, 'variable_company', 'a General.CompanyName variable')))
    if c.era == 'C' and c.chance(0.07): secs.insert(3, ('h1x', 'Related information'))
    if c.era == 'C' and c.chance(0.12):
        secs.append(('p', 'Welcome to %s.' % ('<MadCap:snippetText src="%s" />' % relhref(file, SNIP_PRODUCT))))
    return dict(title=title, sections=secs, menu=m['menu'], helpcontrol=(c.era == 'C' and c.chance(0.12)))


def overview(c, mk, tasks, file):
    m = MODS[mk]
    secs = [('p', 'The %s module lets dispatchers work with %s. The list below shows the tasks that you can do.' % (m['name'], m['objs'])),
            ('h2', 'Tasks'),
            ('ul', [t for t in tasks])]
    cl = contact_line(c)
    if cl: secs.append(('p', cl))
    return dict(title='About %s' % m['name'], sections=secs, menu=m['menu'])


def special_topics(c_for):
    """Hand-built topics: big tables, nested layout tables, deep lists, FAQ, install, glossary ... -> list of (era, folder, title, builder)"""
    S = []

    def add(era, folder, name, title, sections, **kw):
        S.append(dict(era=era, folder=folder, name=name, title=title, sections=sections, **kw))

    # big tables
    err_rows = []
    msgs = ['The record is locked by another user', 'The date is not valid', 'The customer was not found', 'The technician is not available at that time', 'The file is too large', 'The connection to the server was lost',
            'The part number already exists', 'You do not have permission', 'The invoice total does not match the lines', 'The route has no stops']
    for i in range(1, 121):
        err_rows.append(['E%04d' % (1000 + i), msgs[i % len(msgs)], c_for('B').pick(['Try again.', 'Contact your administrator.', 'Check the entry and save again.', 'Restart the program.'])])
    add('B', 'Reference', 'error_codes', 'Error codes', [('p', 'The program shows an error code when something goes wrong. Find the code in this table.'),
                                                       ('table', dict(header=['Code', 'Message', 'Action'], rows=err_rows, widths=[70, 260, 200]))])
    price_rows = []
    for i in range(1, 111):
        price_rows.append(['P-%04d' % (2000 + i * 7), 'Replacement part %d' % i, '%d.%02d' % (5 + i * 3 % 90, (i * 7) % 100), '%d' % (3 + i % 40)])
    add('A', 'Inventory', 'Price list.doc', 'Parts price list', [('p', 'Use these prices when you create an invoice. Prices are in dollars and do not include tax.'),
                                                               ('table', dict(header=['Part number', 'Description', 'Price', 'In stock'], rows=price_rows, widths=[90, 200, 70, 70]))])
    map_rows = []
    flds = ['Customer name', 'Account number', 'Invoice number', 'Invoice date', 'Due date', 'Tax code', 'Line description', 'Quantity', 'Unit price', 'Line total']
    for i in range(1, 106):
        f = flds[i % len(flds)]
        map_rows.append([f + (' %d' % (i // len(flds) + 1) if i >= len(flds) else ''), 'ACC_%03d' % i, c_for('C').pick(['Text', 'Number', 'Date', 'Yes/No']), c_for('C').pick(['Required', 'Optional'])])
    add('C', 'Integrations', 'accounting-field-map', 'Accounting field map', [('p', 'This table shows how each field is sent to the accounting system.'),
                                                                         ('table', dict(header=['Field', 'Accounting field', 'Type', 'Use'], rows=map_rows, widths=[160, 120, 80, 80]))])
    # system requirements and shortcuts
    add('C', 'Reference', 'system-requirements', 'System requirements', [('p', 'Check these requirements before you install the program.'), ('table', dict(header=['Item', 'Server', 'Workstation'], rows=[
        ['Memory', '8 GB', '4 GB'], ['Disk space', '40 GB', '2 GB'], ['Display', '1280 x 1024', '1024 x 768'], ['Network', '100 Mbps', '10 Mbps'], ['Screen', '17 in', '15 in']], widths=[120, 140, 140]))])
    add('B', 'Reference', 'shortcuts', 'Keyboard shortcuts', [('p', 'These shortcuts work on every screen.'), ('table', dict(header=['Keys', 'Action'], rows=[
        ['<kbd>Ctrl</kbd>+<kbd>N</kbd>', 'Create a new record'], ['<kbd>Ctrl</kbd>+<kbd>S</kbd>', 'Save'], ['<kbd>F5</kbd>', 'Refresh the list'], ['<kbd>Ctrl</kbd>+<kbd>P</kbd>', 'Print'], ['<kbd>Esc</kbd>', 'Close the dialog']], widths=[160, 300]))])
    # glossary-ish
    add('D', 'Reference', 'glossary', 'Glossary', [('p', 'These terms have a special meaning in the program.'), ('ul', [
        '<b>Dispatcher</b>: a person who schedules jobs and assigns them.', '<b>Depot</b>: the place where a technician starts the day.', '<b>Stop</b>: one visit on a route.',
        '<b>Resolution</b>: the description of the work that was done.', '<b>Sync</b>: the exchange of data between the mobile app and the server.'])])
    # install
    add('A', 'Install', 'Server install.doc', 'Install the server', [('p', 'Install the server on a computer that stays on all day.'), ('h2', 'Before you begin'), ('ul', ['Log in as a local administrator.', 'Close all other programs.', 'Back up your data.']),
                                                                    ('h2', 'Procedure'), ('steps', ['Insert the installation disc or open the download.', 'Double-click <b>Setup.exe</b>.', 'Follow the instructions on the screen.', 'Restart the computer when the program asks you to.']),
                                                                    ('note', 'Installation takes about 15 minutes.')])
    add('B', 'Install', 'install_client', 'Install the workstation software', [('p', 'Do these steps on every computer that dispatchers use.'), ('steps', ['Open the address of the server in your browser.', 'Click Download client.', 'Run the file that you downloaded.', 'Type the server address when the program asks for it.'])])
    add('C', 'Install', 'install-mobile-app', 'Install the mobile app', [('p', 'The mobile app runs on phones and tablets.'), ('h2', 'Procedure'), ('steps', ['Open the app store on the device.', 'Search for the program name.', 'Tap <b>Install</b>.', 'Open the app and enter the server address.'])])
    add('D', 'Install', 'upgrade-notes', 'Upgrade from version 2', [('p', 'Version 3 changes the database. Plan for a short outage.'), ('h2', 'Procedure'), ('steps', ['Back up the database.', 'Stop the server.', 'Run the upgrade program.', 'Start the server and check the log.'])])
    # release notes
    add('B', 'Reference', 'whats_new_3', "What's new in version 3", [('p', 'Version 3 has a new calendar and a faster search.'), ('ul', ['New calendar view with day, week and month.', 'The search box now finds part numbers.', 'You can attach photos to a work order.', 'Several bugs are fixed.'])])
    add('A', 'Reference', 'Whats new in 2.5.doc', "What's new in version 2.5", [('p', 'Version 2.5 adds recurring appointments.'), ('ul', ['Recurring appointments.', 'Tax codes on sites.', 'Faster printing.'])])
    # faq / troubleshooting with dropdowns
    faq = [('dropdown', q, a) for q, a in TROUBLE]
    add('C', 'Support', 'troubleshooting', 'Troubleshooting', [('p', 'Find your problem in the list, and then read the answer.')] + faq)
    add('C', 'Support', 'faq', 'Frequently asked questions', [('p', 'These are the questions that dispatchers ask most.'),
                                                              ('dropdown', 'Can I use the program on a tablet?', 'Yes. Open the address of the server in a browser, or install the mobile app.'),
                                                              ('dropdown', 'How often is my data backed up?', 'The server backs up the database every night at 2 AM.'),
                                                              ('dropdown', 'Who can see invoices?', 'Dispatchers and administrators. Technicians cannot see invoices.')])
    add('B', 'Support', 'contact_support', 'Contact support', [('p', 'Our support team is available Monday to Friday, 8 AM to 6 PM.'), ('p', 'Phone: %s' % P('1-800-555-0142', 'phone', 'a variable')),
                                                              ('p', 'Web: %s' % P('http://support.marlowe-dispatch.example.com/help', 'url', 'a variable')), ('p', 'E-mail: %s' % P('support@marlowe-dispatch.example.com', 'url', 'a variable'))])
    add('A', 'Support', 'About support.doc', 'About technical support', [('p', 'Before you call, write down the version number and the exact text of any error message.'),
                                                                        ('p', 'Telephone support is %s for customers who have a service contract.' % P('free of charge', 'claims_free', ''))])
    add('D', 'Getting Started', 'getting-started', 'Getting started', [('p', 'This guide helps new dispatchers do their first tasks.'), ('h2', 'Your first day'), ('steps', ['Log in with the user name that your administrator gave you.', 'Open the <b>Work Orders</b> screen.', 'Create a work order for a test customer.', 'Assign the work order to a technician.'])])
    add('A', 'Getting Started', 'Welcome.doc', 'Welcome', [('p', 'Welcome to the program. This guide has been written for dispatchers and administrators.'),
                                                          ('p', 'Copyright 2012 %s. All rights reserved.' % P(COMPANY_LITERAL, 'variable_company', 'a General.CompanyName variable'))])
    add('D', 'Reference', 'legal', 'Legal notices', [('p', 'The software and this guide are protected by copyright. Trademarks belong to their owners.'), ('p', 'Copyright 2019 %s. All rights reserved.' % P(COMPANY_LITERAL, 'variable_company', 'a General.CompanyName variable'))])
    # deep lists
    add('B', 'Admin', 'permission_tree', 'Permission tree', [('p', 'Permissions are grouped. A group contains smaller groups.'), ('deep', (['Company', 'Region', 'Depot', 'Team', 'Crew', 'Technician', 'Permission'], 7))])
    add('C', 'Admin', 'org-structure', 'Organization structure', [('p', 'Each level can have several members.'), ('deep', (['Organization', 'Division', 'Branch', 'Department', 'Group', 'Role', 'User'], 7))])
    add('A', 'Admin', 'Checklist.doc', 'Setup checklist', [('p', 'Do these things in order.'), ('deep', (['Plan', 'Install', 'Configure', 'Import data', 'Train users', 'Go live', 'Review'], 7))])
    return S


def nested_layout(c, era, title):
    inner = '<table border="1" cellpadding="2"><tr><td>Screen</td><td>Use</td></tr><tr><td>Work Orders</td><td>Create and track jobs</td></tr><tr><td>Schedule</td><td>Book appointments</td></tr><tr><td>Reports</td><td>Run reports</td></tr></table>'
    if era == 'A':
        return ('<table class="MsoNormalTable" border="0" cellspacing="0" cellpadding="0" width="100%" x-plant="table_header"><tr><td width="30%" valign="top"><p class="MsoNormal"><b>Menu</b><o:p></o:p></p></td>'
                '<td width="70%" valign="top"><p class="MsoNormal">Choose a screen from the list.<o:p></o:p></p>' + inner.replace('<td>', '<td><p class="MsoNormal">').replace('</td>', '<o:p></o:p></p></td>') + '</td></tr></table>')
    return ('<table width="100%" border="0" cellpadding="4" x-plant="table_header"><tr><td valign="top" width="30%"><b>Menu</b></td><td valign="top">Choose a screen from the list.<br />' + inner + '</td></tr></table>')
