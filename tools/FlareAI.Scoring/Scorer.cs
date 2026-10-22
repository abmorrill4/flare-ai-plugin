using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace FlareAI.Scoring
{
    public sealed class ScoreOptions
    {
        /// <summary>Resolves text-quote selectors to offsets, so findings with block + offsets are compared by position. Optional.</summary>
        public IBlockTextSource TextSource;
        /// <summary>Score only this split (dev or test); null scores everything.</summary>
        public string Split;
        /// <summary>Treat <c>proposed</c> labels as verified. Default false: only verified labels score; proposed and disputed ones count as ambiguous.</summary>
        public bool IncludeProposed;
    }

    public enum Outcome { TruePositive, FalsePositive, WrongRule, Ambiguous, Unscored }

    public enum Detection { Detected, Missed, WrongRuleOnly }

    public sealed class FindingResult
    {
        public ScoreFinding Finding;
        public Outcome Outcome;
        /// <summary>The label that decided the outcome (violation for TP/wrong rule, clean/exempt/block-complete for FP); null if unscored.</summary>
        public Label Label;
        /// <summary>Why a false positive is one: "clean", "exempt" or "block-complete".</summary>
        public string Reason;
    }

    public sealed class LabelResult
    {
        public Label Label;
        public Detection Detection;
        /// <summary>Selector could not be resolved against the block text (only reported when a text source is given).</summary>
        public bool Unanchored;
    }

    /// <summary>Counts for one group. Finding counts: Tp, Fp, WrongRule, Ambiguous, Unscored. Label counts: Support, Detected, Fn, WrongRuleOnly.</summary>
    public sealed class Tally
    {
        public int Tp, Fp, WrongRule, Ambiguous, Unscored;
        public int Support, Detected, Fn, WrongRuleOnly;

        public int ScoredFindings { get { return Tp + Fp + WrongRule; } }
        /// <summary>Tp / (Tp + Fp + WrongRule); 1 when nothing was scored.</summary>
        public double Precision { get { return ScoredFindings == 0 ? 1.0 : (double)Tp / ScoredFindings; } }
        /// <summary>Detected / Support; 1 when there are no verified violations.</summary>
        public double Recall { get { return Support == 0 ? 1.0 : (double)Detected / Support; } }
        public double F1 { get { double p = Precision, r = Recall; return p + r == 0 ? 0 : 2 * p * r / (p + r); } }

        public JObject ToJson()
        {
            return new JObject
            {
                ["tp"] = Tp, ["fp"] = Fp, ["wrongRule"] = WrongRule, ["fn"] = Fn, ["wrongRuleOnly"] = WrongRuleOnly,
                ["ambiguous"] = Ambiguous, ["unscored"] = Unscored, ["support"] = Support, ["detected"] = Detected,
                ["precision"] = Math.Round(Precision, 4), ["recall"] = Math.Round(Recall, 4), ["f1"] = Math.Round(F1, 4)
            };
        }
    }

    public sealed class ScoreReport
    {
        public const string Rule = "rule", Layer = "layer", ContextLine = "context.line", ContextSpan = "context.span",
            Project = "project", Split = "split", Kind = "kind";
        public static readonly string[] Dimensions = { Rule, Layer, ContextLine, ContextSpan, Project, Split, Kind };

        public Tally Overall = new Tally();
        public Dictionary<string, SortedDictionary<string, Tally>> By = new Dictionary<string, SortedDictionary<string, Tally>>();
        public List<FindingResult> Findings = new List<FindingResult>();
        public List<LabelResult> Labels = new List<LabelResult>();
        public int UnanchoredLabels;
        public int LabelsScored;
        public int FindingCount { get { return Findings.Count; } }

        /// <summary>Scored findings / (all findings - ambiguous). The share of findings the labels can judge; tracked so it can only go up.</summary>
        public double Coverage
        {
            get
            {
                int denom = Findings.Count - Overall.Ambiguous;
                return denom <= 0 ? 1.0 : (double)Overall.ScoredFindings / denom;
            }
        }

        public ScoreReport()
        {
            foreach (var d in Dimensions) By[d] = new SortedDictionary<string, Tally>(StringComparer.Ordinal);
        }

        public Tally Get(string dimension, string key)
        {
            Tally t;
            return By[dimension].TryGetValue(key, out t) ? t : new Tally();
        }

        public JObject ToJson()
        {
            var o = new JObject();
            o["overall"] = Overall.ToJson();
            o["findings"] = Findings.Count;
            o["labelsScored"] = LabelsScored;
            o["unanchoredLabels"] = UnanchoredLabels;
            o["coverage"] = Math.Round(Coverage, 4);
            foreach (var d in Dimensions)
            {
                var g = new JObject();
                foreach (var kv in By[d]) g[kv.Key] = kv.Value.ToJson();
                o[d] = g;
            }
            return o;
        }
    }

    /// <summary>
    /// Scores findings against span labels. Outcome of a finding, first rule that applies:
    /// <list type="number">
    /// <item>TP: overlaps a verified violation and its rule is in the label's <c>rules</c>.</item>
    /// <item>FP: overlaps a clean or exempt label whose <c>rules</c> include the finding's rule.</item>
    /// <item>Wrong rule: overlaps a verified violation but its rule is not accepted there (right text, other rule).</item>
    /// <item>Ambiguous: overlaps only ambiguous, disputed or (by default) proposed labels. Excluded from precision.</item>
    /// <item>FP: inside a block-complete block (rules match) but overlapping no violation.</item>
    /// <item>Unscored: no label speaks about it. Counted, and reported through <see cref="ScoreReport.Coverage"/>.</item>
    /// </list>
    /// A verified violation is Detected when a TP finding overlaps it, WrongRuleOnly when only wrong-rule findings do, else Fn.
    /// </summary>
    public static class Scorer
    {
        private enum Ov { None, Yes, RuleOnly }

        public static ScoreReport Score(IEnumerable<Label> allLabels, IEnumerable<ScoreFinding> allFindings, ScoreOptions options = null)
        {
            options = options ?? new ScoreOptions();
            var report = new ScoreReport();

            var fileSplit = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var labels = new List<Label>();
            foreach (var l in allLabels)
            {
                if (l == null) continue;
                fileSplit[NormFile(l.File)] = l.Split;
                if (options.Split != null && !string.Equals(l.Split, options.Split, StringComparison.Ordinal)) continue;
                labels.Add(l);
            }
            var byFile = new Dictionary<string, List<Label>>(StringComparer.OrdinalIgnoreCase);
            foreach (var l in labels)
            {
                List<Label> list;
                string k = NormFile(l.File);
                if (!byFile.TryGetValue(k, out list)) byFile[k] = list = new List<Label>();
                list.Add(l);
            }

            // resolve selectors once
            var spans = new Dictionary<Label, ResolvedSpan>();
            var unanchored = new HashSet<Label>();
            if (options.TextSource != null)
            {
                foreach (var l in labels)
                {
                    if (l.Quote == null || l.Block == null) continue;
                    var rs = Anchor.Resolve(options.TextSource.GetText(l.File, l.Block), l.Quote, l.Prefix, l.Suffix);
                    spans[l] = rs;
                    if (!rs.Resolved) unanchored.Add(l);
                }
            }

            var accepted = new Dictionary<Label, List<ScoreFinding>>();
            var wrong = new HashSet<Label>();
            var detectedBy = new Dictionary<Label, ScoreFinding>();

            foreach (var f in allFindings)
            {
                string fsplit;
                string fkey = NormFile(f.File);
                fileSplit.TryGetValue(fkey, out fsplit);
                if (options.Split != null && !string.Equals(fsplit, options.Split, StringComparison.Ordinal)) continue;

                List<Label> inFile;
                if (!byFile.TryGetValue(fkey, out inFile)) inFile = new List<Label>();

                var res = new FindingResult { Finding = f, Outcome = Outcome.Unscored };
                var accViol = new List<Label>();
                var wrongViol = new List<Label>();
                Label fpClean = null, ambig = null, blockFp = null;
                foreach (var l in inFile)
                {
                    bool counts = l.IsVerified || (options.IncludeProposed && string.Equals(l.Status, "proposed", StringComparison.Ordinal));
                    if (l.Kind == LabelKind.BlockComplete)
                    {
                        if (counts && blockFp == null && f.Block != null && l.Block != null && PathRelated(f.Block, l.Block) && RuleSpec.Any(l.Rules, f)) blockFp = l;
                        continue;
                    }
                    var ov = Overlap(f, l, spans);
                    if (ov == Ov.None) continue;
                    bool ruleOk = RuleSpec.Any(l.Rules, f);
                    if (ov == Ov.RuleOnly && !ruleOk) continue;
                    if (!counts || l.Kind == LabelKind.Ambiguous)
                    {
                        if (ov == Ov.Yes && ambig == null) ambig = l;
                        continue;
                    }
                    switch (l.Kind)
                    {
                        case LabelKind.Violation:
                            if (ruleOk) accViol.Add(l); else wrongViol.Add(l);
                            break;
                        case LabelKind.Clean:
                        case LabelKind.Exempt:
                            if (ruleOk && fpClean == null) fpClean = l;
                            break;
                    }
                }

                if (accViol.Count > 0)
                {
                    res.Outcome = Outcome.TruePositive;
                    res.Label = accViol[0];
                    foreach (var l in accViol)
                    {
                        List<ScoreFinding> list;
                        if (!accepted.TryGetValue(l, out list)) accepted[l] = list = new List<ScoreFinding>();
                        list.Add(f);
                    }
                }
                else if (fpClean != null) { res.Outcome = Outcome.FalsePositive; res.Label = fpClean; res.Reason = fpClean.Kind; }
                else if (wrongViol.Count > 0) { res.Outcome = Outcome.WrongRule; res.Label = wrongViol[0]; }
                else if (ambig != null) { res.Outcome = Outcome.Ambiguous; res.Label = ambig; }
                else if (blockFp != null) { res.Outcome = Outcome.FalsePositive; res.Label = blockFp; res.Reason = LabelKind.BlockComplete; }
                foreach (var l in wrongViol) wrong.Add(l);
                report.Findings.Add(res);

                Count(report, res, fsplit);
            }

            // labels: verified violations
            foreach (var l in labels)
            {
                if (l.Kind != LabelKind.Violation) continue;
                if (!(l.IsVerified || (options.IncludeProposed && string.Equals(l.Status, "proposed", StringComparison.Ordinal)))) continue;
                var lr = new LabelResult { Label = l, Unanchored = unanchored.Contains(l) };
                List<ScoreFinding> hits;
                string ruleKey;
                if (accepted.TryGetValue(l, out hits)) { lr.Detection = Detection.Detected; ruleKey = hits[0].RuleId ?? ("(" + hits[0].Origin + ")"); }
                else if (wrong.Contains(l)) { lr.Detection = Detection.WrongRuleOnly; ruleKey = l.Rules.Count > 0 ? l.Rules[0] : "(none)"; }
                else { lr.Detection = Detection.Missed; ruleKey = l.Rules.Count > 0 ? l.Rules[0] : "(none)"; }
                report.Labels.Add(lr);
                report.LabelsScored++;
                if (lr.Unanchored) report.UnanchoredLabels++;
                string layer = hits != null ? hits[0].Layer : (l.Rules.Count > 0 ? RuleSpec.LayerOf(l.Rules[0]) : "other");
                foreach (var t in Tallies(report, ruleKey, layer, l, l.Split)) CountLabel(t, lr.Detection);
                CountLabel(report.Overall, lr.Detection);
            }
            return report;
        }


        private static void Count(ScoreReport report, FindingResult res, string fileSplit)
        {
            var f = res.Finding;
            var l = res.Label;
            string rule = f.RuleId ?? ("(" + f.Origin + ")");
            string project = l != null && l.Project.Length > 0 ? l.Project : (f.Project ?? "");
            string split = l != null ? l.Split : (fileSplit ?? "");
            var targets = Tallies(report, rule, f.Layer, l, split, project);
            targets.Add(report.Overall);
            foreach (var t in targets)
            {
                switch (res.Outcome)
                {
                    case Outcome.TruePositive: t.Tp++; break;
                    case Outcome.FalsePositive: t.Fp++; break;
                    case Outcome.WrongRule: t.WrongRule++; break;
                    case Outcome.Ambiguous: t.Ambiguous++; break;
                    default: t.Unscored++; break;
                }
            }
        }

        private static void CountLabel(Tally t, Detection d)
        {
            t.Support++;
            if (d == Detection.Detected) t.Detected++;
            else if (d == Detection.WrongRuleOnly) { t.WrongRuleOnly++; }
            else t.Fn++;
        }

        /// <summary>Tallies a result belongs to, one per dimension key (multi-valued context dimensions add several).</summary>
        private static List<Tally> Tallies(ScoreReport r, string rule, string layer, Label l, string split, string project = null)
        {
            var list = new List<Tally>();
            if (project == null) project = l != null ? l.Project : "";
            list.Add(Slot(r, ScoreReport.Rule, rule));
            list.Add(Slot(r, ScoreReport.Layer, layer));
            list.Add(Slot(r, ScoreReport.Project, project.Length == 0 ? "(none)" : project));
            list.Add(Slot(r, ScoreReport.Split, string.IsNullOrEmpty(split) ? "(none)" : split));
            list.Add(Slot(r, ScoreReport.Kind, l != null && l.DefectKind != null ? l.DefectKind : "(none)"));
            var lines = l != null && l.Context != null ? l.Context.Line : null;
            var spans = l != null && l.Context != null ? l.Context.Span : null;
            if (lines == null || lines.Count == 0) list.Add(Slot(r, ScoreReport.ContextLine, "(none)"));
            else foreach (var c in lines.Distinct()) list.Add(Slot(r, ScoreReport.ContextLine, c));
            if (spans == null || spans.Count == 0) list.Add(Slot(r, ScoreReport.ContextSpan, "(none)"));
            else foreach (var c in spans.Distinct()) list.Add(Slot(r, ScoreReport.ContextSpan, c));
            return list;
        }

        private static Tally Slot(ScoreReport r, string dim, string key)
        {
            Tally t;
            if (!r.By[dim].TryGetValue(key, out t)) r.By[dim][key] = t = new Tally();
            return t;
        }

        // ---- overlap -----------------------------------------------------------------------------------------------------

        private static Ov Overlap(ScoreFinding f, Label l, Dictionary<Label, ResolvedSpan> spans)
        {
            if (l.Block != null && f.Block != null)
            {
                if (l.Quote == null) return PathRelated(f.Block, l.Block) ? Ov.Yes : Ov.None;
                if (!string.Equals(f.Block, l.Block, StringComparison.Ordinal)) return Ov.None;
                ResolvedSpan rs;
                if (f.Start.HasValue && f.End.HasValue && spans.TryGetValue(l, out rs) && rs.Resolved)
                {
                    int fs = f.Start.Value, fe = Math.Max(f.End.Value, fs + 1);
                    return fs < rs.End && rs.Start < fe ? Ov.Yes : Ov.None;
                }
                if (string.IsNullOrEmpty(f.Excerpt)) return Ov.Yes; // a whole-block finding touches everything in the block
                return TextOverlap(f.Excerpt, l.Quote) ? Ov.Yes : Ov.None;
            }
            if (l.Quote == null) return Ov.RuleOnly;
            return TextOverlap(f.Excerpt, l.Quote) ? Ov.Yes : Ov.None;
        }

        /// <summary>Equal, or one contains the other (the shorter one at least two characters with a letter or digit, or a single punctuation mark), ignoring case and whitespace runs.</summary>
        public static bool TextOverlap(string a, string b)
        {
            string na = Norm(a), nb = Norm(b);
            if (na.Length == 0 || nb.Length == 0) return false;
            if (na == nb) return true;
            string shorter = na.Length <= nb.Length ? na : nb, longer = na.Length <= nb.Length ? nb : na;
            // A lone punctuation mark ("!") may be inside a longer label ("go!"); a lone letter or digit may not.
            if (shorter.Length < 2 && char.IsLetterOrDigit(shorter[0])) return false;
            if (!shorter.Any(c => char.IsLetterOrDigit(c)) && shorter.Length > 1) return false;
            return longer.IndexOf(shorter, StringComparison.Ordinal) >= 0;
        }

        private static string Norm(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                if (char.IsWhiteSpace(c)) { if (sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(' '); }
                else sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString().Trim();
        }

        private static bool PathRelated(string a, string b)
        {
            if (a == b) return true;
            return a.StartsWith(b + "/", StringComparison.Ordinal) || b.StartsWith(a + "/", StringComparison.Ordinal);
        }

        private static string NormFile(string f)
        {
            if (f == null) return "";
            f = f.Replace('\\', '/');
            while (f.StartsWith("./", StringComparison.Ordinal)) f = f.Substring(2);
            return f;
        }
    }
}
