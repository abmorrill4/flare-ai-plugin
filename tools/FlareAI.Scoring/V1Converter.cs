using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Newtonsoft.Json.Linq;

namespace FlareAI.Scoring
{
    /// <summary>
    /// One-off conversion of the v1 ground truth (<c>corpus/ground-truth.json</c>, defects only) into v2 span labels for
    /// <c>corpus/DigitalBanking/labels.jsonl</c>, plus the clean/exempt labels that make precision measurable.
    /// Deterministic: same inputs, same bytes.
    /// </summary>
    public sealed class V1Converter
    {
        private readonly string _projectRoot, _project;
        private readonly Dictionary<string, string> _vars = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, XmlDocument> _docs = new Dictionary<string, XmlDocument>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<BlockText>> _blocks = new Dictionary<string, List<BlockText>>(StringComparer.Ordinal);
        private readonly HashSet<string> _boilerplate = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _claimed = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> _problems = new List<string>();

        public IList<string> Problems { get { return _problems; } }

        public V1Converter(string projectRoot, string projectName)
        {
            _projectRoot = projectRoot;
            _project = projectName;
            LoadVariables();
        }

        private void LoadVariables()
        {
            string dir = Path.Combine(_projectRoot, "Project", "VariableSets");
            if (!Directory.Exists(dir)) return;
            foreach (var f in Directory.GetFiles(dir, "*.flvar"))
            {
                var doc = new XmlDocument(); doc.Load(f);
                string set = Path.GetFileNameWithoutExtension(f);
                foreach (XmlElement v in doc.SelectNodes("//Variable"))
                    _vars[set + "." + v.GetAttribute("Name")] = v.InnerText;
            }
        }

        private string Var(string name) { string v; return _vars.TryGetValue(name, out v) ? v : null; }

        private List<string> ContentFiles()
        {
            var root = Path.Combine(_projectRoot, "Content");
            return Directory.GetFiles(root, "*.*", SearchOption.AllDirectories)
                .Where(p => p.EndsWith(".htm", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".flsnp", StringComparison.OrdinalIgnoreCase))
                .Select(p => Rel(p)).OrderBy(p => p, StringComparer.Ordinal).ToList();
        }

        private string Rel(string full)
        {
            return full.Substring(_projectRoot.TrimEnd('\\', '/').Length + 1).Replace('\\', '/');
        }

        private XmlDocument Doc(string file)
        {
            XmlDocument d;
            if (!_docs.TryGetValue(file, out d)) _docs[file] = d = Xhtml.Load(Path.Combine(_projectRoot, file.Replace('/', Path.DirectorySeparatorChar)));
            return d;
        }

        private List<BlockText> Blocks(string file)
        {
            List<BlockText> b;
            if (!_blocks.TryGetValue(file, out b)) _blocks[file] = b = Xhtml.Blocks(Doc(file), Var).ToList();
            return b;
        }

        public List<Label> Convert(string groundTruthPath)
        {
            var files = ContentFiles();
            // boilerplate = block text repeated in 5 or more files
            var counts = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var f in files)
                foreach (var b in Blocks(f))
                {
                    HashSet<string> set;
                    if (!counts.TryGetValue(b.Text, out set)) counts[b.Text] = set = new HashSet<string>(StringComparer.Ordinal);
                    set.Add(f);
                }
            foreach (var kv in counts) if (kv.Value.Count >= 5) _boilerplate.Add(kv.Key);

            var gt = JObject.Parse(File.ReadAllText(groundTruthPath, Encoding.UTF8));
            var labels = new List<Label>();
            int n = 0;
            foreach (JObject d in (JArray)gt["defects"])
            {
                var l = ConvertDefect(d, ++n);
                if (l != null) labels.Add(l);
            }
            var viol = new List<Label>(labels);
            foreach (var l in Clean(files, viol)) labels.Add(l);
            return labels;
        }

