using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using FlareAI.Scoring;
using Newtonsoft.Json.Linq;

namespace FlareAI.Labeler
{
    /// <summary>One span a labeller reported, resolved against the block text.</summary>
    public sealed class Candidate
    {
        public string Labeller;      // a | b
        public string Model;
        public string File, XPath, Quote, Prefix, Suffix, Kind, Fix;
        public int Start, End;
        public string BlockKey { get { return File + "|" + XPath; } }
        public bool Overlaps(Candidate o) { return Start < o.End && o.Start < End; }
    }

    /// <summary>What a labeller returned, read back from the stored runs.</summary>
    public sealed class LabellerResult
    {
        public string Labeller;
        public HashSet<string> Models = new HashSet<string>(StringComparer.Ordinal);
        public HashSet<string> Covered = new HashSet<string>(StringComparer.Ordinal);
        public List<Candidate> Candidates = new List<Candidate>();
        public int Invalid, AmbiguousQuotes, Calls;
        public long Input, Output;
        public double Usd;

        public static LabellerResult Load(string adjudicationDir, string labeller, CorpusProject project, Taxonomy taxonomy)
        {
            var r = new LabellerResult { Labeller = labeller };
            string dir = Path.Combine(adjudicationDir, "runs", labeller);
            if (!Directory.Exists(dir)) return r;
            var kinds = taxonomy.Ids;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var f in Directory.GetFiles(dir, "*.json").OrderBy(x => x, StringComparer.Ordinal))
            {
                var rec = JObject.Parse(File.ReadAllText(f, Encoding.UTF8));
                string model = (string)rec["model"];
                r.Models.Add(model);
                r.Calls++;
                var u = rec["usage"];
                if (u != null) { r.Input += (long?)u["input"] ?? 0; r.Output += (long?)u["output"] ?? 0; r.Usd += (double?)u["usd"] ?? 0; }
                var blocks = new Dictionary<string, JToken>(StringComparer.Ordinal);
                foreach (var b in (JArray)rec["blocks"]) blocks[(string)b["id"]] = b;
                foreach (var b in blocks.Values) r.Covered.Add((string)b["file"] + "|" + (string)b["block"]);
                var resp = rec["response"] as JObject;
                var arr = resp == null ? null : resp["blocks"] as JArray;
                if (arr == null) continue;
                foreach (var rb in arr)
                {
                    JToken meta;
                    if (!blocks.TryGetValue((string)rb["id"] ?? "", out meta)) { r.Invalid++; continue; }
                    string file = (string)meta["file"], xpath = (string)meta["block"];
                    string text = project.Text.GetText(file, xpath);
                    if (text == null) { r.Invalid++; continue; }
                    foreach (var sp in (rb["spans"] as JArray) ?? new JArray())
                    {
                        string quote = (string)sp["quote"], kind = (string)sp["kind"], before = (string)sp["before"];
                        if (string.IsNullOrEmpty(quote) || kind == null || !kinds.Contains(kind)) { r.Invalid++; continue; }
                        var a = Anchor.Resolve(text, quote, before, null);
                        if (!a.Resolved) { r.Invalid++; continue; }
                        if (a.Ambiguous) r.AmbiguousQuotes++;
                        string q, pre, suf;
                        Anchor.MakeSelector(text, a.Start, a.End, 24, out q, out pre, out suf);
                        string key = file + "|" + xpath + "|" + a.Start + "|" + a.End + "|" + kind;
                        if (!seen.Add(key)) continue;
                        r.Candidates.Add(new Candidate
                        {
                            Labeller = labeller, Model = model, File = file, XPath = xpath, Quote = q, Prefix = pre, Suffix = suf, Kind = kind,
                            Fix = (string)sp["fix"], Start = a.Start, End = a.End
                        });
                    }
                }
            }
            return r;
        }
    }

    public sealed class KindAgreement
    {
        public string Kind;
        public int Blocks, A, B, Both;
        public double? Kappa;
    }

    public sealed class Pair { public Candidate A, B; }

    public sealed class AgreementResult
    {
        public List<Pair> Agreed = new List<Pair>();
        public List<Pair> Contradictions = new List<Pair>();
        public List<Candidate> OnlyA = new List<Candidate>(), OnlyB = new List<Candidate>();
        public SortedDictionary<string, KindAgreement> Kinds = new SortedDictionary<string, KindAgreement>(StringComparer.Ordinal);
        public HashSet<string> BlocksBoth = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Kinds with a kappa below <paramref name="threshold"/> (kinds with no kappa, because nobody used them, are not listed).</summary>
        public IEnumerable<KindAgreement> Below(double threshold)
        {
            return Kinds.Values.Where(k => k.Kappa.HasValue && k.Kappa.Value < threshold);
        }

        /// <summary>
        /// Matches the two labellers' spans block by block, over the blocks both processed. Overlapping spans of the same kind agree
        /// (the shorter span is kept as the label's quote); overlapping spans of different kinds contradict; the rest are single-source.
        /// Kappa is per kind over those blocks: does each labeller report the kind in the block or not.
        /// </summary>
        public static AgreementResult Compare(LabellerResult a, LabellerResult b, IEnumerable<string> allKinds)
        {
            var res = new AgreementResult();
            foreach (var key in a.Covered) if (b.Covered.Contains(key)) res.BlocksBoth.Add(key);
            var ca = a.Candidates.Where(c => res.BlocksBoth.Contains(c.BlockKey)).GroupBy(c => c.BlockKey).ToDictionary(g => g.Key, g => g.OrderBy(c => c.Start).ToList());
            var cb = b.Candidates.Where(c => res.BlocksBoth.Contains(c.BlockKey)).GroupBy(c => c.BlockKey).ToDictionary(g => g.Key, g => g.OrderBy(c => c.Start).ToList());
            foreach (var key in ca.Keys.Union(cb.Keys).OrderBy(k => k, StringComparer.Ordinal))
            {
                List<Candidate> la, lb;
                if (!ca.TryGetValue(key, out la)) la = new List<Candidate>();
                if (!cb.TryGetValue(key, out lb)) lb = new List<Candidate>();
                var used = new HashSet<Candidate>();
                foreach (var x in la)
                {
                    var same = lb.FirstOrDefault(y => !used.Contains(y) && y.Kind == x.Kind && y.Overlaps(x));
                    if (same != null) { used.Add(same); res.Agreed.Add(new Pair { A = x, B = same }); continue; }
                    var other = lb.FirstOrDefault(y => !used.Contains(y) && y.Overlaps(x));
                    if (other != null) { used.Add(other); res.Contradictions.Add(new Pair { A = x, B = other }); continue; }
                    res.OnlyA.Add(x);
                }
                foreach (var y in lb) if (!used.Contains(y)) res.OnlyB.Add(y);
            }

            int n = res.BlocksBoth.Count;
            var setA = new HashSet<string>(a.Candidates.Where(c => res.BlocksBoth.Contains(c.BlockKey)).Select(c => c.Kind + "|" + c.BlockKey), StringComparer.Ordinal);
            var setB = new HashSet<string>(b.Candidates.Where(c => res.BlocksBoth.Contains(c.BlockKey)).Select(c => c.Kind + "|" + c.BlockKey), StringComparer.Ordinal);
            foreach (var kind in allKinds)
            {
                int ka = setA.Count(s => s.StartsWith(kind + "|", StringComparison.Ordinal));
                int kb = setB.Count(s => s.StartsWith(kind + "|", StringComparison.Ordinal));
                int both = setA.Count(s => s.StartsWith(kind + "|", StringComparison.Ordinal) && setB.Contains(s));
                if (ka == 0 && kb == 0) continue;
                res.Kinds[kind] = new KindAgreement { Kind = kind, Blocks = n, A = ka, B = kb, Both = both, Kappa = Kappa(n, ka, kb, both) };
            }
            return res;
        }

        /// <summary>Cohen's kappa for two yes/no raters over n items; null when chance agreement is 1 (no variation to measure).</summary>
        public static double? Kappa(int n, int a, int b, int both)
        {
            if (n <= 0) return null;
            double po = (n - a - b + 2.0 * both) / n;   // items both said yes plus items both said no
            double pe = ((double)a / n) * ((double)b / n) + ((double)(n - a) / n) * ((double)(n - b) / n);
            if (pe >= 1.0) return null;
            return Math.Round((po - pe) / (1.0 - pe), 3);
        }

        public string Markdown(LabellerResult a, LabellerResult b, double threshold, double? decoyAcceptance, int decoys, int decoysAccepted)
        {
            var sb = new StringBuilder();
            sb.Append("<!-- labeler:agreement -->\n## Labeller agreement\n\n");
            sb.Append("Blind pass over ").Append(BlocksBoth.Count).Append(" blocks; labeller A ").Append(string.Join(", ", a.Models.ToArray())).Append(", labeller B ").Append(string.Join(", ", b.Models.ToArray())).Append(".\n\n");
            sb.Append("Spans: ").Append(Agreed.Count).Append(" agreed, ").Append(Contradictions.Count).Append(" contradicted, ").Append(OnlyA.Count).Append(" only A, ").Append(OnlyB.Count).Append(" only B. Invalid answers dropped: A ").Append(a.Invalid).Append(", B ").Append(b.Invalid).Append(".\n\n");
            sb.Append("| Kind | A | B | Both | Cohen's kappa |\n|---|---|---|---|---|\n");
            foreach (var k in Kinds.Values)
                sb.Append("| ").Append(k.Kind).Append(" | ").Append(k.A).Append(" | ").Append(k.B).Append(" | ").Append(k.Both).Append(" | ")
                  .Append(k.Kappa.HasValue ? k.Kappa.Value.ToString("0.00", CultureInfo.InvariantCulture) : "n/a").Append(k.Kappa.HasValue && k.Kappa.Value < threshold ? " (below " + threshold.ToString("0.0", CultureInfo.InvariantCulture) + ": relabel or mark ambiguous)" : "").Append(" |\n");
            sb.Append('\n');
            sb.Append("Decoy acceptance: ").Append(decoys == 0 ? "no decoys judged yet" : decoysAccepted + " of " + decoys + " (" + ((decoyAcceptance ?? 0) * 100).ToString("0", CultureInfo.InvariantCulture) + "%)").Append(".\n\n");
            sb.Append("Cost: A $").Append(a.Usd.ToString("0.00", CultureInfo.InvariantCulture)).Append(" (").Append(a.Calls).Append(" calls), B $").Append(b.Usd.ToString("0.00", CultureInfo.InvariantCulture)).Append(" (").Append(b.Calls).Append(" calls).\n\n");
            sb.Append("<!-- /labeler:agreement -->\n");
            return sb.ToString();
        }
    }
}
