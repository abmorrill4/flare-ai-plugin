using System;
using System.Collections.Generic;
using System.Linq;
using FlareAI.Scoring;

namespace FlareAI.Labeler
{
    /// <summary>
    /// Plugin findings that no label speaks about, in blocks that carry a <c>block-complete</c> label. Those are the findings whose
    /// truth the labels cannot tell yet; they go to the adjudication queue without their rule or origin.
    /// </summary>
    public static class FindingSource
    {
        public static List<Candidate> Unmatched(CorpusProject project, IList<Label> labels, IList<ScoreFinding> findings)
        {
            var completeBlocks = new HashSet<string>(labels.Where(l => l.Kind == LabelKind.BlockComplete).Select(l => l.File + "|" + l.Block), StringComparer.Ordinal);
            var report = Scorer.Score(labels, findings, new ScoreOptions { TextSource = project.Text });
            var list = new List<Candidate>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in report.Findings.Where(r => r.Outcome == Outcome.Unscored))
            {
                var f = r.Finding;
                if (string.IsNullOrEmpty(f.File)) continue;
                string block = f.Block; int start = 0, end = 0;
                string text = null;
                if (block != null && f.Start.HasValue && f.End.HasValue) { text = project.Text.GetText(f.File, block); start = f.Start.Value; end = f.End.Value; }
                else if (!string.IsNullOrEmpty(f.Excerpt))
                {
                    // locate the excerpt: the one block of the file that contains it exactly once
                    var hits = project.Blocks(f.File).Where(b => b.Text.IndexOf(f.Excerpt, StringComparison.Ordinal) >= 0).Take(2).ToList();
                    if (hits.Count != 1 || hits[0].Text.IndexOf(f.Excerpt, StringComparison.Ordinal) != hits[0].Text.LastIndexOf(f.Excerpt, StringComparison.Ordinal)) continue;
                    block = hits[0].XPath; text = hits[0].Text; start = text.IndexOf(f.Excerpt, StringComparison.Ordinal); end = start + f.Excerpt.Length;
                }
                if (text == null || end <= start || end > text.Length || !completeBlocks.Contains(f.File + "|" + block)) continue;
                if (!seen.Add(f.File + "|" + block + "|" + start + "|" + end)) continue;
                string q, pre, suf;
                Anchor.MakeSelector(text, start, end, 24, out q, out pre, out suf);
                list.Add(new Candidate { File = f.File, XPath = block, Start = start, End = end, Quote = q, Prefix = pre, Suffix = suf });
            }
            return list;
        }
    }
}