        // ---- violations ----------------------------------------------------------------------------------------------------

        private Label ConvertDefect(JObject d, int ordinal)
        {
            string file = (string)d["file"], kind = (string)d["kind"], source = (string)d["source"], rule = (string)d["rule"];
            string excerpt = (string)d["excerpt"], sentence = (string)d["sentence"];
            string id = (string)d["id"];
            var l = new Label
            {
                Id = _project + "/V" + ordinal.ToString("D4"),
                File = file,
                Kind = LabelKind.Violation,
                DefectKind = kind,
                Origin = "seeded",
                Status = "verified",
                By = new List<string> { "generator" },
                Split = SplitOf(file),
                Rules = RulesOf(rule, source, kind),
                Seed = new JObject { ["generator"] = "v1", ["defect"] = id, ["source"] = source, ["rule"] = rule }
            };

            if (excerpt == null)
            {
                XmlElement el = WholeElement(file, kind);
                if (el == null) { _problems.Add(id + ": no element for " + kind); return null; }
                l.Block = Xhtml.XPathOf(el);
                _claimed.Add(file + "|" + l.Block + "|");
                l.Context = new LabelContext { Line = el.Name == "img" || el.Name == "table" ? ParentLine(el) : Xhtml.LineContext(el) };
                return l;
            }

            BlockText block = null; int start = -1;
            string ns = Collapse(sentence);
            foreach (var b in Blocks(file))
            {
                int s = ns == null ? -1 : b.Text.IndexOf(ns, StringComparison.Ordinal);
                int searchFrom = s >= 0 ? s : 0;
                int e = FindExcerpt(b.Text, excerpt, searchFrom, s >= 0 ? s + ns.Length : b.Text.Length);
                if (e < 0 || _claimed.Contains(file + "|" + b.XPath + "|" + e)) continue;
                if (s < 0 && block != null) continue;
                block = b; start = e;
                if (s >= 0) break;
            }
            if (block == null) { _problems.Add(id + ": text not found: " + excerpt); return null; }
            _claimed.Add(file + "|" + block.XPath + "|" + start);
            int end = start + excerpt.Length;
            string quote, prefix, suffix;
            Anchor.MakeSelector(block.Text, start, end, 24, out quote, out prefix, out suffix);
            l.Block = block.XPath; l.Quote = quote; l.Prefix = prefix.Length == 0 ? null : prefix; l.Suffix = suffix.Length == 0 ? null : suffix;
            l.Context = new LabelContext
            {
                Line = Xhtml.LineContext(block.Element),
                Span = Xhtml.SpanContext(block, start, end, file, _boilerplate.Contains(block.Text))
            };
            return l;
        }

        private static string Collapse(string s) { return s == null ? null : Regex.Replace(s, @"\s+", " ").Trim(); }

        private static int FindExcerpt(string text, string excerpt, int from, int to)
        {
            int i = text.IndexOf(excerpt, from, StringComparison.Ordinal);
            if (i < 0 || i + excerpt.Length > to) i = text.IndexOf(excerpt, from, StringComparison.OrdinalIgnoreCase);
            return i >= 0 && i + excerpt.Length <= to ? i : -1;
        }

        private static List<string> ParentLine(XmlElement el)
        {
            for (var p = el.ParentNode as XmlElement; p != null; p = p.ParentNode as XmlElement)
                if (Xhtml.IsBlock(p.Name)) return Xhtml.LineContext(p);
            return new List<string> { "paragraph" };
        }

