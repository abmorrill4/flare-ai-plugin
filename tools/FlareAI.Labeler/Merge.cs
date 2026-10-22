using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FlareAI.Scoring;

namespace FlareAI.Labeler
{
    public sealed class MergeResult
    {
        public int Added, AddedVerified, AddedProposed, AddedDisputed, AddedAmbiguous, Confirmed;
        public int DecisionsApplied, DecisionsIgnored;
        public int Decoys, DecoysAccepted;
        public int Pending;
        public List<string> Notes = new List<string>();
        public double? DecoyAcceptance { get { return Decoys == 0 ? (double?)null : (double)DecoysAccepted / Decoys; } }
    }

    /// <summary>
    /// Folds the pass into the labels, in code, never by hand. Agreement of both labellers gives <c>verified</c>; one source gives
    /// <c>proposed</c>; a contradiction gives <c>disputed</c>; a decision turns a queue item into <c>verified</c> (accept),
    /// nothing (reject) or <c>ambiguous</c>. A decision on a test-split file counts only when a human made it.
    /// </summary>
    public static class Merge
    {
        public static MergeResult Apply(CorpusProject project, List<Label> labels, IList<Proposal> proposals, IList<QueueKey> keys, IList<Decision> decisions,
            Taxonomy taxonomy, IList<Label> reference, ISet<string> ambiguousKinds)
        {
            var res = new MergeResult();
            var byQid = keys.ToDictionary(k => k.Qid, k => k);
            var byProposal = new Dictionary<string, QueueKey>(StringComparer.Ordinal);
            foreach (var k in keys) if (k.Proposal != null) byProposal[k.Proposal] = k;
            var decided = decisions.GroupBy(d => d.Qid).ToDictionary(g => g.Key, g => g.Last());
            var rules = new KindRules(labels.Concat(reference ?? new List<Label>()), taxonomy);
            var items = new Dictionary<string, QueueItem>();
            int next = labels.Where(l => l.Id != null && l.Id.StartsWith(project.Name + "/M", StringComparison.Ordinal))
                .Select(l => { int n; return int.TryParse(l.Id.Substring(project.Name.Length + 2), NumberStyles.None, CultureInfo.InvariantCulture, out n) ? n : 0; })
                .DefaultIfEmpty(0).Max() + 1;

            // decoys: how often the adjudicator accepted a claim that was made up
            foreach (var k in keys.Where(k => k.Type == "decoy"))
            {
                Decision d;
                if (!decided.TryGetValue(k.Qid, out d)) continue;
                res.Decoys++;
                if (d.Value == "accept") res.DecoysAccepted++;
            }

            foreach (var p in proposals)
            {
                if (p.Source == "agreed" && p.Relation == "confirms")
                {
                    var existing = labels.FirstOrDefault(l => l.Id == p.Existing);
                    if (existing != null)
                    {
                        bool changed = false;
                        foreach (var by in p.By) if (!existing.By.Contains(by)) { existing.By.Add(by); changed = true; }
                        if (changed) res.Confirmed++;
                    }
                    continue;
                }
                string status; string labelType = LabelKind.Violation; var by2 = new List<string>(p.By); string kind = p.Kind;
                QueueKey key; byProposal.TryGetValue(p.Id, out key);
                if (p.Source == "agreed" && p.Relation == "new") status = "verified";
                else
                {
                    Decision d;
                    if (key == null || !decided.TryGetValue(key.Qid, out d))
                    {
                        if (p.Source == "single") status = "proposed";
                        else if (p.Source == "contradiction") status = "disputed";
                        else { res.Pending++; continue; }          // a conflict or an unmatched finding waits for a person
                    }
                    else
                    {
                        string split = project.TestFiles != null && project.TestFiles.Contains(p.File) ? "test" : "dev";
                        if (split == "test" && d.By != "human")
                        {
                            res.DecisionsIgnored++;
                            res.Notes.Add(d.Qid + ": a test-split decision needs by \"human\" (got \"" + d.By + "\"); left pending");
                            res.Pending++;
                            continue;
                        }
                        res.DecisionsApplied++;
                        if (d.Value == "reject") continue;
                        if (d.Value == "ambiguous") { labelType = LabelKind.Ambiguous; status = "verified"; }
                        else if (d.Value == "accept") status = "verified";
                        else { res.Notes.Add(d.Qid + ": unknown decision '" + d.Value + "'"); res.Pending++; continue; }
                        if (kind == null) kind = d.Kind;
                        if (!by2.Contains(d.By ?? "human")) by2.Add(d.By ?? "human");
                        if (labelType == LabelKind.Violation && kind == null) { res.Notes.Add(d.Qid + ": accepted without a kind"); res.Pending++; continue; }
                    }
                }
                if (labelType == LabelKind.Violation && status == "verified" && ambiguousKinds != null && ambiguousKinds.Contains(kind))
                {
                    labelType = LabelKind.Ambiguous;
                    res.Notes.Add(p.Id + ": kind " + kind + " has low agreement, written as ambiguous");
                }
                if (labels.Any(l => l.File == p.File && l.Block == p.Block && l.Quote == p.Quote && l.Prefix == p.Prefix && l.Suffix == p.Suffix && l.Kind == labelType && l.DefectKind == (labelType == LabelKind.Violation ? kind : null) && l.By.Intersect(by2).Any(b => b.StartsWith("llm-", StringComparison.Ordinal))))
                    continue;   // already merged by an earlier run
                var label = NewLabel(project, p, labelType, status, by2, kind, rules, next++);
                labels.Add(label);
                res.Added++;
                if (label.Kind == LabelKind.Ambiguous) res.AddedAmbiguous++;
                else if (label.Status == "verified") res.AddedVerified++;
                else if (label.Status == "proposed") res.AddedProposed++;
                else res.AddedDisputed++;
            }
            return res;
        }

