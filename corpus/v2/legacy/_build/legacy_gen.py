"""Generator for the synthetic messy legacy project.  Writes topics with planted, exactly-labelled defects (markers resolved by legacy_label.py)."""
import os, re, sys, json, random, shutil
import os as _os; sys.path.insert(0, _os.path.dirname(_os.path.abspath(__file__)))
from corplib import *
from legacy_lex import *

OUT = WT + '/corpus/v2/legacy'
PID = 'legacy'
SEED = 20261023
MS, ME1, ME2, MEND = '\ue000', '\ue001', '\ue002', '\ue003'   # plant markers: kind SEP fix SEP-END bad END
NS_MC = 'xmlns:MadCap="http://www.madcapsoftware.com/Schemas/MadCap.xsd"'
NS_WORD = 'xmlns:o="urn:schemas-microsoft-com:office:office" xmlns:w="urn:schemas-microsoft-com:office:word" xmlns:v="urn:schemas-microsoft-com:vml"'
BR = '<br />'
NB = '&#160;'

# ---- term forms by era: (weight, singular, plural) -------------------------------------------------------------------
TERMS = {
    'wo': {'A': [(50, 'Work Order', 'Work Orders'), (30, 'work order', 'work orders'), (10, 'WO', 'WOs'), (10, 'job ticket', 'job tickets')],
           'B': [(40, 'work order', 'work orders'), (25, 'WO', 'WOs'), (20, 'job ticket', 'job tickets'), (15, 'service call', 'service calls')],
           'C': [(70, 'work order', 'work orders'), (20, 'Work Order', 'Work Orders'), (10, 'WO', 'WOs')],
           'D': [(90, 'work order', 'work orders'), (10, 'service call', 'service calls')]},
    'tech': {'A': [(50, 'technician', 'technicians'), (30, 'field engineer', 'field engineers'), (20, 'tech', 'techs')],
             'B': [(40, 'tech', 'techs'), (30, 'technician', 'technicians'), (30, 'field rep', 'field reps')],
             'C': [(75, 'technician', 'technicians'), (15, 'tech', 'techs'), (10, 'field rep', 'field reps')],
             'D': [(95, 'technician', 'technicians'), (5, 'field engineer', 'field engineers')]},
    'cust': {'A': [(60, 'customer', 'customers'), (40, 'client', 'clients')], 'B': [(50, 'customer', 'customers'), (50, 'client', 'clients')],
             'C': [(80, 'customer', 'customers'), (20, 'client', 'clients')], 'D': [(95, 'customer', 'customers'), (5, 'client', 'clients')]},
    'login': {'A': [(40, 'log in', 'log in'), (40, 'sign in', 'sign in'), (20, 'log on', 'log on')], 'B': [(30, 'log in', 'log in'), (30, 'login', 'login'), (20, 'sign in', 'sign in'), (20, 'log on', 'log on')],
              'C': [(60, 'log in', 'log in'), (30, 'sign in', 'sign in'), (10, 'login', 'login')], 'D': [(85, 'log in', 'log in'), (15, 'sign in', 'sign in')]},
    'email': {'A': [(40, 'e-mail', 'e-mail'), (40, 'E-mail', 'E-mail'), (20, 'email', 'email')], 'B': [(50, 'e-mail', 'e-mail'), (30, 'email', 'email'), (20, 'Email', 'Email')],
              'C': [(70, 'email', 'email'), (30, 'e-mail', 'e-mail')], 'D': [(95, 'email', 'email'), (5, 'e-mail', 'e-mail')]},
}
PRODUCT_LITERAL = 'Marlowe Dispatch'
PRODUCT_VARIANTS = ['MarloweDispatch', 'Marlowe-Dispatch', 'MD', 'Dispatch']
COMPANY_LITERAL = 'Marlowe Systems'


