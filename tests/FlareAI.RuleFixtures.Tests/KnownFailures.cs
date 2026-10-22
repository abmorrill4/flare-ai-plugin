using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlareAI.RuleFixtures.Tests
{
    /// <summary>
    /// tests/baselines/rule-fixtures.known-failures.json: the cases that fail today, grouped by the issue that owns them.
    /// The suite fails on a failure that isn't listed and on a listed case that now passes, so the list only shrinks.
    /// Regenerate it deliberately with FLAREAI_UPDATE_KNOWN=1 (after fixing a bug, to drop the cases it fixed).
    /// </summary>
    public static class KnownFailures
    {
        private static readonly Dictionary<string, string> Titles = new Dictionary<string, string>
        {
            { "#53", "Lint honours content scope (steps, UI labels, links, tables, notes), not only headings" },
            { "#54", "Same-span findings: deterministic owner, and turning off covers every rule on that span" },
            { "#55", "Report and skip lint patterns that can only match markup" },
            { "#56", "Google guide compiler: one canonical rule per term or span across pages" },
            { "#59", "Google fixes that create new findings" },
            { "#62", "Validation test: duplicates, conflicts, Bad/Good examples, fixes that add findings" },
            { "#65", "Google guide accuracy (not yet attributed to a child issue)" }
        };

        /// <summary>The issue that owns a failing case. A rule of thumb from the cause; a human re-files a case by editing the list.</summary>
        public static string IssueOf(Failure f)
        {
            switch (f.Check)
            {
                case "pattern-plain": return "#55";
                case "bad-clean": return "#53";
                case "bad-fires":
                    if (f.Cause.StartsWith("shadowed by "))
                    {
                        var other = f.Cause.Substring("shadowed by ".Length);
                        return Prefix(other) == Prefix(f.RuleId) ? "#54" : "#56";
                    }
                    return "#65";
                case "good-clean": return f.Cause.Contains("word-list/296") ? "#59" : "#62";
                case "fix-clean": return "#59";
                case "off-project":
                case "off-team":
                case "stable":
                case "reload": return "#54";
                default: return "#65";
            }
        }

        private static string Prefix(string id) { var i = id.IndexOf('/'); return i < 0 ? id : id.Substring(0, i); }

        public static Dictionary<string, string> Load()
        {
            var known = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!File.Exists(Repo.KnownFailures)) return known;
            var root = JObject.Parse(File.ReadAllText(Repo.KnownFailures));
            foreach (JProperty issue in root["issues"])
                foreach (var c in issue.Value["cases"]) known[(string)c] = issue.Name;
            return known;
        }

        public static void Save(IEnumerable<Failure> failures)
        {
            var groups = failures.GroupBy(f => f.Id).Select(g => g.First()).GroupBy(IssueOf).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
            var issues = new JObject();
            foreach (var g in groups)
            {
                string title; Titles.TryGetValue(g.Key, out title);
                issues[g.Key] = new JObject
                {
                    { "title", title ?? "" },
                    { "count", g.Count() },
                    { "cases", new JArray(g.Select(f => f.Id).OrderBy(x => x, StringComparer.Ordinal)) }
                };
            }
            var root = new JObject
            {
                { "about", "Rule fixture cases that fail today, by owning issue. Case ids are check|rule|context|case. CI fails on a failure not listed here and on a listed case that now passes: only ever remove entries (run with FLAREAI_UPDATE_KNOWN=1 after a fix)." },
                { "issues", issues }
            };
            Directory.CreateDirectory(Path.GetDirectoryName(Repo.KnownFailures));
            File.WriteAllText(Repo.KnownFailures, root.ToString(Formatting.Indented).Replace("\r\n", "\n") + "\n", new UTF8Encoding(false));
        }
    }
}
