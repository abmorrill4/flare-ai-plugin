using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using FlareAI.Scoring;
using Newtonsoft.Json.Linq;

namespace FlareAI.Labeler
{
    public sealed class QaIssue
    {
        public string Severity;   // error | warn | info
        public string Code;
        public string LabelId;
        public string File;
        public string Message;
    }

    public sealed class QaStats
    {
        public int Labels, Files, Snippets, FilesWithBlocks, Blocks, BlocksLabelled, BlocksComplete, BlocksCompleteVerified;
        public int Violations, ViolationsVerified, ViolationsTest, ViolationsVerifiedTest, ViolationFiles;
        public int ScoredLabels;
        public double ViolationsPerTopic;
        public double BlockCoverage;
        public double TestShare;
        public SortedDictionary<string, int> ByLabelStatus = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public SortedDictionary<string, int> ViolationKinds = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public SortedDictionary<string, int> ViolationKindsVerifiedTest = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public List<string> ThinTestKinds = new List<string>();
    }

    public sealed class QaReport
    {
        public string Project;
        public List<QaIssue> Issues = new List<QaIssue>();
        public QaStats Stats = new QaStats();

        public int Count(string severity) { return Issues.Count(i => i.Severity == severity); }

        public SortedDictionary<string, int> ByCode()
        {
            var d = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var i in Issues) { int n; d[i.Severity + " " + i.Code] = d.TryGetValue(i.Severity + " " + i.Code, out n) ? n + 1 : 1; }
            return d;
        }

