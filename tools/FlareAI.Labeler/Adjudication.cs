using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FlareAI.Scoring;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlareAI.Labeler
{
    /// <summary>A label the pass would add (or an existing label it confirms), with where it came from.</summary>
    public sealed class Proposal
    {
        /// <summary>agreed | single | contradiction | conflict | finding</summary>
        [JsonProperty("source")] public string Source;
        /// <summary>For agreed proposals: new, or confirms (the existing label id in <see cref="Existing"/>).</summary>
        [JsonProperty("relation")] public string Relation;
        [JsonProperty("existing", NullValueHandling = NullValueHandling.Ignore)] public string Existing;
        [JsonProperty("id")] public string Id;
        [JsonProperty("file")] public string File;
        [JsonProperty("block")] public string Block;
        [JsonProperty("quote")] public string Quote;
        [JsonProperty("prefix")] public string Prefix;
        [JsonProperty("suffix")] public string Suffix;
        [JsonProperty("kind", NullValueHandling = NullValueHandling.Ignore)] public string Kind;
        [JsonProperty("fix", NullValueHandling = NullValueHandling.Ignore)] public string Fix;
        [JsonProperty("by")] public List<string> By = new List<string>();

        public static string MakeId(string file, string block, int start, int end, string kind, string source)
        {
            return LabelPlan.Sha(file + "|" + block + "|" + start + "|" + end + "|" + kind + "|" + source).Substring(0, 10);
        }
    }

    public sealed class QueueItem
    {
        [JsonProperty("qid")] public string Qid;
        [JsonProperty("file")] public string File;
        [JsonProperty("block")] public string Block;
        [JsonProperty("blockText")] public string BlockText;
        [JsonProperty("quote")] public string Quote;
        [JsonProperty("prefix")] public string Prefix;
        [JsonProperty("suffix")] public string Suffix;
        [JsonProperty("kind", NullValueHandling = NullValueHandling.Ignore)] public string Kind;
        [JsonProperty("fix", NullValueHandling = NullValueHandling.Ignore)] public string Fix;
        [JsonProperty("split")] public string Split;
    }

    /// <summary>What a queue id really is. Kept in a separate file so the adjudicator sees no provenance.</summary>
    public sealed class QueueKey
    {
        [JsonProperty("qid")] public string Qid;
        /// <summary>decoy | single | contradiction | conflict | finding</summary>
        [JsonProperty("type")] public string Type;
        [JsonProperty("proposal", NullValueHandling = NullValueHandling.Ignore)] public string Proposal;
    }

    public sealed class Decision
    {
        [JsonProperty("qid")] public string Qid;
        /// <summary>accept | reject | ambiguous</summary>
        [JsonProperty("decision")] public string Value;
        [JsonProperty("by")] public string By;
        /// <summary>Required when accepting an item that has no kind (an unmatched finding).</summary>
        [JsonProperty("kind", NullValueHandling = NullValueHandling.Ignore)] public string Kind;
        [JsonProperty("note", NullValueHandling = NullValueHandling.Ignore)] public string Note;
    }

    public sealed class QueueBuild
    {
        public List<Proposal> Proposals = new List<Proposal>();
        public List<QueueItem> Items = new List<QueueItem>();
        public List<QueueKey> Keys = new List<QueueKey>();
        public int Decoys, Covered, ConflictsWithExisting;
        public int ConfirmedExisting, SeededNotFound;
    }

    public static class Adjudication
    {
        public const double DecoyShare = 0.20;

        /// <summary>
        /// Turns the two labellers' spans into proposals, compares them with the labels already there, and builds the adjudication queue:
        /// every single-source span, every contradiction, every conflict with an existing label, and (when given) every plugin finding no
        /// label speaks about, mixed with decoys (spans nobody reported) so that decoys are one fifth of the queue.
        /// </summary>
        public static QueueBuild Build(CorpusProject project, IList<Label> labels, AgreementResult ag, IEnumerable<Candidate> unmatchedFindings, int seed)
        {
            var qb = new QueueBuild();
            var byBlock = labels.Where(l => l.File != null && l.Block != null).GroupBy(l => l.File + "|" + l.Block).ToDictionary(g => g.Key, g => g.ToList());
            var queueProposals = new List<Proposal>();

            foreach (var p in ag.Agreed)
            {
                var span = (p.B.End - p.B.Start) < (p.A.End - p.A.Start) ? p.B : p.A;
                var rel = Relate(project, byBlock, span, p.A.Kind);
                var prop = Make("agreed", span, p.A.Kind, new List<string> { "llm-a", "llm-b" });
                prop.Fix = p.A.Fix ?? p.B.Fix;
                if (rel.Item1 == "confirms") { prop.Relation = "confirms"; prop.Existing = rel.Item2; qb.ConfirmedExisting++; }
                else if (rel.Item1 == "ambiguous") { qb.Covered++; continue; }
                else if (rel.Item1 == "conflict") { prop.Source = "conflict"; prop.Relation = "conflict"; prop.Existing = rel.Item2; qb.ConflictsWithExisting++; queueProposals.Add(prop); }
                else prop.Relation = "new";
                qb.Proposals.Add(prop);
            }
            foreach (var p in ag.Contradictions)
            {
                foreach (var c in new[] { p.A, p.B })
                {
                    var prop = Make("contradiction", c, c.Kind, new List<string> { c.Labeller == "a" ? "llm-a" : "llm-b" });
                    prop.Fix = c.Fix;
                    qb.Proposals.Add(prop); queueProposals.Add(prop);
                }
            }
            foreach (var c in ag.OnlyA.Concat(ag.OnlyB))
            {
                var rel = Relate(project, byBlock, c, c.Kind);
                if (rel.Item1 != "none") { qb.Covered++; if (rel.Item1 == "confirms") qb.ConfirmedExisting++; continue; }   // an existing label already speaks about this span
                var prop = Make("single", c, c.Kind, new List<string> { c.Labeller == "a" ? "llm-a" : "llm-b" });
                prop.Fix = c.Fix;
                qb.Proposals.Add(prop); queueProposals.Add(prop);
            }
            foreach (var c in unmatchedFindings ?? new Candidate[0])
            {
                var prop = Make("finding", c, c.Kind, new List<string>());
                qb.Proposals.Add(prop); queueProposals.Add(prop);
            }

            // seeded labels the labellers did not report: a measure of the labellers and of the seeds, not a label change
            var reported = new HashSet<string>(ag.Agreed.Select(p => p.A).Concat(ag.Agreed.Select(p => p.B)).Concat(ag.OnlyA).Concat(ag.OnlyB).Concat(ag.Contradictions.SelectMany(p => new[] { p.A, p.B })).Select(c => c.BlockKey));
            qb.SeededNotFound = labels.Count(l => l.Kind == LabelKind.Violation && l.Origin == "seeded" && l.Quote != null && ag.BlocksBoth.Contains(l.File + "|" + l.Block) && !reported.Contains(l.File + "|" + l.Block));

            // items
            var items = new List<Tuple<QueueItem, QueueKey>>();
            foreach (var prop in queueProposals)
            {
                string text = project.Text.GetText(prop.File, prop.Block) ?? "";
                items.Add(Tuple.Create(
                    new QueueItem { File = prop.File, Block = prop.Block, BlockText = text, Quote = prop.Quote, Prefix = prop.Prefix, Suffix = prop.Suffix, Kind = prop.Kind, Fix = prop.Fix, Split = SplitOf(project, prop.File) },
                    new QueueKey { Type = prop.Source == "agreed" ? "conflict" : prop.Source, Proposal = prop.Id }));
            }
            int decoys = items.Count == 0 ? 0 : (int)Math.Ceiling(items.Count * DecoyShare / (1 - DecoyShare));
            var rnd = new Random(seed);
            var decoyKinds = ag.Kinds.Keys.Concat(new[] { "utilize", "wordy", "vague", "passive", "we", "will" }).Distinct().ToList();
            var pool = ag.BlocksBoth.OrderBy(k => k, StringComparer.Ordinal).ToList();
            int guard = 0;
            while (qb.Decoys < decoys && pool.Count > 0 && guard++ < decoys * 50)
            {
                string key = pool[rnd.Next(pool.Count)];
                int bar = key.IndexOf('|');
                string file = key.Substring(0, bar), xpath = key.Substring(bar + 1);
                string text = project.Text.GetText(file, xpath);
                if (text == null) continue;
                var span = PickWords(text, rnd);
                if (span == null) continue;
                var cand = new Candidate { File = file, XPath = xpath, Start = span.Item1, End = span.Item2 };
                if (ag.Agreed.Concat(ag.Contradictions).Any(p => p.A.BlockKey == key && (p.A.Overlaps(cand) || p.B.Overlaps(cand)))) continue;
                if (ag.OnlyA.Concat(ag.OnlyB).Any(c => c.BlockKey == key && c.Overlaps(cand))) continue;
                if ((byBlock.ContainsKey(key) ? byBlock[key] : new List<Label>()).Any(l => Overlap(text, l, cand))) continue;
                string q, pre, suf;
                Anchor.MakeSelector(text, cand.Start, cand.End, 24, out q, out pre, out suf);
                items.Add(Tuple.Create(
                    new QueueItem { File = file, Block = xpath, BlockText = text, Quote = q, Prefix = pre, Suffix = suf, Kind = decoyKinds[rnd.Next(decoyKinds.Count)], Split = SplitOf(project, file) },
                    new QueueKey { Type = "decoy" }));
                qb.Decoys++;
            }

            // shuffle so decoys are not adjacent to the cases they were mixed with, then number
            var shuffled = items.OrderBy(t => rnd.Next()).ToList();
            for (int i = 0; i < shuffled.Count; i++)
            {
                string qid = "Q" + (i + 1).ToString("D4");
                shuffled[i].Item1.Qid = qid; shuffled[i].Item2.Qid = qid;
                qb.Items.Add(shuffled[i].Item1); qb.Keys.Add(shuffled[i].Item2);
            }
            return qb;
        }

        private static string SplitOf(CorpusProject p, string file) { return p.TestFiles != null && p.TestFiles.Contains(file) ? "test" : "dev"; }

        private static Proposal Make(string source, Candidate c, string kind, List<string> by)
        {
            return new Proposal
            {
                Source = source, Id = Proposal.MakeId(c.File, c.XPath, c.Start, c.End, kind, source), File = c.File, Block = c.XPath,
                Quote = c.Quote, Prefix = c.Prefix, Suffix = c.Suffix, Kind = kind, By = by
            };
        }

        /// <summary>none | confirms (an existing violation of the same kind) | conflict (other kind, or a clean/exempt label for the same rules) | ambiguous.</summary>
        private static Tuple<string, string> Relate(CorpusProject project, Dictionary<string, List<Label>> byBlock, Candidate c, string kind)
        {
            List<Label> here;
            if (!byBlock.TryGetValue(c.BlockKey, out here)) return Tuple.Create("none", (string)null);
            string text = project.Text.GetText(c.File, c.XPath);
            string result = "none", id = null;
            foreach (var l in here)
            {
                if (l.Kind == LabelKind.BlockComplete || !Overlap(text, l, c)) continue;
                if (l.Kind == LabelKind.Ambiguous) { if (result == "none") { result = "ambiguous"; id = l.Id; } continue; }
                if (l.Kind == LabelKind.Violation)
                {
                    if (l.DefectKind == kind) return Tuple.Create("confirms", l.Id);
                    result = "conflict"; id = l.Id;
                }
                else if (l.Rules.Any(r => RuleVocabulary.Intersects(r, "lint") || RuleVocabulary.Intersects(r, "ai:" + kind))) { result = "conflict"; id = l.Id; }
            }
            return Tuple.Create(result, id);
        }

        private static bool Overlap(string text, Label l, Candidate c)
        {
            var s = Anchor.Resolve(text, l.Quote, l.Prefix, l.Suffix);
            return s.Resolved && s.Start < c.End && c.Start < s.End;
        }

        private static Tuple<int, int> PickWords(string text, Random rnd)
        {
            var starts = new List<int>();
            for (int i = 0; i < text.Length; i++)
                if (char.IsLetter(text[i]) && (i == 0 || !char.IsLetterOrDigit(text[i - 1]))) starts.Add(i);
            if (starts.Count == 0) return null;
            int s = starts[rnd.Next(starts.Count)];
            int words = 1 + rnd.Next(3), e = s;
            while (words > 0 && e < text.Length)
            {
                while (e < text.Length && char.IsLetterOrDigit(text[e])) e++;
                words--;
                if (words > 0) { int n = e; while (n < text.Length && text[n] == ' ') n++; if (n >= text.Length || !char.IsLetter(text[n])) break; e = n; }
            }
            return e - s >= 3 ? Tuple.Create(s, e) : null;
        }

        // ---- files ---------------------------------------------------------------------------------------------------------

        public static void WriteLines<T>(string path, IEnumerable<T> items)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var sb = new StringBuilder();
            foreach (var i in items) sb.Append(JsonConvert.SerializeObject(i, Formatting.None)).Append('\n');
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        public static List<T> ReadLines<T>(string path)
        {
            var list = new List<T>();
            if (!File.Exists(path)) return list;
            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
                if (line.Trim().Length > 0) list.Add(JsonConvert.DeserializeObject<T>(line));
            return list;
        }
    }
}