class Ctx(object):
    def __init__(self, rng, era, name):
        self.rng, self.era, self.name = rng, era, name
        self.imgs = 0
        self.uses_var = False

    def pick(self, seq): return self.rng.choice(seq)

    def chance(self, p): return self.rng.random() < p

    def wchoice(self, items):
        tot = sum(i[0] for i in items); r = self.rng.random() * tot
        for it in items:
            r -= it[0]
            if r <= 0: return it
        return items[-1]

    def term(self, key, plural=False, cap=False):
        it = self.wchoice(TERMS[key][self.era])
        s = it[2] if plural else it[1]
        return s[0].upper() + s[1:] if cap else s

    def fx(self, html):
        """Quote style by era, applied to text outside tags and outside plant markers."""
        mode = {'A': 'curly', 'B': 'straight', 'D': 'straight'}.get(self.era)
        if self.era == 'C': mode = self.pick(['curly', 'straight', 'mixed'])
        if self.era == 'D' and self.chance(0.12): mode = 'curly'
        parts = re.split(r'(<[^>]+>)', html)
        out = []
        for p in parts:
            if p.startswith('<') and p.endswith('>'): out.append(p); continue
            if mode == 'curly':
                p = re.sub(r"(?<=\w)'(?=\w)", '\u2019', p)
                p = re.sub(r"'(?=s\b)", '\u2019', p)
                p = re.sub(r'(^|[\s(])"', lambda m: m.group(1) + '\u201c', p)
                p = p.replace('"', '\u201d')
                p = re.sub(r"(^|[\s(])'", lambda m: m.group(1) + '\u2018', p)
                p = p.replace("'", '\u2019')
            elif mode == 'mixed':
                p = re.sub(r"(?<=\w)'(?=\w)", lambda m: self.pick(["'", '\u2019']), p)
            out.append(p)
        return ''.join(out)


def esc(s): return s.replace('&', '&amp;').replace('<', '&lt;').replace('>', '&gt;')


def P(bad, kind, fix=''):
    """A planted defect: the text `bad` carries its label (kind, fix) in markers that legacy_label.py resolves and strips."""
    return MS + kind + ME1 + esc(fix) + ME2 + bad + MEND


def PRODUCT(c, allow_var=True):
    r = c.rng.random()
    var_p = {'A': 0.0, 'B': 0.0, 'C': 0.35, 'D': 0.55}[c.era]
    lit_p = {'A': 0.45, 'B': 0.35, 'C': 0.30, 'D': 0.25}[c.era]
    if allow_var and r < var_p:
        c.uses_var = True
        return '<MadCap:variable name="General.ProductName" />'
    if r < var_p + lit_p:
        return P(PRODUCT_LITERAL, 'variable_product', 'a General.ProductName variable')
    return c.pick(PRODUCT_VARIANTS)


def UI(c, label):
    """A UI label, written the way the era writes it."""
    r = c.rng.random()
    if c.era == 'D' or (c.era == 'C' and r < 0.7):
        return '<b>%s</b>' % esc(label)
    if c.era == 'C':
        return '<i>%s</i>' % P(esc(label), 'ui_not_bold', '<b>%s</b>' % label)
    if c.era == 'B':
        if r < 0.45: return '"%s"' % P(esc(label), 'ui_not_bold', '<b>%s</b>' % label)
        if r < 0.75: return '<b>%s</b>' % esc(label)
        return '<font color="#003366">%s</font>' % P(esc(label), 'ui_not_bold', '<b>%s</b>' % label)
    # A
    if r < 0.55: return '"%s"' % P(esc(label), 'ui_not_bold', '<b>%s</b>' % label)
    if r < 0.8: return '<b><span style=\'font-family:"Arial","sans-serif"\'>%s</span></b>' % esc(label)
    return '<i>%s</i>' % P(esc(label), 'ui_not_bold', '<b>%s</b>' % label)


def subst(c, text, ctxm, extra=None):
    """Fill {o} {os} {menu} {fN} placeholders and <<UI>> labels."""
    m = ctxm
    def term_o(plural=False, cap=False):
        if m['key'] == 'wo': return c.term('wo', plural, cap)
        s = m['objs'] if plural else m['obj']
        return s[0].upper() + s[1:] if cap else s
    text = text.replace('{O}', term_o(False, True)).replace('{os}', term_o(True)).replace('{o}', term_o())
    text = text.replace('{tech}', c.term('tech')).replace('{techs}', c.term('tech', True)).replace('{cust}', c.term('cust')).replace('{custs}', c.term('cust', True))
    text = text.replace('{wos}', c.term('wo', True)).replace('{wo}', c.term('wo'))
    text = text.replace('{menu}', m['menu'])
    text = text.replace('{login}', c.term('login')).replace('{email}', c.term('email'))
    for i, f in enumerate(m['fields']):
        text = text.replace('{f%d}' % (i + 1), f)
        text = text.replace('{fl%d}' % (i + 1), f.lower())
    text = re.sub(r'<<([^>]+)>>', lambda mm: UI(c, mm.group(1)), text)
    return text


