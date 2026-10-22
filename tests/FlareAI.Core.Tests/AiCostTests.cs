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
    }
}
