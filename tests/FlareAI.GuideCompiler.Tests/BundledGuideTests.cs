using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FlareAI.Core.Style;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FlareAI.GuideCompiler.Tests
{
    /// <summary>
    /// The bundled Google guide as the passes left it (styleguide/.../Google and tools/google-style/compiled.json): the
    /// acceptance checks of #56-#59. Fails when a recompile brings a problem back without the passes catching it.
    /// </summary>
    public class BundledGuideTests
    {
        private static readonly string Root = FindRoot();
        private static readonly Lazy<Composition> Guide = new Lazy<Composition>(() => new StyleEngine(new string[0], Path.Combine(Root, "styleguide")).Compose(null));
        private static readonly Lazy<JObject> Compiled = new Lazy<JObject>(() => JObject.Parse(File.ReadAllText(Path.Combine(Root, @"tools\google-style\compiled.json"), Encoding.UTF8)));

        private static string FindRoot()
        {
            var d = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "FlareAI.sln"))) d = d.Parent;
            if (d == null) throw new InvalidOperationException("FlareAI.sln not found");
            return d.FullName;
        }

        private static List<LintRule> GoogleRows { get { return Guide.Value.Rules.Where(r => (r.Id ?? "").StartsWith("google:")).ToList(); } }

        // ---- #57 ---------------------------------------------------------------------------------------------------------

        [Theory]
        [InlineData("Store it on the PD.")]
        [InlineData("Touch and hold the icon.")]
        [InlineData("Ask the IG owner.")]
        [InlineData("Deploy it on GCP.")]
        public void Product_vocabulary_doesnt_lint_a_guide_without_those_products(string text)
        {
            var hits = Guide.Value.Lint(new[] { text }, null, true).Where(h => (h.RuleId ?? "").StartsWith("google:")).ToList();
            Assert.True(hits.Count == 0, string.Join("; ", hits.Select(h => h.RuleId + " '" + h.Match + "'").ToArray()));
        }

        [Fact]
        public void Lint_rows_carry_product_tags()
        {
            Assert.True(GoogleRows.Count(r => r.Products.Count > 0) >= 20);
            Assert.Contains(GoogleRows, r => r.Products.Contains("Android") && r.Avoid.Contains("touch and hold"));
        }

        // ---- #58 ---------------------------------------------------------------------------------------------------------

        [Fact]
        public void Every_term_cell_names_only_its_own_entry()
        {
            foreach (var r in ((JArray)Compiled.Value["word-list"]["rules"]).Where(r => (string)r["kind"] == "term"))
            {
                var said = Regex.Replace(Regex.Replace((string)r["heading"] + " " + (string)r["text"], @"\([^)]*\)", ""), @"\s+", " ").ToLowerInvariant();
                foreach (var f in Topic.TermForms((string)r["term"]))
                    Assert.True(said.Contains(f.ToLowerInvariant()), r["id"] + ": Term '" + r["term"] + "' names '" + f + "', which is neither its heading nor its rule text");
            }
        }

        [Fact]
        public void Please_sends_only_the_please_rules_of_the_p_section()
        {
            var engine = new StyleEngine(new string[0], Path.Combine(Root, "styleguide"));
            var ids = engine.RuleIdsFor(null, GuideScope.Of(new string[0], "Please click Save."));
            var items = engine.Rules(null).Where(i => ids.Contains(i.Id) && i.Id.StartsWith("google:word-list/") && i.Triggers.Count > 0).ToList();
            var p = items.Where(i => i.Triggers.Any(t => t.StartsWith("p", StringComparison.OrdinalIgnoreCase))).ToList();
            Assert.NotEmpty(p);
            Assert.All(p, i => Assert.Contains(i.Triggers, t => t.Equals("please", StringComparison.OrdinalIgnoreCase)));
        }

        // ---- #59 ---------------------------------------------------------------------------------------------------------

        [Theory]
        [InlineData("Use a supported format, e.g. JSON.")]
        [InlineData("E.g. JSON works.")]
        [InlineData("Use the default region, i.e., the closest one.")]
        public void Fixing_once_leaves_nothing_behind(string text)
        {
            var hits = Guide.Value.Lint(new[] { text }, null).Where(h => h.Replacement != null).ToList();
            Assert.NotEmpty(hits);
            var t = text;
            foreach (var h in hits.OrderByDescending(h => h.Start)) t = t.Substring(0, h.Start) + h.Replacement + t.Substring(h.Start + h.Length);
            var after = Guide.Value.Lint(new[] { t }, null);
            Assert.True(after.Count == 0, "'" + t + "': " + string.Join("; ", after.Select(h => h.RuleId + " '" + h.Match + "'").ToArray()));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void The_validator_reports_no_errors_on_the_google_layer(bool everyProduct)
        {
            var tmp = Path.Combine(Path.GetTempPath(), "guidetests-" + Guid.NewGuid().ToString("N"));
            try
            {
                var comp = Guide.Value;
                if (everyProduct)
                {
                    // A team guide that covers every product: the Product-tagged rows lint, so their Examples are checked too.
                    Directory.CreateDirectory(Path.Combine(tmp, @"Project\ConditionTagSets"));
                    File.WriteAllText(Path.Combine(tmp, "Team.flprj"), "<CatapultProject />");
                    File.WriteAllText(Path.Combine(tmp, @"Project\ConditionTagSets\Product.flcts"),
                        "<CatapultConditionTagSet><ConditionTag Name=\"Android\" /><ConditionTag Name=\"GoogleCloud\" /><ConditionTag Name=\"GoogleWorkspace\" /></CatapultConditionTagSet>");
                    comp = new StyleEngine(new[] { tmp }, Path.Combine(Root, "styleguide")).Compose(null);
                    Assert.Contains("GoogleCloud", comp.OurProducts);
                }
                var errors = comp.Validate().Issues.Where(i => i.Level == "error" && i.Layer == comp.Layers[0].Name).ToList();
                Assert.True(errors.Count == 0, string.Join("\n", errors.Select(e => e.Location + ": " + e.Message).ToArray()));
            }
            finally { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); }
        }

        // ---- #56 ---------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Each row's Avoid terms (as written and in lower case, in a sentence) linted with every rule: two Google rules on the
        /// exact same words must agree on the fix, and only rules that fire in different places (scopes neither of which
        /// contains the other) may share a span.
        /// </summary>
        [Fact]
        public void No_two_google_rules_own_the_same_words()
        {
            var rows = GoogleRows;
            var probes = rows.SelectMany(r => r.Avoid.Where(a => a.Trim().Length > 0))
                             .Where(a => !Regex.IsMatch(a, @"\\|\[|\(\?"))   // literal terms, not patterns
                             .SelectMany(a => new[] { "Use " + a + " here.", "Use " + a.ToLowerInvariant() + " here." })
                             .Concat(rows.Select(r => r.Example).Where(e => !string.IsNullOrEmpty(e))).Distinct().ToList();
            var hits = Guide.Value.Lint(probes, null, true).Where(h => (h.RuleId ?? "").StartsWith("google:")).ToList();
            var byId = rows.ToDictionary(r => r.Id);
            var shared = new List<string>();
            foreach (var g in hits.GroupBy(h => h.Index + ":" + h.Start + ":" + h.Match.TrimEnd().Length))
            {
                var ids = g.Select(h => h.RuleId).Distinct().ToList();
                if (ids.Count < 2) continue;
                var fixes = g.Where(h => !string.IsNullOrEmpty(h.Replacement)).Select(h => h.Replacement.Trim().TrimEnd(',').ToLowerInvariant()).Distinct().ToList();
                Assert.True(fixes.Count <= 1, "different fixes for '" + g.First().Match + "': " + string.Join(", ", ids.ToArray()));
                var scopes = ids.Select(i => new HashSet<string>(byId[i].Applies.Where(a => Structural.Contains(a)))).ToList();
                var disjoint = scopes.All(s => s.Count > 0) && scopes.Select(s => string.Join(",", s.OrderBy(x => x).ToArray())).Distinct().Count() == scopes.Count;
                if (!disjoint) shared.Add("'" + g.First().Match + "': " + string.Join(", ", ids.OrderBy(x => x).ToArray()));
            }
            Assert.True(shared.Count == 0, shared.Distinct().Count() + " span(s) owned twice:\n" + string.Join("\n", shared.Distinct().Take(30).ToArray()));
        }

        private static readonly HashSet<string> Structural = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Headings", "Steps", "Lists", "Tables", "Notes", "Links", "UI", "Examples", "API", "Code", "Images", "Captions" };

        [Fact]
        public void Every_source_unit_of_a_compiled_page_is_covered_marked_or_reported()
        {
            var units = JObject.Parse(File.ReadAllText(Path.Combine(Root, @"tools\google-style\source\units.json"), Encoding.UTF8));
            foreach (var p in Compiled.Value.Properties())
            {
                var c = (JObject)p.Value;
                var seen = new HashSet<string>(((JArray)c["rules"]).SelectMany(r => r["units"].Select(u => (string)u)));
                seen.UnionWith(((JArray)c["not_applicable"]).Select(x => (string)x["unit"]));
                seen.UnionWith(((JArray)c["uncovered"]).Select(x => (string)x));
                // Rows the passes dropped as duplicates handed their units to a rule on another page.
                seen.UnionWith(Compiled.Value.Properties().SelectMany(q => ((JArray)q.Value["rules"]).SelectMany(r => r["units"].Select(u => (string)u))));
                var dropped = ((JArray)c["dropped"]).SelectMany(d => (d["rule"]?["units"] ?? new JArray()).Select(u => (string)u));
                seen.UnionWith(dropped);
                var missing = ((JArray)units[p.Name]).Select(u => (string)u["id"]).Where(id => !seen.Contains(id)).ToList();
                Assert.True(missing.Count == 0, p.Name + ": " + string.Join(", ", missing.ToArray()));
            }
        }
    }
}