# ---- steps ----------------------------------------------------------------------------------------------------------
OPEN = ['Open the <<{menu}>> screen.', 'Select <<{menu}>> on the main menu.', 'Go to the <<{menu}>> screen.', 'On the main menu, choose <<{menu}>>.']
PICK = ['Select the {o} in the list.', 'Find the {o} in the list, and then select it.', 'In the list, click the {o} that you want.']
SAVE = ['Click <<Save>>.', 'Press <<Save>> to keep your changes.', 'When you are done, click <<Save>>.']
RESULT = ['The {o} appears in the list.', 'The screen shows a confirmation message.', 'The status bar shows the time of the change.', 'The list refreshes with your change.']

CORE = {
    'create': ['Click <<New>>.', 'Enter the {fl1} in the <<{f1}>> box.', 'Select a value in the <<{f2}>> list.', 'Enter the {fl3} in the <<{f3}>> box.'],
    'edit': ['Click <<Edit>>.', 'Change the <<{f1}>> and <<{f2}>> boxes.'], 'delete': ['Click <<Delete>>.', 'Click <<Yes>> in the confirmation box.'],
    'search': ['Type part of the {fl1} in the <<Search>> box.', 'Press <kbd>Enter</kbd>.', 'To narrow the list, choose a value in the <<{f2}>> filter.'],
    'assign': ['Click <<Assign>>.', 'Select a {tech} in the list.', 'Click <<OK>>.'], 'close': ['Click <<Close {O}>>.', 'Enter the work that was done in the <<Resolution>> box.'],
    'reopen': ['Click <<Reopen>>.', 'Enter the reason in the <<Notes>> box.'], 'duplicate': ['Click <<Actions>> > <<Duplicate>>.', 'Change the <<{f1}>> box if it is different.'],
    'print': ['Click <<Print>>.', 'Choose a printer and the number of copies.', 'Click <<OK>>.'], 'export': ['Click <<Export>>.', 'Choose the format: CSV or Excel.', 'Choose where to save the file, and then click <<Save>>.'],
    'attach': ['Click the <<Attachments>> tab.', 'Click <<Add>>, and then <<Browse>>.', 'Select the file, and then click <<Open>>.'],
    'bulk': ['Select the checkbox next to each {o}.', 'Click <<Actions>> > <<Update selected>>.', 'Choose the field to change and enter the new value.'],
    'priority': ['Click <<Edit>>.', 'Choose a value in the <<Priority>> list.'], 'note': ['Click the <<Notes>> tab.', 'Type your text, and then click <<Add note>>.'],
    'merge': ['Select the two {os} to merge.', 'Click <<Actions>> > <<Merge>>.', 'Choose the {o} to keep.'], 'archive': ['Click <<Archive>>.', 'Click <<Yes>> in the confirmation box.'],
    'restore': ['Choose <<Archived>> in the <<View>> list.', 'Click <<Restore>>.'], 'import': ['Click <<Tools>> > <<Import>>.', 'Click <<Browse>> and choose the spreadsheet.', 'Match each column to a field, and then click <<Import>>.'],
    'contact': ['Click the <<Contacts>> tab.', 'Click <<Add contact>>, and then enter the name, {email} address and phone number.'], 'contract': ['Click the <<Contract>> tab.', 'Choose the service level in the <<Plan>> list.', 'Enter the start and end dates.'],
    'equipment': ['Click the <<Equipment>> tab.', 'Click <<Add equipment>>, and then enter the model and serial number.'], 'hours': ['Click the <<Working hours>> tab.', 'Enter the start and end time for each day.'],
    'skills': ['Click the <<Skills>> tab.', 'Select each skill that applies, and then set the level.'], 'deactivate': ['Click <<Deactivate>>.', 'Click <<Yes>> in the confirmation box.'],
    'reactivate': ['Choose <<Inactive>> in the <<View>> list.', 'Click <<Reactivate>>.'], 'vehicle': ['Click the <<Vehicle>> tab.', 'Choose a vehicle in the list.'],
    'book': ['Click <<New appointment>>.', 'Choose the {tech} and the <<{f1}>>.', 'Enter the <<{f2}>> and <<{f3}>>.'], 'move': ['Drag the {o} to its new time.', 'Click <<Yes>> to confirm.'],
    'cancel': ['Click <<Cancel {O}>>.', 'Choose a reason in the list.'], 'calendar': ['Choose <<Day>>, <<Week>> or <<Month>>.', 'Use the arrows to move to another period.'],
    'conflict': ['Click the red marker next to the {o}.', 'Move one of the {os}, or assign it to another {tech}.'], 'recurring': ['Click <<Repeat>>.', 'Choose how often the {o} repeats and when it ends.'],
    'plan': ['Click <<New route>>.', 'Choose the date and the start depot.', 'Drag {os} from the list onto the route.'], 'optimize': ['Click <<Optimize>>.', 'Wait while the program recalculates the order of the stops.'],
    'stop': ['Double-click the stop.', 'Change the arrival window or the notes.'], 'send': ['Click <<Send to mobile>>.', 'Choose the {tech}.'],
    'void': ['Click <<Void>>.', 'Enter the reason, and then click <<OK>>.'], 'payment': ['Click <<Payments>> > <<Add payment>>.', 'Enter the amount and the date, and then choose the payment method.'],
    'tax': ['Click <<Edit>>.', 'Choose a value in the <<Tax code>> list.'], 'adjust': ['Select the part, and then click <<Adjust>>.', 'Enter the new quantity and the reason.'],
    'order': ['Click <<Order>>.', 'Choose the supplier and enter the quantity.'], 'receive': ['Click <<Receive>>.', 'Enter the quantity that arrived.'],
    'run': ['Choose the {o} in the list.', 'Choose the <<Date range>>.', 'Click <<Run>>.'], 'schedule': ['Click <<Schedule>>.', 'Choose how often the {o} runs and who receives it.'],
    'custom': ['Click <<New {o}>>.', 'Drag the fields that you want onto the page.', 'Choose how to group and sort the data.'], 'share': ['Click <<Share>>.', 'Choose who can see the {o}.'],
    'login': ['Open the app and enter your user name.', 'Enter your password.', 'Tap <<{login}>>.'], 'view': ['Tap <<Jobs>>.', 'Swipe left or right to change the day.'],
    'accept': ['Tap the new job.', 'Tap <<Accept>>.'], 'status': ['Open the job.', 'Tap <<Status>>, and then choose the new status.'], 'signature': ['Tap <<Signature>>.', 'Ask the customer to sign on the screen.', 'Tap <<Done>>.'],
    'photo': ['Tap <<Photo>>.', 'Point the camera at the equipment and tap the shutter.'], 'sync': ['Tap <<Menu>> > <<Sync now>>.', 'Wait for the progress bar to finish.'], 'offline': ['Turn on <<Offline mode>> in the settings.', 'Update your jobs as usual.'],
    'password': ['Select the user.', 'Click <<Reset password>>.', 'Tell the user the temporary password.'], 'role': ['Select the user.', 'Choose a value in the <<Role>> list.'], 'permissions': ['Select the role.', 'Select each permission that the role needs.'],
    'audit': ['Choose the date range.', 'Click <<Show>>.', 'Click a row to see the details.'], 'backup': ['Click <<Back up now>>.', 'Choose the folder for the backup file.'], 'restore_db': ['Click <<Restore>>.', 'Choose the backup file.', 'Click <<OK>> and wait for the program to restart.'],
    'email': ['Enter the mail server address and the port.', 'Enter the user name and password for the mail account.', 'Click <<Send test message>>.'],
    'connect': ['Click <<Add connector>>.', 'Choose the type of system.', 'Enter the server address and the account details.'], 'map': ['Click <<Field map>>.', 'Match each field on the left to a field on the right.'],
    'test': ['Click <<Test>>.', 'Read the result at the bottom of the page.'], 'synclog': ['Click <<Log>>.', 'Choose the date range.'], 'disconnect': ['Click <<Disconnect>>.', 'Click <<Yes>> in the confirmation box.'], 'apikey': ['Click <<New key>>.', 'Copy the key before you close the page.'],
}
ITEM_VERBS = {'edit', 'delete', 'assign', 'close', 'reopen', 'duplicate', 'print', 'attach', 'priority', 'note', 'archive', 'deactivate', 'move', 'cancel', 'stop', 'send', 'void', 'payment', 'tax', 'adjust', 'share',
              'contact', 'contract', 'equipment', 'hours', 'skills', 'vehicle', 'password', 'role', 'map', 'disconnect', 'test', 'optimize', 'order', 'receive', 'status', 'signature', 'photo'}
