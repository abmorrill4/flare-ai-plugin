using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using FlareAI.Core.Actions;
using FlareAI.Core.Style;
using Xunit;

namespace FlareAI.Core.Tests
{
    /// <summary>
    /// The cost changes from docs/testing/ai-cost.md, asserted on request shape (what is sent, what is cached), not on
    /// model answers: every client here is scripted.
    /// </summary>
    public class AiCostTests
    {
        internal static string Repo
        {
            get
            {
                var d = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                while (d != null && !File.Exists(Path.Combine(d.FullName, "FlareAI.sln"))) d = d.Parent;
                return d.FullName;
            }
        }

        internal static StyleEngine Bundled()
        {
            var g = Path.Combine(Repo, "styleguide");
            return new StyleEngine(new[] { g }, g);
        }

        // ---- 1. Validate style guide: the team's layers by default, in parallel -------------------------------------------

        [Fact]
        public void ValidateAiCheck_SkipsGoogleExamplesUnlessAsked()
        {
            var all = Bundled().Examples(null);
            Assert.True(all.Count(e => e.Layer == "google") > 500);           // compiled Google pages: validated offline
            var own = ActionRunner.ExamplesToVerify(all, false);
            Assert.NotEmpty(own);
            Assert.All(own, e => Assert.NotEqual("google", e.Layer));
            Assert.Equal(all.Count, ActionRunner.ExamplesToVerify(all, true).Count);
            Assert.True(ActionRunner.VerifyCalls(own) < 20);                   // seconds and cents, not 886 calls
        }

        [Fact]
        public void ValidateAiCheck_RunsCallsInParallel_AndKeepsResultsInOrder()
        {
            var style = new FakeStyleService();
            int now = 0, peak = 0;
            var llm = new FakeLlmClient(r =>
            {
                var n = Interlocked.Increment(ref now);
                lock (style) peak = Math.Max(peak, n);
                Thread.Sleep(60);
                Interlocked.Decrement(ref now);
                return r.User.Contains("Customers")
                    ? @"[{""rule_id"":""company:terminology/1"",""rule"":""members"",""severity"":""warning"",""excerpt"":""Customers"",""replacement"":""Members"",""why"":""x""}]"
                    : "[]";
            });
            var runner = new ActionRunner(llm, new PromptBuilder(() => style.Guide(null)), 1024, style) { Parallelism = 4 };
            var examples = Enumerable.Range(0, 6).Select(i => new GuideExample
            {
                Id = "company:terminology/1", Layer = "company", Bad = i % 2 == 0 ? "Customers can reset their PIN " + i + "." : "Clients reset it " + i + ".",
                Good = "Members can reset their PIN " + i + "."
            }).ToList();
            var progress = new List<int>();
            var results = runner.VerifyExamples(examples, (d, t) => { lock (progress) progress.Add(d); });

            Assert.Equal(12, llm.Requests.Count);
            Assert.True(peak > 1, "calls ran one at a time");
            Assert.Equal(12, progress.Max());
            for (var i = 0; i < 6; i++)
            {
                Assert.Same(examples[i], results[i].Example);
                Assert.Equal(i % 2 == 0, results[i].BadCaught);
                Assert.True(results[i].GoodClean);
            }
        }

        // ---- 2. Cache lifetime per task; one shared prefix for review, rewrite and restructure --------------------------

        /// <summary>A runner over the fake guide whose model answers every task with an empty, valid contract.</summary>
        internal static ActionRunner Runner(FakeLlmClient llm, FakeStyleService style = null)
        {
            style = style ?? new FakeStyleService();
            return new ActionRunner(llm, PromptBuilder.For(style, null), 1024, style);
        }

        internal static FakeLlmClient Quiet()
        {
            return new FakeLlmClient(r =>
                r.Task == "restructure" ? "<revised>" + FragmentOf(r.User) + "</revised>"
                : r.Task == "review" ? "[]"
                : r.Task == "alt" ? "{\"decorative\":false,\"alt\":\"The Transfers page.\"}"
                : r.Task == "assist" ? "{\"description\":\"d\",\"keywords\":[],\"groups\":[],\"micro_content\":[],\"glossary\":[]}"
                : r.Task == "extract" ? "{\"rules\":[]}"
                : r.Task == "refine" ? "{\"replacement\":\"Members\",\"why\":\"x\",\"applies\":true}"
                : "{\"findings\":[],\"changes\":[],\"needs_restructure\":false,\"note\":\"\",\"revised\":\"\"}");
        }

