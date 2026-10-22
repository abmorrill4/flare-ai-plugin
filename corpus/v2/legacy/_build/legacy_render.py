"""Era renderers and the topic plan for the synthetic legacy project."""
import posixpath, urllib.parse, copy
from legacy_gen import *


def head_common(title, css='../Resources/Stylesheets/Default.css', extra_ns='', extra_head=''):
    return ('<?xml version="1.0" encoding="utf-8"?>\n<html %s%s>\n<head>\n<title>%s</title>\n%s<link href="%s" rel="stylesheet" type="text/css" />\n</head>\n'
            % (NS_MC, extra_ns, esc(title), extra_head, css))


WORD_STYLE = """<style>
<!--
 /* Font Definitions */
 @font-face {font-family:"Cambria Math";}
 /* Style Definitions */
 p.MsoNormal, li.MsoNormal, div.MsoNormal {margin:0in; margin-bottom:.0001pt; font-size:11.0pt; font-family:"Calibri","sans-serif";}
 p.MsoListParagraph {margin-left:.5in;}
 @page WordSection1 {size:8.5in 11.0in; margin:1.0in 1.0in 1.0in 1.0in;}
 div.WordSection1 {page:WordSection1;}
-->
</style>
<!--[if gte mso 9]><xml><o:DocumentProperties><o:Author>jsmith</o:Author><o:Version>12.00</o:Version></o:DocumentProperties></xml><![endif]-->
"""


def msop(c, inner, cls='MsoNormal', style=None):
    sty = style or c.pick(['font-size:10.0pt;font-family:"Arial","sans-serif"', 'font-size:11.0pt;font-family:"Calibri","sans-serif"', 'font-size:10.0pt'])
    return '<p class="%s"><span style=\'%s\'>%s<o:p></o:p></span></p>' % (cls, sty, inner)


def pad(c):
    return NB * c.rng.choice([2, 3, 4, 6, 8])



def relhref(from_file, to_file):
    d = posixpath.dirname(from_file)
    r = posixpath.relpath(to_file, d) if d else to_file
    return urllib.parse.quote(r, safe='/.-_~')


def wordspan(c, inner):
    return '<span style=\'font-family:"Arial","sans-serif"\'>%s</span>' % inner


