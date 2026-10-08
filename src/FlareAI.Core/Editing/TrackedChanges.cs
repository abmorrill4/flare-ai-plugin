using System;
using System.Collections.Generic;
using System.Xml;

namespace FlareAI.Core.Editing
{
    /// <summary>
    /// Flare stores pending tracked changes inline: content carries <c>MadCap:changes="id[ id…]"</c> and
    /// <c>head/MadCap:changeData</c> says whether each id is an AddChange or a RemoveChange.
    /// </summary>
    public static class TrackedChanges
    {
        /// <summary>True if any node in the blocks carries pending tracked-change markup.</summary>
        public static bool HasPending(IList<XmlNode> blocks)
        {
            foreach (var b in blocks)
                if (b.NodeType == XmlNodeType.Element && b.SelectSingleNode("descendant-or-self::*[@MadCap:changes]", Ns(b.OwnerDocument)) != null)
                    return true;
            return false;
        }

        /// <summary>Detached clones of the blocks with pending deletions removed — what the author actually sees as current text.</summary>
        public static IList<XmlNode> WithoutDeletions(IList<XmlNode> blocks)
        {
            var result = new List<XmlNode>();
            if (blocks.Count == 0) return result;
            var removed = RemoveIds(blocks[0].OwnerDocument);
            foreach (var b in blocks)
            {
                if (IsDeleted(b, removed)) continue;
                var clone = b.CloneNode(true);
                if (removed.Count > 0 && clone.NodeType == XmlNodeType.Element)
                    foreach (XmlNode n in clone.SelectNodes(".//*[@MadCap:changes]", Ns(clone.OwnerDocument)))
                        if (n.ParentNode != null && IsDeleted(n, removed)) n.ParentNode.RemoveChild(n);
                result.Add(clone);
            }
            return result;
        }

        public static bool IsDeleted(XmlNode n, HashSet<string> removed)
        {
            var attr = n.Attributes == null ? null : n.Attributes["changes", BlockSelector.MadCapNs];
            if (attr == null) return false;
            foreach (var id in attr.Value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                if (removed.Contains(id)) return true;
            return false;
        }

        public static HashSet<string> RemoveIds(XmlDocument doc)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (doc == null) return ids;
            foreach (XmlElement e in doc.SelectNodes("//MadCap:changeData/MadCap:RemoveChange", Ns(doc)))
                ids.Add(e.GetAttribute("id", BlockSelector.MadCapNs));
            return ids;
        }

        private static XmlNamespaceManager Ns(XmlDocument doc)
        {
            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("MadCap", BlockSelector.MadCapNs);
            return ns;
        }
    }
}
