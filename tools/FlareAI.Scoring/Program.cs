using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace FlareAI.Scoring
{
    /// <summary>Defect taxonomy ids from <c>corpus/schema/defects.yaml</c> (the lines <c>- id: name</c>).</summary>
    public static class DefectTaxonomy
    {
        public static HashSet<string> LoadIds(string path)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var rx = new Regex(@"^\s*-\s*id:\s*([A-Za-z0-9_\-]+)\s*$");
            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                var m = rx.Match(line);
                if (m.Success) ids.Add(m.Groups[1].Value);
            }
            return ids;
        }
    }

    public static class Program
    {
        private const string Usage =
@"FlareAI.Scoring - corpus label tools

  validate   --labels <file>... [--kinds corpus/schema/defects.yaml] [--root <project folder>]
  score      --labels <file>... --findings <findings.jsonl> [--root <project folder>] [--split dev|test] [--include-proposed] [--out <dir>]
  convert-v1 --truth corpus/ground-truth.json --project corpus/DigitalBanking --out corpus/DigitalBanking/labels.jsonl [--name DigitalBanking]

--root lets labels be checked (validate) and compared by offsets (score) against the topics; it applies to labels of one project.";

        public static int Main(string[] args)
        {
            if (args.Length == 0) { Console.Error.WriteLine(Usage); return 2; }
            var opt = Parse(args.Skip(1).ToArray());
            try
            {
                switch (args[0])
                {
                    case "validate": return Validate(opt);
                    case "score": return ScoreCmd(opt);
                    case "convert-v1": return ConvertV1(opt);
                    default: Console.Error.WriteLine(Usage); return 2;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        private static Dictionary<string, List<string>> Parse(string[] a)
        {
            var d = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            string cur = null;
            foreach (var s in a)
            {
                if (s.StartsWith("--", StringComparison.Ordinal)) { cur = s.Substring(2); if (!d.ContainsKey(cur)) d[cur] = new List<string>(); }
                else if (cur != null) d[cur].Add(s);
            }
            return d;
        }

        private static string One(Dictionary<string, List<string>> o, string k)
        {
            List<string> v;
            return o.TryGetValue(k, out v) && v.Count > 0 ? v[0] : null;
        }

        private static int Validate(Dictionary<string, List<string>> o)
        {
            var labels = LabelIO.ReadMany(o.ContainsKey("labels") ? o["labels"] : new List<string>());
            string kinds = One(o, "kinds"), root = One(o, "root");
            var errors = LabelValidator.Validate(labels, kinds == null ? null : DefectTaxonomy.LoadIds(kinds), root == null ? null : new CorpusBlockTextSource(root));
            foreach (var e in errors.Take(200)) Console.Error.WriteLine(e);
            Console.WriteLine(labels.Count + " labels, " + errors.Count + " problems");
            return errors.Count == 0 ? 0 : 1;
        }

        private static int ScoreCmd(Dictionary<string, List<string>> o)
        {
            var labels = LabelIO.ReadMany(o["labels"]);
            var findings = FindingIO.Read(One(o, "findings"));
            string root = One(o, "root");
            var report = Scorer.Score(labels, findings, new ScoreOptions
            {
                TextSource = root == null ? null : new CorpusBlockTextSource(root),
                Split = One(o, "split"),
                IncludeProposed = o.ContainsKey("include-proposed")
            });
            string outDir = One(o, "out");
            if (outDir != null)
            {
                Directory.CreateDirectory(outDir);
                File.WriteAllText(Path.Combine(outDir, "score.json"), report.ToJson().ToString(Formatting.Indented), new UTF8Encoding(false));
                File.WriteAllLines(Path.Combine(outDir, "scored-findings.jsonl"), report.Findings.Select(r => JsonConvert.SerializeObject(new
                {
                    id = r.Finding.Id, file = r.Finding.File, rule = r.Finding.RuleId, excerpt = r.Finding.Excerpt,
                    outcome = r.Outcome.ToString(), label = r.Label == null ? null : r.Label.Id, reason = r.Reason
                })), new UTF8Encoding(false));
            }
            var t = report.Overall;
            Console.WriteLine("findings " + report.FindingCount + ": TP " + t.Tp + ", FP " + t.Fp + ", wrong rule " + t.WrongRule + ", ambiguous " + t.Ambiguous + ", unscored " + t.Unscored + "; coverage " + report.Coverage.ToString("P1"));
            Console.WriteLine("violations " + t.Support + ": detected " + t.Detected + ", FN " + t.Fn + ", wrong-rule-only " + t.WrongRuleOnly + "; precision " + t.Precision.ToString("P1") + ", recall " + t.Recall.ToString("P1"));
            foreach (var kv in report.By[ScoreReport.Layer])
                Console.WriteLine("  " + kv.Key.PadRight(10) + " TP " + kv.Value.Tp + " FP " + kv.Value.Fp + " wrong " + kv.Value.WrongRule + " unscored " + kv.Value.Unscored + "  support " + kv.Value.Support + " detected " + kv.Value.Detected);
            return 0;
        }

        private static int ConvertV1(Dictionary<string, List<string>> o)
        {
            string project = One(o, "project");
            string name = One(o, "name") ?? Path.GetFileName(project.TrimEnd('\\', '/'));
            var conv = new V1Converter(project, name);
            var labels = conv.Convert(One(o, "truth"));
            foreach (var p in conv.Problems) Console.Error.WriteLine(p);
            LabelIO.Write(One(o, "out"), labels);
            Console.WriteLine(labels.Count + " labels (" + labels.Count(l => l.Kind == LabelKind.Violation) + " violations) written, " + conv.Problems.Count + " unresolved defects");
            return conv.Problems.Count == 0 ? 0 : 1;
        }
    }
}
