using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using FlareAI.Core.Llm;
using FlareAI.Scoring;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlareAI.Labeler
{
    public sealed class RunOptions
    {
        /// <summary>False (the default) prices the plan and sends nothing.</summary>
        public bool Execute;
        /// <summary>Hard ceiling in US dollars for the whole run; required when <see cref="Execute"/> is set.</summary>
        public double MaxCostUsd;
        public string ModelA = "claude-sonnet-5-5";
        public string ModelB = "claude-opus-5-5";
        /// <summary>Spans a labeller is expected to report per block; sizes the output estimate.</summary>
        public double SpansPerBlock = 0.1;
        public string Effort = "medium";
        public int MaxTokens = 8000;
    }

    public sealed class RunSummary
    {
        public bool DryRun;
        public int Batches, BlocksPlanned;
        public int CallsMade, CacheHits, Errors;
        public double SpentUsd;
        public bool StoppedByBudget;
        public int BatchesDoneByBoth;
        public List<string> Messages = new List<string>();
        public LabelPlan.Estimate EstimateA, EstimateB;
        public double EstimatedUsd { get { return (EstimateA == null ? 0 : EstimateA.Usd) + (EstimateB == null ? 0 : EstimateB.Usd); } }
    }

    /// <summary>
    /// The blind pass. Two clients (different models) get the same batches; each answer is stored by hash under
    /// <c>adjudication/runs/&lt;a|b&gt;/</c>, so a repeated run costs nothing for batches already done and a stopped run resumes.
    /// A batch is sent only if the spend so far plus its price is within the cap; both labellers go batch by batch so that
    /// stopping leaves them with the same blocks.
    /// </summary>
    public sealed class LabelRunner
    {
        private readonly ILlmClient _a, _b;
        private readonly Taxonomy _taxonomy;
        private readonly string _runsDir;

        public LabelRunner(ILlmClient a, ILlmClient b, Taxonomy taxonomy, string adjudicationDir)
        {
            _a = a; _b = b; _taxonomy = taxonomy;
            _runsDir = Path.Combine(adjudicationDir, "runs");
        }

        public static string RunFile(string adjudicationDir, string labeller, string model, string hash)
        {
            return Path.Combine(adjudicationDir, "runs", labeller, Safe(model) + "-" + hash + ".json");
        }

        private static string Safe(string s) { return new string((s ?? "model").Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : '_').ToArray()); }

        public RunSummary Run(LabelPlan plan, RunOptions opt)
        {
            var sum = new RunSummary { DryRun = !opt.Execute, Batches = plan.Batches.Count, BlocksPlanned = plan.Blocks };
            sum.EstimateA = plan.PriceFor(opt.ModelA, opt.SpansPerBlock);
            sum.EstimateB = plan.PriceFor(opt.ModelB, opt.SpansPerBlock);
            if (!opt.Execute) return sum;
            if (opt.MaxCostUsd <= 0) throw new InvalidOperationException("Running needs --max-cost <usd> greater than 0.");
            if (string.Equals(opt.ModelA, opt.ModelB, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The two labellers must be different models.");
            var kinds = LabelPlan.Kinds(_taxonomy).Select(k => k.Id).ToList();
            var schema = LabelPlan.Schema(kinds);
            int sysTokens = LabelPlan.Tokens(plan.System.Length);

            for (int i = 0; i < plan.Batches.Count; i++)
            {
                var batch = plan.Batches[i];
                string fa = RunFile(Dir(), "a", opt.ModelA, batch.Hash), fb = RunFile(Dir(), "b", opt.ModelB, batch.Hash);
                bool needA = !File.Exists(fa), needB = !File.Exists(fb);
                if (!needA && !needB) { sum.CacheHits += 2; sum.BatchesDoneByBoth++; continue; }

                // The cap is a promise, so each call is priced at its worst case: the batch's input (15% on the character estimate) and
                // the full output ceiling, which also bounds any reasoning tokens. The system prompt is priced as a cache write when this
                // process has not sent one yet.
                bool first = sum.CallsMade == 0;
                double projected = 0;
                if (needA) projected += WorstCase(opt.ModelA, batch, sysTokens, first, opt);
                if (needB) projected += WorstCase(opt.ModelB, batch, sysTokens, first, opt);
                if (sum.SpentUsd + projected > opt.MaxCostUsd)
                {
                    sum.StoppedByBudget = true;
                    sum.Messages.Add("Stopped before batch " + (i + 1) + " of " + plan.Batches.Count + ": spent " + sum.SpentUsd.ToString("0.0000") + " + " + projected.ToString("0.0000") + " would pass the " + opt.MaxCostUsd.ToString("0.00") + " cap.");
                    break;
                }
                bool okA = !needA || Send(_a, "a", opt.ModelA, opt, plan, batch, schema, fa, sum);
                bool okB = !needB || Send(_b, "b", opt.ModelB, opt, plan, batch, schema, fb, sum);
                if (okA && okB) sum.BatchesDoneByBoth++;
                if (sum.Messages.Any(m => m.StartsWith("Stopped:", StringComparison.Ordinal))) break;
            }
            return sum;
        }

        private static double WorstCase(string model, PlanBatch batch, int sysTokens, bool first, RunOptions opt)
        {
            var r = LabelPlan.BatchResponse(batch, sysTokens, first, opt.SpansPerBlock);
            r.InputTokens = (int)(r.InputTokens * 1.15);
            r.OutputTokens = opt.MaxTokens;
            bool priced;
            return LabelPlan.Cost(model, r, out priced);
        }

        private string Dir() { return Path.GetDirectoryName(_runsDir); }

        private bool Send(ILlmClient client, string labeller, string model, RunOptions opt, LabelPlan plan, PlanBatch batch, JObject schema, string file, RunSummary sum)
        {
            var req = new LlmRequest { Task = "label", Model = model, System = plan.System, User = batch.User, Schema = schema, MaxTokens = opt.MaxTokens, Effort = opt.Effort };
            var sw = Stopwatch.StartNew();
            try
            {
                var resp = client.Complete(req);
                sum.CallsMade++;
                bool priced;
                double usd = LabelPlan.Cost(resp.Model ?? model, resp, out priced);
                sum.SpentUsd += usd;
                var rec = new JObject
                {
                    ["labeller"] = labeller, ["model"] = model, ["batch"] = batch.Hash,
                    ["blocks"] = new JArray(batch.Blocks.Select((b, i) => new JObject { ["id"] = batch.Ids[i], ["file"] = b.File, ["block"] = b.XPath })),
                    ["usage"] = new JObject { ["input"] = resp.InputTokens, ["output"] = resp.OutputTokens, ["cacheRead"] = resp.CacheReadTokens, ["cacheWrite"] = resp.CacheWriteTokens, ["usd"] = Math.Round(usd, 6) },
                    ["response"] = resp.Json ?? JObject.Parse(resp.Text)
                };
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(file, rec.ToString(Formatting.Indented), new UTF8Encoding(false));
                return true;
            }
            catch (LlmException ex)
            {
                sum.Errors++;
                sum.Messages.Add("batch " + batch.Hash + " (" + labeller + "): " + ex.Failure + ": " + ex.Message);
                // a key or budget problem will not fix itself on the next batch
                if (ex.Failure == LlmFailure.Auth || ex.Failure == LlmFailure.ModelDenied || ex.Failure == LlmFailure.Budget)
                    sum.Messages.Add("Stopped: " + ex.Failure);
                return false;
            }
        }
    }
}
