using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FlareAI.Core.Style;
using Newtonsoft.Json.Linq;

namespace FlareAI.GuideCompiler
{
    /// <summary>
    /// The severity policy for compiled lint rows (#61), from fixes.json "severity": error only for the rows listed under
    /// "error" (offensive terms); a flag-only row (no safe fix) is a suggestion unless listed under "keep_warning" (a clear
    /// "don't use" term with no single replacement, such as inclusive language); rows listed under "suggestion" (typographic
    /// and advisory rows) are suggestions. Entries are rule ids or page prefixes ("google:inclusive-documentation/"). A
    /// severity that fixes.json sets on one rule is a decision and wins over the policy.
    /// </summary>
    internal sealed class SeverityPolicy
    {
        private List<string> _error = new List<string>(), _keep = new List<string>(), _suggestion = new List<string>();
        private HashSet<string> _decided = new HashSet<string>(StringComparer.Ordinal);

        public static SeverityPolicy Of(PassConfig cfg)
        {
            var s = cfg.Fixes["severity"] as JObject ?? new JObject();
            Func<string, List<string>> list = n => (s[n] ?? new JArray()).Select(x => (string)x).ToList();
            return new SeverityPolicy
            {
                _error = list("error"), _keep = list("keep_warning"), _suggestion = list("suggestion"),
                _decided = new HashSet<string>(((JArray)cfg.Fixes["rules"] ?? new JArray()).Where(d => d["severity"] != null).Select(d => (string)d["rule"]), StringComparer.Ordinal)
            };
        }

        private static bool Listed(List<string> list, string id) { return list.Any(x => x == id || (x.EndsWith("/") && id.StartsWith(x, StringComparison.Ordinal))); }

        public bool ErrorAllowed(string id) { return Listed(_error, id); }

        /// <summary>The severity the policy gives this lint row; null when the row's own severity stands.</summary>
        public string For(Compiled c)
        {
            if (_decided.Contains(c.Id)) return null;
            var sev = (string)c.Rule["severity"];
            var want = sev;
            if (Listed(_suggestion, c.Id)) want = "suggestion";
            if (want == "error" && !ErrorAllowed(c.Id)) want = "warning";
            if (want == "warning" && GuideLayer.IsFlagOnly((string)c.Rule["use"]) && !Listed(_keep, c.Id)) want = "suggestion";
            return want == sev ? null : want;
        }

        public string Why(Compiled c, string want)
        {
            if (Listed(_suggestion, c.Id)) return "typographic or advisory row";
            if ((string)c.Rule["severity"] == "error" && want != "error" && !GuideLayer.IsFlagOnly((string)c.Rule["use"])) return "error is for offensive terms only";
            return "flag-only: no safe fix";
        }
    }

    /// <summary>
    /// The #62 checks over a compiled Google layer: every finding class of the accuracy audit that can be detected mechanically.
    /// The compiler runs them after its passes (coverage.md lists failures) and tests/FlareAI.GuideCompiler.Tests fails on any.
    /// </summary>
    internal static class GuideChecks
    {
        public static readonly string[] Names = { "overlap", "conflict", "bad-not-flagged", "good-flagged", "fix-creates", "markup", "product", "term", "severity" };

        private static readonly HashSet<string> Structural = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Headings", "Steps", "Lists", "Tables", "Notes", "Links", "UI", "Examples", "API", "Code", "Images", "Captions" };