NO_SAVE = {'search', 'print', 'export', 'view', 'calendar', 'audit', 'run', 'login', 'sync', 'offline', 'synclog', 'test', 'delete', 'cancel', 'void', 'archive', 'deactivate', 'disconnect', 'conflict', 'optimize', 'backup', 'restore_db', 'photo', 'accept', 'signature'}
MOBILE = {'login', 'view', 'accept', 'status', 'signature', 'photo', 'sync', 'offline'}
IMAGES = ['screen-main.png', 'screen-new.png', 'screen-list.png', 'screen-dialog.png', 'screen-calendar.png', 'screen-map.png']


def step_list(c, m, verb):
    steps = []
    if verb not in MOBILE:
        steps.append(c.pick(OPEN))
        if verb in ITEM_VERBS: steps.append(c.pick(PICK))
    steps += CORE[verb]
    if verb not in NO_SAVE: steps.append(c.pick(SAVE))
    if verb in ('create', 'book', 'plan', 'connect', 'custom', 'duplicate'): steps.append(c.pick(RESULT))
    return [subst(c, s, m) for s in steps]


def intro_text(c, m, verb, title):
    v = {'create': 'create', 'edit': 'change', 'delete': 'delete', 'search': 'find', 'assign': 'assign', 'close': 'close', 'reopen': 'reopen', 'duplicate': 'copy', 'print': 'print', 'export': 'export'}.get(verb, verb if verb in ('merge', 'archive', 'restore', 'import', 'send', 'run', 'share', 'plan', 'book', 'move', 'cancel', 'connect', 'test', 'map', 'view') else 'manage')
    ing = {'create': 'creating', 'change': 'changing', 'delete': 'deleting', 'find': 'finding', 'assign': 'assigning', 'close': 'closing', 'reopen': 'reopening', 'copy': 'copying', 'print': 'printing', 'export': 'exporting',
           'merge': 'merging', 'archive': 'archiving', 'restore': 'restoring', 'import': 'importing', 'send': 'sending', 'run': 'running', 'share': 'sharing', 'plan': 'planning', 'book': 'booking', 'move': 'moving', 'cancel': 'canceling', 'connect': 'connecting', 'test': 'testing', 'map': 'mapping', 'view': 'viewing', 'manage': 'managing'}[v]
    bad_p = {'A': 0.8, 'B': 0.75, 'C': 0.4, 'D': 0.15}[c.era]
    a = 'an' if (m['obj'][0] in 'aeiou' and not m['obj'].startswith('user')) else 'a'
    sub = lambda t: t.replace('{Menu}', m['menu']).replace('{v}', v).replace('{ing}', ing).replace('{a}', a).replace('{o}', c.term('wo') if m['key'] == 'wo' else m['obj'])
    if c.chance(bad_p):
        t, kind, bad, fix = c.pick(INTROS_BAD)
        if kind == 'simply' and False: pass
        s = sub(t).replace('{B}', P(bad, kind, fix))
        # sentence-start capitalisation of the planted words that start a sentence
        return s
    return sub(c.pick(INTROS_CLEAN))


