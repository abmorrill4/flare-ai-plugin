using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FlareAI.CorpusKit
{
    /// <summary>A name in the source text that becomes a <c>MadCap:variable</c> (Name is "Set.Variable").</summary>
    internal sealed class VarMap
    {
        public string Source, Name, Value;

        /// <summary>Parses <c>Source=Set.Name[:Value]</c>; Value defaults to Source.</summary>
        public static VarMap Parse(string spec)
        {
            var eq = spec.IndexOf('=');
            if (eq <= 0) throw new UsageException("--var expects Source=Set.Name[:Value], got '" + spec + "'");
            var v = new VarMap { Source = spec.Substring(0, eq) };
            var rest = spec.Substring(eq + 1);
            var colon = rest.IndexOf(':');
            if (colon >= 0) { v.Name = rest.Substring(0, colon); v.Value = rest.Substring(colon + 1); }
            else { v.Name = rest; v.Value = v.Source; }
            if (v.Name.IndexOf('.') < 0) v.Name = "General." + v.Name;
            return v;
        }
    }

    internal sealed class ConvertContext
    {
        public string SourceRel;                       // source file relative to the source root
        public string SourceFull;
        public string TopicRel;                        // Content/section/slug.htm
        public Dictionary<string, string> LinkMap;     // lower-cased source rel path -> topic rel path
        public Dictionary<string, string> ImageMap;    // source full path -> project rel path (shared across topics)
        public List<string> Warnings;
        public List<string> AltDropped;
        public string UiStyle = "bold";
        public double AltDropRate;
        public long Seed;
    }

    /// <summary>Maps parsed source HTML to Flare XHTML: admonitions, UI labels, xrefs, tables with <c>th</c>, images, drop-downs.</summary>
    internal static class FlareConverter
    {
        private static readonly Dictionary<string, string> AdmonMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            {"note","note"},{"info","note"},{"seealso","note"},{"admonition-note","note"},{"alert-info","note"},{"callout-note","note"},
            {"tip","tip"},{"hint","tip"},{"success","tip"},{"alert-success","tip"},{"admonition-tip","tip"},
            {"warning","warning"},{"attention","warning"},{"alert-warning","warning"},{"admonition-warning","warning"},
            {"caution","caution"},{"danger","caution"},{"error","caution"},{"critical","caution"},{"alert-danger","caution"},
            {"important","important"},{"admonition-important","important"}
        };
        private static readonly HashSet<string> UiClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "guilabel", "menuselection", "uicontrol", "ui-control", "gui-label", "menuitem", "ui", "guimenu", "guimenuitem", "guibutton" };
        private static readonly HashSet<string> Drop = new HashSet<string>(StringComparer.Ordinal)
            { "script", "style", "nav", "head", "title", "meta", "link", "hr", "colgroup", "col", "form", "button", "input", "noscript", "iframe" };
        private static readonly Regex UiVerb = new Regex(@"\b(select|click|clicking|choose|open|tap|press|go to|selecting|from the|on the|in the|using the)\s+(the\s+)?$", RegexOptions.IgnoreCase);
        private static readonly Regex Label = new Regex(@"^\s*(?:\[!(?:NOTE|TIP|WARNING|CAUTION|IMPORTANT)\]|(?:Note|Tip|Warning|Caution|Important|Danger|Attention|Info)\s*[:.])\s*", RegexOptions.IgnoreCase);
        private static readonly HashSet<string> InlineNames = new HashSet<string>(StringComparer.Ordinal)
            { "a", "b", "i", "code", "kbd", "span", "img", "MadCap:xref", "MadCap:variable", "br", "sub", "sup", "MadCap:snippetText" };

        private sealed class State { public bool InOl; }

        public static string TitleOf(XElement root)
        {
            var t = root.Descendants("title").FirstOrDefault();
            if (t != null && t.Value.Trim().Length > 0) return Collapse(t.Value);
            var h = root.Descendants("h1").FirstOrDefault();
            return h != null ? Collapse(h.Value) : null;
        }

        /// <summary>Converts the parsed source into body content for a Flare topic.</summary>
        public static List<XNode> Convert(XElement root, ConvertContext cx)
        {
            var src = root.Descendants("body").FirstOrDefault() ?? root;
            var body = new XElement("body");
            Children(src, body, cx, new State());
            WrapStrays(body);
            Tidy(body);
            return body.Nodes().ToList();
        }

        // ---- walkers -------------------------------------------------------------------------------------------

        private static void Children(XElement src, XElement dest, ConvertContext cx, State st)
        {
            foreach (var n in src.Nodes().ToList()) Node(n, dest, cx, st);
        }

        private static void Node(XNode n, XElement dest, ConvertContext cx, State st)
        {
            var t = n as XText;
            if (t != null) { dest.Add(new XText(Collapse(t.Value, true))); return; }
            var e = n as XElement;
            if (e == null) return;
            var name = e.Name.LocalName;
            if (Drop.Contains(name)) return;

            string adm = AdmonType(e);
            if (adm != null) { Admonition(e, adm, dest, cx, st); return; }

            switch (name)
            {
                case "h1": case "h2": case "h3": case "h4": case "h5": case "h6":
                    dest.Add(Copy(e, name, cx, st)); return;
                case "p":
                    dest.Add(Copy(e, "p", cx, st)); return;
                case "ul": case "dl": case "dt": case "dd": case "tr": case "tbody": case "thead": case "tfoot":
                    dest.Add(Copy(e, name, cx, st)); return;
                case "ol":
                    {
                        var o = new XElement("ol");
                        Children(e, o, cx, new State { InOl = true });
                        dest.Add(o); return;
                    }
                case "li":
                    dest.Add(Copy(e, "li", cx, st)); return;
                case "td": case "th":
                    {
                        var c = Copy(e, name, cx, st);
                        foreach (var a in new[] { "colspan", "rowspan" }) if (e.Attribute(a) != null) c.SetAttributeValue(a, (string)e.Attribute(a));
                        dest.Add(c); return;
                    }
                case "caption": dest.Add(Copy(e, "caption", cx, st)); return;
                case "table": dest.Add(Table(e, cx, st)); return;
                case "pre":
                    {
                        var pre = new XElement("pre", new XText(e.Value.Replace("\r\n", "\n").TrimEnd('\n')));
                        var cls = (string)e.Attribute("class") ?? (string)e.Descendants("code").Select(c => c.Attribute("class")).FirstOrDefault(a => a != null);
                        if (cls != null)
                        {
                            var lang = cls.Split(' ').FirstOrDefault(x => x.StartsWith("language-", StringComparison.Ordinal) || x.StartsWith("lang-", StringComparison.Ordinal));
                            if (lang != null) pre.SetAttributeValue("class", lang);
                        }
                        dest.Add(pre); return;
                    }
                case "blockquote":
                    {
                        var q = new XElement("blockquote");
                        Children(e, q, cx, st);
                        WrapStrays(q);
                        string kind;
                        if (LabelledAdmonition(q, out kind)) { EmitAdmonition(q.Nodes().ToList(), kind, dest, true); return; }
                        dest.Add(q); return;
                    }
                case "details": Details(e, dest, cx, st); return;
                case "br": dest.Add(new XElement("br")); return;
                case "img": { var im = Image(e, cx); if (im != null) dest.Add(im); return; }
                case "a": Link(e, dest, cx, st); return;
                case "strong": case "b": Bold(e, dest, cx, st); return;
                case "em": case "i": dest.Add(Copy(e, "i", cx, st)); return;
                case "code": case "tt": case "samp": dest.Add(Copy(e, "code", cx, st)); return;
                case "kbd":
                    if (Fx.ClassTokens(e).Any(c => UiClasses.Contains(c))) dest.Add(Copy(e, "span", cx, st, "uicontrol"));
                    else dest.Add(Copy(e, "kbd", cx, st));
                    return;
                case "sub": case "sup": dest.Add(Copy(e, name, cx, st)); return;
                case "span":
                    if (Fx.ClassTokens(e).Any(c => UiClasses.Contains(c))) dest.Add(Copy(e, "span", cx, st, "uicontrol"));
                    else Children(e, dest, cx, st);
                    return;
                default:
                    Children(e, dest, cx, st); // div, section, article, figure, abbr, u, mark, small, font, center, ...
                    return;
            }
        }

        private static XElement Copy(XElement e, string name, ConvertContext cx, State st, string cls = null)
        {
            var c = new XElement(name);
            if (cls != null) c.SetAttributeValue("class", cls);
            else
            {
                var keep = Fx.ClassTokens(e).Where(x => AdmonMap.ContainsKey(x) && (name == "p")).Select(x => AdmonMap[x]).FirstOrDefault();
                if (keep != null) c.SetAttributeValue("class", keep);
            }
            Children(e, c, cx, st);
            return c;
        }

        private static XElement Table(XElement e, ConvertContext cx, State st)
        {
            var t = new XElement("table");
            var cap = e.Elements("caption").FirstOrDefault();
            if (cap != null) t.Add(Copy(cap, "caption", cx, st));
            var rows = e.Descendants("tr").Where(r => r.Ancestors("table").First() == e).ToList();
            var head = new XElement("thead");
            var bodyEl = new XElement("tbody");
            bool inHead = e.Elements("thead").Any();
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                bool isHead = (r.Parent != null && r.Parent.Name.LocalName == "thead") || (!inHead && i == 0 && r.Elements().All(c => c.Name.LocalName == "th"));
                var row = new XElement("tr");
                foreach (var cell in r.Elements().Where(c => c.Name.LocalName == "td" || c.Name.LocalName == "th"))
                {
                    var nm = isHead ? "th" : cell.Name.LocalName;
                    var c = Copy(cell, nm, cx, st);
                    foreach (var a in new[] { "colspan", "rowspan" }) if (cell.Attribute(a) != null) c.SetAttributeValue(a, (string)cell.Attribute(a));
                    row.Add(c);
                }
                (isHead ? head : bodyEl).Add(row);
            }
            if (head.HasElements) t.Add(head);
            if (bodyEl.HasElements) t.Add(bodyEl);
            return t;
        }

        private static void Details(XElement e, XElement dest, ConvertContext cx, State st)
        {
            var summary = e.Elements("summary").FirstOrDefault();
            var hot = new XElement(Fx.MadCap + "dropDownHotspot");
            if (summary != null) Children(summary, hot, cx, st); else hot.Add(new XText("More"));
            var body = new XElement(Fx.MadCap + "dropDownBody");
            foreach (var n in e.Nodes().Where(n => n != summary).ToList()) Node(n, body, cx, st);
            WrapStrays(body);
            dest.Add(new XElement(Fx.MadCap + "dropDown", new XElement(Fx.MadCap + "dropDownHead", hot), body));
        }

        // ---- inline: bold / UI labels, links, images -----------------------------------------------------------

        private static void Bold(XElement e, XElement dest, ConvertContext cx, State st)
        {
            bool ui = st.InOl;
            if (!ui)
            {
                var prev = dest.LastNode as XText;
                ui = prev != null && UiVerb.IsMatch(prev.Value) && e.Value.Length <= 40;
            }
            if (ui && cx.UiStyle == "uicontrol") dest.Add(Copy(e, "span", cx, st, "uicontrol"));
            else dest.Add(Copy(e, "b", cx, st));
        }

        private static readonly Regex External = new Regex(@"^(https?:|mailto:|tel:|ftp:)", RegexOptions.IgnoreCase);

        private static void Link(XElement e, XElement dest, ConvertContext cx, State st)
        {
            var href = (string)e.Attribute("href");
            if (string.IsNullOrWhiteSpace(href) || href.StartsWith("#", StringComparison.Ordinal)) { Children(e, dest, cx, st); return; }
            if (External.IsMatch(href))
            {
                var a = new XElement("a", new XAttribute("href", href));
                Children(e, a, cx, st);
                dest.Add(a); return;
            }
            var path = Uri.UnescapeDataString(Regex.Replace(href, "[#?].*$", ""));
            var target = Fx.Resolve(cx.SourceRel, path);
            string topic = null;
            foreach (var cand in new[] { target, target + ".md", target + ".html", target + ".htm", target + "/index.md", target + "/index.html", target + "/readme.md", Regex.Replace(target, @"\.html?$", ".md") })
                if (cx.LinkMap.TryGetValue(cand.ToLowerInvariant(), out topic)) break;
            if (topic == null)
            {
                cx.Warnings.Add(cx.SourceRel + ": link '" + href + "' does not point to an imported page; kept as plain text");
                Children(e, dest, cx, st); return;
            }
            var x = new XElement(Fx.MadCap + "xref", new XAttribute("href", Fx.RelativeTo(Path.GetDirectoryName(cx.TopicRel) ?? "", topic)));
            Children(e, x, cx, st);
            dest.Add(x);
        }

        private static XElement Image(XElement e, ConvertContext cx)
        {
            var src = (string)e.Attribute("src");
            if (string.IsNullOrWhiteSpace(src)) return null;
            if (External.IsMatch(src) || src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                cx.Warnings.Add(cx.SourceRel + ": external image '" + src + "' is not downloaded; dropped");
                return null;
            }
            var full = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(cx.SourceFull) ?? ".", Uri.UnescapeDataString(src).Replace('/', Path.DirectorySeparatorChar)));
            if (!File.Exists(full))
            {
                cx.Warnings.Add(cx.SourceRel + ": image '" + src + "' not found; dropped");
                return null;
            }
            string dst;
            if (!cx.ImageMap.TryGetValue(full, out dst))
            {
                var baseName = Fx.Slug(Path.GetFileNameWithoutExtension(full));
                if (baseName.Length == 0) baseName = "image";
                var ext = Path.GetExtension(full).ToLowerInvariant();
                dst = "Content/Resources/Images/" + baseName + ext;
                int k = 2;
                while (cx.ImageMap.ContainsValue(dst)) dst = "Content/Resources/Images/" + baseName + "-" + (k++) + ext;
                cx.ImageMap[full] = dst;
            }
            var img = new XElement("img", new XAttribute("src", Fx.RelativeTo(Path.GetDirectoryName(cx.TopicRel) ?? "", dst)));
            var alt = (string)e.Attribute("alt");
            bool drop = cx.AltDropRate > 0 && Rng.Bucket(cx.TopicRel + "|" + src, cx.Seed) < (int)(cx.AltDropRate * 100);
            if (alt != null && alt.Trim().Length > 0 && !drop) img.SetAttributeValue("alt", alt);
            else cx.AltDropped.Add(cx.TopicRel + "|" + dst);
            return img;
        }

        // ---- admonitions ---------------------------------------------------------------------------------------

        private static string AdmonType(XElement e)
        {
            var n = e.Name.LocalName;
            if (n != "div" && n != "aside" && n != "section" && n != "p" && n != "blockquote") return null;
            foreach (var c in Fx.ClassTokens(e)) { string v; if (AdmonMap.TryGetValue(c, out v)) return v; }
            return null;
        }

        private static void Admonition(XElement e, string kind, XElement dest, ConvertContext cx, State st)
        {
            var tmp = new XElement("tmp");
            if (e.Name.LocalName == "p")
            {
                var p = new XElement("p");
                Children(e, p, cx, st);
                tmp.Add(p);
            }
            else
            {
                foreach (var n in e.Nodes().ToList())
                {
                    var el = n as XElement;
                    if (el != null && Fx.ClassTokens(el).Any(c => c.Equals("admonition-title", StringComparison.OrdinalIgnoreCase) || c.Equals("title", StringComparison.OrdinalIgnoreCase)) && el.Name.LocalName == "p") continue;
                    Node(n, tmp, cx, st);
                }
                WrapStrays(tmp);
            }
            EmitAdmonition(tmp.Nodes().ToList(), kind, dest, false);
        }

        private static bool LabelledAdmonition(XElement q, out string kind)
        {
            kind = null;
            var p = q.Elements().FirstOrDefault();
            if (p == null || p.Name.LocalName != "p") return false;
            var m = Label.Match(p.Value);
            if (!m.Success) return false;
            var w = Regex.Match(m.Value, @"[A-Za-z]+").Value.ToLowerInvariant();
            string v;
            kind = AdmonMap.TryGetValue(w, out v) ? v : "note";
            return true;
        }

        /// <summary>Writes admonition paragraphs as <c>p.note</c> (etc.); the "Note:" label is dropped because the stylesheet supplies it.</summary>
        private static void EmitAdmonition(List<XNode> nodes, string kind, XElement dest, bool stripLabel)
        {
            bool first = true;
            foreach (var n in nodes)
            {
                var el = n as XElement;
                if (el == null) continue;
                if (el.Name.LocalName == "p")
                {
                    el.SetAttributeValue("class", kind);
                    if (first) StripLabel(el);
                    first = false;
                    el.Remove();
                    dest.Add(el);
                }
                else { el.Remove(); dest.Add(el); }
            }
        }

        private static void StripLabel(XElement p)
        {
            var all = string.Concat(p.DescendantNodes().OfType<XText>().Select(t => t.Value));
            var m = Label.Match(all);
            if (!m.Success) return;
            int remove = m.Length;
            foreach (var t in p.DescendantNodes().OfType<XText>().ToList())
            {
                if (remove <= 0) break;
                int take = Math.Min(remove, t.Value.Length);
                t.Value = t.Value.Substring(take);
                remove -= take;
            }
            foreach (var empty in p.DescendantNodes().OfType<XText>().Where(t => t.Value.Length == 0).ToList()) empty.Remove();
            foreach (var inl in p.Descendants().Where(x => !x.Nodes().Any() && (x.Name.LocalName == "b" || x.Name.LocalName == "i")).ToList()) inl.Remove();
        }

        // ---- cleanup -------------------------------------------------------------------------------------------

        private static string Collapse(string s, bool keepEdges = false)
        {
            var r = Regex.Replace(s.Replace(' ', ' '), @"\s+", " ");
            return keepEdges ? r : r.Trim();
        }

        private static bool IsInline(XNode n)
        {
            var t = n as XText;
            if (t != null) return true;
            var e = n as XElement;
            return e != null && InlineNames.Contains(Fx.Prefixed(e.Name));
        }

        /// <summary>Wraps loose text and inline runs directly under a container in paragraphs.</summary>
        private static void WrapStrays(XElement c)
        {
            var nodes = c.Nodes().ToList();
            var run = new List<XNode>();
            Action flush = () =>
            {
                if (run.Count == 0) return;
                bool meaningful = run.Any(x => !(x is XText) || ((XText)x).Value.Trim().Length > 0);
                if (meaningful)
                {
                    var p = new XElement("p");
                    run[0].AddBeforeSelf(p);
                    foreach (var x in run) { x.Remove(); p.Add(x); }
                }
                else foreach (var x in run) x.Remove();
                run.Clear();
            };
            foreach (var n in nodes)
            {
                if (IsInline(n)) run.Add(n); else flush();
            }
            flush();
        }

        private static readonly HashSet<string> TextBlocks = new HashSet<string>(StringComparer.Ordinal)
            { "p", "li", "td", "th", "h1", "h2", "h3", "h4", "h5", "h6", "dt", "dd", "caption" };

        private static void Tidy(XElement body)
        {
            foreach (var e in body.DescendantsAndSelf().Where(x => TextBlocks.Contains(x.Name.LocalName)).ToList())
            {
                var texts = e.DescendantNodes().OfType<XText>().ToList();
                if (texts.Count > 0) { texts[0].Value = texts[0].Value.TrimStart(); texts[texts.Count - 1].Value = texts[texts.Count - 1].Value.TrimEnd(); }
                // merge adjacent text nodes and drop empties
                foreach (var t in texts.Where(t => t.Value.Length == 0).ToList()) t.Remove();
            }
            foreach (var p in body.Descendants("p").Where(p => !p.Nodes().Any()).ToList()) p.Remove();
            // a text block that holds only whitespace between blocks (from unwrapped divs)
            var containers = new HashSet<string>(StringComparer.Ordinal) { "body", "ul", "ol", "dl", "table", "thead", "tbody", "tr", "blockquote", "dropDown", "dropDownBody", "dropDownHead" };
            foreach (var t in body.DescendantNodes().OfType<XText>().Where(t => t.Value.Trim().Length == 0 && t.Parent != null && containers.Contains(t.Parent.Name.LocalName)).ToList()) t.Remove();
        }

        // ---- names to variables --------------------------------------------------------------------------------

        private static readonly HashSet<string> NoVars = new HashSet<string>(StringComparer.Ordinal) { "pre", "code", "kbd" };

        public static int ApplyVariables(XElement body, IList<VarMap> vars)
        {
            if (vars == null || vars.Count == 0) return 0;
            int count = 0;
            var ordered = vars.OrderByDescending(v => v.Source.Length).ToList();
            var combined = new Regex("(?<![\\w-])(" + string.Join("|", ordered.Select(v => Regex.Escape(v.Source))) + ")(?![\\w-])");
            foreach (var t in body.DescendantNodes().OfType<XText>().ToList())
            {
                if (t.Ancestors().Any(a => NoVars.Contains(a.Name.LocalName) || (a.Name.LocalName == "span" && Fx.ClassTokens(a).Contains("uicontrol")))) continue;
                var s = t.Value;
                var ms = combined.Matches(s).Cast<Match>().ToList();
                if (ms.Count == 0) continue;
                var parts = new List<object>();
                int pos = 0;
                foreach (var m in ms)
                {
                    if (m.Index > pos) parts.Add(new XText(s.Substring(pos, m.Index - pos)));
                    var v = ordered.First(x => x.Source == m.Value);
                    parts.Add(new XElement(Fx.MadCap + "variable", new XAttribute("name", v.Name)));
                    pos = m.Index + m.Length; count++;
                }
                if (pos < s.Length) parts.Add(new XText(s.Substring(pos)));
                t.ReplaceWith(parts.ToArray());
            }
            return count;
        }
    }
}
