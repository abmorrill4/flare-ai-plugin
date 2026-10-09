using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Xml;
using FlareAI.Core.Actions;
using FlareAI.Core.Editing;

namespace FlareAI.Core.Style
{
    /// <summary>
    /// Accessibility, structure and white-label checks a text linter can't see: images without alt text, skipped heading
    /// levels, a second h1, tables without a header row, link text that doesn't say where it goes, and hard-coded
    /// phone numbers, email addresses and URLs (in white-label docs these belong in variables). Each finding is
    /// anchored on nearby text so Go to can select it; fixing it is the author's call (Kind "manual").
    /// </summary>
    public static class StructureCheck
    {
        private static readonly Regex Phone = new Regex(@"(?<!\d)(?:\+?1[ .-]?)?(?:\(\d{3}\)\s?|\d{3}[ .-])\d{3}[ .-]\d{4}(?!\d)", RegexOptions.Compiled);
        private static readonly Regex Email = new Regex(@"[\w.+-]+@[\w-]+(?:\.[\w-]+)+", RegexOptions.Compiled);
        private static readonly Regex Url = new Regex(@"\b(?:https?://|www\.)[^\s<>""]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly HashSet<string> VagueLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "here", "click here", "this link", "link", "more", "read more", "learn more", "this page", "this", "go", "details" };

        public static List<StyleFinding> Of(IList<XmlNode> blocks)
        {
            var found = new List<StyleFinding>();
            var lastLevel = 0;
            var h1s = 0;
            foreach (var block in blocks)
            {
                foreach (var el in Elements(block))
                {
                    var name = el.LocalName.ToLowerInvariant();
                    if (name == "img")
                    {
                        var alt = el.GetAttribute("alt");
                        if (alt.Trim().Length == 0 && el.GetAttribute("role") != "presentation")
                            Add(found, el, "Structure.AltText", "error", "This image has no alt text: describe what it shows (or mark it decorative) for screen readers.");
                    }
                    else if (name.Length == 2 && name[0] == 'h' && char.IsDigit(name[1]))
                    {
                        var level = name[1] - '0';
                        if (level == 1 && ++h1s > 1) Add(found, el, "Structure.OneH1", "warning", "A topic has one h1 (its title); make this an h2.");
                        if (lastLevel > 0 && level > lastLevel + 1)
                            Add(found, el, "Structure.HeadingOrder", "warning", "Heading jumps from h" + lastLevel + " to h" + level + ": use h" + (lastLevel + 1) + " so screen readers and the TOC keep the outline.");
                        lastLevel = level;
                    }
                    else if (name == "table")
                    {
                        if (el.SelectSingleNode(".//*[local-name()='th']") == null)
                            Add(found, el, "Structure.TableHeader", "warning", "This table has no header row: use th cells for column headings so screen readers can announce them.");
                    }
                    else if (name == "a" || (name == "xref" && el.NamespaceURI == BlockSelector.MadCapNs))
                    {
                        var text = Regex.Replace(el.InnerText, @"\s+", " ").Trim().TrimEnd('.', '!');
                        if (VagueLinks.Contains(text))
                            Add(found, el, "Structure.LinkText", "warning", "Link text \"" + text + "\" doesn't say where it goes: name the destination.");
                    }
                }
                foreach (var run in TextRuns.Of(new List<XmlNode> { block }))
                {
                    if (InsideLink(run)) continue;
                    foreach (var pair in new[] { new KeyValuePair<Regex, string>(Phone, "phone number"), new KeyValuePair<Regex, string>(Email, "email address"), new KeyValuePair<Regex, string>(Url, "URL") })
                        foreach (Match m in pair.Key.Matches(run.Value))
                        {
                            var f = FindingLocator.ForSpan(run, m.Index, m.Length, null, TextRuns.BlockOf(run));
                            Set(f, "Structure.HardCoded", "suggestion", "Hard-coded " + pair.Value + ": in white-label docs, insert a variable so each client's value shows.");
                            found.Add(f);
                        }
                }
            }
            return found;
        }

        private static IEnumerable<XmlElement> Elements(XmlNode block)
        {
            if (block is XmlElement) yield return (XmlElement)block;
            if (block.NodeType != XmlNodeType.Element) yield break;
            foreach (XmlElement e in block.SelectNodes(".//*")) yield return e;
        }

        private static bool InsideLink(XmlNode n)
        {
            for (var p = n.ParentNode; p != null; p = p.ParentNode)
                if (p.LocalName == "a" || p.LocalName == "xref") return true;
            return false;
        }

        /// <summary>Anchors on the element's own text, or the nearest text before it (an image has none).</summary>
        private static void Add(List<StyleFinding> found, XmlElement el, string rule, string severity, string message)
        {
            var runs = TextRuns.Of(new List<XmlNode> { el });
            XmlText run = runs.Count > 0 ? runs[0] : null;
            if (run == null)
            {
                // The last text before the element in its block, else the block's first text.
                var block = TextRuns.BlockOf(el) ?? el;
                var order = new List<XmlNode>();
                Walk(block, order);
                var at = order.IndexOf(el);
                foreach (var n in order)
                {
                    var t = n as XmlText;
                    if (t == null || t.Value.Trim().Length == 0) continue;
                    if (order.IndexOf(n) < at || run == null) run = t;
                    if (order.IndexOf(n) > at) break;
                }
            }
            if (run == null) return;
            var text = run.Value;
            var start = text.Length - text.TrimStart().Length;
            var length = Math.Min(60, text.Trim().Length);
            if (length == 0) return;
            var f = FindingLocator.ForSpan(run, start, length, null, TextRuns.BlockOf(run));
            Set(f, rule, severity, message);
            found.Add(f);
        }

        private static void Set(StyleFinding f, string rule, string severity, string message)
        {
            f.Source = "structure"; f.Layer = "structure"; f.Rule = rule; f.Severity = severity; f.Suggestion = message; f.Replacement = null;
        }

        private static void Walk(XmlNode n, List<XmlNode> order)
        {
            order.Add(n);
            foreach (XmlNode c in n.ChildNodes) Walk(c, order);
        }
    }
}