class R(object):
    """Renders a list of sections in the style of one era."""

    def __init__(self, c, t):
        self.c, self.t = c, t
        self.out = []
        self.h = 1          # last heading level written
        self.skip_used = False

    # ---- helpers
    def img_attrs(self, alt_ok_p):
        c = self.c
        if c.chance(alt_ok_p):
            return ' alt="%s"' % esc(c.pick(['Screen shot of the %s screen' % self.t.get('menu', 'main'), 'The main window']))
        return ' x-plant="alt_missing"'

    # ---- era A
    def A(self, sec):
        c, k = self.c, sec[0]
        if k == 'p': return msop(c, c.fx(sec[1]))
        if k == 'h2':
            if c.chance(0.4): return '<h2 class="Heading2"><span style=\'font-size:13.0pt\'>%s<o:p></o:p></span></h2>' % esc(sec[1])
            return '<p class="MsoNormal"><b><span style=\'font-size:13.0pt;font-family:"Arial","sans-serif"\'>%s<o:p></o:p></span></b></p>' % esc(sec[1])
        if k == 'steps':
            items = sec[1]
            if c.chance(0.55):
                return '\n'.join('<p class="MsoListParagraphCxSpMiddle" style=\'margin-left:.5in;text-indent:-.25in\'><!--[if !supportLists]--><span style=\'font-family:"Arial","sans-serif"\'>%d.<span style=\'font:7.0pt "Times New Roman"\'>%s</span></span><!--[endif]--><span style=\'font-family:"Arial","sans-serif"\'>%s<o:p></o:p></span></p>' % (i, NB * 7, c.fx(s)) for i, s in enumerate(items, 1))
            return '<ol>\n' + '\n'.join('<li class="MsoNormal"><span style=\'font-size:10.0pt\'>%s</span></li>' % c.fx(s) for s in items) + '\n</ol>'
        if k == 'ul':
            return '\n'.join('<p class="MsoListParagraphCxSpMiddle" style=\'margin-left:.5in;text-indent:-.25in\'><!--[if !supportLists]--><span style=\'font-family:Symbol\'>·<span style=\'font:7.0pt "Times New Roman"\'>%s</span></span><!--[endif]--><span style=\'font-family:"Arial","sans-serif"\'>%s<o:p></o:p></span></p>' % (NB * 7, c.fx(s)) for s in sec[1])
        if k == 'note':
            return '<p class="MsoNormal"><o:p>%s</o:p></p>\n' % NB + msop(c, '<b>Note:</b>' + pad(c) + c.fx(sec[1]))
        if k == 'img':
            return '<p class="MsoNormal"><span style=\'font-family:"Arial","sans-serif"\'><img width="%d" height="%d" src="../Resources/Images/%s"%s v:shapes="_x0000_i%d" /><o:p></o:p></span></p>' % (
                c.pick([420, 480, 512]), c.pick([260, 300, 320]), sec[1], self.img_attrs(0.0), 1025 + c.imgs)
        if k == 'table': return self.table(sec[1], 'A')
        if k == 'related': return ''
        if k == 'raw': return sec[1]
        if k == 'deep': return self.deep(sec[1])
        return self.generic(sec)

    # ---- era B
    def B(self, sec):
        c, k = self.c, sec[0]
        if k == 'p':
            s = c.fx(sec[1])
            if c.chance(0.25): s = s + BR + BR
            return '<p>%s</p>' % s + ('\n<p></p>' if c.chance(0.3) else '')
        if k == 'h2': return c.pick(['<p><font face="Arial" size="3"><b>%s</b></font></p>', '<p><b>%s</b></p>', '<p><b><u>%s</u></b></p>', '<p><strong>%s</strong></p>']) % esc(sec[1])
        if k == 'steps':
            items = sec[1]
            if c.chance(0.6):
                return '<p>' + (BR + '\n').join(('%d. ' % i) + c.fx(s) for i, s in enumerate(items, 1)) + '</p>'
            return '<ol type="1">\n' + '\n'.join('<li><font face="Arial" size="2">%s</font></li>' % c.fx(s) for s in items) + '\n</ol>'
        if k == 'ul':
            if c.chance(0.5): return '<p>' + (BR + '\n').join('-%s%s' % (NB * 2, c.fx(s)) for s in sec[1]) + '</p>'
            return '<ul>\n' + '\n'.join('<li>%s</li>' % c.fx(s) for s in sec[1]) + '\n</ul>'
        if k == 'note': return '<p></p>\n<p><b><font color="red">NOTE:</font></b>%s%s</p>' % (NB * c.rng.choice([1, 2, 3]), c.fx(sec[1]))
        if k == 'img':
            attr = c.pick([' x-plant="alt_missing"', ' x-plant="alt_missing"', ' alt=""', ' alt="image" x-plant="alt_junk"'])
            if attr == ' alt=""': attr = ' alt="" x-plant="alt_missing"'
            return '<p><img src="../Resources/Images/%s" border="0" width="%d" height="%d"%s /></p>' % (sec[1], c.pick([400, 450]), c.pick([240, 280]), attr)
        if k == 'table': return self.table(sec[1], 'B')
        if k == 'related': return ''
        if k == 'raw': return sec[1]
        if k == 'deep': return self.deep(sec[1])
        return self.generic(sec)

    # ---- era C
    def C(self, sec):
        c, k = self.c, sec[0]
        if k == 'p':
            s = c.fx(sec[1])
            if c.chance(0.06): s = '<span MadCap:conditions="Default.Obsolete">%s</span> ' % s + 'Contact your administrator for the latest settings.'
            return '<p>%s</p>' % s
        if k == 'h2':
            if self.h == 1 and not self.skip_used and c.chance(0.4):
                self.skip_used = True; self.h = 3
                return '<h3 x-plant="heading_skip">%s</h3>' % esc(sec[1])
            self.h = 2
            return '<h2>%s</h2>' % esc(sec[1])
        if k == 'steps':
            return '<ol>\n' + '\n'.join('<li%s>%s</li>' % (' MadCap:conditions="Default.Obsolete"' if c.chance(0.03) else '', c.fx(s)) for s in sec[1]) + '\n</ol>'
        if k == 'ul': return '<ul>\n' + '\n'.join('<li>%s</li>' % c.fx(s) for s in sec[1]) + '\n</ul>'
        if k == 'note': return '<p class="note">%s</p>' % (c.pick(['', '<span MadCap:conditions="Default.Print">Note: </span>', 'Note: ']) + c.fx(sec[1]))
        if k == 'img':
            return '<p><img src="../Resources/Images/%s"%s style="width: %dpx;height: %dpx;" /></p>' % (sec[1], self.img_attrs(0.5), c.pick([420, 480]), c.pick([260, 300]))
        if k == 'table': return self.table(sec[1], 'C')
        if k == 'related': return self.related(sec[1], True)
        if k == 'h1x': return '<h1 x-plant="second_h1">%s</h1>' % esc(sec[1])
        if k == 'raw': return sec[1]
        if k == 'deep': return self.deep(sec[1])
        return self.generic(sec)

    # ---- era D
    def D(self, sec):
        c, k = self.c, sec[0]
        if k == 'p': return '<p>%s</p>' % c.fx(sec[1])
        if k == 'h2': self.h = 2; return '<h2>%s</h2>' % esc(sec[1])
        if k == 'steps': return '<ol>\n' + '\n'.join('<li>%s</li>' % c.fx(s) for s in sec[1]) + '\n</ol>'
        if k == 'ul': return '<ul>\n' + '\n'.join('<li>%s</li>' % c.fx(s) for s in sec[1]) + '\n</ul>'
        if k == 'note': return '<p class="note">%s</p>' % c.fx(sec[1])
        if k == 'img':
            return '<p><img src="../Resources/Images/%s"%s /></p>' % (sec[1], self.img_attrs(0.85))
        if k == 'table': return self.table(sec[1], 'D')
        if k == 'related': return self.related(sec[1], False)
        if k == 'raw': return sec[1]
        if k == 'deep': return self.deep(sec[1])
        return self.generic(sec)

    # ---- shared
    def generic(self, sec):
        k = sec[0]
        if k == 'dropdown':
            return '<MadCap:dropDown>\n<MadCap:dropDownHead>\n<MadCap:dropDownHotspot>%s</MadCap:dropDownHotspot>\n</MadCap:dropDownHead>\n<MadCap:dropDownBody>\n<p>%s</p>\n</MadCap:dropDownBody>\n</MadCap:dropDown>' % (esc(sec[1]), self.c.fx(sec[2]))
        if k == 'snippet': return '<MadCap:snippetBlock src="%s" />' % sec[1]
        return ''

    def related(self, items, sloppy):
        if not items: return ''
        parts = []
        for text, target in items:
            href = relhref(self.t['file'], target)
            parts.append('<MadCap:xref href="%s">%s</MadCap:xref>' % (href, esc(text)))
        return '<p>See also: ' + '; '.join(parts) + '.</p>'

    def deep(self, spec):
        lines, levels = spec
        out = []
        for lvl in range(levels):
            out.append('<ul>')
            out.append('<li>%s (level %d)' % (lines[lvl % len(lines)], lvl + 1))
        out.append('</li>\n</ul>\n' * levels)
        return '\n'.join(out)

    def table(self, spec, era):
        c = self.c
        header, rows = spec['header'], spec['rows']
        use_th = {'A': False, 'B': False, 'C': c.chance(0.5), 'D': True}[era]
        plant = ' x-plant="table_header"' if (header and not use_th) else ''
        if era == 'A':
            top = '<table class="MsoNormalTable" border="1" cellspacing="0" cellpadding="0" style=\'border-collapse:collapse;border:none\'%s>' % plant
            cell = lambda s, w: '<td width="%d" valign="top" style=\'width:%dpt;border:solid windowtext 1.0pt;padding:0in 5.4pt 0in 5.4pt\'><p class="MsoNormal"><span style=\'font-size:10.0pt\'>%s<o:p></o:p></span></p></td>' % (w, w, s)
        elif era == 'B':
            top = '<table border="1" cellpadding="3" cellspacing="0" width="100%%"%s>' % plant
            cell = lambda s, w: '<td%s><font face="Arial" size="2">%s</font></td>' % (' bgcolor="#EEEEEE"' if c.chance(0.1) else '', s)
        else:
            top = '<table style="width: 100%%;"%s>' % plant if era == 'C' else '<table>'
            cell = lambda s, w: '<td>%s</td>' % s
        ws = spec.get('widths') or [120] * len(rows[0] if rows else header)
        out = [top]
        if header:
            if use_th:
                out.append('<thead>\n<tr>' + ''.join('<th>%s</th>' % esc(h) for h in header) + '</tr>\n</thead>\n<tbody>')
            else:
                out.append('<tr>' + ''.join(cell('<b>%s</b>' % esc(h), w) for h, w in zip(header, ws)) + '</tr>')
        elif era == 'D': out.append('<tbody>')
        for row in rows:
            out.append('<tr>' + ''.join(cell(c.fx(x), w) for x, w in zip(row, ws)) + '</tr>')
        if header and use_th or (not header and era == 'D'): out.append('</tbody>')
        out.append('</table>')
        return '\n'.join(out)


