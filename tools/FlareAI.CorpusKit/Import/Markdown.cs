using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace FlareAI.CorpusKit
{
    /// <summary>
    /// Markdown to HTML for import. Covers what documentation sources use: ATX/setext headings, paragraphs, nested lists,
    /// pipe tables, fenced code, blockquotes, GitHub <c>[!NOTE]</c> alerts, MkDocs <c>!!!</c> and Docusaurus <c>:::</c>
    /// admonitions, raw HTML blocks, and inline emphasis, code, links and images.
    /// </summary>
    internal static class Markdown
    {
        private static readonly Regex ListMarker = new Regex(@"^(\s*)([-*+]|\d+[.)])\s+(.*)$");
        private static readonly Regex Atx = new Regex(@"^\s{0,3}(#{1,6})\s+(.*?)\s*#*\s*$");
        private static readonly Regex TableSep = new Regex(@"^\s*\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?\s*$");
        private static readonly Regex Hr = new Regex(@"^\s{0,3}([-*_])(\s*\1){2,}\s*$");

        /// <summary>Splits optional YAML front matter off the text and returns the body and the title, if given.</summary>
        public static string StripFrontMatter(string md, out string title)
        {
            title = null;
            md = md.TrimStart('﻿').Replace("\r\n", "\n").Replace('\r', '\n');
            if (!md.StartsWith("---\n", StringComparison.Ordinal)) return md;
            int end = md.IndexOf("\n---", 4, StringComparison.Ordinal);
            if (end < 0) return md;
            var fm = md.Substring(4, end - 4);
            var m = Regex.Match(fm, @"^title:\s*[""']?(.+?)[""']?\s*$", RegexOptions.Multiline);
            if (m.Success) title = m.Groups[1].Value;
            int after = md.IndexOf('\n', end + 1);
            return after < 0 ? "" : md.Substring(after + 1);
        }

        public static string ToHtml(string md)
        {
            string t;
            md = StripFrontMatter(md, out t);
            return Blocks(md.Split('\n').ToList());
        }

        private static string Blocks(List<string> lines)
        {
            var sb = new StringBuilder();
            int i = 0;
            while (i < lines.Count)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line)) { i++; continue; }

                // fenced code
                var fence = Regex.Match(line, @"^\s*(```+|~~~+)\s*([\w+#.-]*)");
                if (fence.Success)
                {
                    var marker = fence.Groups[1].Value;
                    var lang = fence.Groups[2].Value;
                    var code = new List<string>();
                    i++;
                    while (i < lines.Count && !lines[i].TrimStart().StartsWith(marker, StringComparison.Ordinal)) code.Add(lines[i++]);
                    i++;
                    sb.Append("<pre").Append(lang.Length > 0 ? " class=\"language-" + lang + "\"" : "").Append(">")
                      .Append(WebUtility.HtmlEncode(string.Join("\n", code))).Append("</pre>\n");
                    continue;
                }

                // admonitions: Docusaurus ::: and MkDocs !!!
                var dos = Regex.Match(line, @"^\s*:::\s*(\w+)");
                if (dos.Success)
                {
                    var inner = new List<string>();
                    i++;
                    while (i < lines.Count && !lines[i].TrimStart().StartsWith(":::", StringComparison.Ordinal)) inner.Add(lines[i++]);
                    i++;
                    sb.Append("<div class=\"admonition ").Append(dos.Groups[1].Value.ToLowerInvariant()).Append("\">").Append(Blocks(inner)).Append("</div>\n");
                    continue;
                }
                var mk = Regex.Match(line, @"^\s*!!!\s*(\w+)(?:\s+""(.*)"")?\s*$");
                if (mk.Success)
                {
                    var inner = new List<string>();
                    i++;
                    while (i < lines.Count && (lines[i].StartsWith("    ", StringComparison.Ordinal) || lines[i].StartsWith("\t", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(lines[i])))
                        inner.Add(lines[i++].StartsWith("    ", StringComparison.Ordinal) ? lines[i - 1].Substring(4) : lines[i - 1].TrimStart('\t'));
                    sb.Append("<div class=\"admonition ").Append(mk.Groups[1].Value.ToLowerInvariant()).Append("\">").Append(Blocks(inner)).Append("</div>\n");
                    continue;
                }

                var atx = Atx.Match(line);
                if (atx.Success)
                {
                    int lvl = atx.Groups[1].Length;
                    sb.Append("<h").Append(lvl).Append(">").Append(Inline(atx.Groups[2].Value)).Append("</h").Append(lvl).Append(">\n");
                    i++; continue;
                }
                if (i + 1 < lines.Count && Regex.IsMatch(lines[i + 1], @"^\s{0,3}(=+|-+)\s*$") && !ListMarker.IsMatch(line) && !Hr.IsMatch(line) && line.Trim().Length > 0)
                {
                    int lvl = lines[i + 1].TrimStart().StartsWith("=", StringComparison.Ordinal) ? 1 : 2;
                    sb.Append("<h").Append(lvl).Append(">").Append(Inline(line.Trim())).Append("</h").Append(lvl).Append(">\n");
                    i += 2; continue;
                }
                if (Hr.IsMatch(line)) { sb.Append("<hr />\n"); i++; continue; }

                if (line.TrimStart().StartsWith(">", StringComparison.Ordinal))
                {
                    var q = new List<string>();
                    while (i < lines.Count && lines[i].TrimStart().StartsWith(">", StringComparison.Ordinal))
                    {
                        var l = lines[i].TrimStart().Substring(1);
                        q.Add(l.StartsWith(" ", StringComparison.Ordinal) ? l.Substring(1) : l);
                        i++;
                    }
                    sb.Append("<blockquote>").Append(Blocks(q)).Append("</blockquote>\n");
                    continue;
                }

                if (line.Contains("|") && i + 1 < lines.Count && TableSep.IsMatch(lines[i + 1]) && lines[i + 1].Contains("-"))
                {
                    var header = Cells(line);
                    i += 2;
                    sb.Append("<table><thead><tr>");
                    foreach (var h in header) sb.Append("<th>").Append(Inline(h)).Append("</th>");
                    sb.Append("</tr></thead><tbody>");
                    while (i < lines.Count && lines[i].Contains("|") && !string.IsNullOrWhiteSpace(lines[i]))
                    {
                        sb.Append("<tr>");
                        var cells = Cells(lines[i]);
                        for (int c = 0; c < header.Count; c++) sb.Append("<td>").Append(Inline(c < cells.Count ? cells[c] : "")).Append("</td>");
                        sb.Append("</tr>");
                        i++;
                    }
                    sb.Append("</tbody></table>\n");
                    continue;
                }

                var lm = ListMarker.Match(line);
                if (lm.Success)
                {
                    i = List(lines, i, sb);
                    continue;
                }

                if (line.TrimStart().StartsWith("<", StringComparison.Ordinal) && Regex.IsMatch(line.TrimStart(), @"^<(/?)(div|table|img|figure|details|summary|p|ul|ol|h[1-6]|pre|blockquote|section|aside|dl|hr|br)\b", RegexOptions.IgnoreCase))
                {
                    var raw = new List<string>();
                    while (i < lines.Count && !string.IsNullOrWhiteSpace(lines[i])) raw.Add(lines[i++]);
                    sb.Append(string.Join("\n", raw)).Append("\n");
                    continue;
                }

                if ((line.StartsWith("    ", StringComparison.Ordinal) || line.StartsWith("\t", StringComparison.Ordinal)))
                {
                    var code = new List<string>();
                    while (i < lines.Count && (lines[i].StartsWith("    ", StringComparison.Ordinal) || lines[i].StartsWith("\t", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(lines[i])))
                        code.Add(lines[i++].StartsWith("    ", StringComparison.Ordinal) ? lines[i - 1].Substring(4) : lines[i - 1].TrimStart('\t'));
                    sb.Append("<pre>").Append(WebUtility.HtmlEncode(string.Join("\n", code).TrimEnd())).Append("</pre>\n");
                    continue;
                }

                // paragraph
                var para = new List<string>();
                while (i < lines.Count && !string.IsNullOrWhiteSpace(lines[i]) && !StartsBlock(lines, i, para.Count == 0))
                    para.Add(lines[i++].Trim());
                if (para.Count == 0) { para.Add(lines[i++].Trim()); }
                sb.Append("<p>").Append(Inline(string.Join(" ", para))).Append("</p>\n");
            }
            return sb.ToString();
        }

        private static bool StartsBlock(List<string> lines, int i, bool first)
        {
            if (first) return false;
            var l = lines[i];
            return Atx.IsMatch(l) || ListMarker.IsMatch(l) || Regex.IsMatch(l, @"^\s*(```|~~~|:::|!!!)") || l.TrimStart().StartsWith(">", StringComparison.Ordinal) || Hr.IsMatch(l)
                || (l.Contains("|") && i + 1 < lines.Count && TableSep.IsMatch(lines[i + 1]));
        }

        private static List<string> Cells(string row)
        {
            row = row.Trim();
            if (row.StartsWith("|", StringComparison.Ordinal)) row = row.Substring(1);
            if (row.EndsWith("|", StringComparison.Ordinal)) row = row.Substring(0, row.Length - 1);
            var cells = new List<string>();
            var cur = new StringBuilder();
            bool code = false;
            for (int k = 0; k < row.Length; k++)
            {
                char c = row[k];
                if (c == '`') code = !code;
                if (c == '\\' && k + 1 < row.Length && row[k + 1] == '|') { cur.Append('|'); k++; continue; }
                if (c == '|' && !code) { cells.Add(cur.ToString().Trim()); cur.Clear(); continue; }
                cur.Append(c);
            }
            cells.Add(cur.ToString().Trim());
            return cells;
        }

        /// <summary>Parses one list starting at <paramref name="i"/>; returns the index after it.</summary>
        private static int List(List<string> lines, int i, StringBuilder sb)
        {
            var first = ListMarker.Match(lines[i]);
            int baseIndent = first.Groups[1].Length;
            bool ordered = char.IsDigit(first.Groups[2].Value[0]);
            sb.Append(ordered ? "<ol>" : "<ul>");
            while (i < lines.Count)
            {
                var m = ListMarker.Match(lines[i]);
                if (!m.Success || m.Groups[1].Length != baseIndent || char.IsDigit(m.Groups[2].Value[0]) != ordered) break;
                int contentIndent = baseIndent + m.Groups[2].Length + 1;
                var item = new List<string> { m.Groups[3].Value };
                i++;
                while (i < lines.Count)
                {
                    var l = lines[i];
                    if (string.IsNullOrWhiteSpace(l))
                    {
                        int j = i;
                        while (j < lines.Count && string.IsNullOrWhiteSpace(lines[j])) j++;
                        if (j < lines.Count && Indent(lines[j]) > baseIndent) { for (; i < j; i++) item.Add(""); continue; }
                        break;
                    }
                    var lm = ListMarker.Match(l);
                    if (Indent(l) > baseIndent) { item.Add(Dedent(l, Math.Min(contentIndent, Indent(l)))); i++; continue; }
                    if (lm.Success) break;
                    if (Atx.IsMatch(l) || l.TrimStart().StartsWith("```", StringComparison.Ordinal)) break;
                    item.Add(l.Trim()); i++; // lazy continuation
                }
                var inner = Blocks(item).Trim();
                if (!item.Any(string.IsNullOrWhiteSpace))
                {
                    // tight item: the first paragraph is the item's own text, not a nested <p>
                    var lead = Regex.Match(inner, @"^<p>(.*?)</p>", RegexOptions.Singleline);
                    if (lead.Success) inner = lead.Groups[1].Value + inner.Substring(lead.Length);
                }
                sb.Append("<li>").Append(inner).Append("</li>");
            }
            sb.Append(ordered ? "</ol>\n" : "</ul>\n");
            return i;
        }

        private static int Indent(string l)
        {
            int n = 0;
            foreach (var c in l) { if (c == ' ') n++; else if (c == '\t') n += 4; else break; }
            return n;
        }

        private static string Dedent(string l, int n)
        {
            int k = 0, removed = 0;
            while (k < l.Length && removed < n && (l[k] == ' ' || l[k] == '\t')) { removed += l[k] == '\t' ? 4 : 1; k++; }
            return l.Substring(k);
        }

        private static readonly Regex InlineTag = new Regex(@"</?(kbd|br|span|sub|sup|b|i|em|strong|code|a|img|abbr|u|mark)\b[^>]*>", RegexOptions.IgnoreCase);

        /// <summary>Inline Markdown to HTML.</summary>
        public static string Inline(string s)
        {
            var hold = new List<string>();
            Func<string, string> park = h => { hold.Add(h); return "\u0001" + (hold.Count - 1) + "\u0001"; };

            s = Regex.Replace(s, @"(`+)(.+?)\1", m => park("<code>" + WebUtility.HtmlEncode(m.Groups[2].Value.Trim()) + "</code>"));
            s = InlineTag.Replace(s, m => park(m.Value));
            s = Regex.Replace(s, @"<(https?://[^>\s]+)>", m => park("<a href=\"" + WebUtility.HtmlEncode(m.Groups[1].Value) + "\">" + WebUtility.HtmlEncode(m.Groups[1].Value) + "</a>"));
            s = s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
            s = Regex.Replace(s, @"\\([\\`*_{}\[\]()#+\-.!|])", m => park(WebUtility.HtmlEncode(m.Groups[1].Value)));
            s = Regex.Replace(s, @"!\[([^\]]*)\]\(([^)\s]+)(?:\s+""[^""]*"")?\)", m => park("<img src=\"" + m.Groups[2].Value + "\" alt=\"" + m.Groups[1].Value + "\" />"));
            s = Regex.Replace(s, @"\[([^\]]+)\]\(([^)\s]+)(?:\s+""[^""]*"")?\)", m => park("<a href=\"" + m.Groups[2].Value + "\">") + m.Groups[1].Value + park("</a>"));
            s = Regex.Replace(s, @"\*\*(?=\S)(.+?)(?<=\S)\*\*", "<strong>$1</strong>");
            s = Regex.Replace(s, @"(?<![\w])__(?=\S)(.+?)(?<=\S)__(?![\w])", "<strong>$1</strong>");
            s = Regex.Replace(s, @"(?<![\w*])\*(?=[^\s*])(.+?)(?<=[^\s*])\*(?![\w*])", "<em>$1</em>");
            s = Regex.Replace(s, @"(?<![\w])_(?=[^\s_])(.+?)(?<=[^\s_])_(?![\w])", "<em>$1</em>");
            for (int pass = 0; pass < 3; pass++)
                s = Regex.Replace(s, "\u0001(\\d+)\u0001", m => hold[int.Parse(m.Groups[1].Value)]);
            return s;
        }
    }
}
