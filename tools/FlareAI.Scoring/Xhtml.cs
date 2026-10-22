using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

namespace FlareAI.Scoring
{
    /// <summary>A stretch of a block's plain text contributed by one DOM node.</summary>
    public sealed class TextRun
    {
        public int Start, End;
        public XmlNode Node;
    }

    /// <summary>A block element and its plain text (see <c>docs/testing/CORPUS.md</c>, "Block plain text").</summary>
    public sealed class BlockText
    {
        public string XPath;
        public XmlElement Element;
        public string Text = "";
        public List<TextRun> Runs = new List<TextRun>();
    }

    /// <summary>
    /// Independent reader for Flare XHTML topics and snippets: block enumeration, XPath ids, plain text, and the
    /// context vocabulary. Deliberately does not use <c>FlareAI.Core</c> (labels must not depend on the engine's extractor).
    /// </summary>
    public static class Xhtml
    {
        /// <summary>Elements that own a stretch of text. Their plain text excludes nested block elements.</summary>
        private static readonly HashSet<string> BlockNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "h1", "h2", "h3", "h4", "h5", "h6", "p", "li", "td", "th", "dt", "dd", "caption", "figcaption", "blockquote", "pre", "div",
            "MadCap:dropDownHead", "MadCap:toggler"
        };

        public static bool IsBlock(string qualifiedName) { return BlockNames.Contains(qualifiedName); }

        public static XmlDocument Load(string path)
        {
            var doc = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
            doc.Load(path);
            return doc;
        }

        public static XmlDocument Parse(string xml)
        {
            var doc = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
            doc.LoadXml(xml);
            return doc;
        }

        // ---- XPath ids ------------------------------------------------------------------------------------------------

        /// <summary>Absolute path of qualified names with 1-based same-name sibling indexes, always present except on html, head and body: <c>/html/body/ol[1]/li[3]/p[1]</c>.</summary>
        public static string XPathOf(XmlElement el)
        {
            var parts = new List<string>();
            XmlNode n = el;
            while (n != null && n.NodeType == XmlNodeType.Element)
            {
                int idx = 1;
                for (var s = n.PreviousSibling; s != null; s = s.PreviousSibling)
                    if (s.NodeType == XmlNodeType.Element && s.Name == n.Name) idx++;
                parts.Add(n.Name == "html" || n.Name == "body" || n.Name == "head" ? n.Name : n.Name + "[" + idx.ToString(CultureInfo.InvariantCulture) + "]");
                n = n.ParentNode;
            }
            parts.Reverse();
            return "/" + string.Join("/", parts.ToArray());
        }

        /// <summary>Resolves a path made by <see cref="XPathOf"/>; null when it does not exist.</summary>
        public static XmlElement Resolve(XmlDocument doc, string xpath)
        {
            if (string.IsNullOrEmpty(xpath) || xpath[0] != '/') return null;
            var steps = xpath.Substring(1).Split('/');
            XmlElement cur = null;
            for (int i = 0; i < steps.Length; i++)
            {
                string name = steps[i];
                int idx = 1;
                int b = name.IndexOf('[');
                if (b >= 0)
                {
                    int e = name.IndexOf(']', b);
                    if (e < 0 || !int.TryParse(name.Substring(b + 1, e - b - 1), NumberStyles.None, CultureInfo.InvariantCulture, out idx) || idx < 1) return null;
                    name = name.Substring(0, b);
                }
                if (i == 0)
                {
                    cur = doc.DocumentElement;
                    if (cur == null || cur.Name != name) return null;
                    continue;
                }
                XmlElement next = null;
                int seen = 0;
                foreach (XmlNode c in cur.ChildNodes)
                {
                    if (c.NodeType == XmlNodeType.Element && c.Name == name && ++seen == idx) { next = (XmlElement)c; break; }
                }
                if (next == null) return null;
                cur = next;
            }
            return cur;
        }

        // ---- plain text ------------------------------------------------------------------------------------------------

