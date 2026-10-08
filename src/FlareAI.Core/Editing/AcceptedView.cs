using System.Collections.Generic;
using System.Xml;

namespace FlareAI.Core.Editing
{
    /// <summary>
    /// Blocks as the author currently reads them: pending deletions removed, pending insertions unwrapped into
    /// plain content, change ids stripped. Text nodes are kept one-to-one (never merged) so every text run in the
    /// view has a live twin with identical text. This is what the model sees and what rewrites are diffed against.
    /// </summary>
    public static class AcceptedView
    {
        public static IList<XmlNode> Of(IList<XmlNode> blocks)
        {
            var result = new List<XmlNode>();
            if (blocks.Count == 0) return result;
            var removed = TrackedChanges.RemoveIds(blocks[0].OwnerDocument);
            foreach (var b in blocks)
            {
                if (TrackedChanges.IsDeleted(b, removed)) continue;
                var copy = b.CloneNode(true);
                Clean(copy, removed);
                result.Add(copy);
            }
            return result;
        }

        private static void Clean(XmlNode n, HashSet<string> removed)
        {
            var children = new List<XmlNode>();
            foreach (XmlNode c in n.ChildNodes) children.Add(c);
            foreach (var c in children)
            {
                if (c.NodeType != XmlNodeType.Element) continue;
                if (TrackedChanges.IsDeleted(c, removed)) { n.RemoveChild(c); continue; }
                Clean(c, removed);
                if (c.LocalName == "change" && c.NamespaceURI == BlockSelector.MadCapNs)
                {
                    while (c.FirstChild != null) n.InsertBefore(c.FirstChild, c);
                    n.RemoveChild(c);
                }
                else if (c.Attributes != null && c.Attributes["changes", BlockSelector.MadCapNs] != null)
                {
                    c.Attributes.Remove(c.Attributes["changes", BlockSelector.MadCapNs]);
                }
            }
        }
    }
}