def note_text(c, m):
    bad_p = {'A': 0.7, 'B': 0.65, 'C': 0.4, 'D': 0.2}[c.era]
    if c.chance(bad_p):
        t, kind, bad, fix = c.pick(NOTES_BAD)
        return t.replace('{B}', P(bad, kind, fix)).replace('{o}', c.term('wo') if m['key'] == 'wo' else m['obj']).replace('click Save', 'click ' + UI(c, 'Save'))
    t = c.pick(NOTES_CLEAN)
    return t.replace('{Save}', UI(c, 'Save'))


def contact_line(c):
    r = c.rng.random()
    out = []
    if r < {'A': 0.5, 'B': 0.5, 'C': 0.35, 'D': 0.2}[c.era]:
        out.append('For help, call %s.' % P('1-800-555-0142', 'phone', 'a variable'))
    if c.chance({'A': 0.35, 'B': 0.45, 'C': 0.3, 'D': 0.15}[c.era]):
        out.append('Visit %s for more information.' % P('http://support.marlowe-dispatch.example.com/help', 'url', 'a variable'))
    if c.chance(0.2): out.append('Email %s.' % P('dispatch-support@marlowe-dispatch.example.com', 'url', 'a variable'))
    return ' '.join(out)


def claim_line(c):
    if c.chance({'A': 0.12, 'B': 0.12, 'C': 0.08, 'D': 0.03}[c.era]):
        return c.pick(['The program is %s for every dispatcher.' % P('free of charge', 'claims_free', ''), 'Each update is %s to arrive within one minute.' % P('guaranteed', 'claims_guaranteed', ''),
                       'A new reporting module is %s.' % P('coming soon', 'claims_soon', '')])
    return ''


