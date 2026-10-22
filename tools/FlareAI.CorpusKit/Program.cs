using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlareAI.CorpusKit
{
    /// <summary>
    /// Dev tool that builds realistic Flare projects for the corpus (never shipped). See tools/FlareAI.CorpusKit/README in the usage text.
    /// </summary>
    internal static class Program
    {
        private const string Usage = @"FlareAI.CorpusKit - builds Flare projects for the House Style corpus (dev tool, never shipped)

  import   --src DIR --out PROJECT [--name N] [--var ""Source=Set.Name[:Value]""]... [--alt-drop-rate 0..1] [--seed N]
           [--snippet-min 3] [--ui-style bold|uicontrol] [--section general]
           Markdown/HTML to Flare XHTML topics, snippets (passages repeated in >= snippet-min topics) and variables.
           Local images are copied; external images are NOT downloaded (dropped, listed in corpus-kit.json warnings).
  scaffold --project DIR [--name N] [--conditions Print,ScreenOnly] [--glossary ""Term=Definition;...""] [--force]
           Writes .flprj, variable sets, condition tag set, Main and Print TOCs, HTML5 and PDF targets, glossary, stylesheet.
  inject   --project DIR [--defects FILE] [--seed N] [--density 0.12] [--labels FILE] [--out DIR] [--kinds a,b] [--project-id ID]
           Inserts defects from defects.yaml (default: built in) and appends exact span labels to labels.jsonl.
           --out injects into a copy (fresh-seed holdout); the same seed always gives the same result.
  contexts --project DIR [--labels FILE] [--out FILE] [--update]
           Computes line/span contexts from the DOM (independent of Core). Writes block records to --out
           (default PROJECT/contexts.jsonl); with --update, rewrites the context of every label in --labels.
  check    --project DIR [--labels FILE]
           Every file parses; every xref, snippet, image, link, TOC entry, variable and condition resolves;
           every label anchors uniquely. Exit code 1 when anything fails.
";

        public static int Main(string[] argv)
        {
            Args a;
            try { a = new Args(argv); } catch (Exception e) { Console.Error.WriteLine(e.Message); return 2; }
            try
            {
                switch ((a.Command ?? "").ToLowerInvariant())
                {
                    case "import": return Import(a);
                    case "scaffold": return Scaffold(a);
                    case "inject": return Inject(a);
                    case "contexts": return Contexts(a);
                    case "check": return Check(a);
                    default: Console.Error.WriteLine(Usage); return a.Command == null || a.Command == "help" ? 0 : 2;
                }
            }
            catch (UsageException e) { Console.Error.WriteLine(e.Message); return 2; }
            catch (Exception e) when (e is IOException || e is FormatException || e is JsonException || e is System.Xml.XmlException)
            {
                Console.Error.WriteLine("error: " + e.Message); return 1;
            }
        }

        private static int Import(Args a)
        {
            var o = new ImportOptions
            {
                Src = a.Require("src"), Out = a.Require("out"), Name = a.Get("name"),
                AltDropRate = a.Double("alt-drop-rate", 0), Seed = a.Long("seed", 1), SnippetMin = (int)a.Long("snippet-min", 3),
                UiStyle = a.Get("ui-style", "bold"), DefaultSection = a.Get("section", "general")
            };
            foreach (var v in a.All("var")) o.Vars.Add(VarMap.Parse(v));
            var m = Importer.Run(o);
            Console.WriteLine("Imported " + m.Topics.Count + " topics, " + m.Snippets.Count + " snippets, " + m.Variables.Count + " variables into " + o.Out);
            foreach (var w in m.Warnings) Console.WriteLine("  warning: " + w);
            return 0;
        }

        private static int Scaffold(Args a)
        {
            var o = new ScaffoldOptions { Project = a.Require("project"), Name = a.Get("name"), Force = a.Flag("force") };
            var cond = a.Get("conditions");
            if (cond != null) o.Conditions = cond.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
            var gl = a.Get("glossary");
            if (gl != null)
                foreach (var pair in gl.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var eq = pair.IndexOf('=');
                    if (eq > 0) o.Glossary[pair.Substring(0, eq).Trim()] = pair.Substring(eq + 1).Trim();
                }
            var written = Scaffolder.Run(o);
            Console.WriteLine("Scaffolded " + written.Count + " files in " + o.Project);
            foreach (var w in written) Console.WriteLine("  " + w);
            return 0;
        }

        private static int Inject(Args a)
        {
            var o = new InjectOptions
            {
                Project = a.Require("project"), Out = a.Get("out"), Defects = a.Get("defects"), Labels = a.Get("labels"), ProjectId = a.Get("project-id"),
                Seed = a.Long("seed", 1), Density = a.Double("density", 0.12)
            };
            var kinds = a.Get("kinds");
            if (kinds != null) o.Kinds = new HashSet<string>(kinds.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()));
            var r = Injector.Run(o);
            Console.WriteLine("Injected " + r.Defects + " defects into " + r.Topics + " files; labels: " + r.LabelsPath);
            Print("kinds", r.ByKind); Print("variants", r.ByVariant); Print("line contexts", r.ByLine); Print("span contexts", r.BySpan);
            return 0;
        }

        private static void Print(string title, IDictionary<string, int> d)
        {
            Console.WriteLine("  " + title + ": " + string.Join(", ", d.Select(kv => kv.Key + "=" + kv.Value)));
        }

        private static int Contexts(Args a)
        {
            var root = Path.GetFullPath(a.Require("project"));
            var idx = ProjectIndex.Load(root);
            if (a.Flag("update"))
            {
                var labelsPath = a.Get("labels") ?? Path.Combine(root, "labels.jsonl");
                if (!File.Exists(labelsPath)) { Console.WriteLine("No labels file at " + labelsPath + "; nothing to update"); return 0; }
                var labels = LabelIO.Read(labelsPath);
                var cache = new Dictionary<string, Dictionary<System.Xml.Linq.XElement, BlockInfo>>(StringComparer.OrdinalIgnoreCase);
                var docs = new Dictionary<string, System.Xml.Linq.XDocument>(StringComparer.OrdinalIgnoreCase);
                int updated = 0, skipped = 0;
                foreach (var l in labels)
                {
                    var path = Path.Combine(root, (l.File ?? "").Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(path)) { skipped++; continue; }
                    System.Xml.Linq.XDocument doc;
                    if (!docs.TryGetValue(l.File, out doc)) docs[l.File] = doc = Fx.Load(path);
                    var el = BlockWalker.Resolve(doc, l.Block ?? "");
                    if (el == null) { skipped++; continue; }
                    Dictionary<System.Xml.Linq.XElement, BlockInfo> map;
                    if (!cache.TryGetValue(l.File, out map)) cache[l.File] = map = BlockWalker.Blocks(doc, l.File, idx).ToDictionary(b => b.Element, b => b);
                    BlockInfo bi;
                    if (!map.TryGetValue(el, out bi))
                    {
                        l.Context = new LabelContext { Line = BlockWalker.LineContexts(el.AncestorsAndSelf().FirstOrDefault(x => map.ContainsKey(x)) ?? el) };
                        updated++; continue;
                    }
                    var ctx = new LabelContext { Line = bi.Line };
                    if (l.Quote != null)
                    {
                        var probe = (l.Prefix ?? "") + l.Quote + (l.Suffix ?? "");
                        int at = bi.Text.IndexOf(probe, StringComparison.Ordinal);
                        if (at < 0) { skipped++; continue; }
                        int start = at + (l.Prefix ?? "").Length;
                        ctx.Span = BlockWalker.SpanContexts(bi, start, start + l.Quote.Length, idx);
                    }
                    l.Context = ctx; updated++;
                }
                LabelIO.Write(labelsPath, labels);
                Console.WriteLine("Updated contexts for " + updated + " labels (" + skipped + " skipped) in " + labelsPath);
                return skipped == 0 ? 0 : 1;
            }

            var outPath = a.Get("out") ?? Path.Combine(root, "contexts.jsonl");
            var sb = new StringBuilder();
            int n = 0;
            foreach (var f in Fx.TopicFiles(root))
            {
                var rel = Fx.Rel(root, f);
                foreach (var b in BlockWalker.Blocks(Fx.Load(f), rel, idx))
                {
                    var rec = new JObject
                    {
                        ["file"] = rel, ["block"] = b.XPath, ["text"] = b.Text, ["line"] = new JArray(b.Line),
                        ["spans"] = new JArray(Spans(b, idx))
                    };
                    sb.Append(rec.ToString(Formatting.None)).Append('\n'); n++;
                }
            }
            File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
            Console.WriteLine("Wrote " + n + " block records to " + outPath);
            return 0;
        }

        /// <summary>Per text segment: its range and span contexts, so a consumer can look up any character offset.</summary>
        private static IEnumerable<JObject> Spans(BlockInfo b, ProjectIndex idx)
        {
            foreach (var s in b.Segs)
            {
                var ctx = BlockWalker.SpanContexts(b, s.Start, s.End, idx);
                if (ctx.Count == 0) continue;
                yield return new JObject { ["start"] = s.Start, ["end"] = s.End, ["span"] = new JArray(ctx) };
            }
        }

        private static int Check(Args a)
        {
            var r = Checker.Run(a.Require("project"), a.Get("labels"));
            foreach (var e in r.Errors) Console.WriteLine("  " + e);
            Console.WriteLine((r.Ok ? "OK: " : "FAILED: ") + r.Files + " files, " + r.Topics + " topics, " + r.Labels + " labels, " + r.Errors.Count + " problems");
            return r.Ok ? 0 : 1;
        }
    }
}
