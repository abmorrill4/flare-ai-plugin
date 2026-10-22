using System.Collections.Generic;
using System.Linq;
using FlareAI.Core.Actions;
using FlareAI.Core.Style;
using Xunit;

namespace FlareAI.Core.Tests
{
    public class RuleBacklogTests
    {
        private static StyleFinding Hit(string rule, string file, bool automatic, string severity = "warning")
        {
            return new StyleFinding
            {
                Source = "lint", Rule = rule, RuleId = rule, Layer = "company", Severity = severity, Excerpt = "x",
                Replacement = automatic ? "y" : null, Suggestion = "Message for " + rule, DocumentPath = @"C:\P\Content\" + file
            };
        }

        /// <summary>117 suggestions in 42 files: 63 exact fixes from 9 rules, 54 that need a rewrite from 3 more.</summary>
        private static List<StyleFinding> PilotBacklog()
        {
            var all = new List<StyleFinding>();
            for (var i = 0; i < 63; i++) all.Add(Hit("auto" + (i % 9), "t" + (i % 42) + ".htm", true));
            for (var i = 0; i < 54; i++) all.Add(Hit("manual" + (i % 3), "t" + (i % 30) + ".htm", false));
            return all;
        }

        [Fact]
        public void Summary_IsTheHeadlineTheTesterAskedFor()
        {
            Assert.Equal("117 suggestions in 42 files. 63 can be fixed automatically across 9 rules.", RuleBacklog.Summary(PilotBacklog(), null));
            Assert.Equal("117 suggestions in 42 files. 63 can be fixed automatically across 9 rules. 2 rules skipped for now.", RuleBacklog.Summary(PilotBacklog(), null, 2));
        }

        [Fact]
        public void Summary_EdgeCases()
        {
            Assert.Equal("No open suggestions.", RuleBacklog.Summary(new List<StyleFinding>(), null));
            Assert.Equal("1 suggestion in 1 file. It can be fixed automatically by one rule.", RuleBacklog.Summary(new[] { Hit("a", "t.htm", true) }, null));
            Assert.Equal("2 suggestions in 2 files. None can be fixed automatically: review them one rule at a time.",
                         RuleBacklog.Summary(new[] { Hit("a", "t.htm", false), Hit("a", "u.htm", false) }, null));
            Assert.Equal("2 suggestions in 1 file. All can be fixed automatically across 2 rules.",
                         RuleBacklog.Summary(new[] { Hit("a", "t.htm", true), Hit("b", "T.htm", true) }, null));
        }

        [Fact]
        public void Group_CountsFilesAndOccurrences_SortsByCount_AndTakesTheGuidesWording()
        {
            var found = PilotBacklog();
            found.Add(Hit("auto0", @"Resources\Snippets\Note.flsnp", true, "error"));
            var rules = RuleBacklog.Group(found, id => id == "auto0" ? new GuideRule { Id = id, Layer = "company", Section = "Plain words", Text = "Avoid the pattern \\butilize\\b → use 'use'. Plain words." } : null, null);

            Assert.Equal(12, rules.Count);
            Assert.Equal(new[] { "manual0", "manual1", "manual2" }, rules.Take(3).Select(r => r.Key));   // 18 each, then the 7-8s
            var top = rules.Single(r => r.Key == "auto0");
            Assert.Equal(8, top.Occurrences);
            Assert.Equal(8, top.Files.Count);
            Assert.Equal(new[] { @"C:\P\Content\Resources\Snippets\Note.flsnp" }, top.Snippets);
            Assert.Equal("error", top.Severity);
            Assert.Equal("Our rules · Plain words", top.Title);
            Assert.Equal("Plain words.", top.Reason);                                  // the pattern is not for writers
            Assert.Equal("8 in 8 files · all automatic", top.Counts());
            var manual = rules.Single(r => r.Key == "manual1");
            Assert.Equal("Message for manual1", manual.Reason);                         // no guide rule: the message it shows
            Assert.Equal("18 in 10 files · needs you", manual.Counts());
            for (var i = 1; i < rules.Count; i++) Assert.True(rules[i - 1].Occurrences >= rules[i].Occurrences);
        }

        [Fact]
        public void PlainReason_DropsTheMatchingPattern_KeepsReadableRules()
        {
            Assert.Equal("Avoid exclamation points in general.", RuleBacklog.PlainReason("Avoid the pattern (?<=[A-Za-z])!(?=\\s|$) (rewrite). Avoid exclamation points in general."));
            Assert.Null(RuleBacklog.PlainReason("Avoid the pattern \\bfoo\\b (delete it)."));
            Assert.Equal("Avoid 'e-mail' → use 'email'.", RuleBacklog.PlainReason("Avoid 'e-mail' → use 'email'."));
        }

        [Fact]
        public void BatchResult_SaysWhenAStopLeftFilesUnchanged()
        {
            var r = new BatchResult();
            r.AddChanged(@"C:\P\Content\a.htm", 3, true);
            r.AddNotStarted(@"C:\P\Content\b.htm", 2);
            r.AddNotStarted(@"C:\P\Content\c.htm", 1);
            Assert.EndsWith("Stopped: 2 files were not changed.", r.Status());
            Assert.Contains("Not changed: b.htm (2 fixes) — stopped before this file", r.Report());
        }

        [Fact]
        public void BatchPlan_FlagsSnippetsOtherTopicsInclude()
        {
            var fixes = new[] { Hit("a", @"Resources\Note.flsnp", true), Hit("a", @"Resources\Solo.flsnp", true), Hit("a", "t.htm", true) };
            var plan = BatchPlan.Build(fixes, null, p => null, p => p.EndsWith("Note.flsnp") ? 14 : 1);
            var shared = Assert.Single(plan.SharedSnippets);
            Assert.Equal(14, shared.Uses);
            Assert.Equal("1 fix is in a snippet that other topics include: Note.flsnp (used in 14 topics). Changing a snippet changes every topic that includes it.", plan.SnippetNote());
            plan.Groups[0].Selected = false;
            Assert.Null(plan.SnippetNote());
        }
    }
}
