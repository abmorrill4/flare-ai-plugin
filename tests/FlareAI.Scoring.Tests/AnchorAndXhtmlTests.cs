using System;
using System.Linq;
using FlareAI.Scoring;
using Xunit;

namespace FlareAI.Scoring.Tests
{
    public class AnchorTests
    {
        [Fact]
        public void Single_occurrence_resolves_without_context()
        {
            var r = Anchor.Resolve("Ready to go! Now.", "go!", null, null);
            Assert.True(r.Resolved);
            Assert.False(r.Ambiguous);
            Assert.Equal(9, r.Start);
            Assert.Equal(12, r.End);
        }

        [Fact]
        public void Prefix_picks_between_repeated_quotes()
        {
            var r = Anchor.Resolve("one two. one three.", "one", "two. ", null);
            Assert.Equal(9, r.Start);
            Assert.False(r.Ambiguous);
        }

        [Fact]
        public void Suffix_picks_between_repeated_quotes()
        {
            var r = Anchor.Resolve("one two. one three.", "one", null, " three");
            Assert.Equal(9, r.Start);
        }

        [Fact]
        public void Repeated_quote_without_context_is_ambiguous()
        {
            var r = Anchor.Resolve("one two. one three.", "one", null, null);
            Assert.True(r.Resolved);
            Assert.True(r.Ambiguous);
            Assert.Equal(0, r.Start);
        }

        [Fact]
        public void Missing_quote_is_unresolved()
        {
            Assert.False(Anchor.Resolve("abc", "xyz", null, null).Resolved);
            Assert.False(Anchor.Resolve(null, "xyz", null, null).Resolved);
        }

        [Fact]
        public void Without_a_quote_the_whole_block_is_selected()
        {
            var r = Anchor.Resolve("abc", null, null, null);
            Assert.True(r.Resolved);
            Assert.Equal(3, r.End);
        }

        [Fact]
        public void Partial_prefix_still_counts_for_the_score()
        {
            var r = Anchor.Resolve("ab one. zb one.", "one", "zb ", null);
            Assert.Equal(11, r.Start);
        }

        [Fact]
        public void MakeSelector_clips_at_the_block_edges()
        {
            string q, p, s;
            Anchor.MakeSelector("Hello world", 0, 5, 24, out q, out p, out s);
            Assert.Equal("Hello", q);
            Assert.Equal("", p);
            Assert.Equal(" world", s);
        }
    }

    public class RuleSpecTests
    {
        private static ScoreFinding F(string id, string origin = "lint", string kind = null) { return new ScoreFinding { RuleId = id, Origin = origin, Kind = kind }; }

        [Theory]
        [InlineData("google:word-list/221", "google:word-list/221", true)]
        [InlineData("Google:Word-List/221", "google:word-list/221", true)]
        [InlineData("google:word-list/221", "google:word-list/222", false)]
        [InlineData("google:word-list/*", "google:word-list/221", true)]
        [InlineData("google:word-list/*", "google:tone/22", false)]
        [InlineData("google:*", "google:tone/22", true)]
        [InlineData("google:*", "company:claims/2", false)]
        [InlineData("*", "company:claims/2", true)]
        [InlineData("lint", "structure:AltText", true)]
        [InlineData("structure:AltText", "structure:AltText", true)]
        [InlineData("google:word-list/2", "google:word-list/221", false)]
        public void Lint_rule_specs(string spec, string ruleId, bool expected)
        {
            Assert.Equal(expected, RuleSpec.Matches(spec, F(ruleId)));
        }

        [Fact]
        public void Ai_specs()
        {
            Assert.True(RuleSpec.Matches("ai", F(null, "ai")));
            Assert.False(RuleSpec.Matches("ai", F("google:tone/1")));
            Assert.True(RuleSpec.Matches("ai:passive", F(null, "ai", "passive")));
            Assert.False(RuleSpec.Matches("ai:passive", F(null, "ai", "wordy")));
            Assert.False(RuleSpec.Matches("ai:passive", F("google:passive/1", "lint", "passive")));
            Assert.False(RuleSpec.Matches("lint", F(null, "ai")));
            Assert.True(RuleSpec.Matches("*", F(null, "ai")));
        }

        [Fact]
        public void Layer_comes_from_the_rule_id_or_origin()
        {
            Assert.Equal("google", F("google:tone/22").Layer);
            Assert.Equal("structure", F("structure:AltText", "structure").Layer);
            Assert.Equal("ai", F("google:tone/22", "ai").Layer);
            Assert.Equal("structure", F(null, "structure").Layer);
        }

        [Fact]
        public void Empty_spec_never_matches()
        {
            Assert.False(RuleSpec.Matches("", F("a:b/1")));
            Assert.False(RuleSpec.Any(null, F("a:b/1")));
        }
    }