        private XmlElement WholeElement(string file, string kind)
        {
            var root = Doc(file).DocumentElement;
            var all = Xhtml.Descendants(root).ToList();
            Func<XmlElement, bool> free = e => !_claimed.Contains(file + "|" + Xhtml.XPathOf(e) + "|");
            switch (kind)
            {
                case "alt_missing":
                    return all.FirstOrDefault(e => e.Name == "img" && string.IsNullOrWhiteSpace(e.GetAttribute("alt")) && free(e));
                case "table_header":
                    return all.FirstOrDefault(e => e.Name == "table" && !Xhtml.Descendants(e).Any(x => x.Name == "th") && free(e));
                case "second_h1":
                    return all.Where(e => e.Name == "h1").Skip(1).FirstOrDefault(free);
                case "heading_skip":
                    int prev = 0;
                    foreach (var e in all)
                    {
                        if (e.Name.Length == 2 && e.Name[0] == 'h' && char.IsDigit(e.Name[1]))
                        {
                            int lvl = e.Name[1] - '0';
                            if (prev > 0 && lvl - prev > 1 && free(e)) return e;
                            prev = lvl;
                        }
                    }
                    return null;
            }
            return null;
        }

        private static List<string> RulesOf(string rule, string source, string kind)
        {
            var r = new List<string>();
            if (rule.StartsWith("company:", StringComparison.Ordinal)) r.Add(rule + "/*");
            else if (rule == "google") r.Add("google:*");
            else if (rule == "project:variables") { r.Add("project:variables"); r.Add("project:variables/*"); }
            else if (rule.StartsWith("Structure.", StringComparison.Ordinal)) r.Add("structure:" + rule.Substring("Structure.".Length));
            if (source == "ai" || rule == "general") r.Add("ai:" + kind);
            // The pilot hand count credited the Google phone-number rule on the hard-coded phone (right text, different rationale).
            if (kind == "phone") r.Add("google:phone-numbers/*");
            // The company plain-language row also names "click here"; both rows flag the same text correctly.
            if (kind == "click_here") r.Add("company:plain-language/*");
            return r;
        }

        /// <summary>Stable 30% test split by file: FNV-1a of the path.</summary>
        public static string SplitOf(string file)
        {
            uint h = 2166136261;
            foreach (byte b in Encoding.UTF8.GetBytes(file)) { h ^= b; h *= 16777619; }
            return h % 10 < 3 ? "test" : "dev";
        }

        // ---- clean and exempt labels -------------------------------------------------------------------------------------------

