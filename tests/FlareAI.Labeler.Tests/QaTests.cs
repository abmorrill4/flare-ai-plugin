using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlareAI.Scoring;
using Xunit;

namespace FlareAI.Labeler.Tests
{
    public class QaTests
    {
        private const string F = "Content/a.htm";
        private const string P1 = "/html/body/p[1]";

        private static QaReport Run(TempProject p, params Label[] labels)
        {
            return LabelQa.Run(p.Open(), labels, TempProject.Taxonomy(), null);
        }

        private static string[] Codes(QaReport r) { return r.Issues.Select(i => i.Code).OrderBy(x => x).ToArray(); }

        [Fact]
        public void A_clean_project_has_no_findings_and_counts_coverage()
        {
            using (var p = new TempProject())
            {
                p.Topic(F, "<p>Click here to utilize the app!</p><p>Second paragraph with text.</p>");
                var r = Run(p,
                    p.Label("violation", F, P1, "utilize", "utilize", new[] { "company:plain-language/*" }),
                    p.Label("block-complete", F, P1, null, status: "proposed", by: new[] { "generator" }));
                Assert.Empty(r.Issues.Where(i => i.Severity != "info"));
                Assert.Equal(2, r.Stats.Blocks);
                Assert.Equal(1, r.Stats.BlocksLabelled);
                Assert.Equal(0.5, r.Stats.BlockCoverage);
                Assert.Equal(1, r.Stats.Violations);
                Assert.Equal(1, r.Stats.BlocksComplete);
                Assert.Equal(0, r.Stats.BlocksCompleteVerified);
                Assert.Equal(1, r.Stats.ScoredLabels);
            }
        }

        [Fact]
        public void A_quote_that_no_longer_anchors_is_an_error()
        {
            using (var p = new TempProject())
            {
                p.Topic(F, "<p>Click here to use the app!</p>");
                var r = Run(p, p.Label("violation", F, P1, "utilize", "utilize", new[] { "company:plain-language/*" }));
                Assert.Contains("unanchored", Codes(r));
                Assert.Equal(1, r.Count("error"));
            }
        }

        [Fact]
        public void A_missing_block_and_an_ambiguous_quote_are_errors()
        {
            using (var p = new TempProject())
            {
                p.Topic(F, "<p>go! and go!</p>");
                var r = Run(p,
                    p.Label("violation", F, "/html/body/p[7]", "go!", "exclaim", new[] { "google:*" }),
                    p.Label("violation", F, P1, "go!", "exclaim", new[] { "google:*" }));
                Assert.Contains("unanchored-block", Codes(r));
                Assert.Contains("ambiguous-quote", Codes(r));
            }
        }

        [Fact]
        public void Unknown_rule_ids_are_errors()
        {
            using (var p = new TempProject())
            {
                p.Topic(F, "<p>Wow!</p>");
                var r = Run(p, p.Label("violation", F, P1, "Wow!", "exclaim", new[] { "google:exclamation-points" }));
                Assert.Contains("rule-vocabulary", Codes(r));
            }
        }

        [Fact]
        public void Exact_duplicates_and_same_kind_overlaps()
        {
            using (var p = new TempProject())
            {
                p.Topic(F, "<p>Please utilize it now.</p>");
                var a = p.Label("violation", F, P1, "utilize", "utilize", new[] { "lint" });
                var b = p.Label("violation", F, P1, "utilize", "utilize", new[] { "lint" });
                var c = p.Label("violation", F, P1, "utilize it", "utilize", new[] { "lint" });
                var r = Run(p, a, b, c);
                Assert.Contains("duplicate-label", Codes(r));
                Assert.Contains("overlap-same-kind", Codes(r));
            }
        }

        [Fact]
        public void Overlapping_kinds_are_a_note_not_a_failure()
        {
            using (var p = new TempProject())
            {
                p.Topic(F, "<p>Please utilize it.</p>");
                var r = Run(p,
                    p.Label("violation", F, P1, "Please utilize", "utilize", new[] { "lint" }),
                    p.Label("violation", F, P1, "utilize", "passive", new[] { "ai:passive" }));
                Assert.Equal(new[] { "overlap-kinds" }, Codes(r));
                Assert.Equal("info", r.Issues[0].Severity);
            }
        }

        [Fact]
        public void A_violation_over_a_clean_span_for_the_same_rules_is_a_conflict()
        {
            using (var p = new TempProject())
            {
                p.Topic(F, "<p>Please <b>utilize</b> it.</p>");
                var v = p.Label("violation", F, P1, "utilize", "utilize", new[] { "google:*" });
                var clean = p.Label("clean", F, P1, "utilize", rules: new[] { "*" }, by: new[] { "hand-read" });
                var r = Run(p, v, clean);
                Assert.Equal(new[] { "violation-vs-clean" }, Codes(r));
                Assert.Equal("error", r.Issues[0].Severity);
            }
        }