        private static string FragmentOf(string user)
        {
            var i = user.LastIndexOf("Fragment:", StringComparison.Ordinal);
            return user.Substring(i + "Fragment:".Length).Trim();
        }

        private static Newtonsoft.Json.Linq.JObject Body(FlareAI.Core.Llm.LlmRequest r)
        {
            return Newtonsoft.Json.Linq.JObject.Parse(FlareAI.Core.Llm.AnthropicClient.BuildBody("claude-sonnet-5-5", r));
        }

        [Fact]
        public void ReviewRewriteAndRestructure_ShareOneCachedPrefix_CachedForAnHour()
        {
            var doc = new FakeDocumentAdapter("sample-topic.htm");
            doc.SelectXPath("//body/p[2]/text()");
            var llm = Quiet();
            var runner = Runner(llm);
            var blocks = runner.PickBlocks(doc);
            runner.Review(blocks, runner.Lint(blocks));
            runner.Rewrite(doc, blocks, "Shorten this.");
            runner.Restructure(doc, blocks, "Make this a list.");

            var review = llm.Requests.First(r => r.Task == "review");
            var rewrite = llm.Requests.First(r => r.Task == "rewrite");
            var restructure = llm.Requests.First(r => r.Task == "restructure");
            foreach (var other in new[] { rewrite, restructure })
            {
                // Same model, same schema (it sits ahead of the system prompt in the cache key), same cached system block.
                Assert.Equal(review.Model, other.Model);
                Assert.True(Newtonsoft.Json.Linq.JToken.DeepEquals(Body(review)["output_config"], Body(other)["output_config"]));
                Assert.True(Newtonsoft.Json.Linq.JToken.DeepEquals(Body(review)["system"][0], Body(other)["system"][0]));
            }
            Assert.Equal("1h", (string)Body(review)["system"][0]["cache_control"]["ttl"]);
            // What differs (which fields to fill, the task) is in the user message, after the cache.
            Assert.StartsWith(Contracts.ReviewFields, review.User);
            Assert.StartsWith(Contracts.RewriteFields, rewrite.User);
            Assert.StartsWith(Contracts.RestructureFields, restructure.User);
        }

        [Fact]
        public void Restructure_ReadsTheRevisedField()
        {
            var doc = new FakeDocumentAdapter("sample-topic.htm");
            doc.SelectXPath("//body/p[2]/text()");
            var llm = new FakeLlmClient(r => "{\"findings\":[],\"changes\":[],\"needs_restructure\":false,\"note\":\"\",\"revised\":\"<ul><li>One.</li><li>Two.</li></ul>\"}");
            var runner = Runner(llm);
            var proposal = runner.Restructure(doc, runner.PickBlocks(doc), "Make this a list.");
            Assert.Equal("<ul><li>One.</li><li>Two.</li></ul>", proposal.ProposedXml);
        }

        [Theory]
        [InlineData("draft", "5m")]
        [InlineData("extract", "5m")]
        [InlineData("refine", "5m")]
        [InlineData("alt", "none")]
        [InlineData("assist", "none")]
        [InlineData("review", "1h")]
        [InlineData("rewrite", "1h")]
        public void RarelyUsedTasks_DontPayForAnHourLongCacheWrite(string task, string ttl)
        {
            Assert.Equal(ttl, new FlareAI.Core.Llm.Routing().CacheFor(task));
            var body = Body(new FlareAI.Core.Llm.LlmRequest { Task = task, System = "system", User = "u", CacheTtl = ttl });
            var control = body["system"][0]["cache_control"];
            if (ttl == "none") Assert.Null(control);
            else if (ttl == "5m") { Assert.Equal("ephemeral", (string)control["type"]); Assert.Null(control["ttl"]); }
            else Assert.Equal("1h", (string)control["ttl"]);
        }

        // ---- 3. No style guide where no style rule is applied --------------------------------------------------------------

