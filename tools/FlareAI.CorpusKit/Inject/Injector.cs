using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FlareAI.CorpusKit
{
    internal sealed class DefectOp
    {
        public string Op, Find, Bad, Fix, Text, Link, From, To, Starts;
        public List<string> Texts = new List<string>(), Marks = new List<string>(), Variants = new List<string>();
    }

    internal sealed class DefectKind
    {
        public string Kind, Source;
        public List<string> Rules = new List<string>();
        public List<DefectOp> Ops = new List<DefectOp>();
    }

    internal static class DefectCatalog
    {
        public static List<DefectKind> Default()
        {
            using (var s = typeof(DefectCatalog).Assembly.GetManifestResourceStream("defects.default.yaml"))
            using (var r = new StreamReader(s, Encoding.UTF8))
                return Parse(r.ReadToEnd());
        }

        public static List<DefectKind> Load(string path)
        {
            return path == null ? Default() : Parse(File.ReadAllText(path));
        }

        public static List<DefectKind> Parse(string yaml)
        {
            var root = Yaml.Parse(yaml) as Dictionary<string, object>;
            var list = new List<DefectKind>();
            object defects;
            if (root == null || !root.TryGetValue("defects", out defects) || !(defects is List<object>)) throw new FormatException("defects.yaml needs a top-level 'defects' list");
            foreach (var d in (List<object>)defects)
            {
                var m = d as Dictionary<string, object>;
                if (m == null) continue;
                var k = new DefectKind { Kind = Yaml.Str(m, "kind"), Source = Yaml.Str(m, "source") ?? "lint", Rules = Yaml.StrList(m, "rules") };
                if (string.IsNullOrEmpty(k.Kind)) throw new FormatException("A defect has no 'kind'");
                object ops;
                if (m.TryGetValue("ops", out ops) && ops is List<object>)
                    foreach (var o in (List<object>)ops)
                    {
                        var om = o as Dictionary<string, object>;
                        if (om == null) continue;
                        var op = new DefectOp
                        {
                            Op = Yaml.Str(om, "op"), Find = Yaml.Str(om, "find"), Bad = Yaml.Str(om, "bad"), Fix = Yaml.Str(om, "fix"), Text = Yaml.Str(om, "text"),
                            Link = Yaml.Str(om, "link"), From = Yaml.Str(om, "from"), To = Yaml.Str(om, "to"), Starts = Yaml.Str(om, "starts"),
                            Texts = Yaml.StrList(om, "texts"), Marks = Yaml.StrList(om, "marks"), Variants = Yaml.StrList(om, "variants")
                        };
                        if (new[] { "replace", "append", "prepend", "remove-alt", "retag" }.All(x => x != op.Op)) throw new FormatException("Defect '" + k.Kind + "': unknown op '" + op.Op + "'");
                        if (op.Op == "replace" && (op.Find == null || op.Bad == null)) throw new FormatException("Defect '" + k.Kind + "': replace needs find and bad");
                        if (op.Op == "append" && op.Texts.Count == 0) throw new FormatException("Defect '" + k.Kind + "': append needs texts");
                        if (op.Op == "prepend" && op.Text == null) throw new FormatException("Defect '" + k.Kind + "': prepend needs text");
                        k.Ops.Add(op);
                    }
                list.Add(k);
            }
            return list;
        }
    }

    internal sealed class InjectOptions
    {
        public string Project, Out, Defects, Labels, ProjectId;
        public long Seed = 1;
        public double Density = 0.12;
        public HashSet<string> Kinds;
    }

    internal sealed class InjectResult
    {
        public int Topics, Defects;
        public SortedDictionary<string, int> ByKind = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public SortedDictionary<string, int> ByVariant = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public SortedDictionary<string, int> ByLine = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public SortedDictionary<string, int> BySpan = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public List<Label> NewLabels = new List<Label>();
        public string LabelsPath, Root;
    }

    /// <summary>
    /// Inserts defects from the taxonomy into a project's topics and writes exact span labels (block id + quote + prefix/suffix).
    /// Seeded: the same seed and inputs give the same output, independent of file order and machine.
    /// </summary>
    internal static class Injector
    {
        private sealed class Applied
        {
            public DefectKind Kind; public DefectOp Op; public XElement Block;
            public XText Anchor; public int RawIndex; public string Quote, Fix, Variant;
            public XElement Target;            // structural: the element whose xpath is the label block
            public bool WholeBlock;            // retag: quote is the entire block text
        }

        private static readonly HashSet<string> NoInject = new HashSet<string>(StringComparer.Ordinal) { "code", "kbd", "tt", "samp", "MadCap:variable", "pre" };
        private static readonly Dictionary<string, Regex> ReCache = new Dictionary<string, Regex>();

        public static InjectResult Run(InjectOptions o)
        {
            var root = Path.GetFullPath(o.Project);
            if (!Directory.Exists(root)) throw new UsageException("Project folder not found: " + o.Project);
            if (o.Out != null)
            {
                var dst = Path.GetFullPath(o.Out);
                CopyDir(root, dst);
                root = dst;
            }
            var kinds = DefectCatalog.Load(o.Defects).Where(k => o.Kinds == null || o.Kinds.Contains(k.Kind)).ToList();
            if (kinds.Count == 0) throw new UsageException("No defect kinds selected");
            var labelsPath = o.Labels ?? Path.Combine(root, "labels.jsonl");
            var existing = LabelIO.Read(labelsPath);
            var projectId = o.ProjectId ?? Path.GetFileName(root.TrimEnd('\\', '/'));
            int nextId = existing.Select(l => ParseId(l.Id)).DefaultIfEmpty(0).Max() + 1;
            var labelledBlocks = new HashSet<string>(existing.Select(l => l.File + "|" + l.Block), StringComparer.Ordinal);
            var labelledFiles = new HashSet<string>(existing.Select(l => l.File), StringComparer.OrdinalIgnoreCase);

            var idx = ProjectIndex.Load(root);
            var res = new InjectResult { LabelsPath = labelsPath, Root = root };

            foreach (var file in Fx.TopicFiles(root))
            {
                var rel = Fx.Rel(root, file);
                XDocument doc;
                try { doc = Fx.Load(file); } catch (Exception) { continue; }
                if (Fx.Body(doc) == null) continue;
                res.Topics++;
                var rng = new Rng(o.Seed, rel);
                var applied = new List<Applied>();

                // text defects, block by block in document order
                foreach (var b in BlockWalker.Blocks(doc, rel, idx))
                {
                    if (b.Element.Name.LocalName == "pre") continue;
                    if (labelledBlocks.Contains(rel + "|" + b.XPath)) continue;
                    if (rng.NextDouble() >= o.Density) continue;
                    // Replace ops need a matching phrase and are rarer than append/prepend, so try them first most of the time;
                    // otherwise prepend/append would crowd out the surface variants.
                    // Kinds are drawn uniformly (a kind with many ops must not outweigh one with a single op).
                    var pairs = new List<KeyValuePair<DefectKind, DefectOp>>();
                    var shuffledKinds = kinds.ToList();
                    rng.Shuffle(shuffledKinds);
                    foreach (var k in shuffledKinds)
                    {
                        var kops = k.Ops.Where(op => op.Op != "remove-alt" && op.Op != "retag").ToList();
                        rng.Shuffle(kops);
                        foreach (var op in kops) pairs.Add(new KeyValuePair<DefectKind, DefectOp>(k, op));
                    }
                    bool replaceFirst = rng.NextDouble() < 0.7;
                    var ordered = pairs.Where(p => (p.Value.Op == "replace") == replaceFirst).Concat(pairs.Where(p => (p.Value.Op == "replace") != replaceFirst));
                    foreach (var p in ordered)
                    {
                        var a = TryApply(p.Key, p.Value, b, rel, rng);
                        if (a != null) { applied.Add(a); break; }
                    }
                }

                // structural defects
                foreach (var k in kinds)
                    foreach (var op in k.Ops)
                    {
                        if (op.Op == "remove-alt")
                        {
                            foreach (var img in Fx.Body(doc).Descendants("img").ToList())
                            {
                                var alt = (string)img.Attribute("alt");
                                if (string.IsNullOrWhiteSpace(alt) || rng.NextDouble() >= o.Density) continue;
                                if (labelledBlocks.Contains(rel + "|" + BlockWalker.XPathOf(img))) continue;
                                img.Attribute("alt").Remove();
                                applied.Add(new Applied { Kind = k, Op = op, Target = img, Variant = "remove-alt", Fix = alt });
                            }
                        }
                        else if (op.Op == "retag" && !labelledFiles.Contains(rel))
                        {
                            var h = Fx.Body(doc).Descendants(op.From).FirstOrDefault();
                            if (h != null && rng.NextDouble() < Math.Min(1.0, o.Density * 3) && !applied.Any(x => x.Block == h))
                            {
                                h.Name = op.To;
                                applied.Add(new Applied { Kind = k, Op = op, Block = h, WholeBlock = true, Variant = "retag:" + op.From + ">" + op.To, Fix = op.From });
                            }
                        }
                    }

                if (applied.Count == 0) continue;
                Fx.Save(doc, file);

                // labels from the final DOM (re-walk: the walker is the source of truth for text and contexts)
                var final = BlockWalker.Blocks(doc, rel, idx).ToDictionary(b => b.Element, b => b);
                var docLabels = new List<KeyValuePair<int, Label>>();
                foreach (var a in applied)
                {
                    var label = new Label
                    {
                        File = rel, Verdict = "violation", Kind = a.Kind.Kind, Rules = a.Kind.Rules.Count > 0 ? a.Kind.Rules : null, Fix = a.Fix,
                        Origin = "seeded", Status = "proposed", By = new List<string> { "generator" },
                        Seed = new LabelSeed { Generator = "corpuskit/inject@1", Variant = a.Variant },
                        Split = Rng.Bucket(rel, 0) < 70 ? "dev" : "test"
                    };
                    int pos = int.MaxValue;
                    if (a.Target != null)
                    {
                        label.Block = BlockWalker.XPathOf(a.Target);
                        var host = a.Target.AncestorsAndSelf().FirstOrDefault(x => final.ContainsKey(x));
                        label.Context = new LabelContext { Line = host != null ? final[host].Line : new List<string> { "paragraph" } };
                    }
                    else
                    {
                        BlockInfo bi;
                        if (!final.TryGetValue(a.Block, out bi)) continue;
                        label.Block = bi.XPath;
                        int start, len;
                        if (a.WholeBlock) { start = 0; len = bi.Text.Length; label.Fix = a.Fix; }
                        else
                        {
                            var seg = bi.Segs.FirstOrDefault(s => s.Text == a.Anchor);
                            if (seg == null) continue;
                            start = seg.Map[Math.Min(a.RawIndex, seg.Map.Length - 1)];
                            len = a.Quote.Length;
                            if (start + len > bi.Text.Length || string.CompareOrdinal(bi.Text, start, a.Quote, 0, len) != 0)
                            {
                                start = bi.Text.IndexOf(a.Quote, seg.Start, StringComparison.Ordinal);
                                if (start < 0) continue;
                            }
                        }
                        string prefix, suffix;
                        BlockWalker.QuoteContext(bi.Text, start, len, out prefix, out suffix);
                        label.Quote = bi.Text.Substring(start, len); label.Prefix = prefix; label.Suffix = suffix;
                        label.Context = new LabelContext { Line = bi.Line, Span = BlockWalker.SpanContexts(bi, start, start + len, idx) };
                        pos = start;
                    }
                    docLabels.Add(new KeyValuePair<int, Label>(pos, label));
                }
                foreach (var l in docLabels.OrderBy(x => x.Value.Block, StringComparer.Ordinal).ThenBy(x => x.Key).Select(x => x.Value))
                {
                    l.Id = projectId + "/L" + (nextId++).ToString("D4");
                    res.NewLabels.Add(l);
                    res.Defects++;
                    Bump(res.ByKind, l.Kind); Bump(res.ByVariant, l.Seed.Variant);
                    foreach (var c in l.Context.Line) Bump(res.ByLine, c);
                    foreach (var c in l.Context.Span) Bump(res.BySpan, c);
                }
            }
            LabelIO.Write(labelsPath, existing.Concat(res.NewLabels));
            return res;
        }

        private static void Bump(IDictionary<string, int> d, string k) { int v; d.TryGetValue(k, out v); d[k] = v + 1; }

        private static int ParseId(string id)
        {
            if (id == null) return 0;
            var m = Regex.Match(id, @"(\d+)$");
            return m.Success ? int.Parse(m.Groups[1].Value) : 0;
        }

        private static void CopyDir(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dst, Path.GetFileName(f)), true);
            foreach (var d in Directory.GetDirectories(src))
            {
                var name = Path.GetFileName(d);
                if (name == ".git" || name == "bin" || name == "obj") continue;
                CopyDir(d, Path.Combine(dst, name));
            }
        }

        // ---- ops -------------------------------------------------------------------------------------------------

        private static bool Eligible(Seg s, BlockInfo b)
        {
            if (s.Text == null) return false;
            for (var a = s.Parent; a != null && a != b.Element.Parent; a = a.Parent)
            {
                if (NoInject.Contains(Fx.Prefixed(a.Name))) return false;
                if (a.Name.LocalName == "span" && Fx.ClassTokens(a).Contains("uicontrol")) return false;
                if (a == b.Element) break;
            }
            return true;
        }

        private static Applied TryApply(DefectKind k, DefectOp op, BlockInfo b, string rel, Rng rng)
        {
            switch (op.Op)
            {
                case "replace": return Replace(k, op, b, rel, rng);
                case "append": return Append(k, op, b, rng);
                case "prepend": return Prepend(k, op, b);
            }
            return null;
        }

        private static Applied Replace(DefectKind k, DefectOp op, BlockInfo b, string rel, Rng rng)
        {
            Regex re;
            if (!ReCache.TryGetValue(op.Find, out re)) ReCache[op.Find] = re = new Regex(op.Find, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            var cands = new List<KeyValuePair<Seg, Match>>();
            foreach (var s in b.Segs.Where(s => Eligible(s, b)))
                foreach (Match m in re.Matches(s.Text.Value)) if (m.Length > 0) cands.Add(new KeyValuePair<Seg, Match>(s, m));
            if (cands.Count == 0) return null;

            var variants = (op.Variants.Count > 0 ? op.Variants : new List<string> { "plain" }).ToList();
            rng.Shuffle(variants);
            string variant = null;
            List<KeyValuePair<Seg, Match>> pool = null;
            foreach (var v in variants)
            {
                var feasible = cands.Where(c => Feasible(v, c.Key, c.Value, op, b)).ToList();
                if (feasible.Count > 0) { variant = v; pool = feasible; break; }
            }
            if (pool == null) { variant = "plain"; pool = cands; }
            var pick = rng.Pick(pool);
            var seg = pick.Key; var mm = pick.Value;

            var orig = mm.Value;
            var bad = mm.Result(op.Bad);
            var fix = op.Fix != null ? mm.Result(op.Fix) : orig;
            string caseVar = "mid-sentence";
            bool allCaps = orig.Length > 1 && orig.Any(char.IsLetter) && orig == orig.ToUpperInvariant();
            if (allCaps) { bad = bad.ToUpperInvariant(); fix = fix.ToUpperInvariant(); caseVar = "allcaps"; }
            else if (char.IsUpper(orig[0]))
            {
                bad = Cap(bad); fix = Cap(fix);
                var before = seg.Text.Value.Substring(0, mm.Index).TrimEnd();
                caseVar = (before.Length == 0 && b.Segs[0] == seg) || Regex.IsMatch(before, @"[.!?:]$") ? "sentence-start" : "capitalized";
            }
            var node = seg.Text; var val = node.Value;
            var pre = val.Substring(0, mm.Index); var post = val.Substring(mm.Index + mm.Length);
            var a = new Applied { Kind = k, Op = op, Block = b.Element, Quote = bad, Fix = fix };

            switch (variant)
            {
                case "bold":
                case "link":
                    {
                        var wrap = variant == "bold" ? new XElement("b", bad) : new XElement(Fx.MadCap + "xref", new XAttribute("href", Path.GetFileName(rel)), bad);
                        Replace(node, pre, wrap, post);
                        a.Anchor = (XText)wrap.FirstNode; a.RawIndex = 0;
                        break;
                    }
                case "across-inline":
                    {
                        int sp = bad.IndexOf(' ');
                        var first = bad.Substring(0, sp);
                        var wrap = new XElement("span", new XAttribute("class", "keyword"), first);
                        Replace(node, pre, wrap, bad.Substring(sp) + post);
                        a.Anchor = (XText)wrap.FirstNode; a.RawIndex = 0;
                        break;
                    }
                default:
                    node.Value = pre + bad + post;
                    a.Anchor = node; a.RawIndex = mm.Index;
                    break;
            }
            a.Variant = caseVar + "+" + variant;
            return a;
        }

        private static void Replace(XText node, string pre, XElement wrap, string post)
        {
            var parts = new List<object>();
            if (pre.Length > 0) parts.Add(new XText(pre));
            parts.Add(wrap);
            if (post.Length > 0) parts.Add(new XText(post));
            node.ReplaceWith(parts.ToArray());
        }

        private static bool Feasible(string variant, Seg s, Match m, DefectOp op, BlockInfo b)
        {
            switch (variant)
            {
                case "plain": return true;
                case "bold":
                case "link": return s.Text.Parent == b.Element;
                case "across-inline": return s.Text.Parent == b.Element && op.Bad.Contains(" ") && m.Result(op.Bad).Contains(" ");
                case "adjacent-variable":
                    {
                        var p = s.Text.PreviousNode as XElement; var n = s.Text.NextNode as XElement;
                        return (p != null && p.Name == Fx.MadCap + "variable") || (n != null && n.Name == Fx.MadCap + "variable");
                    }
            }
            return false;
        }

        private static string Cap(string s) { return s.Length > 0 && char.IsLower(s[0]) ? char.ToUpperInvariant(s[0]) + s.Substring(1) : s; }

        private static Applied Append(DefectKind k, DefectOp op, BlockInfo b, Rng rng)
        {
            if (b.Element.Name.LocalName != "p") return null;
            var last = b.Element.LastNode as XText;
            if (last == null || !Regex.IsMatch(last.Value.TrimEnd(), @"[.!?]$") || b.Segs.Count == 0 || b.Text.Length == 0) return null;
            int i = rng.Next(op.Texts.Count);
            var text = op.Texts[i];
            var mark = i < op.Marks.Count ? op.Marks[i] : text;
            int mi = text.IndexOf(mark, StringComparison.Ordinal);
            if (mi < 0) { mark = text; mi = 0; }
            var trimmed = last.Value.TrimEnd();
            var a = new Applied { Kind = k, Op = op, Block = b.Element, Quote = mark, Fix = op.Fix, Variant = "append" };
            if (op.Link != null)
            {
                last.Value = trimmed + " " + text.Substring(0, mi);
                var link = new XElement("a", new XAttribute("href", op.Link), mark);
                var rest = text.Substring(mi + mark.Length);
                last.AddAfterSelf(rest.Length > 0 ? new object[] { link, new XText(rest) } : new object[] { link });
                a.Anchor = (XText)link.FirstNode; a.RawIndex = 0; a.Variant = "append+link";
            }
            else
            {
                last.Value = trimmed + " " + text;
                a.Anchor = last; a.RawIndex = trimmed.Length + 1 + mi;
            }
            return a;
        }

        // Words a sentence can start with so that "Please <word>" or "Please note that <word>" still reads naturally.
        private static readonly HashSet<string> Imperative = new HashSet<string>(StringComparer.Ordinal)
        {
            "select", "click", "open", "enter", "choose", "check", "go", "press", "copy", "download", "run", "create", "add", "remove", "confirm",
            "save", "read", "see", "start", "stop", "sign", "install", "edit", "review", "turn", "set", "type", "keep", "pass", "make", "find",
            "change", "restart", "rotate", "use", "contact", "ask", "verify", "wait", "close", "log", "send", "delete", "update", "upload", "follow"
        };
        private static readonly HashSet<string> Declarative = new HashSet<string>(StringComparer.Ordinal)
            { "the", "a", "an", "each", "every", "this", "these", "your", "it", "all", "some", "most", "jobs", "workers", "members", "users", "accounts" };

        private static Applied Prepend(DefectKind k, DefectOp op, BlockInfo b)
        {
            var n = b.Element.Name.LocalName;
            if (n != "p" && n != "li") return null;
            var first = b.Segs.FirstOrDefault();
            if (first == null || first.Text == null || first.Start != 0 || first.Text.Parent != b.Element) return null;
            var m = Regex.Match(first.Text.Value, @"^(\s*)([A-Z])([a-z]+)\b");
            if (!m.Success) return null;
            var word = (m.Groups[2].Value + m.Groups[3].Value).ToLowerInvariant();
            bool imperative = Imperative.Contains(word), declarative = Declarative.Contains(word);
            if (op.Starts == "imperative" && !imperative) return null;
            if (op.Starts == "declarative" && !declarative) return null;
            var val = first.Text.Value;
            first.Text.Value = m.Groups[1].Value + op.Text + char.ToLowerInvariant(m.Groups[2].Value[0]) + val.Substring(m.Groups[1].Length + 1);
            return new Applied
            {
                Kind = k, Op = op, Block = b.Element, Anchor = first.Text, RawIndex = m.Groups[1].Length, Quote = op.Text.Trim(),
                Fix = null, Variant = "prepend"
            };
        }
    }
}
