using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Xml;
using FlareAI.Core.Editing;
using FlareAI.Core.Style;

namespace FlareAI.Core.Actions
{
    /// <summary>
    /// Puts findings in reading order under the heading they belong to, so a long topic reads as sections of work
    /// instead of one flat list. Snippet findings go last, under "Snippet: name". Also marks findings whose text
    /// is gone (edited since the review) as stale.
    /// </summary>
    public static class ReviewOrder
    {
        public static void Annotate(XmlDocument doc, IEnumerable<StyleFinding> findings)
        {
            var body = doc == null ? null : BlockSelector.FindBody(doc);
            var index = new Dictionary<XmlNode, int>();
            var heading = new Dictionary<XmlNode, string>();
            var runOrder = new Dictionary<XmlNode, int>();
            if (body != null)
            {
                var current = "";
                var i = 0;
                foreach (XmlNode c in body.ChildNodes)
                {
                    if (c.NodeType != XmlNodeType.Element) continue;
                    if (IsHeading(c)) current = Clean(c.InnerText);
                    index[c] = i++;
                    heading[c] = current;
                }
                var runs = TextRuns.Of(new List<XmlNode> { body });
                for (var r = 0; r < runs.Count; r++) runOrder[runs[r]] = r;
            }
            foreach (var f in findings)
            {
                if (f.DocumentPath != null)
                {
                    f.Section = "Snippet: " + System.IO.Path.GetFileNameWithoutExtension(f.DocumentPath);
                    f.Order = long.MaxValue / 2;
                    continue;
                }
                XmlText run = null; int start = 0;
                var found = doc != null && FindingLocator.TryLocate(doc, f, out run, out start);
                var block = found ? TextRuns.BlockOf(run) : f.Block;
                int bi, ri;
                f.Section = block != null && heading.ContainsKey(block) ? heading[block] : "";
                f.Order = block != null && index.TryGetValue(block, out bi)
                    ? bi * 100000000L + (found && runOrder.TryGetValue(run, out ri) ? ri * 10000L + start : 0)
                    : long.MaxValue / 4;
                f.IsStale = !found && !f.IsFixed;
            }
        }

        public static bool IsHeading(XmlNode n)
        {
            var name = n.LocalName.ToLowerInvariant();
            return name.Length == 2 && name[0] == 'h' && name[1] >= '1' && name[1] <= '6';
        }

        private static string Clean(string s) { return Regex.Replace(s ?? "", @"\s+", " ").Trim(); }

        /// <summary>
        /// Splits a long topic's blocks into sections at headings, merging small ones so each review call has enough
        /// context. Topics shorter than <paramref name="singleBelow"/> characters stay one section.
        /// </summary>
        public static List<List<XmlNode>> Sections(IList<XmlNode> blocks, int minChars = 1500, int singleBelow = 3000)
        {
            var total = 0;
            foreach (var b in blocks) total += b.InnerText.Length;
            var result = new List<List<XmlNode>>();
            if (total < singleBelow) { result.Add(new List<XmlNode>(blocks)); return result; }
            var cur = new List<XmlNode>();
            var size = 0;
            foreach (var b in blocks)
            {
                if (IsHeading(b) && size >= minChars) { result.Add(cur); cur = new List<XmlNode>(); size = 0; }
                cur.Add(b);
                size += b.InnerText.Length;
            }
            if (cur.Count > 0) result.Add(cur);
            return result;
        }
    }
}