        [Fact]
        public void A_clean_label_for_other_rules_does_not_conflict()
        {
            using (var p = new TempProject())
            {
                p.Topic(F, "<p>Please <b>utilize</b> it.</p>");
                var v = p.Label("violation", F, P1, "utilize", "utilize", new[] { "google:*" });
                var exempt = p.Label("exempt", F, P1, "utilize", rules: new[] { "company:terminology/*" }, by: new[] { "hand-read" });
                Assert.Empty(Run(p, v, exempt).Issues.Where(i => i.Severity != "info"));
            }
        }

        [Fact]
        public void A_violation_inside_code_or_a_code_block_is_flagged()
        {
            using (var p = new TempProject())
            {
                p.Topic(F, "<p>Run <code>utilize --now</code> today.</p><pre>utilize all</pre>");
                var r = Run(p,
                    p.Label("violation", F, P1, "utilize", "utilize", new[] { "lint" }),
                    p.Label("violation", F, "/html/body/pre[1]", "utilize", "utilize", new[] { "lint" }));
                Assert.Equal(new[] { "violation-on-exempt-markup", "violation-on-exempt-markup" }, Codes(r));
            }
        }

        [Fact]
        public void Kind_and_rule_must_fit()
        {
            using (var p = new TempProject())
            {
                p.Topic(F, "<p>Wow! It is done.</p>");
                var tax = TempProject.Taxonomy();
                var reference = new List<Label> { p.Label("violation", F, P1, "Wow!", "exclaim", new[] { "google:*" }) };
                var wrongLayer = p.Label("violation", F, P1, "Wow!", "exclaim", new[] { "structure:AltText" });
                var aiMismatch = p.Label("violation", F, P1, "It is done", "passive", new[] { "ai:utilize" });
                var r = LabelQa.Run(p.Open(), new[] { wrongLayer, aiMismatch }, tax, reference);
                Assert.Contains("kind-rule", Codes(r));
                Assert.Contains("ai-kind-mismatch", Codes(r));
            }
        }

        [Fact]
        public void Without_a_reference_the_detect_layer_decides()
        {
            using (var p = new TempProject())
            {
                p.Topic(F, "<p>Call 555.1234 now.</p>");
                var r = Run(p, p.Label("violation", F, P1, "555.1234", "phone", new[] { "google:*" }));
                Assert.Contains("kind-rule", Codes(r));
            }
        }

        [Fact]
        public void Split_must_follow_project_json_and_every_label_needs_a_known_source()
        {
            using (var p = new TempProject())
            {
                p.Topic(F, "<p>Wow!</p>").Split(F);
                var r = Run(p, p.Label("violation", F, P1, "Wow!", "exclaim", new[] { "google:*" }, split: "dev", by: new[] { "a-friend" }));
                Assert.Contains("split-mismatch", Codes(r));
                Assert.Contains("unknown-source", Codes(r));
            }
        }

        [Fact]
        public void A_verified_block_complete_needs_both_labellers_or_a_human()
        {
            using (var p = new TempProject())
            {
                p.Topic(F, "<p>Wow!</p>");
                var r = Run(p, p.Label("block-complete", F, P1, null, status: "verified", by: new[] { "generator" }));
                Assert.Contains("block-complete-unconfirmed", Codes(r));
                var ok = Run(p, p.Label("block-complete", F, P1, null, status: "verified", by: new[] { "llm-a", "llm-b" }));
                Assert.DoesNotContain("block-complete-unconfirmed", Codes(ok));
            }
        }

        [Fact]
        public void Id_prefix_must_be_the_project()
        {
            using (var p = new TempProject("one"))
            {
                p.Topic(F, "<p>Wow!</p>");
                var l = p.Label("violation", F, P1, "Wow!", "exclaim", new[] { "google:*" });
                l.Id = "other/L0001";
                Assert.Contains("id-project", Codes(Run(p, l)));
            }
        }

        [Fact]
        public void The_labels_md_section_is_replaced_in_place_and_keeps_line_endings()
        {
            using (var p = new TempProject())
            {
                p.Topic(F, "<p>Wow!</p>");
                var r = Run(p, p.Label("violation", F, P1, "Wow!", "exclaim", new[] { "google:*" }));
                string block = LabelQa.Markdown(r);
                string first = Md.Splice("# Labels\r\n\r\ntext\r\n", "qa", block);
                Assert.Contains("<!-- labeler:qa -->", first);
                Assert.DoesNotContain("\n\n\n", first.Replace("\r\n", "\n"));
                Assert.Equal(first, Md.Splice(first, "qa", block));                           // idempotent
                string changed = Md.Splice(first, "qa", block.Replace("1", "2"));
                Assert.Equal(1, changed.Split(new[] { "<!-- labeler:qa -->" }, StringSplitOptions.None).Length - 1);
                Assert.All(changed.Split(new[] { "\r\n" }, StringSplitOptions.None), line => Assert.DoesNotContain("\n", line));
            }
        }