        /// <summary>
        /// Plain text of an element. A block element yields its own inline content (nested blocks are separate blocks);
        /// any other element yields the text of its whole subtree. Whitespace runs collapse to one space, the ends are trimmed,
        /// <c>br</c> is a space, <c>MadCap:variable</c> contributes <paramref name="variable"/>(name) or nothing, images and
        /// comments contribute nothing.
        /// </summary>
        public static BlockText Extract(XmlElement el, Func<string, string> variable)
        {
            var b = new BlockText { Element = el, XPath = XPathOf(el) };
            var sb = new StringBuilder();
            bool own = IsBlock(el.Name);
            Walk(el, el, own, sb, b.Runs, variable);
            // trim the trailing space
            while (sb.Length > 0 && sb[sb.Length - 1] == ' ') sb.Length--;
            b.Text = sb.ToString();
            for (int i = b.Runs.Count - 1; i >= 0; i--)
            {
                var r = b.Runs[i];
                if (r.Start >= b.Text.Length) b.Runs.RemoveAt(i);
                else if (r.End > b.Text.Length) r.End = b.Text.Length;
            }
            return b;
        }

        private static void Walk(XmlNode node, XmlElement root, bool own, StringBuilder sb, List<TextRun> runs, Func<string, string> variable)
        {
            foreach (XmlNode c in node.ChildNodes)
            {
                switch (c.NodeType)
                {
                    case XmlNodeType.Text:
                    case XmlNodeType.CDATA:
                    case XmlNodeType.SignificantWhitespace:
                    case XmlNodeType.Whitespace:
                        Append(sb, runs, c, c.Value);
                        break;
                    case XmlNodeType.Element:
                        var e = (XmlElement)c;
                        if (e.Name == "br") { Append(sb, runs, e, " "); break; }
                        if (e.Name == "img" || e.Name == "script" || e.Name == "style") break;
                        if (e.Name == "MadCap:variable")
                        {
                            string v = variable == null ? null : variable(e.GetAttribute("name"));
                            if (!string.IsNullOrEmpty(v)) Append(sb, runs, e, v);
                            break;
                        }
                        if (IsBlock(e.Name))
                        {
                            if (own) break; // nested block: its own block
                            Append(sb, runs, e, " ");
                            Walk(e, root, false, sb, runs, variable);
                            Append(sb, runs, e, " ");
                            break;
                        }
                        if (!own && IsContainer(e.Name)) Append(sb, runs, e, " ");
                        Walk(e, root, own, sb, runs, variable);
                        if (!own && IsContainer(e.Name)) Append(sb, runs, e, " ");
                        break;
                }
            }
        }

        private static bool IsContainer(string name)
        {
            return name == "ol" || name == "ul" || name == "dl" || name == "table" || name == "thead" || name == "tbody" || name == "tr"
                || name.StartsWith("MadCap:dropDown", StringComparison.Ordinal);
        }

        private static void Append(StringBuilder sb, List<TextRun> runs, XmlNode node, string text)
        {
            int start = sb.Length;
            foreach (char ch in text)
            {
                if (char.IsWhiteSpace(ch) && ch != ' ')
                {
                    if (sb.Length == 0 || sb[sb.Length - 1] == ' ') continue;
                    sb.Append(' ');
                }
                else sb.Append(ch);
            }
            if (sb.Length > start) runs.Add(new TextRun { Start = start, End = sb.Length, Node = node });
        }

        /// <summary>Every block element with non-empty plain text, in document order.</summary>
        public static IEnumerable<BlockText> Blocks(XmlDocument doc, Func<string, string> variable)
        {
            if (doc.DocumentElement == null) yield break;
            foreach (var el in Descendants(doc.DocumentElement))
            {
                if (!IsBlock(el.Name)) continue;
                var b = Extract(el, variable);
                if (b.Text.Length > 0) yield return b;
            }
        }

