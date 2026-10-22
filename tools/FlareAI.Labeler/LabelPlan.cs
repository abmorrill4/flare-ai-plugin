using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using FlareAI.Core.Llm;
using FlareAI.Scoring;
using Newtonsoft.Json.Linq;

namespace FlareAI.Labeler
{
    public sealed class PlanOptions
    {
        /// <summary>dev, test or all.</summary>
        public string Split = "all";
        /// <summary>Stop after this many blocks (0 = all), taken evenly across the project with <see cref="Seed"/>.</summary>
        public int MaxBlocks;
        public int Seed = 1;
        public int BatchChars = 6000;
        public int MaxBlocksPerBatch = 40;
        /// <summary>Blocks shorter than this carry too little text to judge.</summary>
        public int MinChars = 12;
        /// <summary>Optional file of rule texts and examples appended to the kind list (company guide text, never lint patterns).</summary>
        public string ExtraRulesPath;
    }

    public sealed class PlanBatch
    {
        public string Hash;
        public List<CorpusBlock> Blocks = new List<CorpusBlock>();
        public string User;
        /// <summary>Id the model sees ("b1") for each block, in order.</summary>
        public List<string> Ids = new List<string>();
        public int Chars { get { return User.Length; } }
    }

    /// <summary>
    /// What a labelling pass would send: the system prompt (the defect kinds with their summaries and examples) and the blocks
    /// in batches. Built without any model; the dry run prices it, the run sends it.
    /// </summary>
    public sealed class LabelPlan
    {
        public string Project;
        public string System;
        public List<PlanBatch> Batches = new List<PlanBatch>();
        public int Blocks, SkippedBlocks;

        /// <summary>
        /// Kinds the DOM decides (no text to read): a labeller sees block text only, so these are not offered to it.
        /// </summary>
        public static readonly HashSet<string> DomOnlyKinds = new HashSet<string>(StringComparer.Ordinal)
        {
            "alt_missing", "table_header", "second_h1", "heading_skip", "link_text_url"
        };

