using System.Collections.Generic;
using System.Xml;

namespace FlareAI.Core.Editing
{
    /// <summary>
    /// Maps whatever the editor says is "selected" onto whole body-level blocks, which is the unit
    /// we send to the model and replace. v1 rule: nearest ancestor whose parent is &lt;body&gt;.
    /// Falls back to all body children when nothing is selected.
    /// </summary>
    public static class BlockSelector
    {
        public const string MadCapNs = "http://www.madcapsoftware.com/Schemas/MadCap.xsd";

        public static XmlElement FindBody(XmlDocument doc)
        {
            if (doc == null || doc.DocumentElement == null) return null;
            foreach (XmlNode n in doc.DocumentElement.ChildNodes)
                if (n.NodeType == XmlNodeType.Element && n.LocalName == "body") return (XmlElement)n;
            return null;
        }

        public static IList<XmlNode> SelectBlocks(XmlDocument doc, IList<XmlNode> selectionNodes)
        {
            var body = FindBody(doc);
            var result = new List<XmlNode>();
            if (body == null) return result;

            if (selectionNodes == null || selectionNodes.Count == 0)
            {
                foreach (XmlNode child in body.ChildNodes)
                    if (child.NodeType == XmlNodeType.Element) result.Add(child);
                return result;
            }

            var seen = new HashSet<XmlNode>();
            foreach (var n in selectionNodes)
            {
                var block = BodyLevelAncestor(n, body);
                if (block != null && seen.Add(block)) result.Add(block);
            }

            // document order, in case the editor handed us nodes end-first
            result.Sort((a, b) => IndexInParent(a).CompareTo(IndexInParent(b)));

            // Inside one list or table, send only the items/rows touched (siblings), not the whole list or table:
            // smaller prompts, smaller tracked changes. Mixed or structural selections keep the whole block.
            if (result.Count == 1 && IsContainer(result[0]))
            {
                var units = Units(selectionNodes, result[0]);
                if (units != null) return units;
            }
            return result;
        }

        private static bool IsContainer(XmlNode block)
        {
            return block.NamespaceURI.Length == 0 && (block.LocalName == "ul" || block.LocalName == "ol" || block.LocalName == "table" || block.LocalName == "dl");
        }

        /// <summary>The li / tr / dt-dd items holding the selection, if they are siblings; otherwise null.</summary>
        private static IList<XmlNode> Units(IList<XmlNode> selectionNodes, XmlNode container)
        {
            var units = new List<XmlNode>();
            foreach (var n in selectionNodes)
            {
                XmlNode unit = null;
                for (var cur = n; cur != null && cur != container; cur = cur.ParentNode)
                    if (cur.NodeType == XmlNodeType.Element && cur.NamespaceURI.Length == 0 &&
                        (cur.LocalName == "li" || cur.LocalName == "tr" || cur.LocalName == "dt" || cur.LocalName == "dd"))
                        unit = cur; // keep climbing: the outermost item below the container wins (nested lists stay whole)
                if (unit == null) return null;
                if (!units.Contains(unit)) units.Add(unit);
            }
            if (units.Count == 0) return null;
            var parent = units[0].ParentNode;
            if (units.Exists(u => u.ParentNode != parent)) return null;
            units.Sort((a, b) => IndexInParent(a).CompareTo(IndexInParent(b)));
            return units;
        }

        /// <summary>Snippet blocks and other opaque includes cannot be rewritten in place.</summary>
        public static bool IsOpaque(XmlNode block)
        {
            if (block == null || block.NamespaceURI != MadCapNs) return false;
            switch (block.LocalName)
            {
                case "snippetBlock":
                case "snippetText":
                case "variable":
                case "keyword":
                case "concept":
                case "pageBreak":
                    return true;
                default:
                    return false;
            }
        }

        private static XmlNode BodyLevelAncestor(XmlNode n, XmlElement body)
        {
            var cur = n;
            while (cur != null && cur.ParentNode != null)
            {
                if (cur.ParentNode == body) return cur.NodeType == XmlNodeType.Element ? cur : null;
                cur = cur.ParentNode;
            }
            return null;
        }

        private static int IndexInParent(XmlNode n)
        {
            var i = 0;
            for (var s = n.ParentNode.FirstChild; s != null; s = s.NextSibling, i++)
                if (s == n) return i;
            return int.MaxValue;
        }
    }
}
