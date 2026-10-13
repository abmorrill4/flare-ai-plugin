using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;

namespace FlareAI.Core.Editing
{
    /// <summary>
    /// A stylesheet for Flare's editor view that marks the blocks with open proposals: a colored bar in the margin and a
    /// light tint. Flare applies it to the view only (never saved into the topic). Its CSS engine has no :nth-of-type, so
    /// each block is addressed by its exact sibling chain from the body: "body > h1 + p + ol > li + li".
    /// </summary>
    public static class MarkerCss
    {
        private static readonly Dictionary<string, int> Rank = new Dictionary<string, int> { { "error", 0 }, { "warning", 1 }, { "suggestion", 2 } };
        private static readonly string[] Bar = { "#c62828", "#e8a33d", "#5b8def" };
        // Non-colour cue: solid = error, dashed = warning, dotted = tip.
        private static readonly string[] Style = { "solid", "dashed", "dotted" };
        private static readonly string[] Tint = { "#fdecea", "#fff8e8", "#eef3fd" };

        /// <summary>The selector for one block, or null when it isn't under the body (snippet text, head).</summary>
        public static string SelectorFor(XmlNode block)
        {
            var levels = new List<string>();
            for (var n = block; n != null && n.NodeType == XmlNodeType.Element; n = n.ParentNode)
            {
                if (n.LocalName == "body") { levels.Reverse(); return "body > " + string.Join(" > ", levels.ToArray()); }
                var chain = new List<string>();
                for (var s = n; s != null; s = s.PreviousSibling) if (s.NodeType == XmlNodeType.Element) chain.Add(Name(s));
                chain.Reverse();
                levels.Add(string.Join(" + ", chain.ToArray()));
            }
            return null;
        }

        private static string Name(XmlNode e)
        {
            return e.NamespaceURI == BlockSelector.MadCapNs ? "MadCap|" + e.LocalName : e.LocalName;
        }

        /// <summary>One rule per marked block, colored by its most severe open proposal.</summary>
        public static string Css(IEnumerable<KeyValuePair<XmlNode, string>> blocks)
        {
            var sb = new StringBuilder("/* FlareAI proposal markers: editor view only */\n");
            foreach (var g in blocks.Where(b => b.Key != null).GroupBy(b => b.Key))
            {
                var sel = SelectorFor(g.Key);
                if (sel == null) continue;
                var r = g.Select(b => { int v; return Rank.TryGetValue((b.Value ?? "").ToLowerInvariant(), out v) ? v : 2; }).Min();
                sb.Append(sel).Append(" { border-left: 4px ").Append(Style[r]).Append(' ').Append(Bar[r]).Append("; padding-left: 4px; background-color: ").Append(Tint[r]).Append("; }\n");
            }
            return sb.ToString();
        }
    }
}
