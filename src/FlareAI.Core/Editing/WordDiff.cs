using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Xml;
using FlareAI.Core.Actions;
using FlareAI.Core.Style;

namespace FlareAI.Core.Editing
{
    /// <summary>
    /// Turns a rewrite into word-level text replacements when the model kept the structure (same elements and
    /// attributes, only text changed). Each replacement is a <see cref="StyleFinding"/> anchored by text and
    /// context, so it is applied through the same re-locating, tracked path as one-click fixes.
    /// Returns null when the structure changed (caller falls back to a block replacement).
    /// </summary>
    public static class WordDiff
    {
        private static readonly Regex Token = new Regex(@"\s+|\w+(?:['’]\w+)*|[^\w\s]", RegexOptions.Compiled);
        private const int MaxCells = 250000; // token-LCS budget per text slot; beyond it the slot is one edit

        /// <param name="view">What the model was given (<see cref="AcceptedView"/> of the live blocks).</param>
        /// <param name="proposed">The validated proposal.</param>
        /// <param name="live">The live blocks, parallel to <paramref name="view"/>.</param>
        public static List<StyleFinding> Plan(IList<XmlNode> view, IList<XmlNode> proposed, IList<XmlNode> live)
        {
            if (view.Count != proposed.Count || view.Count != live.Count) return null;
            var edits = new List<StyleFinding>();
            for (var i = 0; i < view.Count; i++)
                if (!Compare(view[i], proposed[i], live[i], edits)) return null;
            return edits;
        }

        private static bool Compare(XmlNode a, XmlNode b, XmlNode liveBlock, List<StyleFinding> edits)
        {
            if (a.NodeType != XmlNodeType.Element || b.NodeType != XmlNodeType.Element) return false;
            if (a.LocalName != b.LocalName || a.NamespaceURI != b.NamespaceURI || Attrs(a) != Attrs(b)) return false;

            // Children as alternating text slots and elements: slot0 el0 slot1 el1 … slotN.
            List<List<XmlText>> slotsA; List<XmlNode> elsA; List<string> slotsB; List<XmlNode> elsB;
            Split(a, out slotsA, out elsA);
            List<List<XmlText>> unusedB;
            Split(b, out unusedB, out elsB);
            slotsB = new List<string>();
            foreach (var s in unusedB) slotsB.Add(Concat(s));
            if (elsA.Count != elsB.Count) return false;

            for (var i = 0; i < slotsA.Count; i++)
                if (!DiffSlot(slotsA[i], slotsB[i], liveBlock, edits)) return false;
            for (var i = 0; i < elsA.Count; i++)
                if (!Compare(elsA[i], elsB[i], liveBlock, edits)) return false;
            return true;
        }

        private static void Split(XmlNode parent, out List<List<XmlText>> slots, out List<XmlNode> elements)
        {
            slots = new List<List<XmlText>> { new List<XmlText>() };
            elements = new List<XmlNode>();
            foreach (XmlNode c in parent.ChildNodes)
            {
                if (c.NodeType == XmlNodeType.Element) { elements.Add(c); slots.Add(new List<XmlText>()); }
                else if (c is XmlText) slots[slots.Count - 1].Add((XmlText)c);
                else if (c.NodeType == XmlNodeType.Whitespace || c.NodeType == XmlNodeType.SignificantWhitespace)
                    slots[slots.Count - 1].Add(null); // formatting whitespace: never a target, still a boundary
            }
        }

        private static string Concat(List<XmlText> runs)
        {
            var s = "";
            foreach (var r in runs) if (r != null) s += r.Value;
            return s;
        }