        [Fact]
        public void AltTextAndTopicAssists_DontCarryTheStyleGuide()
        {
            var style = new FakeStyleService { GuideText = "- [company:terminology/1] Refer to end users as \"members\"." };
            var llm = Quiet();
            var runner = Runner(llm, style);
            runner.SuggestAlt(new LlmImageFile { MediaType = "image/png", Base64 = "iVBORw0KGgo=" }, "Transfers");
            var doc = new FakeDocumentAdapter("sample-topic.htm");
            runner.Assist(runner.PickAllBlocks(doc));
            runner.Refine(new StyleFinding { Excerpt = "users", Replacement = "members", Suggestion = "x" }, "The users can sign in.", "Shorter");

            var plain = llm.Requests.Where(r => r.Task == "alt" || r.Task == "assist").ToList();
            Assert.Equal(4, plain.Count);
            foreach (var r in plain)
            {
                Assert.DoesNotContain("members", r.System);
                Assert.True(r.System.Length < 1000, r.Task + " system prompt is " + r.System.Length + " characters");
                Assert.Null(r.SystemPacks);
                Assert.Null(r.SystemTerms);
                Assert.Null(Body(r)["system"][0]["cache_control"]);
            }
            Assert.Contains("members", llm.Requests.Single(r => r.Task == "refine").System);   // refine still applies the rule
        }

        // ---- 4. A one-paragraph rewrite carries the rules wording can act on --------------------------------------------

        [Fact]
        public void Rewrite_SendsTriggeredWordRules_NotFormattingPacks()
        {
            var engine = Bundled();
            var doc = new FakeDocumentAdapter("sample-topic.htm");
            var p = FlareAI.Core.Editing.XhtmlFragment.ParseInto("<p>The <code>listMessages</code> method will return every message in the mailbox, and the client should page through the results with the <code>pageToken</code> field.</p>", doc.Document)[0];
            doc.Document.SelectSingleNode("//body").AppendChild(p);
            var llm = Quiet();
            var runner = new ActionRunner(llm, PromptBuilder.For(engine, null), 1024, engine);
            var blocks = new List<System.Xml.XmlNode> { p };
            runner.Review(blocks, runner.Lint(blocks));
            runner.Rewrite(doc, blocks, null);

            var review = llm.Requests.First(r => r.Task == "review").SystemTerms;
            var rewrite = llm.Requests.First(r => r.Task == "rewrite").SystemTerms;
            Assert.Contains("In ordinary text sentences, use code font", review);   // review still judges formatting
            Assert.DoesNotContain("In ordinary text sentences, use code font", rewrite);   // a run-level rewrite can't change it
            Assert.DoesNotContain("series of x", rewrite);
            Assert.Contains("(will)", rewrite);                   // the text says "will": its word rule comes along
            Assert.DoesNotContain("(click)", rewrite);            // words the text doesn't use: their rules stay home
            Assert.True(rewrite.Length * 3 < review.Length, "rewrite " + rewrite.Length + " vs review " + review.Length);
            Assert.Equal(llm.Requests.First(r => r.Task == "review").System, llm.Requests.First(r => r.Task == "rewrite").System);
        }

        // ---- 5. Re-check after applying the review's own fixes: re-anchor, don't re-review --------------------------------

        private const string TwoFindings = @"[
            {""rule_id"":""general"",""rule"":""plain words"",""severity"":""warning"",""excerpt"":""utilize"",""replacement"":""use"",""why"":""Plain words.""},
            {""rule_id"":""general"",""rule"":""wordy"",""severity"":""suggestion"",""excerpt"":""Please note that the fee"",""replacement"":""The fee"",""why"":""Wordy.""}]";

        private static System.Xml.XmlNode Paragraph(FakeDocumentAdapter doc, string xml)
        {
            var p = FlareAI.Core.Editing.XhtmlFragment.ParseInto(xml, doc.Document)[0];
            var body = doc.Document.SelectSingleNode("//body");
            while (body.FirstChild != null) body.RemoveChild(body.FirstChild);
            body.AppendChild(p);
            return p;
        }

