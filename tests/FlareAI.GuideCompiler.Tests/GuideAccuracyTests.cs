using System;
using System.IO;
using System.Linq;
using FlareAI.Core.Style;
using Xunit;

namespace FlareAI.GuideCompiler.Tests
{
    /// <summary>The bundled Google layer after the #60, #61, #63 and #64 decisions (tools/google-style/fixes.json): what writers see.</summary>
    public class GuideAccuracyTests
    {
        private static readonly string Guide = FindGuide();
        private static readonly Lazy<StyleEngine> Engine = new Lazy<StyleEngine>(() => new StyleEngine(new string[0], Guide));

        private static string FindGuide()
        {
            var d = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (d != null && !File.Exists(Path.Combine(d.FullName, "FlareAI.sln"))) d = d.Parent;
            if (d == null) throw new InvalidOperationException("FlareAI.sln not found");
            return Path.Combine(d.FullName, "styleguide");
        }

        private static string[] Rules(string text, bool heading = false)
        {
            return Engine.Value.Lint(new[] { text }, heading ? new[] { 0 } : null, null).SelectMany(h => h.RuleIds).Where(id => id.StartsWith("google:")).ToArray();
        }

        // ---- #60 over-broad and too-narrow patterns -------------------------------------------------------------------

        [Theory]
        [InlineData("The table has 300+ users.", "google:word-list/425")]
        [InlineData("Find more information on the dashboard.", "google:cross-references/22")]
        [InlineData("Connect a dumb terminal.", "google:inclusive-documentation/10")]
        [InlineData("Load dummy data first.", "google:inclusive-documentation/10")]
        [InlineData("The service is suffering from high latency.", "google:inclusive-documentation/42")]
        [InlineData("Press Control+C to copy.", "google:procedures/42")]
        [InlineData("Contact J.K. Rowling.", "google:abbreviations/25")]
        [InlineData("The meeting starts at 9 A.M. today.", "google:abbreviations/25")]
        [InlineData("Import data from 2024-01-15 onward.", "google:hyphens/31")]
        [InlineData("It costs $19.99 100 times a year.", "google:numbers/43")]
        [InlineData("A toggled setting stays on.", "google:ui-elements/54")]
        [InlineData("Open the file in /etc/postgresql/13/main.", "google:word-list/269")]
        [InlineData("Prevent a denial of service.", "google:word-list/216")]
        [InlineData("For details, see Microservices.", "google:word-list/475")]
        public void Narrowed_rows_leave_ordinary_text_alone(string text, string rule)
        {
            Assert.DoesNotContain(rule, Rules(text));
        }

        [Theory]
        [InlineData("Use version 2.2+.", "google:word-list/425")]
        [InlineData("For more information on indexes, see Manage indexes.", "google:cross-references/22")]
        [InlineData("Press Ctrl+C to copy.", "google:procedures/42")]
        [InlineData("(optional) Type a name.", "google:procedures/20")]
        [InlineData("Contact the I.T. team.", "google:abbreviations/25")]
        [InlineData("It is a highly-available service.", "google:hyphens/15")]
        [InlineData("Upload from 8-20 files.", "google:hyphens/31")]
        [InlineData("Toggle the setting.", "google:ui-elements/54")]
        [InlineData("Press ⌘+C to copy.", "google:ui-elements/70")]
        [InlineData("Press Ctrl + V.", "google:ui-elements/71")]
        [InlineData("Protect it from distributed denial of service attacks.", "google:word-list/216")]
        [InlineData("Single sign on is enabled.", "google:word-list/641")]
        [InlineData("It’s easy to set up.", "google:tone/20")]
        public void Widened_rows_catch_the_documented_case(string text, string rule)
        {
            Assert.Contains(rule, Rules(text));
        }

        // ---- #61 severity -------------------------------------------------------------------------------------------------

        [Fact]
        public void Curly_quotes_are_a_suggestion_with_an_exact_fix()
        {
            var hits = Engine.Value.Lint(new[] { "The section’s title is “Care and feeding.”" }, null, null).Where(h => h.RuleIds.Contains("google:quotation-marks/2")).ToList();
            Assert.Equal(3, hits.Count);
            Assert.All(hits, h => Assert.Equal("suggestion", h.Severity));
            Assert.Equal(new[] { "'", "\"", "\"" }, hits.OrderBy(h => h.Start).Select(h => h.Replacement).ToArray());
        }

        [Fact]
        public void Only_offensive_terms_are_errors()
        {
            var errors = Engine.Value.Compose(null).Rules.Where(r => (r.Id ?? "").StartsWith("google:") && r.Severity == "error").Select(r => r.Id).OrderBy(x => x).ToArray();
            Assert.Equal(new[] { "google:word-list/460", "google:word-list/600", "google:word-list/606" }, errors);
        }

        // ---- #63 web markup and scope -------------------------------------------------------------------------------------

        [Fact]
        public void Web_markup_rules_reach_the_reviewer_only_when_asked()
        {
            Assert.DoesNotContain("google:images/55", Engine.Value.RuleIdsFor(null, GuideScope.Of(new[] { "Images" })));
            Assert.Contains("google:images/17", Engine.Value.RuleIdsFor(null, GuideScope.Of(new[] { "Images" })));
            Assert.Contains("google:images/55", Engine.Value.RuleIdsFor(null, GuideScope.Of(new[] { "Images", ContentTags.WebMarkup })));
            Assert.Contains("google:other-sources/1", Engine.Value.RuleIdsFor(null, GuideScope.Of(new string[0])));   // core, not a Links pack
        }

        // ---- #64 new mechanical rows --------------------------------------------------------------------------------------

        [Fact]
        public void A_heading_period_is_flagged_in_headings_only()
        {
            Assert.Contains("google:periods/21", Rules("Configure the network.", heading: true));
            Assert.DoesNotContain("google:periods/21", Rules("Configure the network.", heading: false));
            Assert.DoesNotContain("google:periods/21", Rules("Products, services, etc.", heading: true));
        }

        [Fact]
        public void A_double_space_between_sentences_is_flagged_but_a_wrapped_line_is_not()
        {
            Assert.Contains("google:periods/22", Rules("Save the file.  Then close it."));
            Assert.Contains("google:periods/22", Rules("Save the file.  Then close it."));
            Assert.DoesNotContain("google:periods/22", Rules("Save the file.\r\n  Then close it."));
            Assert.DoesNotContain("google:periods/22", Rules("1.    Open the screen."));
            var fix = Engine.Value.Lint(new[] { "Save the file.  Then close it." }, null, null).Single(h => h.RuleId == "google:periods/22");
            var t = "Save the file.  Then close it.";
            Assert.Equal("Save the file. Then close it.", t.Substring(0, fix.Start) + fix.Replacement + t.Substring(fix.Start + fix.Length));
        }

        [Theory]
        [InlineData("You can utilize the API.", "google:translation/48", "use")]
        [InlineData("This document makes use of the following terms:", "google:translation/52", "uses")]
        [InlineData("The job will commence at noon.", "google:translation/50", "start")]
        public void Simple_word_rows_fix_the_phrase(string text, string rule, string fix)
        {
            Assert.Contains(Engine.Value.Lint(new[] { text }, null, null), h => h.RuleIds.Contains(rule) && h.Replacement == fix);
        }
    }
}
