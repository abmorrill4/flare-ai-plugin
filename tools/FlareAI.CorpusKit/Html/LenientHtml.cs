using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace FlareAI.CorpusKit
{
    /// <summary>
    /// Tolerant HTML reader for import: unclosed p/li/td, void elements, unquoted attributes, entities, comments.
    /// Returns an element tree under a synthetic <c>root</c>. Not a general HTML5 parser; enough for documentation sources.
    /// </summary>
    internal static class LenientHtml
    {
        private static readonly HashSet<string> Void = new HashSet<string>(StringComparer.Ordinal)
            { "br", "hr", "img", "input", "meta", "link", "col", "area", "base", "wbr", "source", "param" };
        private static readonly HashSet<string> RawText = new HashSet<string>(StringComparer.Ordinal) { "script", "style" };
        private static readonly HashSet<string> BreakP = new HashSet<string>(StringComparer.Ordinal)
            { "p", "div", "h1", "h2", "h3", "h4", "h5", "h6", "ul", "ol", "li", "table", "pre", "blockquote", "dl", "dt", "dd", "hr", "section", "article", "aside", "figure", "details", "tr", "td", "th" };
        private static readonly HashSet<string> Scope = new HashSet<string>(StringComparer.Ordinal)
            { "li", "td", "th", "table", "blockquote", "div", "section", "article", "aside", "body", "dd", "details", "figure" };

        public static XElement Parse(string html)
        {
            var root = new XElement("root");
            var stack = new List<XElement> { root };
            int i = 0, n = html.Length;
            var text = new StringBuilder();

            Action flush = () =>
            {
                if (text.Length == 0) return;
                stack[stack.Count - 1].Add(new XText(WebUtility.HtmlDecode(text.ToString())));
                text.Clear();
            };

            while (i < n)
            {
                char c = html[i];
                if (c != '<') { text.Append(c); i++; continue; }
                if (string.CompareOrdinal(html, i, "<!--", 0, 4) == 0)
                {
                    int e = html.IndexOf("-->", i + 4, StringComparison.Ordinal);
                    i = e < 0 ? n : e + 3; continue;
                }
                if (i + 1 < n && (html[i + 1] == '!' || html[i + 1] == '?'))
                {
                    int e = html.IndexOf('>', i);
                    i = e < 0 ? n : e + 1; continue;
                }
                if (i + 1 < n && html[i + 1] == '/')
                {
                    int e = html.IndexOf('>', i);
                    if (e < 0) { i = n; break; }
                    var name = Clean(html.Substring(i + 2, e - i - 2).Trim());
                    flush();
                    CloseTo(stack, name);
                    i = e + 1; continue;
                }
                if (i + 1 < n && char.IsLetter(html[i + 1]))
                {
                    flush();
                    int j = i + 1;
                    while (j < n && !char.IsWhiteSpace(html[j]) && html[j] != '>' && html[j] != '/') j++;
                    var name = Clean(html.Substring(i + 1, j - i - 1));
                    var el = new XElement(name);
                    bool selfClose = false;
                    // attributes
                    while (j < n)
                    {
                        while (j < n && char.IsWhiteSpace(html[j])) j++;
                        if (j >= n) break;
                        if (html[j] == '>') { j++; break; }
                        if (html[j] == '/') { selfClose = true; j++; continue; }
                        int s = j;
                        while (j < n && !char.IsWhiteSpace(html[j]) && html[j] != '=' && html[j] != '>' && html[j] != '/') j++;
                        var an = html.Substring(s, j - s);
                        string av = "";
                        int k = j;
                        while (k < n && char.IsWhiteSpace(html[k])) k++;
                        if (k < n && html[k] == '=')
                        {
                            k++;
                            while (k < n && char.IsWhiteSpace(html[k])) k++;
                            if (k < n && (html[k] == '"' || html[k] == '\''))
                            {
                                char q = html[k];
                                int e = html.IndexOf(q, k + 1);
                                if (e < 0) e = n;
                                av = html.Substring(k + 1, e - k - 1);
                                j = Math.Min(n, e + 1);
                            }
                            else
                            {
                                int s2 = k;
                                while (k < n && !char.IsWhiteSpace(html[k]) && html[k] != '>') k++;
                                av = html.Substring(s2, k - s2);
                                j = k;
                            }
                        }
                        var key = an.ToLowerInvariant();
                        if (key.Length > 0 && IsXmlName(key) && el.Attribute(key) == null) el.SetAttributeValue(key, WebUtility.HtmlDecode(av));
                    }
                    i = j;
                    AutoClose(stack, name);
                    stack[stack.Count - 1].Add(el);
                    if (RawText.Contains(name))
                    {
                        int e = html.IndexOf("</" + name, i, StringComparison.OrdinalIgnoreCase);
                        if (e < 0) e = n;
                        el.Add(new XText(html.Substring(i, e - i)));
                        int gt = html.IndexOf('>', e);
                        i = gt < 0 ? n : gt + 1;
                    }
                    else if (!selfClose && !Void.Contains(name)) stack.Add(el);
                    continue;
                }
                text.Append(c); i++;
            }
            flush();
            return root;
        }

        private static bool IsXmlName(string s)
        {
            try { XmlConvert.VerifyName(s); return !s.Contains(":"); } catch (XmlException) { return false; }
        }

        private static string Clean(string name)
        {
            name = name.Trim().ToLowerInvariant().Replace(':', '-');
            var sb = new StringBuilder();
            foreach (var ch in name) if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' || ch == '.') sb.Append(ch);
            if (sb.Length == 0 || !(char.IsLetter(sb[0]) || sb[0] == '_')) sb.Insert(0, 'x');
            return sb.ToString();
        }

        private static void CloseTo(List<XElement> stack, string name)
        {
            for (int k = stack.Count - 1; k >= 1; k--)
                if (stack[k].Name.LocalName == name) { stack.RemoveRange(k, stack.Count - k); return; }
        }

        private static void PopWhile(List<XElement> stack, Func<string, bool> pred, Func<string, bool> stopAt)
        {
            for (int k = stack.Count - 1; k >= 1; k--)
            {
                var nm = stack[k].Name.LocalName;
                if (stopAt(nm)) return;
                if (pred(nm)) { stack.RemoveRange(k, stack.Count - k); return; }
            }
        }

        private static void AutoClose(List<XElement> stack, string name)
        {
            if (BreakP.Contains(name))
                PopWhile(stack, t => t == "p", t => Scope.Contains(t));
            switch (name)
            {
                case "li": PopWhile(stack, t => t == "li", t => t == "ul" || t == "ol"); break;
                case "dt":
                case "dd": PopWhile(stack, t => t == "dt" || t == "dd", t => t == "dl"); break;
                case "tr": PopWhile(stack, t => t == "tr", t => t == "table"); break;
                case "td":
                case "th": PopWhile(stack, t => t == "td" || t == "th", t => t == "tr" || t == "table"); break;
                case "thead":
                case "tbody":
                case "tfoot": PopWhile(stack, t => t == "thead" || t == "tbody" || t == "tfoot", t => t == "table"); break;
            }
        }
    }
}