        public static JObject Schema(IEnumerable<string> kinds)
        {
            return new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["required"] = new JArray("blocks"),
                ["properties"] = new JObject
                {
                    ["blocks"] = new JObject
                    {
                        ["type"] = "array",
                        ["items"] = new JObject
                        {
                            ["type"] = "object",
                            ["additionalProperties"] = false,
                            ["required"] = new JArray("id", "spans"),
                            ["properties"] = new JObject
                            {
                                ["id"] = new JObject { ["type"] = "string" },
                                ["spans"] = new JObject
                                {
                                    ["type"] = "array",
                                    ["items"] = new JObject
                                    {
                                        ["type"] = "object",
                                        ["additionalProperties"] = false,
                                        ["required"] = new JArray("quote", "before", "kind", "fix"),
                                        ["properties"] = new JObject
                                        {
                                            ["quote"] = new JObject { ["type"] = "string" },
                                            ["before"] = new JObject { ["type"] = "string" },
                                            ["kind"] = new JObject { ["type"] = "string", ["enum"] = new JArray(kinds.ToArray()) },
                                            ["fix"] = new JObject { ["type"] = "string" }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };
        }

        public static string SystemPrompt(Taxonomy taxonomy, string extraRules)
        {
            var sb = new StringBuilder();
            sb.Append("You label defects in technical documentation, one block of text at a time, for a test corpus.\n");
            sb.Append("For every block, list each span that shows one of the defect kinds below. Report a span only when you are sure it is a defect for a professional documentation team; a UI label written as the product shows it, a quoted program message, code, a product or file name, and text a style guide would let stand are not defects.\n");
            sb.Append("Rules for each span:\n");
            sb.Append("- quote: the exact characters of the defective text, copied from the block (as short as the defect allows).\n");
            sb.Append("- before: up to 24 characters of the block that come immediately before the quote, copied exactly; empty when the quote starts the block.\n");
            sb.Append("- kind: one id from the list.\n");
            sb.Append("- fix: the corrected wording for the quote, or an empty string when there is no single fix.\n");
            sb.Append("A block with no defect gets an empty list. Do not invent defects to be helpful, and do not report anything that is not in the list of kinds.\n\n");
            sb.Append("Defect kinds:\n");
            foreach (var k in Kinds(taxonomy))
            {
                sb.Append("- ").Append(k.Id).Append(": ").Append(k.Summary);
                if (!string.IsNullOrEmpty(k.Bad)) sb.Append(" Example: \"").Append(k.Bad).Append("\"");
                sb.Append('\n');
            }
            if (!string.IsNullOrWhiteSpace(extraRules)) sb.Append("\nAdditional rules and examples from the team's style guide:\n").Append(extraRules.Trim()).Append('\n');
            return sb.ToString();
        }

        public static IEnumerable<DefectKind> Kinds(Taxonomy t)
        {
            return t.All.Where(k => !DomOnlyKinds.Contains(k.Id) && !string.IsNullOrEmpty(k.Summary));
        }

        public static LabelPlan Build(CorpusProject project, Taxonomy taxonomy, PlanOptions opt)
        {
            var plan = new LabelPlan { Project = project.Name };
            string extra = opt.ExtraRulesPath == null ? null : File.ReadAllText(opt.ExtraRulesPath, Encoding.UTF8);
            plan.System = SystemPrompt(taxonomy, extra);

            var all = new List<CorpusBlock>();
            foreach (var f in project.ContentFiles())
            {
                string split = project.TestFiles != null && project.TestFiles.Contains(f) ? "test" : "dev";
                if (opt.Split != "all" && opt.Split != split) continue;
                foreach (var b in project.Blocks(f))
                {
                    bool prose = b.Text.Length >= opt.MinChars && b.Text.Any(char.IsLetter) && !b.Line.Contains("code-block");
                    if (prose) all.Add(b); else plan.SkippedBlocks++;
                }
            }
            if (opt.MaxBlocks > 0 && all.Count > opt.MaxBlocks)
            {
                // an even, seeded sample: every block gets a score, the n smallest are kept, document order is restored
                var scored = all.Select((b, i) => new { b, i, s = Score(opt.Seed, b.Id) }).OrderBy(x => x.s).Take(opt.MaxBlocks).OrderBy(x => x.i).Select(x => x.b);
                plan.SkippedBlocks += all.Count - opt.MaxBlocks;
                all = scored.ToList();
            }
            plan.Blocks = all.Count;

            PlanBatch cur = null;
            foreach (var b in all)
            {
                if (cur != null && (cur.Blocks.Count >= opt.MaxBlocksPerBatch || cur.Chars + b.Text.Length > opt.BatchChars)) { Seal(cur); plan.Batches.Add(cur); cur = null; }
                if (cur == null) cur = new PlanBatch { User = "Label these blocks. The id in brackets is the block id; the word in parentheses is the kind of element.\n" };
                string id = "b" + (cur.Blocks.Count + 1);
                cur.Blocks.Add(b);
                cur.Ids.Add(id);
                cur.User += "\n[" + id + "] (" + b.Line[0] + ") " + b.Text;
            }
            if (cur != null) { Seal(cur); plan.Batches.Add(cur); }
            return plan;
        }

        private static void Seal(PlanBatch b) { b.Hash = Sha(b.User); }

        private static uint Score(int seed, string key)
        {
            uint h = 2166136261u ^ (uint)seed;
            foreach (char c in key) { h ^= c; h *= 16777619u; }
            return h;
        }

        public static string Sha(string s)
        {
            using (var sha = SHA1.Create())
                return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(s)).Select(x => x.ToString("x2")).ToArray()).Substring(0, 16);
        }

        // ---- cost ------------------------------------------------------------------------------------------------------------

        /// <summary>Rough token count of English text with JSON-ish punctuation: 1 token per 3.6 characters (deliberately on the high side).</summary>
        public static int Tokens(int chars) { return (int)Math.Ceiling(chars / 3.6); }

        public sealed class Estimate
        {
            public string Model;
            public int Calls;
            public long Input, Output, CacheRead, CacheWrite;
            public double Usd;
            public bool Priced;
        }

        /// <summary>
        /// Price of running every batch with <paramref name="model"/>. Input is the batch text; the system prompt is written to the 1-hour
        /// cache by the first call and read by the rest (as <see cref="AnthropicClient"/> sends it). Output is a fixed envelope per block plus
        /// <paramref name="spansPerBlock"/> reported spans of about 50 tokens each. Prices come from <see cref="Usage"/>.
        /// </summary>
        public Estimate PriceFor(string model, double spansPerBlock)
        {
            var e = new Estimate { Model = model, Calls = Batches.Count };
            int sys = Tokens(System.Length);
            for (int i = 0; i < Batches.Count; i++)
            {
                var r = BatchResponse(Batches[i], sys, i == 0, spansPerBlock);
                e.Input += r.InputTokens; e.Output += r.OutputTokens; e.CacheRead += r.CacheReadTokens; e.CacheWrite += r.CacheWriteTokens;
            }
            e.Usd = Cost(model, new LlmResponse { InputTokens = (int)e.Input, OutputTokens = (int)e.Output, CacheReadTokens = (int)e.CacheRead, CacheWriteTokens = (int)e.CacheWrite, CacheWrite1hTokens = (int)e.CacheWrite }, out e.Priced);
            return e;
        }

        public static LlmResponse BatchResponse(PlanBatch b, int systemTokens, bool first, double spansPerBlock)
        {
            int outTok = 40 + b.Blocks.Count * 14 + (int)Math.Ceiling(b.Blocks.Count * spansPerBlock * 50);
            return new LlmResponse
            {
                InputTokens = Tokens(b.User.Length) + 20,
                OutputTokens = outTok,
                CacheWriteTokens = first ? systemTokens : 0,
                CacheWrite1hTokens = first ? systemTokens : 0,
                CacheReadTokens = first ? 0 : systemTokens
            };
        }

        /// <summary>Dollars for one response at list prices; <paramref name="priced"/> is false for a model with no price.</summary>
        public static double Cost(string model, LlmResponse r, out bool priced)
        {
            // Usage is the one place that knows the prices; it records into a process-wide table, so read the difference.
            int c0; double u0; Usage.Totals(out c0, out u0);
            Usage.Record("label-price", model, r, 0);
            int c1; double u1; Usage.Totals(out c1, out u1);
            double usd = u1 - u0;
            priced = usd > 0 || (r.InputTokens + r.OutputTokens + r.CacheReadTokens + r.CacheWriteTokens) == 0;
            return usd;
        }
    }
}
