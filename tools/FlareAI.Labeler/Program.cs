using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using FlareAI.Core.Llm;
using FlareAI.Scoring;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlareAI.Labeler
{
    public static class Program
    {
        private const string Usage =
@"FlareAI.Labeler - corpus label normalisation, label QA and the blind two-labeller pass (#78)

  normalize --project <dir>... | --all <corpus/v2>   [--write]
        Map generator rule hints to scorer rule ids; seeded violations written by an exact generator become verified. Dry by default.
  qa        --project <dir>... | --all <corpus/v2>   [--fix] [--json <file>] [--update-labels-md] [--show <n>]
        Deterministic label QA: validity and anchors, duplicates, overlaps, exempt markup, kind/rule fit, split and status, coverage. Exit 1 on errors.
        --fix applies the mechanical repairs (duplicates, natural copies of seeded labels, heuristic clean labels on a seeded span) and rewrites labels.jsonl.
  label     --project <dir>... | --all <corpus/v2>   [--split dev|test|all] [--max-blocks n] [--seed n] [--model-a id] [--model-b id]
                                                      [--spans-per-block f] [--extra-rules <file>] [--execute --max-cost <usd>]
        Prices the blind pass by default (dry run, nothing is sent). --execute sends it, needs ANTHROPIC_API_KEY and a --max-cost cap; answers
        are stored under <project>/adjudication/runs and reused, so a rerun spends nothing on batches already done.
  agree     --project <dir> [--findings <findings.jsonl>] [--seed n] [--kappa-threshold 0.6]
        Compare the two labellers' stored answers: spans, kappa per kind, adjudication queue (with 20% decoys) in <project>/adjudication.
  merge     --project <dir> [--write] [--kappa-threshold 0.6]
        Apply agreement and the decisions in adjudication/decisions.jsonl to labels.jsonl (verified: both agree or a decision accepts).
";

        public static int Main(string[] args)
        {
            if (args.Length == 0) { Console.Error.WriteLine(Usage); return 2; }
            var opt = Parse(args.Skip(1).ToArray());
            try
            {
                switch (args[0])
                {
                    case "normalize": return Normalize(opt);
                    case "qa": return Qa(opt);
                    case "label": return LabelCmd(opt);
                    case "agree": return Agree(opt);
                    case "merge": return MergeCmd(opt);
                    default: Console.Error.WriteLine(Usage); return 2;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidOperationException || ex is JsonException || ex is LlmException || ex is ArgumentException)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        // ---- plumbing ----------------------------------------------------------------------------------------------------------

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

        private static int Int(Dictionary<string, List<string>> o, string k, int def)
        {
            string v = One(o, k);
            int n;
            return v != null && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : def;
        }

        private static double Dbl(Dictionary<string, List<string>> o, string k, double def)
        {
            string v = One(o, k);
            double n;
            return v != null && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out n) ? n : def;
        }

        private static List<string> Projects(Dictionary<string, List<string>> o)
        {
            var list = new List<string>();
            List<string> p;
            if (o.TryGetValue("project", out p)) list.AddRange(p);
            string all = One(o, "all");
            if (all != null)
                list.AddRange(Directory.GetDirectories(all).Where(d => File.Exists(Path.Combine(d, "labels.jsonl"))).OrderBy(d => d, StringComparer.Ordinal));
            if (list.Count == 0) throw new ArgumentException("Give --project <dir> or --all <corpus/v2>.");
            return list;
        }

        private static Taxonomy LoadTaxonomy(Dictionary<string, List<string>> o, string project)
        {
            string path = One(o, "kinds") ?? CorpusProject.FindUp(project, "corpus/schema/defects.yaml");
            if (path == null) throw new ArgumentException("corpus/schema/defects.yaml not found; pass --kinds.");
            return Taxonomy.Load(path);
        }

        private static List<Label> LoadReference(Dictionary<string, List<string>> o, string project)
        {
            string path = One(o, "reference") ?? CorpusProject.FindUp(project, "corpus/DigitalBanking/labels.jsonl");
            return path == null ? null : LabelIO.Read(path);
        }

        // ---- normalize ---------------------------------------------------------------------------------------------------------

        private static int Normalize(Dictionary<string, List<string>> o)
        {
            bool write = o.ContainsKey("write");
            foreach (var dir in Projects(o))
            {
                var project = CorpusProject.Open(dir);
                var tax = LoadTaxonomy(o, dir);
                var labels = project.ReadLabels();
                var res = Normalizer.Run(labels, tax, project);
                Console.WriteLine(project.Name + ": " + res.Labels + " labels, rules rewritten on " + res.RulesRewritten + ", seeded violations promoted to verified " + res.Promoted + (res.PromotionBlocked > 0 ? ", promotion blocked " + res.PromotionBlocked : ""));
                foreach (var kv in res.Rewrites.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal)) Console.WriteLine("    " + kv.Value.ToString().PadLeft(5) + "  " + kv.Key);
                foreach (var n in res.Notes.Take(10)) Console.WriteLine("    " + n);
                if (write && (res.RulesRewritten > 0 || res.Promoted > 0)) LabelIO.Write(project.LabelsPath, labels);
            }
            if (!write) Console.WriteLine("(dry: pass --write to change labels.jsonl)");
            return 0;
        }

        // ---- qa ----------------------------------------------------------------------------------------------------------------

        private static int Qa(Dictionary<string, List<string>> o)
        {
            int errors = 0, show = Int(o, "show", 5);
            var all = new JArray();
            foreach (var dir in Projects(o))
            {
                var project = CorpusProject.Open(dir);
                var tax = LoadTaxonomy(o, dir);
                var labels = project.ReadLabels();
                if (o.ContainsKey("fix"))
                {
                    var fixes = QaFixer.Apply(project, labels);
                    foreach (var n in fixes) Console.WriteLine("    fixed " + n);
                    if (fixes.Count > 0) LabelIO.Write(project.LabelsPath, labels);
                }
                var rep = LabelQa.Run(project, labels, tax, LoadReference(o, dir));
                errors += rep.Count("error");
                var s = rep.Stats;
                Console.WriteLine(project.Name + ": " + rep.Count("error") + " errors, " + rep.Count("warn") + " warnings, " + rep.Count("info") + " notes; "
                    + s.Labels + " labels (" + s.ScoredLabels + " scored), " + s.Violations + " violations (" + s.ViolationsVerified + " verified, " + s.ViolationsVerifiedTest + " in test), "
                    + "blocks labelled " + s.BlocksLabelled + "/" + s.Blocks + ", block-complete " + s.BlocksComplete + " (" + s.BlocksCompleteVerified + " verified)");
                foreach (var kv in rep.ByCode()) Console.WriteLine("    " + kv.Key + ": " + kv.Value);
                foreach (var g in rep.Issues.Where(i => i.Severity != "info").GroupBy(i => i.Code))
                    foreach (var i in g.Take(show)) Console.WriteLine("      " + i.Code + " " + i.LabelId + ": " + i.Message);
                all.Add(rep.ToJson(2000));
                if (o.ContainsKey("update-labels-md")) Md.Update(Path.Combine(dir, "LABELS.md"), "qa", LabelQa.Markdown(rep));
            }
            string json = One(o, "json");
            if (json != null) File.WriteAllText(json, all.ToString(Formatting.Indented), new UTF8Encoding(false));
            return errors == 0 ? 0 : 1;
        }

        // ---- label (plan, price, run) ------------------------------------------------------------------------------------------

        private static int LabelCmd(Dictionary<string, List<string>> o)
        {
            var ro = new RunOptions
            {
                Execute = o.ContainsKey("execute") && !o.ContainsKey("dry-run"),
                MaxCostUsd = Dbl(o, "max-cost", 0),
                ModelA = One(o, "model-a") ?? "claude-sonnet-5-5",
                ModelB = One(o, "model-b") ?? "claude-opus-5-5"
            };
            if (ro.Execute && ro.MaxCostUsd <= 0) throw new ArgumentException("--execute needs --max-cost <usd> (a positive cap). Without --execute this is a dry run.");
            ILlmClient clientA = null, clientB = null;
            if (ro.Execute)
            {
                string key = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
                if (string.IsNullOrEmpty(key)) throw new ArgumentException("ANTHROPIC_API_KEY is not set (scripts\\Load-DotEnv.ps1 loads it from .env).");
                clientA = new AnthropicClient(null, key, ro.ModelA, 180);
                clientB = new AnthropicClient(null, key, ro.ModelB, 180);
            }
            var popt = new PlanOptions
            {
                Split = One(o, "split") ?? "all", MaxBlocks = Int(o, "max-blocks", 0), Seed = Int(o, "seed", 1),
                BatchChars = Int(o, "batch-chars", 6000), ExtraRulesPath = One(o, "extra-rules")
            };

            double totalUsd = 0, spent = 0; long tin = 0, tout = 0; int tblocks = 0, tcalls = 0, tbatches = 0;
            Console.WriteLine((ro.Execute ? "RUN" : "DRY RUN (nothing is sent)") + "; labeller A " + ro.ModelA + ", labeller B " + ro.ModelB + (ro.Execute ? "; cap $" + ro.MaxCostUsd.ToString("0.00", CultureInfo.InvariantCulture) : ""));
            foreach (var dir in Projects(o))
            {
                var project = CorpusProject.Open(dir);
                var tax = LoadTaxonomy(o, dir);
                var plan = LabelPlan.Build(project, tax, popt);
                var existing = project.ReadLabels();
                double spb = Dbl(o, "spans-per-block", Math.Max(0.05, 1.5 * existing.Count(l => l.Kind == LabelKind.Violation) / (double)Math.Max(1, plan.Blocks)));
                ro.SpansPerBlock = spb;
                var runner = new LabelRunner(clientA, clientB, tax, Path.Combine(dir, "adjudication"));
                var sum = runner.Run(plan, ro);
                tblocks += plan.Blocks; tcalls += plan.Batches.Count * 2; tbatches += plan.Batches.Count;
                tin += sum.EstimateA.Input + sum.EstimateA.CacheRead + sum.EstimateA.CacheWrite + sum.EstimateB.Input + sum.EstimateB.CacheRead + sum.EstimateB.CacheWrite;
                tout += sum.EstimateA.Output + sum.EstimateB.Output;
                totalUsd += sum.EstimatedUsd; spent += sum.SpentUsd;
                Console.WriteLine(project.Name.PadRight(16) + plan.Blocks.ToString().PadLeft(6) + " blocks (" + plan.SkippedBlocks + " skipped), " + plan.Batches.Count.ToString().PadLeft(4) + " batches, spans/block " + spb.ToString("0.00", CultureInfo.InvariantCulture)
                    + ": est. A $" + sum.EstimateA.Usd.ToString("0.00", CultureInfo.InvariantCulture) + " + B $" + sum.EstimateB.Usd.ToString("0.00", CultureInfo.InvariantCulture) + " = $" + sum.EstimatedUsd.ToString("0.00", CultureInfo.InvariantCulture)
                    + "  (A in " + (sum.EstimateA.Input + sum.EstimateA.CacheRead + sum.EstimateA.CacheWrite) + " out " + sum.EstimateA.Output + " tokens)");
                if (ro.Execute)
                {
                    Console.WriteLine("    sent " + sum.CallsMade + " calls, reused " + sum.CacheHits + " stored answers, errors " + sum.Errors + ", spent $" + sum.SpentUsd.ToString("0.0000", CultureInfo.InvariantCulture));
                    foreach (var m in sum.Messages.Take(10)) Console.WriteLine("    " + m);
                    ro.MaxCostUsd = Math.Max(0.0000001, ro.MaxCostUsd - sum.SpentUsd);   // the cap is for the whole command
                    if (sum.StoppedByBudget) break;
                }
            }
            Console.WriteLine("TOTAL".PadRight(16) + tblocks.ToString().PadLeft(6) + " blocks, " + tcalls + " calls, " + tin + " input + " + tout + " output tokens: estimated $" + totalUsd.ToString("0.00", CultureInfo.InvariantCulture)
                + (ro.Execute ? "; spent $" + spent.ToString("0.0000", CultureInfo.InvariantCulture) : ""));
            bool priced;
            double perK = LabelPlan.Cost(ro.ModelA, new LlmResponse { OutputTokens = 1000 * tbatches }, out priced) + LabelPlan.Cost(ro.ModelB, new LlmResponse { OutputTokens = 1000 * tbatches }, out priced);
            Console.WriteLine("(estimate: 1 token per 3.6 characters, the system prompt cached after the first call, list prices from FlareAI.Core Usage; no reasoning tokens - each 1,000 per call adds $" + perK.ToString("0.00", CultureInfo.InvariantCulture) + ")");
            return 0;
        }

        // ---- agree -------------------------------------------------------------------------------------------------------------

        private static int Agree(Dictionary<string, List<string>> o)
        {
            string dir = Projects(o).Single();
            var project = CorpusProject.Open(dir);
            var tax = LoadTaxonomy(o, dir);
            string adj = Path.Combine(dir, "adjudication");
            var labels = project.ReadLabels();
            var ra = LabellerResult.Load(adj, "a", project, tax);
            var rb = LabellerResult.Load(adj, "b", project, tax);
            var kinds = LabelPlan.Kinds(tax).Select(k => k.Id).ToList();
            var ag = AgreementResult.Compare(ra, rb, kinds);
            List<Candidate> unmatched = null;
            string findings = One(o, "findings");
            if (findings != null) unmatched = FindingSource.Unmatched(project, labels, FindingIO.Read(findings));
            var qb = Adjudication.Build(project, labels, ag, unmatched, Int(o, "seed", 1));
            Adjudication.WriteLines(Path.Combine(adj, "proposals.jsonl"), qb.Proposals);
            Adjudication.WriteLines(Path.Combine(adj, "queue.jsonl"), qb.Items);
            Adjudication.WriteLines(Path.Combine(adj, "queue.key.jsonl"), qb.Keys);
            double thr = Dbl(o, "kappa-threshold", 0.6);
            File.WriteAllText(Path.Combine(adj, "agreement.json"), new JObject
            {
                ["blocks"] = ag.BlocksBoth.Count, ["agreed"] = ag.Agreed.Count, ["contradictions"] = ag.Contradictions.Count, ["onlyA"] = ag.OnlyA.Count, ["onlyB"] = ag.OnlyB.Count,
                ["invalidA"] = ra.Invalid, ["invalidB"] = rb.Invalid, ["usdA"] = Math.Round(ra.Usd, 4), ["usdB"] = Math.Round(rb.Usd, 4),
                ["confirmedExisting"] = qb.ConfirmedExisting, ["seededNotReported"] = qb.SeededNotFound, ["queue"] = qb.Items.Count, ["decoys"] = qb.Decoys,
                ["kappaThreshold"] = thr,
                ["kinds"] = new JArray(ag.Kinds.Values.Select(k => new JObject { ["kind"] = k.Kind, ["a"] = k.A, ["b"] = k.B, ["both"] = k.Both, ["kappa"] = k.Kappa.HasValue ? (JToken)k.Kappa.Value : JValue.CreateNull() }))
            }.ToString(Formatting.Indented), new UTF8Encoding(false));
            Md.Update(Path.Combine(dir, "LABELS.md"), "agreement", ag.Markdown(ra, rb, thr, null, 0, 0));
            Console.WriteLine(project.Name + ": " + ag.BlocksBoth.Count + " blocks labelled by both; " + ag.Agreed.Count + " agreed, " + ag.Contradictions.Count + " contradicted, " + ag.OnlyA.Count + " only A, " + ag.OnlyB.Count + " only B");
            Console.WriteLine("  confirms " + qb.ConfirmedExisting + " existing labels; " + qb.SeededNotFound + " seeded blocks neither labeller reported in; queue " + qb.Items.Count + " items (" + qb.Decoys + " decoys)");
            foreach (var k in ag.Kinds.Values) Console.WriteLine("  " + k.Kind.PadRight(20) + " A " + k.A.ToString().PadLeft(4) + " B " + k.B.ToString().PadLeft(4) + " both " + k.Both.ToString().PadLeft(4) + "  kappa " + (k.Kappa.HasValue ? k.Kappa.Value.ToString("0.00", CultureInfo.InvariantCulture) : "n/a"));
            return 0;
        }

        // ---- merge -------------------------------------------------------------------------------------------------------------

        private static int MergeCmd(Dictionary<string, List<string>> o)
        {
            string dir = Projects(o).Single();
            var project = CorpusProject.Open(dir);
            var tax = LoadTaxonomy(o, dir);
            string adj = Path.Combine(dir, "adjudication");
            var labels = project.ReadLabels();
            var proposals = Adjudication.ReadLines<Proposal>(Path.Combine(adj, "proposals.jsonl"));
            var keys = Adjudication.ReadLines<QueueKey>(Path.Combine(adj, "queue.key.jsonl"));
            var decisions = Adjudication.ReadLines<Decision>(Path.Combine(adj, "decisions.jsonl"));
            var ra = LabellerResult.Load(adj, "a", project, tax);
            var rb = LabellerResult.Load(adj, "b", project, tax);
            var ag = AgreementResult.Compare(ra, rb, LabelPlan.Kinds(tax).Select(k => k.Id).ToList());
            double thr = Dbl(o, "kappa-threshold", 0.6);
            var low = new HashSet<string>(ag.Below(thr).Select(k => k.Kind), StringComparer.Ordinal);
            var res = Merge.Apply(project, labels, proposals, keys, decisions, tax, LoadReference(o, dir), low);
            Console.WriteLine(project.Name + ": added " + res.Added + " (" + res.AddedVerified + " verified, " + res.AddedProposed + " proposed, " + res.AddedDisputed + " disputed, " + res.AddedAmbiguous + " ambiguous); confirmed " + res.Confirmed
                + " existing; decisions applied " + res.DecisionsApplied + ", ignored " + res.DecisionsIgnored + "; pending " + res.Pending
                + "; decoys accepted " + res.DecoysAccepted + " of " + res.Decoys);
            foreach (var n in res.Notes.Take(20)) Console.WriteLine("  " + n);
            var errors = LabelValidator.Validate(labels, tax.Ids, project.Text);
            foreach (var e in errors.Take(20)) Console.Error.WriteLine("  " + e);
            if (o.ContainsKey("write"))
            {
                if (errors.Count > 0) { Console.Error.WriteLine("Not written: the merged labels do not validate."); return 1; }
                LabelIO.Write(project.LabelsPath, labels);
                Md.Update(Path.Combine(dir, "LABELS.md"), "agreement", ag.Markdown(ra, rb, thr, res.DecoyAcceptance, res.Decoys, res.DecoysAccepted));
            }
            else Console.WriteLine("(dry: pass --write to change labels.jsonl)");
            return 0;
        }
    }
}
