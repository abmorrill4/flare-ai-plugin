using System.Linq;
using FlareAI.Scoring;
using Xunit;

namespace FlareAI.Scoring.Tests
{
    /// <summary>One test per overlap case. Block text used throughout: "Ready to go! Please note this.", violation quote "go!" at [9,12).</summary>
    public class ScorerOverlapTests
    {
        private const string Text = "Ready to go! Please note this.";

        private static ScoreReport ScorePos(Label label, ScoreFinding f) { return B.Run(new[] { label }, new[] { f }, B.WithText(Text)); }

        private static Label V() { return B.Violation("go!", prefix: "Ready to ", suffix: " Please note"); }

        [Fact]
        public void Exact_overlap_is_a_true_positive()
        {
            var r = ScorePos(V(), B.At(9, 12));
            Assert.Equal(Outcome.TruePositive, r.Findings[0].Outcome);
            Assert.Equal(1, r.Overall.Tp);
            Assert.Equal(Detection.Detected, r.Labels[0].Detection);
        }

        [Fact]
        public void Partial_overlap_at_the_start_counts()
        {
            Assert.Equal(Outcome.TruePositive, ScorePos(V(), B.At(5, 10)).Findings[0].Outcome);
        }

        [Fact]
        public void Partial_overlap_at_the_end_counts()
        {
            Assert.Equal(Outcome.TruePositive, ScorePos(V(), B.At(11, 20)).Findings[0].Outcome);
        }

        [Fact]
        public void Finding_inside_the_label_counts()
        {
            Assert.Equal(Outcome.TruePositive, ScorePos(V(), B.At(11, 12)).Findings[0].Outcome);
        }

        [Fact]
        public void Finding_around_the_label_counts()
        {
            Assert.Equal(Outcome.TruePositive, ScorePos(V(), B.At(0, 30)).Findings[0].Outcome);
        }

        [Fact]
        public void Adjacent_finding_does_not_overlap()
        {
            var r = ScorePos(V(), B.At(5, 9));
            Assert.Equal(Outcome.Unscored, r.Findings[0].Outcome);
            Assert.Equal(Detection.Missed, r.Labels[0].Detection);
            Assert.Equal(1, r.Overall.Fn);
        }

        [Fact]
        public void Adjacent_finding_after_the_label_does_not_overlap()
        {
            Assert.Equal(Outcome.Unscored, ScorePos(V(), B.At(12, 14)).Findings[0].Outcome);
        }

        [Fact]
        public void Disjoint_finding_in_the_same_block_is_unscored()
        {
            var r = ScorePos(V(), B.At(13, 19));
            Assert.Equal(Outcome.Unscored, r.Findings[0].Outcome);
            Assert.Equal(1, r.Overall.Unscored);
        }

        [Fact]
        public void Same_offsets_in_another_block_do_not_overlap()
        {
            var r = ScorePos(V(), B.At(9, 12, block: "/html/body/p[2]"));
            Assert.Equal(Outcome.Unscored, r.Findings[0].Outcome);
        }

        [Fact]
        public void Same_offsets_in_another_file_do_not_overlap()
        {
            var r = ScorePos(V(), B.At(9, 12, file: "Content/b.htm"));
            Assert.Equal(Outcome.Unscored, r.Findings[0].Outcome);
        }

        [Fact]
        public void Right_text_other_rule_is_wrong_rule_not_false_positive()
        {
            var r = ScorePos(V(), B.At(9, 12, rule: "google:word-list/9"));
            Assert.Equal(Outcome.WrongRule, r.Findings[0].Outcome);
            Assert.Equal(0, r.Overall.Fp);
            Assert.Equal(1, r.Overall.WrongRule);
            Assert.Equal(Detection.WrongRuleOnly, r.Labels[0].Detection);
            Assert.Equal(0, r.Overall.Fn);
        }

