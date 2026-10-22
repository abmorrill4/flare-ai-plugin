using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlareAI.Core.Llm;
using FlareAI.Scoring;
using FlareAI.TestKit.Llm;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FlareAI.Labeler.Tests
{
    /// <summary>The blind pass with scripted models: no network, no money.</summary>
    public class PassTests
    {
        private const string F = "Content/a.htm";
        private const string A = "claude-sonnet-5-5";
        private const string B = "claude-opus-5-5";

        private static TempProject Project()
        {
            var p = new TempProject();
            p.Topic(F,
                "<h1>Getting started</h1>" +
                "<p>You can utilize the list to confirm the details of each transfer.</p>" +                  // p[1]
                "<p>Your new transfer is ready to go! Check the amount before you save.</p>" +             // p[2]
                "<ol><li><p>Select <b>Save</b> to keep the change.</p></li></ol>" +
                "<pre>utilize --all</pre>" +
                "<p>Each item is reviewed by the bank before release to the account.</p>");                  // p[3]
            return p;
        }

        private static string Answer(LlmRequest r, Func<string, string> spansForBlockText)
        {
            // the request's user text lists "[b1] (kind) text"; answer per block
            var blocks = new JArray();
            foreach (var line in r.User.Split('\n').Where(l => l.StartsWith("[b", StringComparison.Ordinal)))
            {
                int close = line.IndexOf(']');
                string id = line.Substring(1, close - 1);
                string text = line.Substring(line.IndexOf(") ", StringComparison.Ordinal) + 2);
                var spans = JArray.Parse(spansForBlockText(text));
                blocks.Add(new JObject { ["id"] = id, ["spans"] = spans });
            }
            return new JObject { ["blocks"] = blocks }.ToString(Formatting.None);
        }

        private static string Span(string quote, string kind, string before = "", string fix = "") { return new JObject { ["quote"] = quote, ["before"] = before, ["kind"] = kind, ["fix"] = fix }.ToString(Formatting.None); }

        private static ScriptedLlmClient Labeller(string model, Func<string, string> spans)
        {
            return new ScriptedLlmClient().When(r => r.Task == "label" && r.Model == model, r => ScriptedLlmClient.Reply.Text(Answer(r, spans))(r));
        }

        // ---- the plan ---------------------------------------------------------------------------------------------------

        [Fact]
        public void The_plan_offers_text_kinds_and_blocks_only_never_rule_ids_patterns_or_paths()
        {
            using (var p = Project())
            {
                var plan = LabelPlan.Build(p.Open(), TempProject.Taxonomy(), new PlanOptions());
                Assert.Equal(5, plan.Blocks);                    // h1, three paragraphs, the list item; the code block is not offered
                Assert.Contains("- exclaim: Exclamation mark", plan.System);
                Assert.Contains("Example: \"Your new transfer is ready to go!\"", plan.System);
                Assert.DoesNotContain("alt_missing", plan.System);   // a DOM check: nothing to read in a block of text
                string all = plan.System + string.Join("\n", plan.Batches.Select(b => b.User));
                foreach (var forbidden in new[] { "google:", "company:", "structure:", "pattern", "regex", "ruleId", "Content/a.htm", "labels.jsonl", "lint" })
                    Assert.DoesNotContain(forbidden, all);
                Assert.DoesNotContain("utilize --all", all);
            }
        }

        [Fact]
        public void Batches_respect_the_size_limits_and_a_sample_is_seeded()
        {
            using (var p = Project())
            {
                var one = LabelPlan.Build(p.Open(), TempProject.Taxonomy(), new PlanOptions { MaxBlocksPerBatch = 2 });
                Assert.Equal(new[] { 2, 2, 1 }, one.Batches.Select(b => b.Blocks.Count).ToArray());
                var s1 = LabelPlan.Build(p.Open(), TempProject.Taxonomy(), new PlanOptions { MaxBlocks = 3, Seed = 7 });
                var s2 = LabelPlan.Build(p.Open(), TempProject.Taxonomy(), new PlanOptions { MaxBlocks = 3, Seed = 7 });
                Assert.Equal(3, s1.Blocks);
                Assert.Equal(s1.Batches.Select(b => b.Hash).ToArray(), s2.Batches.Select(b => b.Hash).ToArray());
            }
        }

        [Fact]
        public void Split_selection_uses_project_json()
        {
            using (var p = Project())
            {
                p.Split(F);
                Assert.Equal(0, LabelPlan.Build(p.Open(), TempProject.Taxonomy(), new PlanOptions { Split = "dev" }).Blocks);
                Assert.Equal(5, LabelPlan.Build(p.Open(), TempProject.Taxonomy(), new PlanOptions { Split = "test" }).Blocks);
            }
        }

        // ---- dry run and cap ---------------------------------------------------------------------------------------------

        [Fact]
        public void The_default_is_a_dry_run_that_prices_and_sends_nothing()
        {
            using (var p = Project())
            {
                var a = new ScriptedLlmClient(); var b = new ScriptedLlmClient();
                var plan = LabelPlan.Build(p.Open(), TempProject.Taxonomy(), new PlanOptions());
                var sum = new LabelRunner(a, b, TempProject.Taxonomy(), p.Adjudication).Run(plan, new RunOptions());
                Assert.True(sum.DryRun);
                Assert.Equal(0, a.Calls + b.Calls);
                Assert.True(sum.EstimatedUsd > 0);
                Assert.True(sum.EstimateB.Usd > sum.EstimateA.Usd);       // opus costs more than sonnet
                Assert.False(Directory.Exists(p.Adjudication));           // not even a folder
            }
        }

        [Fact]
        public void Running_needs_a_cap_and_two_different_models()
        {
            using (var p = Project())
            {
                var plan = LabelPlan.Build(p.Open(), TempProject.Taxonomy(), new PlanOptions());
                var runner = new LabelRunner(new ScriptedLlmClient(), new ScriptedLlmClient(), TempProject.Taxonomy(), p.Adjudication);
                Assert.Throws<InvalidOperationException>(() => runner.Run(plan, new RunOptions { Execute = true }));
                Assert.Throws<InvalidOperationException>(() => runner.Run(plan, new RunOptions { Execute = true, MaxCostUsd = 1, ModelA = A, ModelB = A }));
            }
        }

        [Fact]
        public void A_cap_below_the_first_batch_sends_nothing()
        {
            using (var p = Project())
            {
                var a = Labeller(A, t => "[]"); var b = Labeller(B, t => "[]");
                var plan = LabelPlan.Build(p.Open(), TempProject.Taxonomy(), new PlanOptions());
                var sum = new LabelRunner(a, b, TempProject.Taxonomy(), p.Adjudication).Run(plan, new RunOptions { Execute = true, MaxCostUsd = 0.0000001 });
                Assert.True(sum.StoppedByBudget);
                Assert.Equal(0, a.Calls + b.Calls);
            }
        }

        [Fact]
        public void The_cap_is_never_passed_and_both_labellers_stay_level()
        {
            using (var p = Project())
            {
                // every answer really uses the full output ceiling, the worst case the guard prices
                Func<string, ScriptedLlmClient> heavy = model => new ScriptedLlmClient().When(r => r.Model == model,
                    r => new LlmResponse { Text = Answer(r, t => "[]"), Model = r.Model, StopReason = "end_turn", InputTokens = 400, OutputTokens = 1000 });
                var a = heavy(A); var b = heavy(B);
                var plan = LabelPlan.Build(p.Open(), TempProject.Taxonomy(), new PlanOptions { MaxBlocksPerBatch = 1 });
                Assert.Equal(5, plan.Batches.Count);
                var sum = new LabelRunner(a, b, TempProject.Taxonomy(), p.Adjudication).Run(plan, new RunOptions { Execute = true, MaxCostUsd = 0.1, MaxTokens = 1000 });
                Assert.True(sum.StoppedByBudget);
                Assert.InRange(sum.BatchesDoneByBoth, 1, 4);
                Assert.Equal(a.Calls, b.Calls);
                Assert.Equal(sum.BatchesDoneByBoth, a.Calls);
                Assert.True(sum.SpentUsd <= 0.1, "spent " + sum.SpentUsd);
                Assert.Contains(sum.Messages, m => m.Contains("cap"));
            }
        }

        [Fact]
        public void A_second_run_reuses_stored_answers_and_spends_nothing()
        {
            using (var p = Project())
            {
                var a = Labeller(A, t => "[]"); var b = Labeller(B, t => "[]");
                var plan = LabelPlan.Build(p.Open(), TempProject.Taxonomy(), new PlanOptions());
                var opt = new RunOptions { Execute = true, MaxCostUsd = 5 };
                var runner = new LabelRunner(a, b, TempProject.Taxonomy(), p.Adjudication);
                var first = runner.Run(plan, opt);
                int calls = a.Calls + b.Calls;
                Assert.Equal(plan.Batches.Count * 2, calls);
                Assert.True(first.SpentUsd > 0);
                var second = runner.Run(plan, opt);
                Assert.Equal(calls, a.Calls + b.Calls);
                Assert.Equal(0, second.SpentUsd);
                Assert.Equal(plan.Batches.Count * 2, second.CacheHits);
            }
        }

        [Fact]
        public void An_auth_failure_stops_the_run()
        {
            using (var p = Project())
            {
                var a = ScriptedLlmClient.Failing(LlmFailure.Auth, 401);
                var b = Labeller(B, t => "[]");
                var plan = LabelPlan.Build(p.Open(), TempProject.Taxonomy(), new PlanOptions { MaxBlocksPerBatch = 1 });
                var sum = new LabelRunner(a, b, TempProject.Taxonomy(), p.Adjudication).Run(plan, new RunOptions { Execute = true, MaxCostUsd = 5 });
                Assert.Equal(1, a.Calls);
                Assert.True(sum.Errors >= 1);
                Assert.Contains(sum.Messages, m => m.StartsWith("Stopped:"));
            }
        }

        // ---- agreement ---------------------------------------------------------------------------------------------------

        private static void RunBoth(TempProject p, Func<string, string> spansA, Func<string, string> spansB)
        {
            var plan = LabelPlan.Build(p.Open(), TempProject.Taxonomy(), new PlanOptions());
            new LabelRunner(Labeller(A, spansA), Labeller(B, spansB), TempProject.Taxonomy(), p.Adjudication).Run(plan, new RunOptions { Execute = true, MaxCostUsd = 5 });
        }

        [Fact]
        public void Cohens_kappa_matches_hand_values()
        {
            Assert.Equal(1.0, AgreementResult.Kappa(10, 4, 4, 4));
            Assert.Equal(0.583, AgreementResult.Kappa(10, 4, 4, 3));         // po 0.8, pe 0.52: 0.28 / 0.48
            Assert.Null(AgreementResult.Kappa(10, 0, 0, 0));
            Assert.Equal(-1.0, AgreementResult.Kappa(10, 5, 5, 0));         // po 0, pe 0.5
        }

        [Fact]
        public void Spans_agree_contradict_or_stand_alone()
        {
            using (var p = Project())
            {
                RunBoth(p,
                    t => t.Contains("utilize") ? "[" + Span("utilize", "utilize", "You can ", "use") + "]"
                       : t.Contains("go!") ? "[" + Span("go!", "exclaim", "ready to ", "go.") + "]"
                       : t.Contains("reviewed") ? "[" + Span("is reviewed", "passive") + "]" : "[]",
                    t => t.Contains("utilize") ? "[" + Span("utilize", "utilize", "You can ", "use") + "]"
                       : t.Contains("reviewed") ? "[" + Span("is reviewed", "exclaim") + "]"
                       : t.Contains("Save") ? "[" + Span("Save", "exclaim") + "]" : "[]");
                var proj = p.Open();
                var tax = TempProject.Taxonomy();
                var kinds = LabelPlan.Kinds(tax).Select(k => k.Id).ToList();
                var ra = LabellerResult.Load(p.Adjudication, "a", proj, tax);
                var rb = LabellerResult.Load(p.Adjudication, "b", proj, tax);
                var ag = AgreementResult.Compare(ra, rb, kinds);
                Assert.Single(ag.Agreed);
                Assert.Equal("utilize", ag.Agreed[0].A.Quote);
                Assert.Single(ag.Contradictions);
                Assert.Single(ag.OnlyA);            // go!
                Assert.Single(ag.OnlyB);            // Save
                Assert.Equal(5, ag.BlocksBoth.Count);
                Assert.Equal(1.0, ag.Kinds["utilize"].Kappa);
                Assert.True(ag.Kinds["exclaim"].Kappa < 0.6);
                Assert.Contains(ag.Below(0.6), k => k.Kind == "exclaim");
                Assert.True(ra.Usd > 0 && rb.Usd > ra.Usd);
            }
        }

        [Fact]
        public void An_invented_quote_is_dropped_and_counted()
        {
            using (var p = Project())
            {
                RunBoth(p, t => "[" + Span("not in the text", "exclaim") + "," + Span("utilize", "no_such_kind") + "]", t => "[]");
                var ra = LabellerResult.Load(p.Adjudication, "a", p.Open(), TempProject.Taxonomy());
                Assert.Empty(ra.Candidates);
                Assert.True(ra.Invalid >= 2);
            }
        }

        // ---- queue and merge ---------------------------------------------------------------------------------------------

        private static QueueBuild Queue(TempProject p, List<Label> labels, Func<string, string> a, Func<string, string> b, out AgreementResult ag)
        {
            RunBoth(p, a, b);
            var proj = p.Open(); var tax = TempProject.Taxonomy();
            ag = AgreementResult.Compare(LabellerResult.Load(p.Adjudication, "a", proj, tax), LabellerResult.Load(p.Adjudication, "b", proj, tax), LabelPlan.Kinds(tax).Select(k => k.Id).ToList());
            return Adjudication.Build(proj, labels, ag, null, 1);
        }

        private static string OnlyA(string t) { return t.Contains("utilize") ? "[" + Span("utilize", "utilize", "You can ", "use") + "]" : t.Contains("go!") ? "[" + Span("go!", "exclaim", "ready to ", "go.") + "]" : "[]"; }
        private static string Same(string t) { return t.Contains("utilize") ? "[" + Span("utilize", "utilize", "You can ", "use") + "]" : "[]"; }

        [Fact]
        public void The_queue_is_one_fifth_decoys_and_shows_no_provenance()
        {
            using (var p = Project())
            {
                AgreementResult ag;
                var qb = Queue(p, new List<Label>(), OnlyA, Same, out ag);
                // one single-source span (go!) in the queue; decoys make it 1 of 5 (ceil(1*0.2/0.8) = 1 decoy -> 2 items): at least 20%
                Assert.Equal(1, qb.Keys.Count(k => k.Type == "single"));
                Assert.True(qb.Decoys >= 1);
                Assert.True(qb.Decoys / (double)qb.Items.Count >= 0.2 - 1e-9 && qb.Decoys / (double)qb.Items.Count <= 0.5);
                string wire = string.Join("\n", qb.Items.Select(i => JsonConvert.SerializeObject(i)));
                foreach (var forbidden in new[] { "llm-", "decoy", "single", "labeller", "\"by\"", "source", "proposal" })
                    Assert.DoesNotContain(forbidden, wire);
                // the decoys are spans no labeller and no label spoke about
                var decoyQids = new HashSet<string>(qb.Keys.Where(k => k.Type == "decoy").Select(k => k.Qid));
                foreach (var d in qb.Items.Where(i => decoyQids.Contains(i.Qid)))
                    Assert.DoesNotContain(ag.Agreed.Select(x => x.A.Quote).Concat(ag.OnlyA.Select(x => x.Quote)), q => d.Quote.Contains(q) && d.BlockText.Contains(q) && d.Quote == q);
                // deterministic
                AgreementResult ag2;
                var again = Queue(p, new List<Label>(), OnlyA, Same, out ag2);
                Assert.Equal(JsonConvert.SerializeObject(qb.Items), JsonConvert.SerializeObject(again.Items));
            }
        }

        [Fact]
        public void Agreement_gives_verified_one_source_proposed_contradiction_disputed()
        {
            using (var p = Project())
            {
                var labels = new List<Label>();
                AgreementResult ag;
                var qb = Queue(p, labels,
                    t => t.Contains("utilize") ? "[" + Span("utilize", "utilize", "You can ", "use") + "]" : t.Contains("go!") ? "[" + Span("go!", "exclaim") + "]" : t.Contains("reviewed") ? "[" + Span("is reviewed", "passive") + "]" : "[]",
                    t => t.Contains("utilize") ? "[" + Span("utilize", "utilize", "You can ", "use") + "]" : t.Contains("reviewed") ? "[" + Span("is reviewed", "exclaim") + "]" : "[]", out ag);
                var res = Merge.Apply(p.Open(), labels, qb.Proposals, qb.Keys, new List<Decision>(), TempProject.Taxonomy(), null, new HashSet<string>());
                Assert.Equal(1, res.AddedVerified);
                Assert.Equal(1, res.AddedProposed);
                Assert.Equal(2, res.AddedDisputed);
                var verified = labels.Single(l => l.Status == "verified");
                Assert.Equal("utilize", verified.DefectKind);
                Assert.Equal(new[] { "llm-a", "llm-b" }, verified.By.ToArray());
                Assert.Equal("natural", verified.Origin);
                Assert.Equal("use", verified.Fix);
                Assert.Equal(new[] { "lint" }, verified.Rules.ToArray());
                Assert.Equal(new[] { "llm-a" }, labels.Single(l => l.Status == "proposed").By.ToArray());
                Assert.Empty(LabelValidator.Validate(labels, TempProject.Taxonomy().Ids, p.Open().Text));
            }
        }

        [Fact]
        public void An_agreed_span_that_matches_an_existing_label_confirms_it_instead_of_adding_one()
        {
            using (var p = Project())
            {
                var existing = p.Label("violation", F, "/html/body/p[1]", "utilize", "utilize", new[] { "company:plain-language/*" });
                var labels = new List<Label> { existing };
                AgreementResult ag;
                var qb = Queue(p, labels, Same, Same, out ag);
                var res = Merge.Apply(p.Open(), labels, qb.Proposals, qb.Keys, new List<Decision>(), TempProject.Taxonomy(), null, new HashSet<string>());
                Assert.Single(labels);
                Assert.Equal(1, res.Confirmed);
                Assert.Contains("llm-a", existing.By);
                Assert.Contains("llm-b", existing.By);
                Assert.Equal("seeded", existing.Origin);
            }
        }

        [Fact]
        public void An_agreed_span_inside_exempt_text_is_a_conflict_for_a_person()
        {
            using (var p = Project())
            {
                var exempt = p.Label("exempt", F, "/html/body/p[1]", "utilize", rules: new[] { "google:*" }, by: new[] { "hand-read" }, origin: "natural");
                var labels = new List<Label> { exempt };
                AgreementResult ag;
                var qb = Queue(p, labels, Same, Same, out ag);
                Assert.Equal(1, qb.Keys.Count(k => k.Type == "conflict"));
                var res = Merge.Apply(p.Open(), labels, qb.Proposals, qb.Keys, new List<Decision>(), TempProject.Taxonomy(), null, new HashSet<string>());
                Assert.Equal(0, res.Added);
                Assert.Equal(1, res.Pending);
            }
        }

        [Fact]
        public void Decisions_merge_in_code_and_a_test_file_needs_a_human()
        {
            using (var p = Project())
            {
                var labels = new List<Label>();
                AgreementResult ag;
                var qb = Queue(p, labels, OnlyA, Same, out ag);
                var real = qb.Keys.Single(k => k.Type == "single");
                var decoys = qb.Keys.Where(k => k.Type == "decoy").ToList();

                // a model's decision on a dev file counts; the decoy it accepts is tallied
                var decisions = new List<Decision> { new Decision { Qid = real.Qid, Value = "accept", By = "agent:reviewer" } };
                foreach (var d in decoys) decisions.Add(new Decision { Qid = d.Qid, Value = "accept", By = "agent:reviewer" });
                var dev = Merge.Apply(p.Open(), labels, qb.Proposals, qb.Keys, decisions, TempProject.Taxonomy(), null, new HashSet<string>());
                Assert.Equal(1, dev.DecisionsApplied);
                Assert.Equal(decoys.Count, dev.Decoys);
                Assert.Equal(1.0, dev.DecoyAcceptance);
                var accepted = labels.Single(l => l.DefectKind == "exclaim");
                Assert.Equal("verified", accepted.Status);
                Assert.Contains("agent:reviewer", accepted.By);

                // the same decision on a test file is ignored unless a human made it
                p.Split(F);
                var labels2 = new List<Label>();
                var testRun = Merge.Apply(p.Open(), labels2, qb.Proposals, qb.Keys, decisions, TempProject.Taxonomy(), null, new HashSet<string>());
                Assert.Equal(1, testRun.DecisionsIgnored);
                Assert.DoesNotContain(labels2, l => l.DefectKind == "exclaim");     // agreement still verifies; the adjudicated item waits
                decisions[0].By = "human";
                var human = Merge.Apply(p.Open(), labels2, qb.Proposals, qb.Keys, decisions, TempProject.Taxonomy(), null, new HashSet<string>());
                Assert.Equal(1, human.AddedVerified);
                Assert.Equal("test", labels2.Single(l => l.DefectKind == "exclaim").Split);
            }
        }

        [Fact]
        public void A_rejected_item_adds_nothing_and_an_ambiguous_one_is_excluded_from_scoring()
        {
            using (var p = Project())
            {
                AgreementResult ag;
                var qb = Queue(p, new List<Label>(), OnlyA, Same, out ag);
                var q = qb.Keys.Single(k => k.Type == "single").Qid;
                var rejected = new List<Label>();
                Merge.Apply(p.Open(), rejected, qb.Proposals, qb.Keys, new[] { new Decision { Qid = q, Value = "reject", By = "human" } }, TempProject.Taxonomy(), null, new HashSet<string>());
                Assert.DoesNotContain(rejected, l => l.DefectKind == "exclaim");
                var ambiguous = new List<Label>();
                Merge.Apply(p.Open(), ambiguous, qb.Proposals, qb.Keys, new[] { new Decision { Qid = q, Value = "ambiguous", By = "human" } }, TempProject.Taxonomy(), null, new HashSet<string>());
                var l2 = ambiguous.Single(l => l.Kind == "ambiguous");
                Assert.Equal("verified", l2.Status);
                Assert.Null(l2.DefectKind);
                Assert.Empty(LabelValidator.Validate(ambiguous, TempProject.Taxonomy().Ids, p.Open().Text));
            }
        }

        [Fact]
        public void A_kind_below_the_kappa_threshold_is_written_as_ambiguous_not_verified()
        {
            using (var p = Project())
            {
                var labels = new List<Label>();
                AgreementResult ag;
                var qb = Queue(p, labels, Same, Same, out ag);
                var res = Merge.Apply(p.Open(), labels, qb.Proposals, qb.Keys, new List<Decision>(), TempProject.Taxonomy(), null, new HashSet<string> { "utilize" });
                Assert.Equal(1, res.AddedAmbiguous);
                Assert.Equal(0, res.AddedVerified);
                Assert.Equal("ambiguous", labels.Single().Kind);
            }
        }

        [Fact]
        public void Merging_twice_adds_nothing_the_second_time()
        {
            using (var p = Project())
            {
                var labels = new List<Label>();
                AgreementResult ag;
                var qb = Queue(p, labels, OnlyA, OnlyA, out ag);
                Merge.Apply(p.Open(), labels, qb.Proposals, qb.Keys, new List<Decision>(), TempProject.Taxonomy(), null, new HashSet<string>());
                int n = labels.Count;
                Assert.True(n >= 2);
                Merge.Apply(p.Open(), labels, qb.Proposals, qb.Keys, new List<Decision>(), TempProject.Taxonomy(), null, new HashSet<string>());
                Assert.Equal(n, labels.Count);
            }
        }

        [Fact]
        public void New_labels_take_the_rules_existing_labels_of_that_kind_use()
        {
            var tax = TempProject.Taxonomy();
            using (var p = Project())
            {
                var seeded = new List<Label>
                {
                    p.Label("violation", F, "/html/body/p[1]", "utilize", "utilize", new[] { "company:plain-language/*" }),
                    p.Label("violation", F, "/html/body/p[2]", "go!", "exclaim", new[] { "google:*" })
                };
                var kr = new KindRules(seeded, tax);
                Assert.Equal(new[] { "company:plain-language/*" }, kr.For("utilize").ToArray());
                Assert.Equal(new[] { "ai:passive" }, kr.For("passive").ToArray());      // no label yet: the detect layer
                Assert.Equal(new[] { "lint" }, kr.For("phone").ToArray());
            }
        }
    }
}
