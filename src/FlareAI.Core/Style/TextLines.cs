using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using FlareAI.Core.Editing;

namespace FlareAI.Core.Style
{
    /// <summary>One piece of a line: an editable text run, or the resolved value of a MadCap:variable.</summary>
    public sealed class Segment
    {
        public XmlText Run { get; set; }
        public string Variable { get; set; }
        public string Text { get; set; }
        public int Offset { get; set; }
    }

    /// <summary>What the linter sees as one paragraph: a p, li, heading, table cell, … with inline markup flattened.</summary>
    public sealed class TextLine
    {
        public List<Segment> Segments = new List<Segment>();
        public bool IsHeading;
        public string Text { get { var sb = new StringBuilder(); foreach (var s in Segments) sb.Append(s.Text); return sb.ToString(); } }
    }

    /// <summary>
    /// Flattens blocks into lint lines so sentence rules see whole sentences: text across &lt;b&gt;, &lt;a&gt;, xrefs and
    /// variables (resolved to their values) joins up; each list item, cell, and paragraph stays its own line.
    /// Same exclusions as <see cref="TextRuns"/>: pending deletions and opaque MadCap content.
    /// </summary>
    public static class TextLines
    {
        private static readonly HashSet<string> LineElements = new HashSet<string>(StringComparer.Ordinal)
        {
            "p", "li", "h1", "h2", "h3", "h4", "h5", "h6", "td", "th", "dt", "dd", "caption", "figcaption", "pre", "blockquote", "div", "title"
        };
        private static readonly HashSet<string> MadCapLines = new HashSet<string>(StringComparer.Ordinal)
        {
            "dropDownHotspot", "togglerHotSpot", "expandingHead", "expandingBody"
        };
        private static readonly HashSet<string> Opaque = new HashSet<string>(StringComparer.Ordinal)
        {
            "snippetBlock", "snippetText", "keyword", "concept", "annotation", "glossaryTerm", "toggler"
        };

        public static List<TextLine> Of(IList<XmlNode> blocks, Func<string, string> variableValue)
        {
            var lines = new List<TextLine>();
            var removed = TrackedChanges.RemoveIds(blocks.Count > 0 ? blocks[0].OwnerDocument : null);
            foreach (var b in blocks) Walk(b, null, lines, removed, variableValue);
            lines.RemoveAll(l => l.Text.Trim().Length == 0);
            return lines;
        }

        private static void Walk(XmlNode n, TextLine line, List<TextLine> lines, HashSet<string> removed, Func<string, string> variableValue)
        {
            if (n.NodeType == XmlNodeType.Text)
            {
                if (line == null) { line = new TextLine(); lines.Add(line); }
                Add(line, new Segment { Run = (XmlText)n, Text = n.Value });
                return;
            }
            if (n.NodeType != XmlNodeType.Element || TrackedChanges.IsDeleted(n, removed)) return;
            var madcap = n.NamespaceURI == BlockSelector.MadCapNs;
            if (madcap && Opaque.Contains(n.LocalName)) return;
            if (madcap && n.LocalName == "variable")
            {
                var name = ((XmlElement)n).GetAttribute("name");
                string value = null;
                try { value = variableValue == null ? null : variableValue(name); } catch (Exception) { }
                if (line == null) { line = new TextLine(); lines.Add(line); }
                Add(line, new Segment { Variable = name, Text = value ?? "" });
                return;
            }

            // A new line starts at every line-level element; inline elements continue the current line.
            if ((!madcap && LineElements.Contains(n.LocalName)) || (madcap && MadCapLines.Contains(n.LocalName)))
            {
                line = new TextLine { IsHeading = !madcap && n.LocalName.Length == 2 && n.LocalName[0] == 'h' && char.IsDigit(n.LocalName[1]) };
                lines.Add(line);
            }
            foreach (XmlNode c in n.ChildNodes)
            {
                Walk(c, line, lines, removed, variableValue);
                // A nested line element (a p inside an li) ends the outer line's flow: text after it is a new line.
                if (line != null && c.NodeType == XmlNodeType.Element && lines.Count > 0 && lines[lines.Count - 1] != line && !Inline(c))
                {
                    line = new TextLine { IsHeading = line.IsHeading };
                    lines.Add(line);
                }
            }
        }

        private static bool Inline(XmlNode e)
        {
            var madcap = e.NamespaceURI == BlockSelector.MadCapNs;
            return !(madcap ? MadCapLines.Contains(e.LocalName) : LineElements.Contains(e.LocalName) || e.LocalName == "ul" || e.LocalName == "ol" || e.LocalName == "table");
        }

        private static void Add(TextLine line, Segment s)
        {
            var offset = 0;
            foreach (var x in line.Segments) offset += x.Text.Length;
            s.Offset = offset;
            line.Segments.Add(s);
        }
    }
}