        [Fact]
        public void Right_rule_wins_over_a_wrong_rule_on_the_same_span()
        {
            var r = B.Run(new[] { V() }, new[] { B.At(9, 12, rule: "google:word-list/9"), B.At(9, 12) }, B.WithText(Text));
            Assert.Equal(Detection.Detected, r.Labels[0].Detection);
            Assert.Equal(1, r.Overall.Tp);
            Assert.Equal(1, r.Overall.WrongRule);
        }

        [Fact]
        public void Two_findings_on_one_violation_are_two_true_positives_and_one_detection()
        {
            var r = B.Run(new[] { V() }, new[] { B.At(9, 12), B.At(10, 12) }, B.WithText(Text));
            Assert.Equal(2, r.Overall.Tp);
            Assert.Equal(1, r.Overall.Support);
            Assert.Equal(1, r.Overall.Detected);
        }

        [Fact]
        public void One_finding_over_two_violations_detects_both()
        {
            var a = B.Violation("Ready", rule: "google:*");
            var b = B.Violation("go!", rule: "google:*", prefix: "Ready to ");
            var r = B.Run(new[] { a, b }, new[] { B.At(0, 12) }, B.WithText(Text));
            Assert.Equal(2, r.Overall.Detected);
            Assert.Equal(1, r.Overall.Tp);
        }

        [Fact]
        public void Violation_without_any_finding_is_a_false_negative()
        {
            var r = B.Run(new[] { V() }, new ScoreFinding[0], B.WithText(Text));
            Assert.Equal(1, r.Overall.Fn);
            Assert.Equal(0.0, r.Overall.Recall);
        }

        [Fact]
        public void Finding_on_an_exempt_span_for_its_rule_is_a_false_positive()
        {
            var r = B.Run(new[] { B.Clean("Please note", "google:word-list/221", LabelKind.Exempt) }, new[] { B.At(13, 24, rule: "google:word-list/221") }, B.WithText(Text));
            Assert.Equal(Outcome.FalsePositive, r.Findings[0].Outcome);
            Assert.Equal("exempt", r.Findings[0].Reason);
        }

        [Fact]
        public void Exempt_for_one_rule_does_not_excuse_another_rule()
        {
            var r = B.Run(new[] { B.Clean("Please note", "google:word-list/221", LabelKind.Exempt) }, new[] { B.At(13, 24, rule: "company:plain-language/5") }, B.WithText(Text));
            Assert.Equal(Outcome.Unscored, r.Findings[0].Outcome);
        }

        [Fact]
        public void Clean_star_makes_any_rule_a_false_positive()
        {
            var r = B.Run(new[] { B.Clean("Please note") }, new[] { B.At(13, 24, rule: "company:plain-language/5") }, B.WithText(Text));
            Assert.Equal(Outcome.FalsePositive, r.Findings[0].Outcome);
            Assert.Equal("clean", r.Findings[0].Reason);
        }

        [Fact]
        public void True_positive_beats_a_clean_label_on_the_same_span()
        {
            var r = B.Run(new[] { V(), B.Clean("Ready to go!") }, new[] { B.At(9, 12) }, B.WithText(Text));
            Assert.Equal(Outcome.TruePositive, r.Findings[0].Outcome);
        }

        [Fact]
        public void Finding_in_a_block_complete_block_without_a_violation_is_a_false_positive()
        {
            var r = B.Run(new[] { V(), B.Complete() }, new[] { B.At(13, 19, rule: "company:plain-language/5") }, B.WithText(Text));
            Assert.Equal(Outcome.FalsePositive, r.Findings[0].Outcome);
            Assert.Equal("block-complete", r.Findings[0].Reason);
        }

        [Fact]
        public void Finding_on_a_violation_in_a_block_complete_block_is_still_a_true_positive()
        {
            var r = B.Run(new[] { V(), B.Complete() }, new[] { B.At(9, 12) }, B.WithText(Text));
            Assert.Equal(Outcome.TruePositive, r.Findings[0].Outcome);
        }