        public static IEnumerable<XmlElement> Descendants(XmlElement root)
        {
            foreach (XmlNode c in root.ChildNodes)
            {
                var e = c as XmlElement;
                if (e == null) continue;
                yield return e;
                foreach (var d in Descendants(e)) yield return d;
            }
        }

        // ---- context vocabulary ------------------------------------------------------------------------------------------

        /// <summary><c>context.line</c> values for a block element.</summary>
        public static List<string> LineContext(XmlElement el)
        {
            string n = el.Name;
            if (n.Length == 2 && n[0] == 'h' && n[1] >= '1' && n[1] <= '6') return One("heading:" + n);
            if (n == "th") return One("table-header");
            if (n == "td") return One("table-cell");
            if (n == "pre") return One("code-block");
            if (n == "blockquote") return One("quote");
            if (n == "caption" || n == "figcaption") return One("caption");
            if (n == "MadCap:dropDownHead") return One("dropdown-head");
            if (n == "MadCap:toggler") return One("toggler");
            string cls = " " + el.GetAttribute("class").ToLowerInvariant() + " ";
            if (cls.Contains(" warning ") || cls.Contains(" caution ") || cls.Contains(" danger ")) return One("warning");
            if (cls.Contains(" note ") || cls.Contains(" tip ")) return One("note");
            if (n == "li")
            {
                var list = el.ParentNode as XmlElement;
                return One(list != null && list.Name == "ol" ? "step" : "list");
            }
            for (var p = el.ParentNode as XmlElement; p != null; p = p.ParentNode as XmlElement)
            {
                switch (p.Name)
                {
                    case "li":
                        var list = p.ParentNode as XmlElement;
                        return One(list != null && list.Name == "ol" ? "step" : "list");
                    case "th": return One("table-header");
                    case "td": return One("table-cell");
                    case "blockquote": return One("quote");
                    case "pre": return One("code-block");
                    case "MadCap:dropDownHead": return One("dropdown-head");
                    case "caption": case "figcaption": return One("caption");
                }
            }
            if (n == "dd") return One("glossary-def");
            return One("paragraph");
        }

        private static List<string> One(string s) { return new List<string> { s }; }

        /// <summary><c>context.span</c> values for the text [start, end) of a block.</summary>
        public static List<string> SpanContext(BlockText block, int start, int end, string file, bool boilerplate)
        {
            var set = new List<string>();
            Action<string> add = s => { if (!set.Contains(s)) set.Add(s); };
            if (end <= start) end = start + 1;
            for (int ri = 0; ri < block.Runs.Count; ri++)
            {
                var r = block.Runs[ri];
                if (r.End > start && r.Start < end)
                {
                    for (XmlNode n = r.Node; n != null && n != block.Element; n = n.ParentNode)
                    {
                        var e = n as XmlElement;
                        if (e == null) continue;
                        switch (e.Name)
                        {
                            case "b": case "strong": add("ui-label"); break;
                            case "i": case "em": add("emphasis"); break;
                            case "code": case "tt": case "samp": add("inline-code"); break;
                            case "kbd": add("kbd"); break;
                            case "a": add("link-text"); break;
                            case "MadCap:xref": add("xref"); break;
                        }
                        if (e.Name == "span" && (" " + e.GetAttribute("class") + " ").Contains(" uicontrol ")) add("ui-label");
                    }
                }
                if (r.Node is XmlElement && ((XmlElement)r.Node).Name == "MadCap:variable" && ((r.End == start) || (r.Start == end))) add("variable-adjacent");
            }
            for (XmlNode n = block.Element; n != null && n.NodeType == XmlNodeType.Element; n = n.ParentNode)
                if (((XmlElement)n).HasAttribute("MadCap:conditions")) { add("conditional"); break; }
            if (file != null && file.EndsWith(".flsnp", StringComparison.OrdinalIgnoreCase)) add("in-snippet-block");
            if (boilerplate) add("boilerplate");
            return set;
        }
    }
}
