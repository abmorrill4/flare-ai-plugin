using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlareAI.Core.Actions;
using Xunit;

namespace FlareAI.Core.Tests
{
    public class TeamFeedbackTests
    {
        private static Decision D(string writer, string verdict, string rule = "company:terminology/1", string reason = null, string excerpt = "customers", string replacement = "members")
        {
            return new Decision { Writer = writer, Verdict = verdict, RuleId = rule, Reason = reason, Excerpt = excerpt, Replacement = replacement, File = "a.htm" };
        }

        [Fact]
        public void OwnersFileMakesTheTeamStrict()
        {
            var root = Path.Combine(Path.GetTempPath(), "guide-" + Guid.NewGuid());
            Directory.CreateDirectory(Path.Combine(root, "FlareAI"));
            try
            {
                Assert.False(Team.Load(root, null, "pat@example.com").IsStrict);
                File.WriteAllText(Path.Combine(root, "FlareAI", "owners.txt"), "# style guide owners\nlead@example.com  # docs lead\n");
                var writer = Team.Load(root, @"\\share\feedback", "pat@example.com");
                Assert.True(writer.IsStrict);
                Assert.False(writer.IsOwner);
                Assert.EndsWith(Path.Combine("feedback", "pat_example.com.jsonl"), writer.SharedFeedbackFile);
                Assert.True(Team.Load(root, null, "LEAD@example.com").IsOwner);
            }
            finally { Directory.Delete(root, true); }
        }

        [Fact]
        public void OneWritersTasteDoesNotRaiseARule()
        {
            var ds = Enumerable.Range(0, 12).Select(i => D("pat", "rejected", reason: "fine here")).ToList();
            Assert.DoesNotContain(FeedbackStats.Analyze(ds), i => i.Kind == "review");
        }

        [Fact]
        public void OftenRejectedAcrossWritersIsRaisedWithReasonsAndExamples()
        {
            var ds = new List<Decision>();
            ds.AddRange(Enumerable.Range(0, 5).Select(i => D("pat", "rejected", reason: "fine here")));
            ds.AddRange(Enumerable.Range(0, 2).Select(i => D("sam", "rejected", reason: "fine here")));
            ds.AddRange(Enumerable.Range(0, 2).Select(i => D("sam", "accepted")));
            var item = FeedbackStats.Analyze(ds).Single(i => i.Kind == "review");
            Assert.Equal("company:terminology/1", item.Rule);
            Assert.Contains(item.Evidence, e => e.Contains("fine here") && e.Contains("7×"));
            Assert.Contains(item.Evidence, e => e.Contains("\"customers\""));
        }

        [Fact]
        public void WritersWhoSplitOnARuleMakeItDisputed()
        {
            var ds = new List<Decision>();
            ds.AddRange(Enumerable.Range(0, 4).Select(i => D("pat", "accepted")));
            ds.AddRange(Enumerable.Range(0, 4).Select(i => D("sam", "rejected", reason: "wrong")));
            var item = FeedbackStats.Analyze(ds).First();
            Assert.Equal("disputed", item.Kind);
            Assert.Contains(item.Evidence, e => e.StartsWith("pat accepts"));
            Assert.Contains(item.Evidence, e => e.StartsWith("sam rejects"));
        }

        [Fact]
        public void RepeatedHandEditsBecomeACandidateRow()
        {
            var ds = new List<Decision> { D("pat", "edited", excerpt: "log in", replacement: "sign in"), D("sam", "edited", excerpt: "Log in", replacement: "sign in") };
            var item = FeedbackStats.Analyze(ds).Single(i => i.Kind == "edit");
            Assert.Contains("log in", item.Title);
            Assert.Equal(2, item.Writers.Count);
        }

        [Fact]
        public void SharedFilesLoadPerWriterAndSkipTornLines()
        {
            var dir = Path.Combine(Path.GetTempPath(), "fb-" + Guid.NewGuid());
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "pat.jsonl"), "{\"writer\":\"pat\",\"decision\":\"accepted\",\"rule\":\"Google.Will\"}\n{\"writer\":\"pat\",\"deci");
                File.WriteAllText(Path.Combine(dir, "sam.jsonl"), "{\"decision\":\"rejected\",\"rule\":\"Google.Will\",\"reason\":\"wrong\"}\n");
                var ds = FeedbackStats.Load(FeedbackStats.FilesIn(dir));
                Assert.Equal(2, ds.Count);
                Assert.Equal("sam", ds.Single(d => d.IsReject).Writer); // no writer field: named after the file
            }
            finally { Directory.Delete(dir, true); }
        }
    }
}