        public JObject ToJson(int maxIssues)
        {
            var o = new JObject
            {
                ["project"] = Project,
                ["errors"] = Count("error"), ["warnings"] = Count("warn"), ["info"] = Count("info"),
                ["byCode"] = JObject.FromObject(ByCode()),
                ["stats"] = JObject.FromObject(Stats),
            };
            var arr = new JArray();
            foreach (var i in Issues.OrderBy(i => i.Severity == "error" ? 0 : i.Severity == "warn" ? 1 : 2).ThenBy(i => i.Code, StringComparer.Ordinal).Take(maxIssues))
                arr.Add(JObject.FromObject(i));
            o["issues"] = arr;
            return o;
        }
    }

    /// <summary>
    /// Deterministic label QA: no model, no linter. Checks that every label is valid and still anchors, that labels do not
    /// duplicate or contradict each other, that violations do not sit on markup the corpus treats as exempt, that the rules and
    /// kind of a violation fit together, and that split and status follow the conventions in docs/testing/CORPUS.md.
    /// </summary>
    public static class LabelQa
    {
        public static readonly string[] KnownSources = { "generator", "source-markup", "pattern", "dom-check", "hand-read", "llm-a", "llm-b", "human" };

        /// <param name="reference">Labels whose kind/rule pairing is the convention to follow (DigitalBanking); may be null.</param>
        public static QaReport Run(CorpusProject project, IList<Label> labels, Taxonomy taxonomy, IList<Label> reference)
        {
            var rep = new QaReport { Project = project.Name };
            var ids = taxonomy.Ids;
            Action<string, string, Label, string> add = (sev, code, l, msg) =>
                rep.Issues.Add(new QaIssue { Severity = sev, Code = code, LabelId = l == null ? null : l.Id, File = l == null ? null : l.File, Message = msg });
            var byId = labels.Where(l => l.Id != null).GroupBy(l => l.Id).ToDictionary(g => g.Key, g => g.First());

            // 1. schema, anchors
            foreach (var e in LabelValidator.Validate(labels, ids, project.Text))
            {
                int c = e.IndexOf(": ", StringComparison.Ordinal);
                string id = c > 0 ? e.Substring(0, c) : null;
                string msg = c > 0 ? e.Substring(c + 2) : e;
                Label l;
                byId.TryGetValue(id ?? "", out l);
                string code = msg.StartsWith("quote not found", StringComparison.Ordinal) ? "unanchored"
                    : msg.StartsWith("block not found", StringComparison.Ordinal) ? "unanchored-block"
                    : msg.StartsWith("quote is ambiguous", StringComparison.Ordinal) ? "ambiguous-quote" : "schema";
                add("error", code, l, msg);
            }

            // 2. rule vocabulary
            foreach (var l in labels)
                foreach (var spec in l.Rules ?? new List<string>())
                {
                    string why = RuleVocabulary.Check(spec, ids);
                    if (why != null) add("error", "rule-vocabulary", l, why);
                }

            // 3. duplicates and overlaps, 4. exempt markup
            var groups = labels.Where(l => l.File != null && l.Block != null).GroupBy(l => l.File + "|" + l.Block);
            foreach (var g in groups)
            {
                var items = g.ToList();
                string text = project.Text.GetText(items[0].File, items[0].Block);
                if (text == null) continue;
                var spans = items.Select(l => Anchor.Resolve(text, l.Quote, l.Prefix, l.Suffix)).ToList();
                var seen = new Dictionary<string, Label>(StringComparer.Ordinal);
                for (int i = 0; i < items.Count; i++)
                {
                    var a = items[i];
                    string key = a.Kind + "|" + a.DefectKind + "|" + a.Quote + "|" + a.Prefix + "|" + a.Suffix + "|" + a.Status;
                    Label first;
                    if (seen.TryGetValue(key, out first)) add("error", "duplicate-label", a, "same block, span, label, kind and status as " + first.Id);
                    else seen[key] = a;
                }
                for (int i = 0; i < items.Count; i++)
                    for (int j = i + 1; j < items.Count; j++)
                    {
                        var a = items[i]; var b = items[j];
                        if (!spans[i].Resolved || !spans[j].Resolved) continue;
                        if (a.Kind == LabelKind.BlockComplete || b.Kind == LabelKind.BlockComplete || a.Kind == LabelKind.Ambiguous || b.Kind == LabelKind.Ambiguous) continue;
                        if (!(spans[i].Start < spans[j].End && spans[j].Start < spans[i].End) && !(a.Quote == null || b.Quote == null)) continue;
                        bool av = a.Kind == LabelKind.Violation, bv = b.Kind == LabelKind.Violation;
                        if (av && bv)
                        {
                            if (a.DefectKind == b.DefectKind) add("warn", "overlap-same-kind", b, "overlaps " + a.Id + " (both " + a.DefectKind + ")");
                            else add("info", "overlap-kinds", b, "overlaps " + a.Id + " (" + a.DefectKind + " and " + b.DefectKind + ")");
                        }
                        else if (av != bv)
                        {
                            var v = av ? a : b; var n = av ? b : a;
                            if (!SpecsIntersect(v.Rules, n.Rules)) continue;
                            bool both = v.IsVerified && n.IsVerified;
                            add(both ? "error" : "warn", "violation-vs-" + n.Kind, v, "overlaps " + n.Id + " (" + n.Kind + " for " + string.Join(", ", n.Rules.ToArray()) + ")");
                        }
                    }

                for (int i = 0; i < items.Count; i++)
                {
                    var l = items[i];
                    if (l.Kind != LabelKind.Violation || l.Quote == null || !spans[i].Resolved) continue;
                    var bt = project.Block(l.File, l.Block);
                    if (bt == null) continue;
                    var line = bt.Element == null ? null : Xhtml.LineContext(bt.Element);
                    var ctx = Xhtml.SpanContext(bt, spans[i].Start, spans[i].End, l.File, false);
                    if ((line != null && line.Contains("code-block")) || ctx.Contains("inline-code") || ctx.Contains("kbd"))
                        add("warn", "violation-on-exempt-markup", l, "the span '" + Clip(l.Quote) + "' is in code, a key name or a code block");
                }
            }

            // 5. kind and rules
            var refLayers = ReferenceLayers(reference);
            foreach (var l in labels.Where(l => l.Kind == LabelKind.Violation))
            {
                var rules = l.Rules ?? new List<string>();
                foreach (var ak in rules.Where(r => r.StartsWith("ai:", StringComparison.Ordinal)))
                    if (ak.Substring(3) != l.DefectKind && ids.Contains(ak.Substring(3)))
                        add("warn", "ai-kind-mismatch", l, "kind is " + l.DefectKind + " but rules name " + ak);
                var dk = taxonomy.Get(l.DefectKind);
                if (dk == null || rules.Contains("*")) continue;
                HashSet<string> want;
                if (refLayers.TryGetValue(l.DefectKind, out want))
                {
                    var have = new HashSet<string>(rules.Select(RuleVocabulary.LayerOf));
                    if (!have.Contains("any") && !have.Overlaps(want))
                        add("warn", "kind-rule", l, l.DefectKind + " is labelled with " + string.Join("/", have.OrderBy(x => x).ToArray()) + " rules; DigitalBanking uses " + string.Join("/", want.OrderBy(x => x).ToArray()));
                }
                else if (dk.Detect == "structure" && !rules.Any(r => r.StartsWith("structure:", StringComparison.Ordinal)))
                    add("warn", "kind-rule", l, l.DefectKind + " is a structure check but no structure rule is accepted");
                else if (dk.Detect == "ai" && !rules.Any(r => r == "ai" || r.StartsWith("ai:", StringComparison.Ordinal)))
                    add("warn", "kind-rule", l, l.DefectKind + " is a judgment check but no ai rule is accepted");
            }

            // 6. status, source, split
            foreach (var l in labels)
            {
                if (!string.IsNullOrEmpty(l.Id) && l.Id.IndexOf('/') > 0 && l.Project != project.Name)
                    add("warn", "id-project", l, "id prefix '" + l.Project + "' is not the project id '" + project.Name + "'");
                foreach (var by in l.By ?? new List<string>())
                    if (Array.IndexOf(KnownSources, by) < 0 && !by.StartsWith("agent:", StringComparison.Ordinal)) add("warn", "unknown-source", l, "by: '" + by + "' is not a documented source");
                if (project.TestFiles != null && l.File != null)
                {
                    string want = project.TestFiles.Contains(l.File) ? "test" : "dev";
                    if (l.Split != want) add("error", "split-mismatch", l, "split is " + l.Split + " but project.json puts the file in " + want);
                }
                if (Normalizer.CanPromote(l)) add("info", "unpromoted-seeded", l, "seeded by an exact generator but still proposed (run normalize)");
                if (l.Kind == LabelKind.BlockComplete && l.IsVerified && !(l.By ?? new List<string>()).Any(b => b == "human" || b == "llm-a" || b == "llm-b"))
                    add("error", "block-complete-unconfirmed", l, "a verified block-complete label needs the two labellers or a human in by");
            }

            Stats(project, labels, rep);
            return rep;
        }

        private static string Clip(string s) { return s.Length <= 40 ? s : s.Substring(0, 37) + "..."; }

        private static bool SpecsIntersect(IEnumerable<string> a, IEnumerable<string> b)
        {
            foreach (var x in a ?? new List<string>()) foreach (var y in b ?? new List<string>()) if (RuleVocabulary.Intersects(x, y)) return true;
            return false;
        }

        private static Dictionary<string, HashSet<string>> ReferenceLayers(IList<Label> reference)
        {
            var d = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            if (reference == null) return d;
            foreach (var l in reference.Where(l => l.Kind == LabelKind.Violation && l.DefectKind != null))
            {
                HashSet<string> s;
                if (!d.TryGetValue(l.DefectKind, out s)) d[l.DefectKind] = s = new HashSet<string>();
                foreach (var r in l.Rules) { string layer = RuleVocabulary.LayerOf(r); if (layer != "any") s.Add(layer); }
            }
            return d;
        }

        // ---- coverage ---------------------------------------------------------------------------------------------------

        private static void Stats(CorpusProject project, IList<Label> labels, QaReport rep)
        {
            var s = rep.Stats;
            s.Labels = labels.Count;
            var files = project.ContentFiles();
            s.Files = files.Count(f => !CorpusProject.IsSnippet(f));
            s.Snippets = files.Count(CorpusProject.IsSnippet);
            var blockKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var f in files)
            {
                var bs = project.Blocks(f);
                if (bs.Count > 0) s.FilesWithBlocks++;
                foreach (var b in bs) blockKeys.Add(b.Id);
            }
            s.Blocks = blockKeys.Count;
            var labelled = new HashSet<string>(StringComparer.Ordinal);
            var completeBlocks = new HashSet<string>(StringComparer.Ordinal);
            var completeVerified = new HashSet<string>(StringComparer.Ordinal);
            var vFiles = new HashSet<string>(StringComparer.Ordinal);
            foreach (var l in labels)
            {
                string label = l.Kind + " " + l.Status;
                int n; s.ByLabelStatus[label] = s.ByLabelStatus.TryGetValue(label, out n) ? n + 1 : 1;
                string key = l.File + "|" + l.Block;
                if (blockKeys.Contains(key)) labelled.Add(key);
                if (l.IsVerified && l.Kind != LabelKind.Ambiguous && l.Kind != LabelKind.BlockComplete) s.ScoredLabels++;
                if (l.Kind == LabelKind.BlockComplete)
                {
                    completeBlocks.Add(key);
                    if (l.IsVerified) completeVerified.Add(key);
                }
                if (l.Kind != LabelKind.Violation) continue;
                s.Violations++;
                vFiles.Add(l.File);
                bool test = l.Split == "test";
                if (test) s.ViolationsTest++;
                if (l.IsVerified)
                {
                    s.ViolationsVerified++;
                    s.ViolationKinds[l.DefectKind ?? "(none)"] = (s.ViolationKinds.TryGetValue(l.DefectKind ?? "(none)", out n) ? n : 0) + 1;
                    if (test)
                    {
                        s.ViolationsVerifiedTest++;
                        s.ViolationKindsVerifiedTest[l.DefectKind ?? "(none)"] = (s.ViolationKindsVerifiedTest.TryGetValue(l.DefectKind ?? "(none)", out n) ? n : 0) + 1;
                    }
                }
            }
            s.BlocksLabelled = labelled.Count;
            s.BlocksComplete = completeBlocks.Count;
            s.BlocksCompleteVerified = completeVerified.Count;
            s.ViolationFiles = vFiles.Count;
            s.ViolationsPerTopic = s.Files == 0 ? 0 : Math.Round(s.Violations / (double)s.Files, 2);
            s.BlockCoverage = s.Blocks == 0 ? 0 : Math.Round(labelled.Count / (double)s.Blocks, 3);
            int testFiles = files.Count(f => labels.Any(l => l.File == f && l.Split == "test") || (project.TestFiles != null && project.TestFiles.Contains(f)));
            s.TestShare = files.Count == 0 ? 0 : Math.Round(testFiles / (double)files.Count, 3);
            foreach (var kv in s.ViolationKinds)
            {
                int t; s.ViolationKindsVerifiedTest.TryGetValue(kv.Key, out t);
                if (t < 3) s.ThinTestKinds.Add(kv.Key + " (" + t + " in test)");
            }
        }

        // ---- LABELS.md --------------------------------------------------------------------------------------------------

        /// <summary>The block written into a project's LABELS.md. Counts only, so the same labels give the same bytes.</summary>
        public static string Markdown(QaReport r)
        {
            var s = r.Stats;
            var sb = new StringBuilder();
            sb.Append(Md.Begin("qa")).Append('\n');
            sb.Append("## Label QA\n\n");
            sb.Append("Written by `FlareAI.Labeler qa --update-labels-md`; deterministic (no model, no linter). Rerun after any label change.\n\n");
            sb.Append("| | |\n|---|---|\n");
            sb.Append("| Result | ").Append(r.Count("error")).Append(" errors, ").Append(r.Count("warn")).Append(" warnings, ").Append(r.Count("info")).Append(" notes |\n");
            sb.Append("| Topics, snippets | ").Append(s.Files).Append(", ").Append(s.Snippets).Append(" |\n");
            sb.Append("| Blocks with text, with at least one label | ").Append(s.Blocks).Append(", ").Append(s.BlocksLabelled).Append(" (").Append(Pct(s.BlockCoverage)).Append(") |\n");
            sb.Append("| `block-complete` blocks, verified | ").Append(s.BlocksComplete).Append(", ").Append(s.BlocksCompleteVerified).Append(" |\n");
            sb.Append("| Violations, verified (scored) | ").Append(s.Violations).Append(", ").Append(s.ViolationsVerified).Append(" |\n");
            sb.Append("| Verified violations in `test` | ").Append(s.ViolationsVerifiedTest).Append(" |\n");
            sb.Append("| Violations per topic | ").Append(s.ViolationsPerTopic.ToString("0.00", CultureInfo.InvariantCulture)).Append(" |\n");
            sb.Append("| Scored labels (verified, not ambiguous or block-complete) | ").Append(s.ScoredLabels).Append(" |\n");
            sb.Append("| Share of files in `test` | ").Append(Pct(s.TestShare)).Append(" |\n\n");
            sb.Append("Labels by type and status: ").Append(string.Join(", ", s.ByLabelStatus.Select(kv => kv.Key + " " + kv.Value).ToArray())).Append(".\n\n");
            var codes = r.ByCode();
            if (codes.Count > 0)
                sb.Append("Findings by check: ").Append(string.Join(", ", codes.Select(kv => kv.Key + " " + kv.Value).ToArray())).Append(".\n\n");
            if (s.ThinTestKinds.Count > 0)
                sb.Append("Kinds with fewer than 3 verified violations in `test` (their per-kind recall there is noise): ").Append(string.Join(", ", s.ThinTestKinds.ToArray())).Append(".\n\n");
            sb.Append(Md.End("qa")).Append('\n');
            return sb.ToString();
        }

        private static string Pct(double v) { return (v * 100).ToString("0.0", CultureInfo.InvariantCulture) + "%"; }
    }
}
