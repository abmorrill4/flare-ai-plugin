using System.Collections.Generic;
using System.Xml;

namespace FlareAI.Core.Editing
{
    /// <summary>
    /// The block replacements Flare 22 can't take as a tracked node operation (<see cref="IDocumentAdapter.ReplaceBlocks"/>),
    /// per docs/flare-api-notes.md, with the reason in the writer's words. Every caller of ReplaceBlocks checks this first, and
    /// the Flare adapter refuses again as a last line of defence.
    /// <list type="bullet">
    /// <item>Content that holds a <c>MadCap:variable</c>: the operation undoes fine but crashes Flare on Redo (#110).</item>
    /// </list>
    /// </summary>
    public static class NodeWrites
    {
        /// <summary>Why <paramref name="oldNodes"/> can't be replaced by <paramref name="newNodes"/> as a node operation; null when it can.</summary>
        public static string Refusal(IList<XmlNode> oldNodes, IList<XmlNode> newNodes)
        {
            return VariableRefusal(oldNodes, newNodes);
        }

        /// <summary>The refusal when any old or new block holds a variable; null otherwise.</summary>
        public static string VariableRefusal(IList<XmlNode> oldNodes, IList<XmlNode> newNodes)
        {
            var name = FirstVariable(oldNodes) ?? FirstVariable(newNodes);
            if (name == null) return null;
            return "House Style can't apply this edit: it would replace a whole paragraph that contains a variable" +
                   (name.Length > 0 ? " (" + name + ")" : "") + ", and Flare crashes if you then redo the change (Ctrl+Y). " +
                   "Ask for a wording-only edit (Rewrite), or make this change yourself.";
        }

        /// <summary>The name of the first variable in the nodes ("" when unnamed), or null when there is none.</summary>
        private static string FirstVariable(IList<XmlNode> nodes)
        {
            if (nodes == null) return null;
            foreach (var n in nodes)
            {
                var v = Find(n);
                if (v != null) return v.Attributes == null || v.Attributes["name"] == null ? "" : v.Attributes["name"].Value;
            }
            return null;
        }

        private static XmlNode Find(XmlNode n)
        {
            if (n == null || n.NodeType != XmlNodeType.Element) return null;
            if (n.LocalName == "variable" && n.NamespaceURI == BlockSelector.MadCapNs) return n;
            foreach (XmlNode c in n.ChildNodes)
            {
                var hit = Find(c);
                if (hit != null) return hit;
            }
            return null;
        }
    }
}
