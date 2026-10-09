using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace FlareAI.Core.Llm
{
    /// <summary>A model + effort for one kind of task.</summary>
    public sealed class Route
    {
        public string Tier { get; set; }
        public string Model { get; set; }
        public string Effort { get; set; }
        public override string ToString() { return Tier + " (" + Model + ", " + Effort + ")"; }
    }

    /// <summary>
    /// Which model and effort each AI task uses. Tasks map to three tiers (fast / standard / deep); tiers map to a model
    /// and effort. Defaults are built in; Settings pick the tier models; a style guide can pin both for the whole team in
    /// FlareAI\models.json:
    /// <code>{ "tiers": { "fast": { "model": "claude-haiku-5-5", "effort": "low" } }, "tasks": { "review": "fast" }, "review_unit": "section" }</code>
    /// </summary>
    public sealed class Routing
    {
        public const string Fast = "fast", Standard = "standard", Deep = "deep";

        /// <summary>Every task the plugin sends to a model, and its default tier.</summary>
        public static readonly Dictionary<string, string> DefaultTasks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "review", Standard },        // per-block style review: judgment + exact citations
            { "rewrite", Standard },       // run-level edits for a block
            { "restructure", Standard },   // XHTML edits that change structure (validated)
            { "refine", Fast },            // one proposal, re-asked
            { "alt", Standard },           // vision: UI labels must be exact
            { "assist", Fast },            // description, keywords, synonyms, micro content, glossary
            { "extract", Standard },       // rules from notes
            { "draft", Deep },             // owner-only edits to the shared style guide
        };

        private readonly Dictionary<string, Route> _tiers = new Dictionary<string, Route>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _tasks = new Dictionary<string, string>(DefaultTasks, StringComparer.OrdinalIgnoreCase);

        /// <summary>How the review is split: "section" (the default: a short topic whole, a long one by headings) or "block" (one call per paragraph, item or row).</summary>
        public string ReviewUnit { get; set; }
        /// <summary>
        /// Rule packs sent with every call, in the long-lived cached block, because most sections need them (measured on
        /// 262 sections: Headings 100%, UI 90%, Lists 59%, Steps 58%; every other pack 22% or less). Team: "always_packs".
        /// </summary>
        public List<string> AlwaysPacks { get; set; }
        /// <summary>Where the routing came from, for Diagnostics ("built-in", "settings", "team: …\models.json").</summary>
        public string Source { get; private set; }

        public Routing(string fastModel = null, string standardModel = null, string deepModel = null)
        {
            _tiers[Fast] = new Route { Tier = Fast, Model = fastModel ?? "claude-haiku-5-5", Effort = "medium" };
            _tiers[Standard] = new Route { Tier = Standard, Model = standardModel ?? "claude-sonnet-5-5", Effort = "medium" };
            _tiers[Deep] = new Route { Tier = Deep, Model = deepModel ?? "claude-opus-5-5", Effort = "high" };
            ReviewUnit = "section";
            AlwaysPacks = new List<string> { "Headings", "UI", "Lists", "Steps" };
            Source = fastModel == null && standardModel == null && deepModel == null ? "built-in" : "settings";
        }

        public Route For(string task)
        {
            string tier;
            if (task == null || !_tasks.TryGetValue(task, out tier)) tier = Standard;
            return _tiers[tier];
        }

        /// <summary>Applies the team's FlareAI\models.json from the style guide, if present (team settings win).</summary>
        public Routing WithTeamFile(string guideRoot)
        {
            if (string.IsNullOrEmpty(guideRoot)) return this;
            var path = Path.Combine(guideRoot, Path.Combine("FlareAI", "models.json"));
            if (!File.Exists(path)) return this;
            try
            {
                var o = JObject.Parse(File.ReadAllText(path, Encoding.UTF8));
                foreach (var t in (o["tiers"] as JObject ?? new JObject()).Properties())
                {
                    Route r;
                    if (!_tiers.TryGetValue(t.Name, out r)) continue;
                    if (t.Value["model"] != null) r.Model = (string)t.Value["model"];
                    if (t.Value["effort"] != null) r.Effort = (string)t.Value["effort"];
                }
                foreach (var t in (o["tasks"] as JObject ?? new JObject()).Properties())
                    if (_tiers.ContainsKey((string)t.Value)) _tasks[t.Name] = (string)t.Value;
                if (o["review_unit"] != null) ReviewUnit = (string)o["review_unit"];
                if (o["always_packs"] is JArray ap) AlwaysPacks = ap.Select(x => (string)x).ToList();
                Source = "team: " + path;
            }
            catch (Exception ex) { Diagnostics.Log("models.json", ex); }
            return this;
        }

        public string Describe()
        {
            var sb = new StringBuilder("Routing (" + Source + "), review unit: " + ReviewUnit + ", always-on packs: " + string.Join(", ", AlwaysPacks.ToArray()) + "\n");
            foreach (var t in DefaultTasks.Keys) sb.Append("  ").Append(t).Append(" → ").Append(For(t)).Append('\n');
            return sb.ToString();
        }
    }

    /// <summary>
    /// Token use and estimated cost per task and model, for this session (Diagnostics) and for the stress harness.
    /// Prices are list prices per million tokens (input, output, cache read); cache writes are billed at 1.25× input (5-minute) or 2× (1-hour).
    /// </summary>
    public static class Usage
    {
        private static readonly Dictionary<string, double[]> Prices = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "claude-haiku-5-5", new[] { 0.10, 0.50, 0.01 } },
            { "claude-sonnet-5-5", new[] { 2.00, 10.00, 0.20 } },
            { "claude-opus-5-5", new[] { 4.00, 20.00, 0.20 } },
        };

        public sealed class Line { public string Task; public string Model; public int Calls; public long Input, Output, CacheRead, CacheWrite; public double Usd; public double Seconds; }

        private static readonly Dictionary<string, Line> Lines = new Dictionary<string, Line>();
        private static readonly object Gate = new object();

        public static void Record(string task, string model, LlmResponse r, double seconds)
        {
            double[] p;
            if (model == null || !Prices.TryGetValue(model, out p)) p = new[] { 0.0, 0.0, 0.0 };
            var write1h = Math.Min(r.CacheWrite1hTokens, r.CacheWriteTokens);
            var usd = (r.InputTokens * p[0] + (r.CacheWriteTokens - write1h) * p[0] * 1.25 + write1h * p[0] * 2 + r.CacheReadTokens * p[2] + r.OutputTokens * p[1]) / 1e6;
            lock (Gate)
            {
                Line l;
                var key = (task ?? "?") + "|" + model;
                if (!Lines.TryGetValue(key, out l)) Lines[key] = l = new Line { Task = task, Model = model };
                l.Calls++; l.Input += r.InputTokens; l.Output += r.OutputTokens; l.CacheRead += r.CacheReadTokens; l.CacheWrite += r.CacheWriteTokens;
                l.Usd += usd; l.Seconds += seconds;
            }
        }

        public static List<Line> Snapshot() { lock (Gate) return new List<Line>(Lines.Values); }
        public static void Reset() { lock (Gate) Lines.Clear(); }
    }
}
