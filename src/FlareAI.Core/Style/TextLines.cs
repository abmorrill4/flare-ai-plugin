using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
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
        /// <summary>The inline markup this piece sits in (UI label, link text, emphasis, code placeholder).</summary>
        public TextContext Context { get; set; }
    }

    /// <summary>What the linter sees as one paragraph: a p, li, heading, table cell, … with inline markup flattened.</summary>
    public sealed class TextLine
    {
        public List<Segment> Segments = new List<Segment>();
        public bool IsHeading;
        /// <summary>Where the line sits: heading, procedure step, list item, table cell, note, ….</summary>
        public TextContext Context;
        public string Text { get { var sb = new StringBuilder(); foreach (var s in Segments) sb.Append(s.Text); return sb.ToString(); } }

        /// <summary>The line as lint input: its text, its line context, and the inline spans (UI labels, links, …) by offset.</summary>
        public LintLine ToLint()
        {
            var l = new LintLine { Text = Text, Known = true, Context =Context | (IsHeading ? TextContext.Heading : TextContext.None) };
            foreach (var s in Segments)
            {
                var inline = s.Context & TextContext.Inline;
                if (inline == TextContext.None || s.Text.Length == 0) continue;
                var last = l.Spans.Count == 0 ? null : l.Spans[l.Spans.Count - 1];
                if (last != null && last.Context == inline && last.Start + last.Length == s.Offset) last.Length += s.Text.Length;   // one label across runs
                else l.Spans.Add(new ContextSpan { Start = s.Offset, Length = s.Text.Length, Context = inline });
            }
            return l;
        }
    }

    /// <summary>
    /// Flattens blocks into lint lines so sentence rules see whole sentences: text across &lt;b&gt;, &lt;a&gt;, xrefs and
    /// variables (resolved to their values) joins up; each list item, cell, and paragraph stays its own line.
    /// Same exclusions as <see cref="TextRuns"/>: pending deletions and opaque MadCap content.
    /// Each line carries where it sits (step, list item, cell, note, …) and each segment the inline markup it is in
    /// (UI label, link text, …), so lint can honour a rule's Content scope.
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
            foreach (var l in lines) if (NoteStart.IsMatch(l.Text)) l.Context |= TextContext.Notes;
            return lines;
        }

        private static void Walk(XmlNode n, TextLine line, List<TextLine> lines, HashSet<string> removed, Func<string, string> variableValue)
        {
            if (n.NodeType == XmlNodeType.Text)
            {
                if (line == null) { line = NewLine(n.ParentNode, false); lines.Add(line); }
                Add(line, new Segment { Run = (XmlText)n, Text = n.Value, Context = InlineOf(n.ParentNode) });
                return;
            }
            if (n.NodeType != XmlNodeType.Element || TrackedChanges.IsDeleted(n, removed)) return;
            var madcap = n.NamespaceURI == BlockSelector.MadCapNs;
            if (madcap && Opaque.Contains(n.LocalName)) return;
            if (TextRuns.IsLiteral(n))
            {
                // Keeps the sentence readable for the linter ("Edit x to…") without linting the code itself.
                if (line == null) { line = NewLine(n.ParentNode, false); lines.Add(line); }
                Add(line, new Segment { Text = "x", Context = InlineOf(n.ParentNode) | TextContext.Code });
                return;
            }
            if (madcap && n.LocalName == "variable")
            {
                var name = ((XmlElement)n).GetAttribute("name");
                string value = null;
                try { value = variableValue == null ? null : variableValue(name); } catch (Exception) { }
                if (line == null) { line = NewLine(n.ParentNode, false); lines.Add(line); }
                Add(line, new Segment { Variable = name, Text = value ?? "", Context = InlineOf(n.ParentNode) });
                return;
            }

            // A new line starts at every line-level element; inline elements continue the current line.
            if ((!madcap && LineElements.Contains(n.LocalName)) || (madcap && MadCapLines.Contains(n.LocalName)))
            {
                line = NewLine(n, !madcap && IsHeadingName(n.LocalName));
                lines.Add(line);
            }
            foreach (XmlNode c in n.ChildNodes)
            {
                Walk(c, line, lines, removed, variableValue);
                // A nested line element (a p inside an li) ends the outer line's flow: text after it is a new line.
                if (line != null && c.NodeType == XmlNodeType.Element && lines.Count > 0 && lines[lines.Count - 1] != line && !Inline(c))
                {
                    line = new TextLine { IsHeading = line.IsHeading, Context = line.Context };
                    lines.Add(line);
                }
            }
        }

        private static TextLine NewLine(XmlNode at, bool heading)
        {
            return new TextLine { IsHeading = heading, Context = LineOf(at) | (heading ? TextContext.Heading : TextContext.None) };
        }

        private static bool IsHeadingName(string name) { return name.Length == 2 && name[0] == 'h' && char.IsDigit(name[1]); }

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

        // ---- context ---------------------------------------------------------------------------------------------

        private static readonly Regex NoteStart = new Regex(@"^\s*(?:Note|Tip|Important|Caution|Warning|Key point)\s*:", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex NoteClass = new Regex(@"(?:^|[\s_-])(?:note|tip|warning|caution|important|attention|danger)(?:$|[\s_\d-])", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex UiClass = new Regex(@"(?:^|\s)(?:ui-?control|guilabel|guibutton|guimenu(?:item)?|menucascade|wintitle|ui-?label|ui)(?:$|\s)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>Where a line element (or the parent of loose text) sits, from itself and its ancestors up to the body.</summary>
        public static TextContext LineOf(XmlNode at)
        {
            var c = TextContext.None;
            var nearestItem = true;
            for (var n = at; n != null && n.NodeType == XmlNodeType.Element; n = n.ParentNode)
            {
                var e = (XmlElement)n;
                var name = e.LocalName;
                if (e.NamespaceURI == BlockSelector.MadCapNs)
                {
                    if (name == "dropDownHotspot" || name == "togglerHotSpot" || name == "expandingHead") c |= TextContext.Hotspot;
                    continue;
                }
                if (name == "body") break;
                switch (name)
                {
                    case "li":
                        c |= TextContext.Lists;
                        // The nearest list item decides: a bullet nested in a step is a list item, not a step.
                        if (nearestItem && e.ParentNode != null && e.ParentNode.LocalName == "ol") c |= TextContext.Steps;
                        nearestItem = false;
                        break;
                    case "dt": case "dd": c |= TextContext.Lists; break;
                    case "td": c |= TextContext.TableCell; break;
                    case "th": c |= TextContext.TableHeader; break;
                    case "caption": c |= TextContext.TableCaption; break;
                    case "figcaption": c |= TextContext.Caption; break;
                    case "blockquote": c |= TextContext.Quote; break;
                    case "pre": c |= TextContext.Code; break;
                    default:
                        if (IsHeadingName(name)) c |= TextContext.Heading;
                        break;
                }
                if (NoteClass.IsMatch(e.GetAttribute("class"))) c |= TextContext.Notes;
            }
            return c;
        }

        /// <summary>The inline markup a text node's parent puts it in, up to its line element.</summary>
        public static TextContext InlineOf(XmlNode parent)
        {
            var c = TextContext.None;
            for (var n = parent; n != null && n.NodeType == XmlNodeType.Element; n = n.ParentNode)
            {
                var e = (XmlElement)n;
                var madcap = e.NamespaceURI == BlockSelector.MadCapNs;
                if (madcap ? MadCapLines.Contains(e.LocalName) : LineElements.Contains(e.LocalName) || e.LocalName == "body") break;
                if (madcap)
                {
                    if (e.LocalName == "xref") c |= TextContext.Link;
                    continue;
                }
                switch (e.LocalName)
                {
                    case "b": case "strong": case "uicontrol": case "guilabel": c |= TextContext.UI; break;
                    case "a": if (e.HasAttribute("href")) c |= TextContext.Link; break;
                    case "i": case "em": case "cite": c |= TextContext.Emphasis; break;
                    case "q": c |= TextContext.Quote; break;
                }
                if (UiClass.IsMatch(e.GetAttribute("class"))) c |= TextContext.UI;
            }
            return c;
        }
    }
}