        private IEnumerable<Label> Clean(List<string> files, List<Label> violations)
        {
            var result = new List<Label>();
            var byBlock = violations.Where(v => v.Quote != null).GroupBy(v => v.File + "|" + v.Block).ToDictionary(g => g.Key, g => g.ToList());
            int next = 0;
            Func<string, string, BlockText, int, int, string, string, string, string[], string[], Label> mk =
                (file, blockPath, block, start, end, kind, reason, by, rules, spanExtra) =>
            {
                string quote, prefix, suffix;
                Anchor.MakeSelector(block.Text, start, end, 24, out quote, out prefix, out suffix);
                var span = Xhtml.SpanContext(block, start, end, file, _boilerplate.Contains(block.Text));
                foreach (var s in spanExtra ?? new string[0]) if (!span.Contains(s)) span.Add(s);
                return new Label
                {
                    Id = _project + "/C" + (++next).ToString("D4"),
                    File = file, Block = blockPath, Quote = quote, Prefix = prefix.Length == 0 ? null : prefix, Suffix = suffix.Length == 0 ? null : suffix,
                    Kind = kind, Reason = reason, Rules = new List<string>(rules),
                    Context = new LabelContext { Line = Xhtml.LineContext(block.Element), Span = span },
                    Origin = "natural", Status = "verified", By = new List<string> { by }, Split = SplitOf(file)
                };
            };

            const string boilerplateText = "Printed copies of this page can be out of date.";
            var units = new Regex(@"\b\d+ (KB|MB|GB|TB)\b");
            foreach (var file in files)
            {
                foreach (var b in Blocks(file))
                {
                    List<Label> mine;
                    byBlock.TryGetValue(file + "|" + b.XPath, out mine);
                    Func<int, int, bool> free = (s, e) => mine == null || !mine.Any(v =>
                    {
                        var rs = Anchor.Resolve(b.Text, v.Quote, v.Prefix, v.Suffix);
                        return rs.Resolved && s < rs.End && rs.Start < e;
                    });

                    // 1. bold UI labels: never a finding for any lint rule; "Sign-On" is the product's own page name, exempt from the sign-on word-list row
                    foreach (var bold in BoldSpans(b))
                    {
                        if (!free(bold.Key, bold.Value)) continue;
                        string t = b.Text.Substring(bold.Key, bold.Value - bold.Key);
                        if (t.IndexOf("Sign-On", StringComparison.Ordinal) >= 0)
                            result.Add(mk(file, b.XPath, b, bold.Key, bold.Value, LabelKind.Exempt,
                                "Bold UI label that must match the product verbatim: the page is named 'Sign-On Settings'.", "hand-read",
                                new[] { "google:word-list/640" }, null));
                        else
                            result.Add(mk(file, b.XPath, b, bold.Key, bold.Value, LabelKind.Clean,
                                "Bold UI label: names a control or page in the product, so no wording rule applies.", "hand-read",
                                new[] { "*" }, null));
                    }

                    // 2. repeated boilerplate sentence (alone, or the lead sentence of a longer conditional paragraph)
                    {
                        int bi = b.Text.IndexOf(boilerplateText, StringComparison.Ordinal);
                        int i = bi < 0 ? -1 : bi + boilerplateText.IndexOf("this page", StringComparison.Ordinal);
                        if (i >= 0 && free(i, i + 9))
                            result.Add(mk(file, b.XPath, b, i, i + 9, LabelKind.Exempt,
                                "Approved print boilerplate repeated in every topic; 'this page' is the intended wording.", "hand-read",
                                new[] { "google:word-list/221" }, new[] { "boilerplate" }));
                    }

                    // 3. snippet reference to the legacy app's page
                    if (file.EndsWith("LegacyApp.flsnp", StringComparison.Ordinal))
                    {
                        int i = b.Text.IndexOf("this page", StringComparison.Ordinal);
                        if (i >= 0 && free(i, i + 9))
                            result.Add(mk(file, b.XPath, b, i, i + 9, LabelKind.Exempt,
                                "Refers to a page of the legacy app, not to the document: 'this document' would be wrong.", "hand-read",
                                new[] { "google:word-list/221" }, null));
                    }

                    // 4. file sizes with a plain space
                    foreach (Match m in units.Matches(b.Text))
                        if (free(m.Index, m.Index + m.Length))
                            result.Add(mk(file, b.XPath, b, m.Index, m.Index + m.Length, LabelKind.Exempt,
                                "Non-breaking-space advice is typographic and suggestion-level; the corpus does not seed it.", "hand-read",
                                new[] { "google:units-of-measure/*" }, null));
                }
            }
            return result;
        }

        /// <summary>Offsets [start, end) of each bold element's text in the block.</summary>
        private static List<KeyValuePair<int, int>> BoldSpans(BlockText b)
        {
            var spans = new List<KeyValuePair<int, int>>();
            var seen = new HashSet<XmlElement>();
            foreach (var r in b.Runs)
            {
                for (XmlNode n = r.Node; n != null && n != b.Element; n = n.ParentNode)
                {
                    var e = n as XmlElement;
                    if (e != null && (e.Name == "b" || e.Name == "strong") && seen.Add(e))
                    {
                        int s = int.MaxValue, en = -1;
                        foreach (var r2 in b.Runs)
                            for (XmlNode m = r2.Node; m != null && m != b.Element; m = m.ParentNode)
                                if (m == e) { s = Math.Min(s, r2.Start); en = Math.Max(en, r2.End); break; }
                        if (en > s) spans.Add(new KeyValuePair<int, int>(s, en));
                    }
                }
            }
            return spans;
        }
    }
}
