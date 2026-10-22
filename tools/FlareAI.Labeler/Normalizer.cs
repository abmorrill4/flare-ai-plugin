using System;
using System.Collections.Generic;
using System.Linq;
using FlareAI.Scoring;

namespace FlareAI.Labeler
{
    public sealed class NormalizeResult
    {
        public int Labels;
        public int RulesRewritten;
        public int Promoted;
        public int PromotionBlocked;
        /// <summary>Distinct rule rewrites (<c>old -&gt; new</c>) and how many labels each touched.</summary>
        public Dictionary<string, int> Rewrites = new Dictionary<string, int>(StringComparer.Ordinal);
        public List<string> Notes = new List<string>();
    }

    /// <summary>
    /// Brings a project's labels to the schema's vocabulary: generator rule hints become scorer rule ids, and a seeded
    /// violation that its generator wrote exactly is <c>verified</c> (the status convention is in docs/testing/CORPUS.md).
    /// Idempotent: a second run changes nothing.
    /// </summary>
    public static class Normalizer
    {
        public static NormalizeResult Run(IList<Label> labels, Taxonomy taxonomy, CorpusProject project)
        {
            var res = new NormalizeResult { Labels = labels.Count };
            var ai = taxonomy == null ? null : taxonomy.Ids;
            foreach (var l in labels)
            {
                var next = new List<string>();
                foreach (var spec in l.Rules ?? new List<string>())
                    foreach (var n in RuleVocabulary.Normalize(spec, ai))
                        if (!next.Contains(n)) next.Add(n);
                if (!next.SequenceEqual(l.Rules ?? new List<string>()))
                {
                    res.RulesRewritten++;
                    foreach (var spec in l.Rules)
                    {
                        if (next.Contains(spec)) continue;
                        string key = spec + " -> " + string.Join(", ", RuleVocabulary.Normalize(spec, ai).ToArray());
                        int n;
                        res.Rewrites[key] = res.Rewrites.TryGetValue(key, out n) ? n + 1 : 1;
                    }
                    l.Rules = next;
                }
                if (CanPromote(l))
                {
                    if (project == null || Anchors(l, project)) { l.Status = "verified"; res.Promoted++; }
                    else
                    {
                        res.PromotionBlocked++;
                        res.Notes.Add(l.Id + ": left proposed, its quote does not anchor");
                    }
                }
            }
            return res;
        }

        /// <summary>
        /// A violation whose generator inserted the defect knows exactly where it is, so existence and place are
        /// <c>verified</c>. What the generator cannot know ("still reads naturally", "nothing else is wrong in the block") is the
        /// business of <c>block-complete</c>, which stays <c>proposed</c> until the two-labeller pass.
        /// </summary>
        public static bool CanPromote(Label l)
        {
            return l.Kind == LabelKind.Violation && l.Origin == "seeded" && l.Status == "proposed" && l.By != null && l.By.Contains("generator");
        }

        private static bool Anchors(Label l, CorpusProject project)
        {
            string text = project.Text.GetText(l.File, l.Block);
            if (text == null) return false;
            if (l.Quote == null) return true;
            var r = Anchor.Resolve(text, l.Quote, l.Prefix, l.Suffix);
            return r.Resolved && !r.Ambiguous;
        }
    }
}
