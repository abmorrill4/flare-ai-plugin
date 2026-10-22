using System;
using System.IO;
using FlareAI.Core.Actions;
using Xunit;

namespace FlareAI.Core.Tests
{
    public class BatchPlanTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "flareai-batch-" + Guid.NewGuid().ToString("N"));
        public void Dispose() { try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

        private static StyleFinding Fix(string rule, string path, string from, string to)
        {
            return new StyleFinding { Rule = rule, DocumentPath = path, Excerpt = from, Replacement = to };
        }

        [Fact]
        public void OnlyTheOpenTopic_NeedsNoPreview()
        {
            var plan = BatchPlan.Build(new[] { Fix("Google.Will", null, "will", "") , Fix("Google.Will", @"C:\p\Open.htm", "will", "") }, @"C:\p\Open.htm", null);
            Assert.False(plan.NeedsPreview);
            Assert.Equal(2, plan.SelectedCount);
            Assert.Equal(1, plan.SelectedFileCount);
        }

        [Fact]
        public void AnotherFile_NeedsPreview_AndGroupsByRuleWithThreeExamples()
        {
            var fixes = new[]
            {
                Fix("Google.Please", @"C:\p\A.htm", "please click", "click"),
                Fix("Google.Please", @"C:\p\B.htm", "please open", "open"),
                Fix("Google.Please", @"C:\p\B.htm", "please run", "run"),
                Fix("Google.Please", @"C:\p\C.htm", "please save", "save"),
                Fix("Company.Terms", @"C:\p\C.htm", "e-mail", "email"),
            };
            var plan = BatchPlan.Build(fixes, @"C:\p\Open.htm", null);
            Assert.True(plan.NeedsPreview);
            Assert.Equal(2, plan.Groups.Count);
            var please = plan.Groups[0];
            Assert.Equal("Please", please.Label);
            Assert.Equal(4, please.Findings.Count);
            Assert.Equal(3, please.Files.Count);
            Assert.Equal(BatchPlan.MaxExamples, please.Examples.Count);
            Assert.Equal("change \"please click\" to \"click\"", please.Examples[0]);
            Assert.Equal("5 changes in 3 files", BatchPlan.Counts(plan.SelectedCount, plan.SelectedFileCount));
        }

        [Fact]
        public void UntickingARule_RemovesItsFixes()
        {
            var plan = BatchPlan.Build(new[] { Fix("Google.A", @"C:\p\A.htm", "x", "y"), Fix("Google.B", @"C:\p\A.htm", "m", "n") }, @"C:\p\Open.htm", null);
            plan.Groups.Find(g => g.Key == "Google.A").Selected = false;
            Assert.Equal(1, plan.SelectedCount);
            Assert.Equal("Google.B", plan.SelectedFindings[0].Rule);
        }

        [Fact]
        public void ReadOnlyAndMissingFiles_AreSkippedWithAReason()
        {
            Directory.CreateDirectory(_root);
            var ok = Path.Combine(_root, "ok.htm"); File.WriteAllText(ok, "x");
            var ro = Path.Combine(_root, "ro.htm"); File.WriteAllText(ro, "x"); File.SetAttributes(ro, FileAttributes.ReadOnly);
            var gone = Path.Combine(_root, "gone.htm");
            try
            {
                var plan = BatchPlan.Build(new[] { Fix("R.1", ok, "a", "b"), Fix("R.1", ro, "a", "b"), Fix("R.1", ro, "c", "d"), Fix("R.1", gone, "a", "b") },
                                           Path.Combine(_root, "open.htm"), BatchPlan.WhyNotWritable);
                Assert.Equal(1, plan.SelectedCount);
                Assert.Equal(2, plan.Skipped.Count);
                Assert.Equal("read-only", plan.Skipped.Find(s => s.Path == ro).Reason);
                Assert.Equal(2, plan.Skipped.Find(s => s.Path == ro).Fixes);
                Assert.Equal("not found", plan.Skipped.Find(s => s.Path == gone).Reason);
            }
            finally { File.SetAttributes(ro, FileAttributes.Normal); }
        }

        [Fact]
        public void Snippets_ShowHowManyTopicsUseThem()
        {
            var plan = BatchPlan.Build(new[] { Fix("R.1", @"C:\p\LegacyApp.flsnp", "a", "b") }, @"C:\p\Open.htm", null, p => 9);
            Assert.Equal("LegacyApp.flsnp (snippet, used in 9 topics)", plan.Groups[0].Files[0]);
            Assert.Equal("This changes LegacyApp.flsnp, used in 9 topics. Make the change?", BatchPlan.SnippetQuestion(@"C:\p\LegacyApp.flsnp", 9));
            Assert.Equal("This changes A.flsnp, used in 1 topic. Make the change?", BatchPlan.SnippetQuestion(@"C:\p\A.flsnp", 1));
        }

        [Fact]
        public void Result_NeverPromisesUndoForSavedFiles()
        {
            var open = new BatchResult();
            open.AddChanged("a.htm", 3, false);
            Assert.Contains("Ctrl+Z", open.Status());

            var saved = new BatchResult();
            saved.AddChanged("b.htm", 2, true);
            saved.AddSkipped("c.htm", 1, "read-only");
            saved.AddFailed("d.htm", 1, "could not open");
            Assert.DoesNotContain("Ctrl+Z", saved.Status());
            Assert.Contains("Review tab", saved.Status());
            var report = saved.Report();
            Assert.Contains("Changed: b.htm (2)", report);
            Assert.Contains("Skipped: c.htm", report);
            Assert.Contains("Failed: d.htm", report);
        }

        [Fact]
        public void Cost_IsPlain()
        {
            Assert.Equal("AI this session: no calls yet", AiCost.Session(0, 0));
            Assert.Equal("AI this session: 1 call (under $0.01)", AiCost.Session(1, 0.004));
            Assert.Equal("AI this session: 12 calls (about $0.37)", AiCost.Session(12, 0.372));
        }

        [Fact]
        public void FilesWithTheSameNameStayApart()
        {
            Assert.Equal(@"accounts\about.htm", BatchPlan.ProjectPath(@"C:\p\Content\accounts\about.htm"));
            Assert.Equal(@"ach\about.htm", BatchPlan.ProjectPath(@"C:\p\Content\ach\about.htm"));
            Assert.Equal("x.htm", BatchPlan.ProjectPath(@"C:\elsewhere\x.htm"));
        }
    }
}
