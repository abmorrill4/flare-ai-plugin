using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FlareAI.CorpusKit
{
    /// <summary>A run of block text that came from one text node (or one variable), with a map back to the raw characters.</summary>
    internal sealed class Seg
    {
        public int Start, End;          // in the block's collapsed text
        public XText Text;              // null for a variable
        public XElement Var;            // the MadCap:variable, or null
        public XElement Parent;
        public int[] Map;               // raw index -> collapsed index (length raw.Length + 1)
    }

    internal sealed class BlockInfo
    {
        public string File, XPath, Text;
        public XElement Element;
        public List<Seg> Segs = new List<Seg>();
        public List<string> Line = new List<string>();
    }

    /// <summary>Project facts the context rules need: variable values, repeated blocks, snippets used as inline text.</summary>
    internal sealed class ProjectIndex
    {
        public string Root;
        public Dictionary<string, string> Variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> BlockFiles = new Dictionary<string, int>(StringComparer.Ordinal);
        public HashSet<string> TextSnippets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public int BoilerplateMin = 5;

        public static ProjectIndex Load(string root, bool countBlocks = true)
        {
            var idx = new ProjectIndex { Root = Path.GetFullPath(root) };
            var vdir = Path.Combine(idx.Root, "Project", "VariableSets");
            if (Directory.Exists(vdir))
                foreach (var f in Directory.GetFiles(vdir, "*.flvar"))
                {
                    var set = Path.GetFileNameWithoutExtension(f);
                    foreach (var v in XDocument.Load(f).Root.Elements("Variable"))
                        idx.Variables[set + "." + (string)v.Attribute("Name")] = v.Value;
                }
            var perBlock = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var f in Fx.TopicFiles(idx.Root))
            {
                var rel = Fx.Rel(idx.Root, f);
                XDocument doc;
                try { doc = Fx.Load(f); } catch (Exception) { continue; }
                foreach (var st in doc.Descendants(Fx.MadCap + "snippetText"))
                {
                    var src = (string)st.Attribute("src");
                    if (src != null) idx.TextSnippets.Add(Fx.Resolve(rel, src));
                }
                if (!countBlocks) continue;
                foreach (var b in BlockWalker.Blocks(doc, rel, idx))
                {
                    HashSet<string> s;
                    if (!perBlock.TryGetValue(b.Text, out s)) perBlock[b.Text] = s = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    s.Add(rel);
                }
            }
            foreach (var kv in perBlock) idx.BlockFiles[kv.Key] = kv.Value.Count;
            return idx;
        }
    }

    /// <summary>
    /// Computes block text and line/span contexts straight from the DOM, with its own small walker. Deliberately shares no code
    /// with FlareAI.Core's extractor, so a bug in the engine's context detection shows up as a scoring difference.
    /// Block text: whitespace collapsed; <c>MadCap:variable</c> contributes its resolved value; nested blocks and snippet
    /// references contribute nothing (they are blocks of their own, or of the snippet file).
    /// </summary>
    internal static class BlockWalker
    {
        private static readonly HashSet<string> BlockNames = new HashSet<string>(StringComparer.Ordinal)
            { "p", "li", "td", "th", "h1", "h2", "h3", "h4", "h5", "h6", "dt", "dd", "caption", "blockquote", "pre", "MadCap:dropDownHead" };
        private static readonly HashSet<string> Containers = new HashSet<string>(StringComparer.Ordinal)
            { "ol", "ul", "dl", "table", "thead", "tbody", "tfoot", "tr", "div", "MadCap:dropDown", "MadCap:dropDownBody", "MadCap:snippetBlock" };
        private static readonly Regex UiVerb = new Regex(@"\b(select|click|clicking|choose|open|tap|press|go to|selecting|from the|on the|in the|using the)\s+(the\s+)?$", RegexOptions.IgnoreCase);

        public static List<BlockInfo> Blocks(XDocument doc, string file, ProjectIndex idx)
        {
            var list = new List<BlockInfo>();
            var body = Fx.Body(doc);
            if (body == null) return list;
            foreach (var e in body.Descendants().ToList())
            {
                if (!BlockNames.Contains(Fx.Prefixed(e.Name))) continue;
                var b = new BlockInfo { File = file, Element = e };
                var sb = new StringBuilder();
                Collect(e, b, sb, idx);
                var text = sb.ToString();
                var trimmed = text.TrimEnd();
                if (trimmed.Length == 0) continue;
                b.Text = trimmed;
                foreach (var s in b.Segs) { if (s.End > trimmed.Length) s.End = trimmed.Length; }
                b.XPath = XPathOf(e);
                b.Line = LineContexts(e);
                list.Add(b);
            }
            return list;
        }

        private static void Collect(XElement e, BlockInfo b, StringBuilder sb, ProjectIndex idx)
        {
            foreach (var n in e.Nodes())
            {
                var t = n as XText;
                if (t != null)
                {
                    var seg = new Seg { Start = sb.Length, Text = t, Parent = e, Map = new int[t.Value.Length + 1] };
                    var v = t.Value;
                    for (int i = 0; i < v.Length; i++)
                    {
                        seg.Map[i] = sb.Length;
                        char c = v[i];
                        if (char.IsWhiteSpace(c) || c == ' ')
                        {
                            if (sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(' ');
                        }
                        else sb.Append(c);
                    }
                    seg.Map[v.Length] = sb.Length;
                    seg.End = sb.Length;
                    if (seg.End > seg.Start) b.Segs.Add(seg);
                    continue;
                }
                var c2 = n as XElement;
                if (c2 == null) continue;
                var name = Fx.Prefixed(c2.Name);
                if (name == "MadCap:variable")
                {
                    string val;
                    var key = (string)c2.Attribute("name") ?? "";
                    var text = idx != null && idx.Variables.TryGetValue(key, out val) ? val : "{" + key + "}";
                    var seg = new Seg { Start = sb.Length, Var = c2, Parent = e };
                    sb.Append(text);
                    seg.End = sb.Length;
                    b.Segs.Add(seg);
                    continue;
                }
                if (name == "br") { if (sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(' '); continue; }
                if (BlockNames.Contains(name) && name != "MadCap:dropDownHead" || Containers.Contains(name) || name == "MadCap:snippetText") continue;
                Collect(c2, b, sb, idx);
            }
        }

        // ---- line context ----------------------------------------------------------------------------------------

        public static List<string> LineContexts(XElement e)
        {
            var r = new List<string>();
            var name = Fx.Prefixed(e.Name);
            var cls = new HashSet<string>(Fx.ClassTokens(e), StringComparer.OrdinalIgnoreCase);
            if (cls.Overlaps(new[] { "note", "tip", "important" })) r.Add("note");
            else if (cls.Overlaps(new[] { "warning", "caution", "danger" })) r.Add("warning");
            else if (cls.Contains("caption")) r.Add("caption");

            string own = null;
            if (name == "MadCap:dropDownHead") own = "dropdown-head";
            else if (name.Length == 2 && name[0] == 'h' && char.IsDigit(name[1])) own = "heading:" + name;
            else if (name == "th") own = "table-header";
            else if (name == "td") own = "table-cell";
            else if (name == "dd") own = "glossary-def";
            else if (name == "caption") own = "caption";
            else if (name == "pre") own = "code-block";
            else if (name == "blockquote") own = "quote";
            else if (name == "li") own = ListKind(e);
            if (own == null && (name == "p" || name == "dt"))
            {
                foreach (var a in e.Ancestors())
                {
                    var an = Fx.Prefixed(a.Name);
                    if (an == "li") { own = ListKind(a); break; }
                    if (an == "td") { own = "table-cell"; break; }
                    if (an == "th") { own = "table-header"; break; }
                    if (an == "blockquote") { own = "quote"; break; }
                    if (an == "dd") { own = "glossary-def"; break; }
                    if (an == "MadCap:dropDownBody" || an == "body") break;
                }
            }
            if (own == null && r.Count == 0) own = "paragraph";
            if (own != null && !r.Contains(own)) r.Add(own);
            return r;
        }

        private static string ListKind(XElement li)
        {
            var list = li.Ancestors().FirstOrDefault(a => a.Name.LocalName == "ol" || a.Name.LocalName == "ul");
            return list != null && list.Name.LocalName == "ol" ? "step" : "list";
        }

        // ---- span context ----------------------------------------------------------------------------------------

        public static List<string> SpanContexts(BlockInfo b, int start, int end, ProjectIndex idx)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            var segs = b.Segs.Where(s => s.Start < end && s.End > start).ToList();
            foreach (var s in segs)
            {
                if (s.Var != null) set.Add("variable-adjacent");
                for (XElement a = s.Parent; a != null && a != b.Element.Parent; a = a.Parent)
                {
                    var n = Fx.Prefixed(a.Name);
                    if (a.Attribute(Fx.MadCap + "conditions") != null) set.Add("conditional");
                    if (a == b.Element) break;
                    switch (n)
                    {
                        case "span": if (Fx.ClassTokens(a).Contains("uicontrol")) set.Add("ui-label"); break;
                        case "b": case "strong": set.Add(IsUiBold(a) ? "ui-label" : "emphasis"); break;
                        case "i": case "em": set.Add("emphasis"); break;
                        case "code": case "tt": case "samp": set.Add("inline-code"); break;
                        case "kbd": set.Add("kbd"); break;
                        case "a": set.Add("link-text"); break;
                        case "MadCap:xref": set.Add("xref"); break;
                    }
                }
                // conditions on enclosing blocks
                for (XElement a = b.Element.Parent; a != null; a = a.Parent)
                    if (a.Attribute(Fx.MadCap + "conditions") != null) { set.Add("conditional"); break; }
            }
            if (segs.Count > 0)
            {
                int first = b.Segs.IndexOf(segs[0]), last = b.Segs.IndexOf(segs[segs.Count - 1]);
                if (first > 0 && b.Segs[first - 1].Var != null && b.Segs[first - 1].End >= start - 1) set.Add("variable-adjacent");
                if (last + 1 < b.Segs.Count && b.Segs[last + 1].Var != null && b.Segs[last + 1].Start <= end + 1) set.Add("variable-adjacent");
            }
            if (idx != null)
            {
                if (b.File.StartsWith("Content/Resources/Snippets/", StringComparison.OrdinalIgnoreCase))
                    set.Add(idx.TextSnippets.Contains(b.File) ? "in-snippet-text" : "in-snippet-block");
                int n;
                if (idx.BlockFiles.TryGetValue(b.Text, out n) && n >= idx.BoilerplateMin) set.Add("boilerplate");
            }
            var order = new[] { "ui-label", "link-text", "xref", "emphasis", "inline-code", "kbd", "variable-adjacent", "in-snippet-block", "in-snippet-text", "conditional", "boilerplate" };
            return order.Where(set.Contains).ToList();
        }

        /// <summary>Bold counts as a UI label inside a numbered step or right after a verb like "select".</summary>
        public static bool IsUiBold(XElement b)
        {
            if (b.Ancestors().Any(a => a.Name.LocalName == "li" && a.Parent != null && a.Parent.Name.LocalName == "ol")) return true;
            var prev = b.PreviousNode as XText;
            return prev != null && UiVerb.IsMatch(prev.Value) && b.Value.Length <= 40;
        }

        // ---- paths -----------------------------------------------------------------------------------------------

        public static string XPathOf(XElement e)
        {
            var steps = new List<string>();
            for (var a = e; a != null; a = a.Parent)
            {
                var n = Fx.Prefixed(a.Name);
                if (a.Parent == null || n == "body") { steps.Add(n); continue; }
                int idx = 1 + a.ElementsBeforeSelf(a.Name).Count();
                steps.Add(n + "[" + idx + "]");
            }
            steps.Reverse();
            return "/" + string.Join("/", steps);
        }

        public static XElement Resolve(XDocument doc, string xpath)
        {
            var parts = xpath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || doc.Root == null) return null;
            XElement cur = doc.Root;
            if (StepName(parts[0]) != Fx.Prefixed(cur.Name)) return null;
            for (int i = 1; i < parts.Length; i++)
            {
                var nm = Fx.Name(StepName(parts[i]));
                int idx = StepIndex(parts[i]);
                cur = cur.Elements(nm).Skip(idx - 1).FirstOrDefault();
                if (cur == null) return null;
            }
            return cur;
        }

        private static string StepName(string s) { var k = s.IndexOf('['); return k < 0 ? s : s.Substring(0, k); }

        private static int StepIndex(string s)
        {
            var k = s.IndexOf('[');
            if (k < 0) return 1;
            int v;
            return int.TryParse(s.Substring(k + 1).TrimEnd(']'), out v) ? v : 1;
        }

        // ---- quotes ----------------------------------------------------------------------------------------------

        /// <summary>Prefix and suffix, grown until prefix+quote+suffix occurs exactly once in the block text.</summary>
        public static void QuoteContext(string text, int start, int len, out string prefix, out string suffix)
        {
            for (int n = 12; ; n += 12)
            {
                int ps = Math.Max(0, start - n), se = Math.Min(text.Length, start + len + n);
                prefix = text.Substring(ps, start - ps);
                suffix = text.Substring(start + len, se - (start + len));
                var probe = prefix + text.Substring(start, len) + suffix;
                if (CountOf(text, probe) == 1 || (ps == 0 && se == text.Length)) return;
            }
        }

        public static int CountOf(string text, string needle)
        {
            if (needle.Length == 0) return 0;
            int c = 0, i = 0;
            while ((i = text.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { c++; i++; }
            return c;
        }
    }
}
