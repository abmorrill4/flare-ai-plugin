using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlareAI.Scoring;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FlareAI.Scoring.Tests
{
    /// <summary>The converted DigitalBanking labels against the real corpus, and the schema files against the code.</summary>
    public class CorpusLabelTests
    {
        private static readonly string Root = B.RepoRoot();
        private static readonly string Project = Path.Combine(Root, "corpus", "DigitalBanking");
        private static readonly string LabelsPath = Path.Combine(Project, "labels.jsonl");

        private static List<Label> Labels() { return LabelIO.Read(LabelsPath); }

        private static ScoreReport LintOnlyReport(ScoreOptions o)
        {
            var findings = FindingIO.Read(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fixtures", "bench-v1-findings.jsonl"));
            return Scorer.Score(Labels(), findings, o);
        }

        [Fact]
        public void All_429_v1_defects_are_converted_to_verified_violations()
        {
            var v = Labels().Where(l => l.Kind == LabelKind.Violation).ToList();
            Assert.Equal(429, v.Count);
            Assert.All(v, l => Assert.Equal("verified", l.Status));
            Assert.All(v, l => Assert.Equal("seeded", l.Origin));
            var ids = new HashSet<string>(v.Select(l => (string)l.Seed["defect"]));
            Assert.Equal(429, ids.Count);
            var truth = JObject.Parse(File.ReadAllText(Path.Combine(Root, "corpus", "ground-truth.json")));
            var expected = ((JArray)truth["defects"]).Select(d => (string)d["id"]).ToList();
            Assert.Equal(expected.OrderBy(x => x), ids.OrderBy(x => x));
        }

        [Fact]
        public void Every_label_is_valid_and_anchors_uniquely_in_the_corpus()
        {
            var kinds = DefectTaxonomy.LoadIds(Path.Combine(Root, "corpus", "schema", "defects.yaml"));
            var errors = LabelValidator.Validate(Labels(), kinds, new CorpusBlockTextSource(Project));
            Assert.True(errors.Count == 0, string.Join("\n", errors.Take(20)));
        }

        [Fact]
        public void Conversion_is_deterministic_and_matches_the_committed_file()
        {
            var conv = new V1Converter(Project, "DigitalBanking");
            var labels = conv.Convert(Path.Combine(Root, "corpus", "ground-truth.json"));
            Assert.Empty(conv.Problems);
            string tmp = Path.GetTempFileName();
            try
            {
                LabelIO.Write(tmp, labels);
                Assert.Equal(File.ReadAllText(LabelsPath).Replace("\r\n", "\n"), File.ReadAllText(tmp));
            }
            finally { File.Delete(tmp); }
        }

        [Fact]
        public void Boilerplate_and_bold_ui_labels_are_labelled_clean_or_exempt()
        {
            var l = Labels();
            Assert.True(l.Count(x => x.Kind == LabelKind.Exempt && x.Rules.Contains("google:word-list/221") && x.Context.Span.Contains("boilerplate")) >= 25);
            Assert.True(l.Count(x => x.Kind == LabelKind.Exempt && x.Rules.Contains("google:word-list/640") && x.Quote == "Sign-On Settings") >= 7);
            Assert.True(l.Count(x => x.Kind == LabelKind.Clean && x.Context.Span.Contains("ui-label")) > 100);
            Assert.All(l.Where(x => x.Kind != LabelKind.Violation), x => Assert.False(string.IsNullOrEmpty(x.Reason)));
        }

        [Fact]
        public void Split_is_by_file_and_about_thirty_percent_test()
        {
            var l = Labels();
            foreach (var g in l.GroupBy(x => x.File)) Assert.Single(g.Select(x => x.Split).Distinct());
            var files = l.GroupBy(x => x.File).ToList();
            double test = files.Count(g => g.First().Split == "test") / (double)files.Count;
            Assert.InRange(test, 0.2, 0.4);
        }

        [Fact]
        public void Lint_only_bench_reproduces_the_hand_counted_google_precision()
        {
            // dist/audit/precision.md: Google lint rules fire 57 times, 23 are true positives (24 bench credits minus one accidental).
            var r = LintOnlyReport(null);
            var google = r.Get(ScoreReport.Layer, "google");
            Assert.Equal(23, google.Tp);
            Assert.Equal(34, google.Fp);
            Assert.Equal(0, google.WrongRule);
            Assert.Equal(0, google.Unscored);
            Assert.Equal(57, google.ScoredFindings);
            Assert.Equal(23.0 / 57.0, google.Precision, 6);
        }

        [Fact]
        public void Lint_only_bench_reproduces_the_hand_counted_per_rule_precision()
        {
            var r = LintOnlyReport(null);
            Assert.Equal(26, r.Get(ScoreReport.Rule, "google:word-list/221").Fp);
            Assert.Equal(0, r.Get(ScoreReport.Rule, "google:word-list/221").Tp);
            Assert.Equal(7, r.Get(ScoreReport.Rule, "google:word-list/640").Fp);
            Assert.Equal(1, r.Get(ScoreReport.Rule, "google:units-of-measure/5").Fp);
            Assert.Equal(7, r.Get(ScoreReport.Rule, "google:phone-numbers/4").Tp);
            Assert.Equal(11, r.Get(ScoreReport.Rule, "google:tone/22").Tp);
            Assert.Equal(5, r.Get(ScoreReport.Rule, "google:abbreviations/18").Tp);
        }

        [Fact]
        public void Lint_only_bench_reproduces_the_other_layers_and_recall()
        {
            var r = LintOnlyReport(null);
            Assert.Equal(373, r.FindingCount);
            Assert.Equal(211, r.Get(ScoreReport.Layer, "company").Tp);   // 239 before DigitalBanking's customer/bank rows moved to its project guide
            Assert.Equal(0, r.Get(ScoreReport.Layer, "company").Fp);
            Assert.Equal(54, r.Get(ScoreReport.Layer, "structure").Tp);
            Assert.Equal(51, r.Get(ScoreReport.Layer, "project").Tp);    // 23 variables + 28 customer/bank terminology
            Assert.Equal(0, r.Overall.Unscored);
            Assert.Equal(1.0, r.Coverage);
            // 321 of 429 by the hand count; the one extra is the image without alt text that the v1 scorer could not tell from
            // its siblings (a finding with no block can only be matched to a whole-element label by rule).
            Assert.Equal(322, r.Overall.Detected);
        }

        [Fact]
        public void Scoring_with_the_text_source_gives_the_same_counts()
        {
            var plain = LintOnlyReport(null);
            var withText = LintOnlyReport(new ScoreOptions { TextSource = new CorpusBlockTextSource(Project) });
            Assert.Equal(plain.Overall.Tp, withText.Overall.Tp);
            Assert.Equal(plain.Overall.Fp, withText.Overall.Fp);
            Assert.Equal(0, withText.UnanchoredLabels);
        }

        [Fact]
        public void The_two_splits_partition_the_findings()
        {
            var dev = LintOnlyReport(new ScoreOptions { Split = "dev" });
            var test = LintOnlyReport(new ScoreOptions { Split = "test" });
            var all = LintOnlyReport(null);
            Assert.Equal(all.FindingCount, dev.FindingCount + test.FindingCount);
            Assert.Equal(all.Overall.Tp, dev.Overall.Tp + test.Overall.Tp);
            Assert.Equal(all.Overall.Fp, dev.Overall.Fp + test.Overall.Fp);
        }
    }

    /// <summary>The checked-in schema files stay in step with the code that reads and validates labels.</summary>
    public class SchemaFileTests
    {
        private static readonly string SchemaDir = Path.Combine(B.RepoRoot(), "corpus", "schema");

        private static JObject Schema() { return JObject.Parse(File.ReadAllText(Path.Combine(SchemaDir, "labels.schema.json"))); }

        private static string[] Enum(JToken t) { return t["enum"].Select(x => (string)x).ToArray(); }

        [Fact]
        public void Schema_enums_match_the_code_vocabulary()
        {
            var p = Schema()["properties"];
            Assert.Equal(LabelKind.All, Enum(p["label"]));
            Assert.Equal(ContextVocabulary.Line, Enum(p["context"]["properties"]["line"]["items"]));
            Assert.Equal(ContextVocabulary.Span, Enum(p["context"]["properties"]["span"]["items"]));
            Assert.Equal(ContextVocabulary.Status, Enum(p["status"]));
            Assert.Equal(ContextVocabulary.Origin, Enum(p["origin"]));
            Assert.Equal(ContextVocabulary.Split, Enum(p["split"]));
        }

        [Fact]
        public void Schema_properties_are_exactly_the_label_fields()
        {
            var props = ((JObject)Schema()["properties"]).Properties().Select(x => x.Name).OrderBy(x => x).ToArray();
            var fields = typeof(Label).GetFields().Select(f => ((Newtonsoft.Json.JsonPropertyAttribute)f.GetCustomAttributes(typeof(Newtonsoft.Json.JsonPropertyAttribute), false).FirstOrDefault())).Where(a => a != null).Select(a => a.PropertyName).OrderBy(x => x).ToArray();
            Assert.Equal(fields, props);
            var required = Schema()["required"].Select(x => (string)x).ToArray();
            Assert.Equal(new[] { "id", "file", "block", "label", "rules", "origin", "status", "by", "split" }, required);
        }

        [Fact]
        public void Taxonomy_lists_every_kind_the_v1_ground_truth_uses_and_no_id_twice()
        {
            var ids = File.ReadAllLines(Path.Combine(SchemaDir, "defects.yaml")).Where(l => l.StartsWith("- id:")).Select(l => l.Substring(5).Trim()).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
            var truth = JObject.Parse(File.ReadAllText(Path.Combine(B.RepoRoot(), "corpus", "ground-truth.json")));
            foreach (var k in ((JArray)truth["defects"]).Select(d => (string)d["kind"]).Distinct()) Assert.Contains(k, ids);
        }

        [Fact]
        public void Validator_reports_each_kind_of_problem()
        {
            var good = B.Violation("go!");
            Assert.Empty(LabelValidator.Validate(new[] { good }));

            Func<Action<Label>, string> bad = mutate =>
            {
                var l = B.Violation("go!"); mutate(l);
                return string.Join("|", LabelValidator.Validate(new[] { l }, new HashSet<string> { "exclaim" }));
            };
            Assert.Contains("violation needs a kind", bad(l => l.DefectKind = null));
            Assert.Contains("not in defects.yaml", bad(l => l.DefectKind = "mystery"));
            Assert.Contains("label must be one of", bad(l => l.Kind = "wrong"));
            Assert.Contains("rules must list", bad(l => l.Rules = new List<string>()));
            Assert.Contains("split must be", bad(l => l.Split = "train"));
            Assert.Contains("status must be", bad(l => l.Status = "ok"));
            Assert.Contains("origin must be", bad(l => l.Origin = "x"));
            Assert.Contains("block must be", bad(l => l.Block = "p[1]"));
            Assert.Contains("file must be relative", bad(l => l.File = "C:\\x.htm"));
            Assert.Contains("unknown context.line", bad(l => l.Context = new LabelContext { Line = { "sidebar" } }));
            Assert.Contains("unknown context.span", bad(l => l.Context = new LabelContext { Span = { "bold" } }));
            Assert.Contains("prefix/suffix need a quote", bad(l => { l.Quote = null; l.Prefix = "x"; }));
            Assert.Contains("by must name", bad(l => l.By = new List<string>()));
        }

        [Fact]
        public void Validator_flags_clean_without_reason_duplicate_ids_and_block_complete_with_quote()
        {
            var c = B.Clean("x"); c.Reason = null;
            Assert.Contains("need a reason", string.Join("|", LabelValidator.Validate(new[] { c })));
            var a = B.Violation("go!"); var b = B.Violation("go!"); b.Id = a.Id;
            Assert.Contains("duplicate id", string.Join("|", LabelValidator.Validate(new[] { a, b })));
            var bc = B.Complete(); bc.Quote = "x";
            Assert.Contains("take no quote", string.Join("|", LabelValidator.Validate(new[] { bc })));
        }

        [Fact]
        public void Validator_checks_anchors_when_given_the_text()
        {
            var src = new DictionaryBlockTextSource().Add(B.File, B.Block, "one two one");
            Assert.Empty(LabelValidator.Validate(new[] { B.Violation("two") }, null, src));
            Assert.Contains("quote not found", string.Join("|", LabelValidator.Validate(new[] { B.Violation("three") }, null, src)));
            Assert.Contains("ambiguous", string.Join("|", LabelValidator.Validate(new[] { B.Violation("one") }, null, src)));
            Assert.Empty(LabelValidator.Validate(new[] { B.Violation("one", prefix: "two ") }, null, src));
            var elsewhere = B.Violation("two", block: "/html/body/p[9]");
            Assert.Contains("block not found", string.Join("|", LabelValidator.Validate(new[] { elsewhere }, null, src)));
        }

        [Fact]
        public void Label_lines_round_trip()
        {
            var l = B.Violation("go!", prefix: "to ", suffix: " now");
            l.Context = new LabelContext { Line = { "step" }, Span = { "ui-label" } };
            l.Seed = new JObject { ["generator"] = "test" };
            var line = LabelIO.ToLine(l);
            Assert.DoesNotContain("\n", line);
            var back = Newtonsoft.Json.JsonConvert.DeserializeObject<Label>(line);
            Assert.Equal(LabelIO.ToLine(back), line);
            Assert.Equal("P", back.Project);
        }
    }
}