def render_topic(c, t, era):
    c.era = era
    r = R(c, t)
    if era == 'A':
        out = [head_common(t['title'], extra_ns=' ' + NS_WORD, extra_head='<meta http-equiv="Content-Type" content="text/html; charset=windows-1252" />\n<meta name="Generator" content="Microsoft Word 12" />\n' + WORD_STYLE),
               '<body lang="EN-US" link="blue" vlink="purple" style=\'tab-interval:.5in\'>\n<div class="WordSection1">']
        if c.chance(0.6): out.append('<h1 class="Heading1"><span style=\'font-size:16.0pt\'>%s<o:p></o:p></span></h1>' % esc(t['title']))
        else: out.append('<p class="MsoTitle"><b><span style=\'font-size:16.0pt;font-family:"Arial","sans-serif"\'>%s<o:p></o:p></span></b></p>' % esc(t['title']))
        tail = '<p class="MsoNormal"><o:p>%s</o:p></p>\n</div>\n</body>\n</html>\n' % NB
    elif era == 'B':
        out = [head_common(t['title']), '<body>']
        hd = c.pick(['font', 'b', 'center', 'u'])
        out.append({'font': '<p><font face="Arial" size="4" color="#003366"><b>%s</b></font></p>', 'b': '<p><b>%s</b></p>', 'center': '<center><b>%s</b></center>', 'u': '<p align="center"><u><b>%s</b></u></p>'}[hd] % esc(t['title']))
        tail = ('<p><a href="http://support.marlowe-dispatch.example.com/help/index.html">%s</a> for more topics.</p>\n' % P('Click here', 'click_here', 'Open the help index')) + '</body>\n</html>\n'
    else:
        out = [head_common(t['title']), '<body>', '<h1>%s</h1>' % esc(t['title'])]
        tail = '</body>\n</html>\n'
    for sec in t['sections']:
        piece = getattr(r, era)(sec)
        if piece: out.append(piece)
    if era == 'C' and t.get('helpcontrol'):
        out.append('<MadCap:HelpControl MadCap:name="HTML Help Control" MadCap:Type="Related Topics" MadCap:Width="0" MadCap:Height="0" />')
    out.append(tail)
    return '\n'.join(out)