        private static List<StyleFinding> Check(ActionRunner runner, ReviewCache cache, System.Xml.XmlNode p, out ReviewUnit unit)
        {
            var blocks = new List<System.Xml.XmlNode> { p };
            var lint = runner.Lint(blocks);
            unit = runner.PrepareReview(blocks, lint)[0];
            var raw = cache.Get(unit);
            if (raw == null) { raw = runner.CallReview(unit); cache.Put(unit, raw); }
            return runner.AnchorReview(raw, unit, lint);
        }

        [Fact]
        public void RecheckAfterApplyingTheReviewsFix_ReanchorsTheRest_WithoutAModelCall()
        {
            var doc = new FakeDocumentAdapter("sample-topic.htm");
            var p = Paragraph(doc, "<p>We utilize the <b>Transfers</b> page to move funds. Please note that the fee is waived for members.</p>");
            var llm = new FakeLlmClient(r => TwoFindings);
            var runner = new ActionRunner(llm, new PromptBuilder(null), 1024);
            var cache = new ReviewCache();
            ReviewUnit unit;
            var first = Check(runner, cache, p, out unit);
            Assert.Equal(2, first.Count);
            Assert.Equal(1, runner.ApplyFixes(doc, first.Where(f => f.Excerpt == "utilize"), "fix"));

            var again = Check(runner, cache, p, out unit);
            Assert.Single(llm.Requests);                                         // no second review
            var left = Assert.Single(again);
            Assert.Equal("Please note that the fee", left.Excerpt);
            Assert.True(left.CanFix);
            Assert.Equal(1, runner.ApplyFixes(doc, again, "fix"));
            Assert.Contains("We use the", p.InnerText);
            Assert.Contains("The fee is waived", p.InnerText);
            Assert.Empty(Check(runner, cache, p, out unit));                     // both applied: nothing left, still no call
            Assert.Single(llm.Requests);
        }

        [Fact]
        public void RecheckAfterAFixSplitsTheRun_AsATrackedChangeDoes_StillReanchors()
        {
            var doc = new FakeDocumentAdapter("sample-topic.htm");
            var p = Paragraph(doc, "<p>We utilize the app. Please note that the fee is waived.</p>");
            var llm = new FakeLlmClient(r => TwoFindings);
            var runner = new ActionRunner(llm, new PromptBuilder(null), 1024);
            var cache = new ReviewCache();
            ReviewUnit unit;
            Check(runner, cache, p, out unit);
            // Flare writes the fix as a tracked change: the accepted view reads "We " + "use" + " the app. …" as three runs.
            p.RemoveAll();
            foreach (var t in new[] { "We ", "use", " the app. Please note that the fee is waived." }) p.AppendChild(doc.Document.CreateTextNode(t));

            var again = Check(runner, cache, p, out unit);
            Assert.Single(llm.Requests);
            var left = Assert.Single(again);
            Assert.Equal("r3", left.RunId);                                      // moved to the run that now holds it
        }

        [Fact]
        public void RecheckAfterTheAuthorRewroteText_ReviewsAgain()
        {
            var doc = new FakeDocumentAdapter("sample-topic.htm");
            var p = Paragraph(doc, "<p>We utilize the app. Please note that the fee is waived.</p>");
            var llm = new FakeLlmClient(r => TwoFindings);
            var runner = new ActionRunner(llm, new PromptBuilder(null), 1024);
            var cache = new ReviewCache();
            ReviewUnit unit;
            Check(runner, cache, p, out unit);
            ((System.Xml.XmlText)p.FirstChild).Value = "We utilize the app. The fee is waived for new members only.";
            Check(runner, cache, p, out unit);
            Assert.Equal(2, llm.Requests.Count);
        }

        [Fact]
        public void TeamCanChangeACacheLifetime()
        {
            var dir = Path.Combine(Path.GetTempPath(), "flareai-cache-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(dir, "FlareAI"));
            try
            {
                File.WriteAllText(Path.Combine(dir, @"FlareAI\models.json"), "{\"cache\":{\"restructure\":\"5m\",\"alt\":\"forever\"}}");
                var routing = new FlareAI.Core.Llm.Routing().WithTeamFile(dir);
                Assert.Equal("5m", routing.CacheFor("restructure"));
                Assert.Equal("none", routing.CacheFor("alt"));   // unknown values are ignored
            }
            finally { Directory.Delete(dir, true); }
        }
    }
}