        private static Label NewLabel(CorpusProject project, Proposal p, string type, string status, List<string> by, string kind, KindRules rules, int n)
        {
            var l = new Label
            {
                Id = project.Name + "/M" + n.ToString("D4", CultureInfo.InvariantCulture),
                File = p.File, Block = p.Block, Quote = p.Quote, Prefix = p.Prefix, Suffix = p.Suffix,
                Kind = type, Origin = "natural", Status = status, By = by,
                Split = project.TestFiles != null && project.TestFiles.Contains(p.File) ? "test" : "dev"
            };
            if (type == LabelKind.Violation)
            {
                l.DefectKind = kind;
                l.Rules = rules.For(kind);
                if (!string.IsNullOrEmpty(p.Fix)) l.Fix = p.Fix;
            }
            else { l.Rules = new List<string> { "*" }; l.Reason = "labellers did not agree on a single reading"; }
            var bt = project.Block(p.File, p.Block);
            if (bt != null && p.Quote != null)
            {
                var span = Anchor.Resolve(bt.Text, p.Quote, p.Prefix, p.Suffix);
                if (span.Resolved)
                    l.Context = new LabelContext { Line = Xhtml.LineContext(bt.Element), Span = Xhtml.SpanContext(bt, span.Start, span.End, p.File, false) };
            }
            return l;
        }
    }

    /// <summary>
    /// The rules a new violation of a kind accepts: what labels of that kind already carry, the most common set
    /// (the DigitalBanking labels are part of the input). A kind with no label yet falls back to its detect layer.
    /// </summary>
    public sealed class KindRules
    {
        private readonly Dictionary<string, List<string>> _byKind = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        private readonly Taxonomy _taxonomy;

        public KindRules(IEnumerable<Label> labels, Taxonomy taxonomy)
        {
            _taxonomy = taxonomy;
            var counts = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
            var sets = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var l in labels.Where(l => l.Kind == LabelKind.Violation && l.DefectKind != null && l.Rules != null && l.Rules.Count > 0))
            {
                string sig = string.Join("\n", l.Rules.ToArray());
                Dictionary<string, int> c;
                if (!counts.TryGetValue(l.DefectKind, out c)) counts[l.DefectKind] = c = new Dictionary<string, int>(StringComparer.Ordinal);
                int n; c[sig] = c.TryGetValue(sig, out n) ? n + 1 : 1;
                sets[sig] = l.Rules;
            }
            foreach (var kv in counts)
                _byKind[kv.Key] = sets[kv.Value.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal).First().Key].ToList();
        }

        public List<string> For(string kind)
        {
            List<string> r;
            if (_byKind.TryGetValue(kind, out r)) return r.ToList();
            var dk = _taxonomy == null ? null : _taxonomy.Get(kind);
            if (dk != null && dk.Detect == "ai") return new List<string> { "ai:" + kind };
            return new List<string> { "lint" };
        }
    }
}