        [Fact]
        public void Thin_test_kinds_are_listed()
        {
            using (var p = new TempProject())
            {
                p.Topic(F, "<p>Wow! Please utilize.</p>").Split(F);
                var r = Run(p,
                    p.Label("violation", F, P1, "Wow!", "exclaim", new[] { "google:*" }, split: "test"),
                    p.Label("violation", F, P1, "utilize", "utilize", new[] { "lint" }, split: "test"));
                Assert.Equal(2, r.Stats.ViolationsVerifiedTest);
                Assert.Equal(2, r.Stats.ThinTestKinds.Count);
            }
        }
    }

    public class QaFixerTests
    {
        private const string F = "Content/a.htm";
        private const string P1 = "/html/body/p[1]";

        [Fact]
        public void Removes_duplicates_natural_copies_and_heuristic_clean_labels_on_a_seeded_span()
        {
            using (var p = new TempProject())
            {
                p.Topic(F, "<p>Please <b>utilize</b> it! Wow!</p><p><img src=\"x.png\"/></p>");
                var seeded = p.Label("violation", F, P1, "utilize", "utilize", new[] { "lint" });
                var dupe = p.Label("violation", F, P1, "Wow!", "exclaim", new[] { "google:*" });
                var dupe2 = p.Label("violation", F, P1, "Wow!", "exclaim", new[] { "google:*" });
                var ui = p.Label("clean", F, P1, "utilize", rules: new[] { "*" }, by: new[] { "source-markup" }, origin: "natural");
                var img = p.Label("violation", F, "/html/body/p[2]/img[1]", null, "alt_missing", new[] { "structure:AltText" });
                var imgNatural = p.Label("violation", F, "/html/body/p[2]/img[1]", null, "alt_missing", new[] { "structure:AltText" }, origin: "natural", by: new[] { "dom-check" });
                var labels = new List<Label> { seeded, dupe, dupe2, ui, img, imgNatural };

                var notes = QaFixer.Apply(p.Open(), labels);

                Assert.Equal(3, notes.Count);
                Assert.DoesNotContain(dupe2, labels);
                Assert.DoesNotContain(ui, labels);
                Assert.DoesNotContain(imgNatural, labels);
                Assert.Contains(seeded, labels);
                Assert.Contains(img, labels);
            }
        }

        [Fact]
        public void A_human_verified_clean_label_is_never_removed()
        {
            using (var p = new TempProject())
            {
                p.Topic(F, "<p>Please <b>utilize</b> it!</p>");
                var seeded = p.Label("violation", F, P1, "utilize", "utilize", new[] { "lint" });
                var clean = p.Label("clean", F, P1, "utilize", rules: new[] { "*" }, by: new[] { "human" }, origin: "natural");
                var labels = new List<Label> { seeded, clean };
                Assert.Empty(QaFixer.Apply(p.Open(), labels));
                Assert.Equal(2, labels.Count);
            }
        }
    }

    /// <summary>The checked-in v2 corpus must keep passing the QA that gates it.</summary>
    public class RealCorpusTests
    {
        private static IEnumerable<string> Projects()
        {
            string v2 = CorpusProject.FindUp(AppDomain.CurrentDomain.BaseDirectory, "corpus/v2");
            return Directory.GetDirectories(v2).Where(d => File.Exists(Path.Combine(d, "labels.jsonl")));
        }

        [Fact]
        public void Every_v2_project_passes_label_qa_without_errors()
        {
            string defects = CorpusProject.FindUp(AppDomain.CurrentDomain.BaseDirectory, "corpus/schema/defects.yaml");
            string dbPath = CorpusProject.FindUp(AppDomain.CurrentDomain.BaseDirectory, "corpus/DigitalBanking/labels.jsonl");
            var tax = Taxonomy.Load(defects);
            var reference = LabelIO.Read(dbPath);
            var failures = new List<string>();
            foreach (var dir in Projects())
            {
                var project = CorpusProject.Open(dir);
                var rep = LabelQa.Run(project, project.ReadLabels(), tax, reference);
                failures.AddRange(rep.Issues.Where(i => i.Severity == "error").Take(5).Select(i => project.Name + " " + i.Code + " " + i.LabelId + ": " + i.Message));
            }
            Assert.Empty(failures);
        }

        [Fact]
        public void Normalizing_the_checked_in_labels_changes_nothing()
        {
            string defects = CorpusProject.FindUp(AppDomain.CurrentDomain.BaseDirectory, "corpus/schema/defects.yaml");
            var tax = Taxonomy.Load(defects);
            foreach (var dir in Projects())
            {
                var project = CorpusProject.Open(dir);
                var res = Normalizer.Run(project.ReadLabels(), tax, project);
                Assert.True(res.RulesRewritten == 0 && res.Promoted == 0, project.Name + ": rewritten " + res.RulesRewritten + ", promoted " + res.Promoted);
            }
        }
    }
}
