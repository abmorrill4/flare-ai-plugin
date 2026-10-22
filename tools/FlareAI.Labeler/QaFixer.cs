using System;
using System.Collections.Generic;
using System.Linq;
using FlareAI.Scoring;

namespace FlareAI.Labeler
{
    /// <summary>
    /// The mechanical repairs of label QA, and only those. Everything else QA reports needs a person.
    /// <list type="number">
    /// <item>An exact duplicate (same block, span, label, kind, status) loses its later copy.</item>
    /// <item>Two violations of the same kind on one span: the natural one (a markup fact read after the seed was inserted) loses to the seeded one, which carries the seed bookkeeping.</item>
    /// <item>A clean or exempt label read from markup or a pattern (<c>source-markup</c>, <c>pattern</c>, <c>dom-check</c>) on exactly the span of a verified seeded violation is dropped:
    /// the injector put the defect into text the heuristic took for a UI label, so the exact generator fact wins over the heuristic.</item>
    /// </list>
    /// </summary>
    public static class QaFixer
    {
        private static readonly string[] Heuristic = { "source-markup", "pattern", "dom-check" };

        public static List<string> Apply(CorpusProject project, List<Label> labels)
        {
            var notes = new List<string>();
            var remove = new HashSet<Label>();
            foreach (var g in labels.Where(l => l.File != null && l.Block != null).GroupBy(l => l.File + "|" + l.Block))
            {
                var items = g.ToList();
                string text = project.Text.GetText(items[0].File, items[0].Block);
                if (text == null) continue;
                var spans = items.ToDictionary(l => l, l => Anchor.Resolve(text, l.Quote, l.Prefix, l.Suffix));
                var live = items.ToList();
                foreach (var a in live.Where(l => l.Kind == LabelKind.Violation))
                    foreach (var b in live.Where(l => l.Kind == LabelKind.Violation && !ReferenceEquals(l, a) && l.DefectKind == a.DefectKind))
                    {
                        if (remove.Contains(a) || remove.Contains(b)) continue;
                        var sa = spans[a]; var sb = spans[b];
                        if (!sa.Resolved || !sb.Resolved) continue;
                        if (a.Quote != null && b.Quote != null && !(sa.Start < sb.End && sb.Start < sa.End)) continue;
                        if (a.Origin == "seeded" && b.Origin == "natural") { remove.Add(b); notes.Add(b.Id + ": removed, natural duplicate of seeded " + a.Id + " (" + a.DefectKind + ")"); }
                    }
                var seen = new Dictionary<string, Label>(StringComparer.Ordinal);
                foreach (var l in items.Where(x => !remove.Contains(x)))
                {
                    string key = l.Kind + "|" + l.DefectKind + "|" + l.Quote + "|" + l.Prefix + "|" + l.Suffix + "|" + l.Status;
                    Label first;
                    if (seen.TryGetValue(key, out first)) { remove.Add(l); notes.Add(l.Id + ": removed, exact duplicate of " + first.Id); }
                    else seen[key] = l;
                }
                foreach (var v in live.Where(l => l.Kind == LabelKind.Violation && l.Origin == "seeded" && l.IsVerified && l.Quote != null))
                    foreach (var c in live.Where(l => (l.Kind == LabelKind.Clean || l.Kind == LabelKind.Exempt) && l.Quote != null && l.By.All(b => Heuristic.Contains(b))))
                    {
                        if (remove.Contains(v) || remove.Contains(c)) continue;
                        var sv = spans[v]; var sc = spans[c];
                        if (!sv.Resolved || !sc.Resolved || sv.Start != sc.Start || sv.End != sc.End) continue;
                        remove.Add(c);
                        notes.Add(c.Id + ": removed, a " + c.Kind + " reading by " + string.Join("/", c.By.ToArray()) + " on the exact span of seeded violation " + v.Id + " (" + v.DefectKind + ")");
                    }
            }
            labels.RemoveAll(l => remove.Contains(l));
            return notes;
        }
    }
}