        [Fact]
        public void Block_complete_with_the_lint_rule_ignores_ai_findings()
        {
            var r = B.Run(new[] { B.Complete() }, new[] { B.At(13, 19, rule: null, origin: "ai") }, B.WithText(Text));
            Assert.Equal(Outcome.Unscored, r.Findings[0].Outcome);
        }

        [Fact]
        public void Block_complete_applies_to_a_finding_inside_a_nested_element_of_the_block()
        {
            var r = B.Run(new[] { B.Complete("/html/body/table[1]") }, new[] { B.At(0, 3, block: "/html/body/table[1]/tbody/tr[1]/td[1]") });
            Assert.Equal(Outcome.FalsePositive, r.Findings[0].Outcome);
        }

        [Fact]
        public void Finding_in_an_unlabelled_block_is_unscored_and_lowers_coverage()
        {
            var r = B.Run(new[] { V() }, new[] { B.At(9, 12), B.At(0, 3, block: "/html/body/p[7]") }, B.WithText(Text));
            Assert.Equal(1, r.Overall.Unscored);
            Assert.Equal(0.5, r.Coverage);
        }

        [Fact]
        public void Ambiguous_label_removes_the_finding_from_precision()
        {
            var r = B.Run(new[] { B.Ambiguous("Please note") }, new[] { B.At(13, 24) }, B.WithText(Text));
            Assert.Equal(Outcome.Ambiguous, r.Findings[0].Outcome);
            Assert.Equal(1.0, r.Overall.Precision);
            Assert.Equal(1.0, r.Coverage);
        }

        [Fact]
        public void Proposed_and_disputed_labels_are_ambiguous_by_default()
        {
            foreach (var status in new[] { "proposed", "disputed" })
            {
                var v = B.Violation("go!", status: status);
                var r = B.Run(new[] { v }, new[] { B.At(9, 12) }, B.WithText(Text));
                Assert.Equal(Outcome.Ambiguous, r.Findings[0].Outcome);
                Assert.Equal(0, r.Overall.Support);
            }
        }

        [Fact]
        public void Include_proposed_scores_proposed_but_never_disputed_labels()
        {
            var o = B.WithText(Text); o.IncludeProposed = true;
            Assert.Equal(Outcome.TruePositive, B.Run(new[] { B.Violation("go!", status: "proposed") }, new[] { B.At(9, 12) }, o).Findings[0].Outcome);
            Assert.Equal(Outcome.Ambiguous, B.Run(new[] { B.Violation("go!", status: "disputed") }, new[] { B.At(9, 12) }, o).Findings[0].Outcome);
        }

        [Fact]
        public void Repeated_quote_is_resolved_by_prefix_so_only_the_right_occurrence_overlaps()
        {
            const string t = "Go now. Go later.";
            var label = B.Violation("Go", rule: "*", prefix: "now. ");
            var first = B.Run(new[] { label }, new[] { B.At(0, 2) }, B.WithText(t));
            var second = B.Run(new[] { label }, new[] { B.At(8, 10) }, B.WithText(t));
            Assert.Equal(Outcome.Unscored, first.Findings[0].Outcome);
            Assert.Equal(Outcome.TruePositive, second.Findings[0].Outcome);
        }

        [Fact]
        public void Unresolvable_quote_is_reported_as_unanchored_and_falls_back_to_text()
        {
            var r = B.Run(new[] { B.Violation("nowhere", rule: "*") }, new[] { B.Text("nowhere") }, B.WithText(Text));
            Assert.Equal(1, r.UnanchoredLabels);
            Assert.Equal(Outcome.TruePositive, r.Findings[0].Outcome);
        }

        // ---- findings without a block (the v1 bench shape): text overlap ----------------------------------------------------