        private static bool DiffSlot(List<XmlText> runs, string after, XmlNode liveBlock, List<StyleFinding> edits)
        {
            var before = Concat(runs);
            if (Same(before, after)) return true;

            var ta = Tokens(before);
            var tb = Tokens(after);
            var chunks = (long)ta.Count * tb.Count <= MaxCells ? Chunks(ta, tb) : new List<int[]> { new[] { 0, ta.Count, 0, tb.Count } };

            var widened = new List<int[]>();
            foreach (var c in chunks)
            {
                int oa = c[0], ob = c[1], pa = c[2], pb = c[3];
                // A pure insertion has nothing to select: widen it over the previous word (or the next one).
                if (oa == ob)
                {
                    var k = oa - 1;
                    while (k >= 0 && IsSpace(ta[k])) k--;
                    if (k >= 0) { pa -= oa - k; oa = k; }
                    else
                    {
                        k = ob;
                        while (k < ta.Count && IsSpace(ta[k])) k++;
                        if (k >= ta.Count) return false; // empty slot: cannot be edited in place
                        pb += k + 1 - ob; ob = k + 1;
                    }
                }
                // Changes separated only by whitespace (or overlapping after widening) read better as one change.
                var last = widened.Count > 0 ? widened[widened.Count - 1] : null;
                if (last != null && OnlySpace(ta, last[1], oa)) { last[1] = Math.Max(last[1], ob); last[3] = Math.Max(last[3], pb); }
                else widened.Add(new[] { oa, ob, pa, pb });
            }
            foreach (var c in widened)
                if (!Emit(runs, Offset(ta, c[0]), Offset(ta, c[1]), Join(tb, c[2], c[3]), liveBlock, edits)) return false;
            return true;
        }

        /// <summary>Maps [start,end) in the slot onto its runs; the first run piece carries the replacement.</summary>
        private static bool Emit(List<XmlText> runs, int start, int end, string replacement, XmlNode liveBlock, List<StyleFinding> edits)
        {
            var pos = 0;
            var first = true;
            foreach (var r in runs)
            {
                var len = r == null ? 0 : r.Value.Length;
                int s = Math.Max(start, pos), e = Math.Min(end, pos + len);
                if (r != null && s < e)
                {
                    if (r.Value.Trim().Length == 0) return false; // whitespace-only runs are not addressable
                    edits.Add(FindingLocator.ForSpan(r, s - pos, e - s, first ? replacement : "", liveBlock));
                    first = false;
                }
                pos += len;
            }
            return !first;
        }

        /// <summary>LCS over tokens (whitespace equals any whitespace); returns changed ranges [oa,ob,pa,pb].</summary>
        private static List<int[]> Chunks(List<string> a, List<string> b)
        {
            int n = a.Count, m = b.Count;
            var dp = new int[n + 1, m + 1];
            for (var i = n - 1; i >= 0; i--)
                for (var j = m - 1; j >= 0; j--)
                    dp[i, j] = Eq(a[i], b[j]) ? dp[i + 1, j + 1] + 1 : Math.Max(dp[i + 1, j], dp[i, j + 1]);

            var result = new List<int[]>();
            int x = 0, y = 0, ca = -1, cb = -1;
            while (x < n || y < m)
            {
                if (x < n && y < m && Eq(a[x], b[y]))
                {
                    if (ca >= 0) { result.Add(new[] { ca, x, cb, y }); ca = -1; }
                    x++; y++;
                }
                else
                {
                    if (ca < 0) { ca = x; cb = y; }
                    if (y < m && (x == n || dp[x, y + 1] >= dp[x + 1, y])) y++; else x++;
                }
            }
            if (ca >= 0) result.Add(new[] { ca, n, cb, m });
            return result;
        }

        private static List<string> Tokens(string s)
        {
            var list = new List<string>();
            foreach (Match t in Token.Matches(s)) list.Add(t.Value);
            return list;
        }

        private static int Offset(List<string> tokens, int index)
        {
            var o = 0;
            for (var i = 0; i < index; i++) o += tokens[i].Length;
            return o;
        }

        private static string Join(List<string> tokens, int from, int to)
        {
            return string.Concat(tokens.GetRange(from, to - from).ToArray());
        }

        private static bool OnlySpace(List<string> tokens, int from, int to)
        {
            for (var i = from; i < to; i++) if (!IsSpace(tokens[i])) return false;
            return true; // also true when from >= to (overlap)
        }

        private static bool IsSpace(string t) { return t.Length > 0 && char.IsWhiteSpace(t[0]); }
        private static bool Eq(string x, string y) { return x == y || (IsSpace(x) && IsSpace(y)); }
        private static bool Same(string x, string y) { return Regex.Replace(x, @"\s+", " ").Trim() == Regex.Replace(y, @"\s+", " ").Trim(); }

        private static string Attrs(XmlNode n)
        {
            if (n.Attributes == null) return "";
            var parts = new List<string>();
            foreach (XmlAttribute a in n.Attributes)
                if (!a.Name.StartsWith("xmlns", StringComparison.Ordinal)) parts.Add(a.Name + "=" + a.Value);
            parts.Sort(StringComparer.Ordinal);
            return string.Join("|", parts.ToArray());
        }
    }
}