        /// <summary>Check name → failures (one line each). <paramref name="comp"/> must cover every product, so Product rows lint too.</summary>
        public static Dictionary<string, List<string>> Run(Composition comp, JObject compiled, PassConfig cfg)
        {
            var result = Names.ToDictionary(n => n, n => new List<string>());
            var google = comp.Rules.Where(r => (r.Id ?? "").StartsWith("google:")).ToList();
            var byId = google.ToDictionary(r => r.Id);
            Func<LintHit, bool> isGoogle = h => (h.RuleId ?? "").StartsWith("google:");

            // overlap / conflict: each row's Avoid terms (as written and in lower case, in a sentence) and Example, linted with
            // every rule. Two rules on the exact same words must agree on the fix, and may share them only when they fire in
            // different places (structural scopes neither of which contains the other).
            var probes = google.SelectMany(r => r.Avoid.Where(a => a.Trim().Length > 0))
                               .Where(a => !Regex.IsMatch(a, @"\\|\[|\(\?"))
                               .SelectMany(a => new[] { "Use " + a + " here.", "Use " + a.ToLowerInvariant() + " here." })
                               .Concat(google.Select(r => r.Example).Where(e => !string.IsNullOrEmpty(e))).Distinct().ToList();
            var hits = comp.Lint(probes, null, true).Where(isGoogle).ToList();
            foreach (var g in hits.GroupBy(h => h.Index + ":" + h.Start + ":" + h.Match.TrimEnd().Length))
            {
                var ids = g.Select(h => h.RuleId).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
                if (ids.Count < 2) continue;
                var fixes = g.Where(h => !string.IsNullOrEmpty(h.Replacement)).Select(h => h.Replacement.Trim().TrimEnd(',').ToLowerInvariant()).Distinct().ToList();
                if (fixes.Count > 1) result["conflict"].Add("'" + g.First().Match + "': " + string.Join(", ", ids.ToArray()) + " fix it as " + string.Join(" / ", fixes.ToArray()));
                var scopes = ids.Select(i => new HashSet<string>(byId[i].Applies.Where(Structural.Contains))).ToList();
                var disjoint = scopes.All(s => s.Count > 0) && scopes.Select(s => string.Join(",", s.OrderBy(x => x).ToArray())).Distinct().Count() == scopes.Count;
                if (!disjoint) result["overlap"].Add("'" + g.First().Match + "': " + string.Join(", ", ids.ToArray()));
            }
            result["overlap"] = result["overlap"].Distinct().ToList();
            result["conflict"] = result["conflict"].Distinct().ToList();

            // bad-not-flagged: every row flags its own Example (and the Bad text the compiler kept for it).
            var compiledRules = Passes.All(compiled).ToDictionary(c => c.Id, c => c);
            var bads = new List<KeyValuePair<LintRule, string>>();
            foreach (var r in google)
            {
                Compiled c;
                var texts = new[] { r.Example, compiledRules.TryGetValue(r.Id, out c) ? (string)c.Rule["bad"] : null }.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct();
                foreach (var t in texts) bads.Add(new KeyValuePair<LintRule, string>(r, t));
            }
            var heads = Enumerable.Range(0, bads.Count).Where(i => bads[i].Key.Applies.Contains("Headings")).ToList();
            var badHits = comp.Lint(bads.Select(b => b.Value).ToList(), heads, true);
            for (var i = 0; i < bads.Count; i++)
                if (!badHits.Any(h => h.Index == i && h.RuleId == bads[i].Key.Id)) result["bad-not-flagged"].Add(bads[i].Key.Id + ": '" + bads[i].Value + "'");

            // good-flagged: a Good example (what the guide says to write), as a paragraph; no Google rule may flag it. A Good example
            // that is markup or code is a source snippet, not text a writer types. fixes.json "checks"."good_flagged_ok" lists the
            // examples Google means to contain the flagged words (an old term in parentheses on first mention, a code keyword).
            var ok = ((JArray)((cfg.Fixes["checks"] as JObject ?? new JObject())["good_flagged_ok"]) ?? new JArray()).Select(x => (string)x["rule"] + "|" + (string)x["by"]).ToList();
            var goods = comp.Items.Where(it => it.Id.StartsWith("google:") && it.Good.Length > 0 && !LintPatterns.MatchesMarkup(it.Good)).ToList();
            var goodHits = comp.Lint(goods.Select(it => new LintLine { Text = it.Good, Known = true }).ToList());
            foreach (var h in goodHits.Where(isGoogle).Where(h => !ok.Contains(goods[h.Index].Id + "|" + h.RuleId)))
                result["good-flagged"].Add(goods[h.Index].Id + ": Good '" + goods[h.Index].Good + "' is flagged by " + h.RuleId + " ('" + h.Match + "')");

            // fix-creates: a row's fix applied to its Example (and each bare Avoid term) leaves a finding that wasn't there.
            var lintRows = compiledRules.Values.Where(c => c.IsLint).ToDictionary(c => c.Id, c => c);
            foreach (var b in Passes.Created(comp, lintRows)) result["fix-creates"].Add(b.Row.Id + ": " + b.Why);

            // markup: rows the loader skipped (they match markup) and compiled rows that still carry markup.
            foreach (var p in comp.Layers[0].Problems.Where(p => p.Message.StartsWith(GuideLayer.MarkupOnly))) result["markup"].Add(p.Location + ": " + p.Message);
            foreach (var c in lintRows.Values)
            {
                var pats = c.Kind == "row" ? Passes.Split((string)c.Rule["avoid"]).Select(Regex.Escape) : new[] { (string)c.Rule["pattern"] };
                if (pats.Any(LintPatterns.MatchesMarkup) && !result["markup"].Any(x => x.Contains("(" + c.Id + ")"))) result["markup"].Add(c.Id + ": matches markup");
            }

            // product: a row about Android, Google Cloud or Workspace documentation carries that Product tag.
            foreach (var c in compiledRules.Values)
            {
                var have = new HashSet<string>(((JArray)c.Rule["products"] ?? new JArray()).Select(x => (string)x), StringComparer.OrdinalIgnoreCase);
                var missing = Passes.ProductCues(c, cfg).Where(a => !have.Contains(a.Key)).Select(a => a.Key + " (" + a.Value + ")").Distinct().ToList();
                if (missing.Count > 0) result["product"].Add(c.Id + ": no Product." + string.Join(", Product.", missing.ToArray()));
            }

            // term: a word-list Term cell names only its own entry (its heading or its rule text).
            var wl = compiled["word-list"] as JObject;
            if (wl != null)
                foreach (var r in ((JArray)wl["rules"]).Where(r => (string)r["kind"] == "term"))
                {
                    var said = Regex.Replace(Regex.Replace((string)r["heading"] + " " + (string)r["text"], @"\([^)]*\)", ""), @"\s+", " ").ToLowerInvariant();
                    foreach (var f in Topic.TermForms((string)r["term"]))
                        if (!said.Contains(f.ToLowerInvariant())) result["term"].Add(r["id"] + ": Term '" + r["term"] + "' names '" + f + "', which is neither its heading nor its rule text");
                }

            // severity: lint rows follow the policy (error only where it allows; flag-only rows suggestion unless kept).
            var policy = SeverityPolicy.Of(cfg);
            foreach (var c in lintRows.Values)
            {
                var want = policy.For(c);
                if (want != null) result["severity"].Add(c.Id + ": " + (string)c.Rule["severity"] + ", policy says " + want);
            }
            return result;
        }

        public static string Summary(Dictionary<string, List<string>> r)
        {
            return string.Join(", ", Names.Select(n => n + " " + r[n].Count).ToArray());
        }
    }
}
