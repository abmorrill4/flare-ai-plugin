using System;
using System.Collections.Generic;
using System.Xml;
using FlareAI.Core.Editing;

namespace FlareAI.Core.Style
{
    /// <summary>
    /// The text nodes of a block that an author can actually edit: in document order, skipping pending
    /// tracked deletions and text owned by opaque MadCap elements (snippets, index keywords, …).
    /// Lint and fixes work per run, so a match never spans markup.
    /// </summary>
    public static class TextRuns
    {
        private static readonly HashSet<string> Opaque = new HashSet<string>
        {
            "snippetBlock", "snippetText", "keyword", "concept", "annotation", "variable", "glossaryTerm", "toggler"
        };

        public static List<XmlText> Of(IList<XmlNode> blocks)
        {
            var runs = new List<XmlText>();
            var removed = TrackedChanges.RemoveIds(blocks.Count > 0 ? blocks[0].OwnerDocument : null);
            foreach (var b in blocks) Walk(b, removed, runs);
            return runs;
        }

        private static void Walk(XmlNode n, HashSet<string> removed, List<XmlText> runs)
        {
            if (n.NodeType == XmlNodeType.Text)
            {
                if (n.Value.Trim().Length > 0) runs.Add((XmlText)n);
                return;
            }
            if (n.NodeType != XmlNodeType.Element) return;
            if (n.NamespaceURI == BlockSelector.MadCapNs && Opaque.Contains(n.LocalName)) return;
            if (TrackedChanges.IsDeleted(n, removed)) return;
            foreach (XmlNode c in n.ChildNodes) Walk(c, removed, runs);
        }

        /// <summary>True when the run sits in an h1–h6 (heading-only rules such as sentence case apply).</summary>
        public static bool IsHeading(XmlNode run)
        {
            for (var n = run.ParentNode; n != null && n.NodeType == XmlNodeType.Element; n = n.ParentNode)
                if (n.NamespaceURI.Length == 0 && n.LocalName.Length == 2 && n.LocalName[0] == 'h' && char.IsDigit(n.LocalName[1])) return true;
            return false;
        }

        /// <summary>Smallest widening of [start, start+length) that occurs exactly once in <paramref name="text"/>.</summary>
        public static void UniqueSpan(string text, int start, int length, out int s, out int l)
        {
            s = start; l = Math.Max(length, 1);
            if (s + l > text.Length) s = Math.Max(0, text.Length - l);
            while (true)
            {
                var piece = text.Substring(s, l);
                var first = text.IndexOf(piece, StringComparison.Ordinal);
                if (first == text.LastIndexOf(piece, StringComparison.Ordinal)) return;
                if (s + l < text.Length) l++;          // grow right first: keeps the change start where it is
                else if (s > 0) { s--; l++; }
                else return;                            // whole run repeats itself: cannot do better
            }
        }

        /// <summary>The body-level block containing a node, or null when the node is detached.</summary>
        public static XmlNode BlockOf(XmlNode n)
        {
            for (var cur = n; cur != null && cur.ParentNode != null; cur = cur.ParentNode)
                if (cur.ParentNode.NodeType == XmlNodeType.Element && cur.ParentNode.LocalName == "body") return cur;
            return null;
        }
    }
}
