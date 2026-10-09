using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace FlareAI.Core.Editing
{
    /// <summary>One piece of a diff line: unchanged, removed, or added text.</summary>
    public sealed class DiffSegment
    {
        public DiffKind Kind { get; set; }
        public string Text { get; set; }
    }

    public enum DiffKind { Same, Deleted, Inserted }

    /// <summary>One readable line (a paragraph, heading, list item or cell) with its word-level changes.</summary>
    public sealed class DiffLine
    {
        /// <summary>"1. ", "• ", "# " … — how the line reads in the topic; empty for a paragraph.</summary>
        public string Prefix { get; set; }
        public List<DiffSegment> Segments { get; set; }
        public bool Changed { get { return Segments.Exists(s => s.Kind != DiffKind.Same); } }
    }

    /// <summary>
    /// Renders a rewrite as the author reads it: text lines (not XHTML), each with deletions and insertions marked
    /// word by word. Lines are matched in order; changed lines are paired and diffed, extra lines are wholly
    /// added or removed. Variables show as [Set.Name], snippets as [snippet file].
    /// </summary>
    public static class ReadableDiff
    {
        private static readonly HashSet<string> LineTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "p", "li", "h1", "h2", "h3", "h4", "h5", "h6", "td", "th", "dt", "dd", "pre", "caption", "title", "blockquote", "div" };

        public static List<DiffLine> Of(IList<XmlNode> before, IList<XmlNode> after)
        {
            var a = Lines(before);
            var b = Lines(after);
            var result = new List<DiffLine>();
            var keysA = a.ConvertAll(l => l.Key);
            var keysB = b.ConvertAll(l => l.Key);
            int x = 0, y = 0;
            foreach (var c in LineChunks(keysA, keysB))
            {
                for (; x < c[0]; x++, y++) result.Add(Same(a[x]));
                int na = c[1] - c[0], nb = c[3] - c[2];
                for (var i = 0; i < Math.Max(na, nb); i++)
                {
                    if (i < na && i < nb) result.Add(Words(a[c[0] + i], b[c[2] + i]));
                    else if (i < na) result.Add(Whole(a[c[0] + i], DiffKind.Deleted));
                    else result.Add(Whole(b[c[2] + i], DiffKind.Inserted));
                }
                x = c[1]; y = c[3];
            }
            for (; x < a.Count; x++) result.Add(Same(a[x]));
            return result;
        }

        /// <summary>The diff of one span (a finding's excerpt and its replacement), for proposal cards.</summary>
        public static List<DiffSegment> OfText(string before, string after)
        {
            return Segments(before ?? "", after ?? "");
        }

        // ---- lines --------------------------------------------------------------------------------

        private sealed class Line { public string Prefix; public string Text; public string Key; }

        private static List<Line> Lines(IList<XmlNode> blocks)
        {
            var lines = new List<Line>();
            foreach (var b in blocks) Collect(b, lines);
            return lines;
        }

        private static void Collect(XmlNode node, List<Line> lines)
        {
            if (node.NodeType != XmlNodeType.Element) return;
            if (IsLine(node))
            {
                var sb = new StringBuilder();
                var nested = new List<XmlNode>();
                Text(node, sb, nested);
                var prefix = Prefix(node);
                // <li><p>text</p>…</li>: the item's first paragraph reads as the item itself.
                if (sb.ToString().Trim().Length == 0 && nested.Count > 0 && nested[0].LocalName == "p" && prefix.Length > 0)
                {
                    nested[0] = Merge(nested[0], sb, nested);
                }
                var text = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
                if (text.Length > 0) lines.Add(new Line { Prefix = prefix, Text = text, Key = prefix + text });
                foreach (var n in nested) if (n != null) Collect(n, lines);
                return;
            }
            foreach (XmlNode c in node.ChildNodes) Collect(c, lines);
        }

        private static bool IsLine(XmlNode n) { return LineTags.Contains(n.LocalName) && n.NamespaceURI != BlockSelector.MadCapNs; }

        /// <summary>Inline text of a line; nested line elements (a p inside an li) become their own lines.</summary>
        private static void Text(XmlNode node, StringBuilder sb, List<XmlNode> nested)
        {
            foreach (XmlNode c in node.ChildNodes)
            {
                if (c is XmlCharacterData && c.NodeType != XmlNodeType.Comment) { sb.Append(c.Value); continue; }
                if (c.NodeType != XmlNodeType.Element) continue;
                var e = (XmlElement)c;
                if (IsLine(e)) { nested.Add(e); continue; }
                if (e.NamespaceURI == BlockSelector.MadCapNs && e.LocalName == "variable") { sb.Append('[').Append(e.GetAttribute("name")).Append(']'); continue; }
                if (e.NamespaceURI == BlockSelector.MadCapNs && (e.LocalName == "snippetText" || e.LocalName == "snippetBlock"))
                { sb.Append("[snippet ").Append(System.IO.Path.GetFileNameWithoutExtension(e.GetAttribute("src"))).Append(']'); continue; }
                if (e.LocalName == "img") { sb.Append("[image ").Append(e.GetAttribute("alt")).Append(']'); continue; }
                if (e.LocalName == "br") { sb.Append(' '); continue; }
                Text(e, sb, nested);
            }
        }

        /// <summary>Reads the paragraph's text into the item's line; its own nested lines follow the item. Returns null (consumed).</summary>
        private static XmlNode Merge(XmlNode p, StringBuilder sb, List<XmlNode> nested)
        {
            var inner = new List<XmlNode>();
            Text(p, sb, inner);
            nested.InsertRange(1, inner);
            return null;
        }

        private static string Prefix(XmlNode n)
        {
            var name = n.LocalName.ToLowerInvariant();
            if (name.Length == 2 && name[0] == 'h' && char.IsDigit(name[1])) return new string('#', name[1] - '0') + " ";
            if (name != "li") return "";
            var list = n.ParentNode;
            if (list == null || list.LocalName != "ol") return "• ";
            var i = 1;
            for (var s = n.PreviousSibling; s != null; s = s.PreviousSibling) if (s.NodeType == XmlNodeType.Element && s.LocalName == "li") i++;
            return i + ". ";
        }

        // ---- diffing --------------------------------------------------------------------------------

        private static DiffLine Same(Line l) { return new DiffLine { Prefix = l.Prefix, Segments = new List<DiffSegment> { new DiffSegment { Kind = DiffKind.Same, Text = l.Text } } }; }
        private static DiffLine Whole(Line l, DiffKind k) { return new DiffLine { Prefix = l.Prefix, Segments = new List<DiffSegment> { new DiffSegment { Kind = k, Text = l.Text } } }; }
        private static DiffLine Words(Line a, Line b) { return new DiffLine { Prefix = b.Prefix, Segments = Segments(a.Text, b.Text) }; }

        private static List<DiffSegment> Segments(string before, string after)
        {
            var ta = WordDiff.Tokens(before);
            var tb = WordDiff.Tokens(after);
            var chunks = (long)ta.Count * tb.Count <= 250000 ? WordDiff.Chunks(ta, tb) : new List<int[]> { new[] { 0, ta.Count, 0, tb.Count } };
            // Changes separated only by a space read as one change ("In order to" → "To", not "In"→"To" + "order to").
            var merged = new List<int[]>();
            foreach (var c in chunks)
            {
                var last = merged.Count > 0 ? merged[merged.Count - 1] : null;
                if (last != null && AllSpace(ta, last[1], c[0])) { last[1] = c[1]; last[3] = c[3]; }
                else merged.Add((int[])c.Clone());
            }
            var segs = new List<DiffSegment>();
            var x = 0;
            foreach (var c in merged)
            {
                // A space both sides end (or start) with is unchanged text, not part of the edit.
                while (c[1] > c[0] && c[3] > c[2] && WordDiff.IsSpace(ta[c[1] - 1]) && WordDiff.IsSpace(tb[c[3] - 1])) { c[1]--; c[3]--; }
                while (c[1] > c[0] && c[3] > c[2] && WordDiff.IsSpace(ta[c[0]]) && WordDiff.IsSpace(tb[c[2]])) { c[0]++; c[2]++; }
                Add(segs, DiffKind.Same, Join(ta, x, c[0]));
                Add(segs, DiffKind.Deleted, Join(ta, c[0], c[1]));
                Add(segs, DiffKind.Inserted, Join(tb, c[2], c[3]));
                x = c[1];
            }
            Add(segs, DiffKind.Same, Join(ta, x, ta.Count));
            return segs;
        }

        private static void Add(List<DiffSegment> segs, DiffKind kind, string text)
        {
            if (text.Length == 0) return;
            var last = segs.Count > 0 ? segs[segs.Count - 1] : null;
            if (last != null && last.Kind == kind) last.Text += text;
            else segs.Add(new DiffSegment { Kind = kind, Text = text });
        }

        private static bool AllSpace(List<string> t, int from, int to)
        {
            for (var i = from; i < to; i++) if (!WordDiff.IsSpace(t[i])) return false;
            return true;
        }

        private static string Join(List<string> t, int from, int to) { return string.Concat(t.GetRange(from, to - from).ToArray()); }

        /// <summary>LCS over whole lines → changed ranges [oa, ob, pa, pb].</summary>
        private static List<int[]> LineChunks(List<string> a, List<string> b)
        {
            int n = a.Count, m = b.Count;
            var dp = new int[n + 1, m + 1];
            for (var i = n - 1; i >= 0; i--)
                for (var j = m - 1; j >= 0; j--)
                    dp[i, j] = a[i] == b[j] ? dp[i + 1, j + 1] + 1 : Math.Max(dp[i + 1, j], dp[i, j + 1]);
            var result = new List<int[]>();
            int x = 0, y = 0, ca = -1, cb = -1;
            while (x < n || y < m)
            {
                if (x < n && y < m && a[x] == b[y]) { if (ca >= 0) { result.Add(new[] { ca, x, cb, y }); ca = -1; } x++; y++; }
                else { if (ca < 0) { ca = x; cb = y; } if (y < m && (x == n || dp[x, y + 1] >= dp[x + 1, y])) y++; else x++; }
            }
            if (ca >= 0) result.Add(new[] { ca, n, cb, m });
            return result;
        }
    }
}