    public class XhtmlTests
    {
        private const string Topic = @"<?xml version=""1.0"" encoding=""utf-8""?>
<html xmlns:MadCap=""http://www.madcapsoftware.com/Schemas/MadCap.xsd"">
  <head><title>T</title></head>
  <body>
    <h1>Title</h1>
    <ol>
      <li><p>Click <b>Sign-On Settings</b> to
        open the page.</p><ul><li>Nested item</li></ul></li>
      <li>Second <MadCap:variable name=""General.Name"" /> step</li>
    </ol>
    <table><tr><th>Head</th></tr><tr><td>Cell</td></tr></table>
    <p class=""note"" MadCap:conditions=""Default.Print"">A <i>note</i><br />line.</p>
    <p><img src=""x.png"" alt="""" /></p>
  </body>
</html>";

        private static System.Collections.Generic.List<BlockText> Blocks()
        {
            return Xhtml.Blocks(Xhtml.Parse(Topic), n => n == "General.Name" ? "Acme" : null).ToList();
        }

        [Fact]
        public void Blocks_have_xpaths_with_indexes_except_html_head_body()
        {
            var paths = Blocks().Select(b => b.XPath).ToList();
            Assert.Contains("/html/body/h1[1]", paths);
            Assert.Contains("/html/body/ol[1]/li[1]/p[1]", paths);
            Assert.Contains("/html/body/ol[1]/li[1]/ul[1]/li[1]", paths);
            Assert.Contains("/html/body/ol[1]/li[2]", paths);
            Assert.Contains("/html/body/table[1]/tr[2]/td[1]", paths);
        }

        [Fact]
        public void Empty_blocks_are_not_listed()
        {
            Assert.DoesNotContain(Blocks(), b => b.XPath == "/html/body/p[2]");
        }

        [Fact]
        public void Plain_text_collapses_whitespace_and_expands_variables()
        {
            var b = Blocks();
            Assert.Equal("Click Sign-On Settings to open the page.", b.First(x => x.XPath == "/html/body/ol[1]/li[1]/p[1]").Text);
            Assert.Equal("Second Acme step", b.First(x => x.XPath == "/html/body/ol[1]/li[2]").Text);
            Assert.Equal("A note line.", b.First(x => x.XPath == "/html/body/p[1]").Text);
        }

        [Fact]
        public void A_block_excludes_nested_blocks()
        {
            var li = Blocks().FirstOrDefault(x => x.XPath == "/html/body/ol[1]/li[1]");
            Assert.Null(li); // its only text is inside the nested p and ul
        }

        [Fact]
        public void Unknown_variables_contribute_nothing()
        {
            var b = Xhtml.Blocks(Xhtml.Parse(Topic), null).First(x => x.XPath == "/html/body/ol[1]/li[2]");
            Assert.Equal("Second step", b.Text);
        }

        [Fact]
        public void Resolve_finds_the_element_an_xpath_names()
        {
            var doc = Xhtml.Parse(Topic);
            foreach (var b in Xhtml.Blocks(doc, null))
                Assert.Same(b.Element, Xhtml.Resolve(doc, b.XPath));
            Assert.Null(Xhtml.Resolve(doc, "/html/body/p[9]"));
            Assert.Null(Xhtml.Resolve(doc, "/html/body/nonsense[1]"));
            Assert.Null(Xhtml.Resolve(doc, "body"));
        }

        [Fact]
        public void Whole_element_text_covers_nested_blocks()
        {
            var doc = Xhtml.Parse(Topic);
            var t = Xhtml.Extract(Xhtml.Resolve(doc, "/html/body/table[1]"), null);
            Assert.Equal("Head Cell", t.Text);
        }

        [Fact]
        public void Line_context_follows_the_vocabulary()
        {
            var b = Blocks();
            Func<string, System.Collections.Generic.List<string>> line = x => Xhtml.LineContext(b.First(y => y.XPath == x).Element);
            Assert.Equal(new[] { "heading:h1" }, line("/html/body/h1[1]"));
            Assert.Equal(new[] { "step" }, line("/html/body/ol[1]/li[1]/p[1]"));
            Assert.Equal(new[] { "step" }, line("/html/body/ol[1]/li[2]"));
            Assert.Equal(new[] { "list" }, line("/html/body/ol[1]/li[1]/ul[1]/li[1]"));
            Assert.Equal(new[] { "table-header" }, line("/html/body/table[1]/tr[1]/th[1]"));
            Assert.Equal(new[] { "table-cell" }, line("/html/body/table[1]/tr[2]/td[1]"));
            Assert.Equal(new[] { "note" }, line("/html/body/p[1]"));
        }

        [Fact]
        public void Span_context_finds_ui_labels_emphasis_conditions_and_variables()
        {
            var b = Blocks();
            var step = b.First(x => x.XPath == "/html/body/ol[1]/li[1]/p[1]");
            Assert.Contains("ui-label", Xhtml.SpanContext(step, 6, 22, "Content/a.htm", false));
            Assert.DoesNotContain("ui-label", Xhtml.SpanContext(step, 0, 5, "Content/a.htm", false));
            var note = b.First(x => x.XPath == "/html/body/p[1]");
            var ctx = Xhtml.SpanContext(note, 2, 6, "Content/a.htm", false);
            Assert.Contains("emphasis", ctx);
            Assert.Contains("conditional", ctx);
            var second = b.First(x => x.XPath == "/html/body/ol[1]/li[2]");
            Assert.Contains("variable-adjacent", Xhtml.SpanContext(second, 0, 7, "Content/a.htm", false));
            Assert.Contains("in-snippet-block", Xhtml.SpanContext(second, 0, 6, "Content/Resources/Snippets/S.flsnp", false));
            Assert.Contains("boilerplate", Xhtml.SpanContext(second, 0, 6, "Content/a.htm", true));
        }
    }
}