        [Fact]
        public void Text_fallback_matches_equal_ignoring_case_and_spacing()
        {
            var r = B.Run(new[] { B.Violation("Please  note") }, new[] { B.Text("please note", "google:tone/22") });
            Assert.Equal(Outcome.TruePositive, r.Findings[0].Outcome);
        }

        [Fact]
        public void Text_fallback_matches_a_longer_finding()
        {
            var r = B.Run(new[] { B.Violation("Please") }, new[] { B.Text("Please note that a") });
            Assert.Equal(Outcome.TruePositive, r.Findings[0].Outcome);
        }

        [Fact]
        public void Text_fallback_matches_a_shorter_finding()
        {
            var r = B.Run(new[] { B.Violation("go!") }, new[] { B.Text("!") });
            Assert.Equal(Outcome.TruePositive, r.Findings[0].Outcome);
        }

        [Fact]
        public void Text_fallback_ignores_a_lone_letter()
        {
            var r = B.Run(new[] { B.Violation("go!") }, new[] { B.Text("g") });
            Assert.Equal(Outcome.Unscored, r.Findings[0].Outcome);
        }

        [Fact]
        public void Text_fallback_needs_the_same_file()
        {
            var r = B.Run(new[] { B.Violation("go!") }, new[] { B.Text("go!", file: "Content/other.htm") });
            Assert.Equal(Outcome.Unscored, r.Findings[0].Outcome);
        }

        [Fact]
        public void File_paths_compare_with_either_slash()
        {
            var r = B.Run(new[] { B.Violation("go!") }, new[] { B.Text("go!", file: "Content\\a.htm") });
            Assert.Equal(Outcome.TruePositive, r.Findings[0].Outcome);
        }

        // ---- whole-element labels -----------------------------------------------------------------------------------------

        [Fact]
        public void Whole_element_label_with_unknown_finding_location_needs_the_rule()
        {
            var v = B.Make(LabelKind.Violation, null, new[] { "structure:AltText" }, null, null, "/html/body/p[1]/img[1]", B.File, "verified", "alt_missing");
            var hit = B.Run(new[] { v }, new[] { B.Text(".", "structure:AltText", origin: "structure") });
            var miss = B.Run(new[] { v }, new[] { B.Text(".", "structure:LinkText", origin: "structure") });
            Assert.Equal(Outcome.TruePositive, hit.Findings[0].Outcome);
            Assert.Equal(Outcome.Unscored, miss.Findings[0].Outcome);
        }

        [Fact]
        public void Whole_element_label_overlaps_findings_inside_the_element()
        {
            var v = B.Make(LabelKind.Violation, null, new[] { "structure:TableHeader" }, null, null, "/html/body/table[1]", B.File, "verified", "table_header");
            var inside = B.Run(new[] { v }, new[] { B.At(0, 1, "structure:TableHeader", block: "/html/body/table[1]/tbody/tr[1]/td[1]", origin: "structure") });
            var outside = B.Run(new[] { v }, new[] { B.At(0, 1, "structure:TableHeader", block: "/html/body/table[2]/tbody/tr[1]/td[1]", origin: "structure") });
            Assert.Equal(Outcome.TruePositive, inside.Findings[0].Outcome);
            Assert.Equal(Outcome.Unscored, outside.Findings[0].Outcome);
        }

        // ---- rules, ai, groups ---------------------------------------------------------------------------------------------

        [Fact]
        public void Ai_finding_matches_an_ai_kind_label_and_a_lint_finding_does_not()
        {
            var v = B.Violation("is reviewed", rule: "ai:passive", kind: "passive");
            var ai = new ScoreFinding { File = B.File, Excerpt = "is reviewed", Origin = "ai", Kind = "passive" };
            var lint = B.Text("is reviewed", "google:passive/1");
            var r = B.Run(new[] { v }, new[] { ai, lint });
            Assert.Equal(Outcome.TruePositive, r.Findings[0].Outcome);
            Assert.Equal(Outcome.WrongRule, r.Findings[1].Outcome);
            Assert.Equal("ai", r.Findings[0].Finding.Layer);
        }

