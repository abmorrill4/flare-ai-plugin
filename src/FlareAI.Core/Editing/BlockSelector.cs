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
            return result;
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