        [Fact]
        public void Split_filter_scores_only_that_split()
        {
            var dev = B.Violation("go!", file: "Content/a.htm");
            var test = B.Violation("go!", file: "Content/b.htm"); test.Split = "test";
            var f = new[] { B.Text("go!", file: "Content/a.htm"), B.Text("go!", file: "Content/b.htm"), B.Text("zzz", file: "Content/b.htm") };
            var all = B.Run(new[] { dev, test }, f);
            var onlyTest = B.Run(new[] { dev, test }, f, new ScoreOptions { Split = "test" });
            Assert.Equal(2, all.Overall.Tp);
            Assert.Equal(1, onlyTest.Overall.Tp);
            Assert.Equal(1, onlyTest.Overall.Unscored);
            Assert.Equal(1, onlyTest.Overall.Support);
        }

        [Fact]
        public void Groups_are_kept_per_rule_layer_project_split_kind_and_context()
        {
            var v = B.Violation("go!", rule: "google:tone/22", kind: "exclaim");
            v.Context = new LabelContext(); v.Context.Line.Add("step"); v.Context.Span.Add("ui-label"); v.Context.Span.Add("conditional");
            var clean = B.Clean("zzz", "company:*"); clean.Context = new LabelContext(); clean.Context.Line.Add("note");
            var r = B.Run(new[] { v, clean }, new[] { B.Text("go!"), B.Text("zzz", "company:plain-language/5") });
            Assert.Equal(1, r.Get(ScoreReport.Rule, "google:tone/22").Tp);
            Assert.Equal(1, r.Get(ScoreReport.Rule, "google:tone/22").Detected);
            Assert.Equal(1, r.Get(ScoreReport.Rule, "company:plain-language/5").Fp);
            Assert.Equal(1, r.Get(ScoreReport.Layer, "google").Tp);
            Assert.Equal(1, r.Get(ScoreReport.Layer, "company").Fp);
            Assert.Equal(1, r.Get(ScoreReport.ContextLine, "step").Tp);
            Assert.Equal(1, r.Get(ScoreReport.ContextLine, "note").Fp);
            Assert.Equal(1, r.Get(ScoreReport.ContextSpan, "ui-label").Tp);
            Assert.Equal(1, r.Get(ScoreReport.ContextSpan, "conditional").Tp);
            Assert.Equal(1, r.Get(ScoreReport.Project, "P").Tp);
            Assert.Equal(1, r.Get(ScoreReport.Split, "dev").Tp);
            Assert.Equal(1, r.Get(ScoreReport.Kind, "exclaim").Tp);
            Assert.Equal(1, r.Get(ScoreReport.Kind, "exclaim").Support);
        }

        [Fact]
        public void Precision_recall_and_f1_follow_the_counts()
        {
            var labels = new[] { B.Violation("go!"), B.Violation("two", rule: "*"), B.Clean("bad") };
            var findings = new[] { B.Text("go!"), B.Text("bad", "company:claims/2") };
            var t = B.Run(labels, findings).Overall;
            Assert.Equal(0.5, t.Precision);
            Assert.Equal(0.5, t.Recall);
            Assert.Equal(0.5, t.F1);
        }

        [Fact]
        public void No_findings_and_no_labels_score_one()
        {
            var t = B.Run(new Label[0], new ScoreFinding[0]).Overall;
            Assert.Equal(1.0, t.Precision);
            Assert.Equal(1.0, t.Recall);
        }

        [Fact]
        public void Metrics_json_has_every_dimension()
        {
            var j = B.Run(new[] { B.Violation("go!") }, new[] { B.Text("go!") }).ToJson();
            foreach (var d in ScoreReport.Dimensions) Assert.NotNull(j[d]);
            Assert.Equal(1, (int)j["overall"]["tp"]);
        }
    }
}
