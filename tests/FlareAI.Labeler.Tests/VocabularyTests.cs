using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FlareAI.Scoring;
using Xunit;

namespace FlareAI.Labeler.Tests
{
    public class VocabularyTests
    {
        private static readonly HashSet<string> Ai = new HashSet<string> { "exclaim", "passive", "undefined_abbr", "ui_not_bold", "vague" };

        [Theory]
        [InlineData("google:word-list/221")]
        [InlineData("google:word-list/*")]
        [InlineData("google:*")]
        [InlineData("company:terminology/3")]
        [InlineData("company:plain-language/*")]
        [InlineData("structure:AltText")]
        [InlineData("project:variables")]
        [InlineData("project:variables/*")]
        [InlineData("ai:vague")]
        [InlineData("ai")]
        [InlineData("lint")]
        [InlineData("*")]
        public void Scorer_vocabulary_is_accepted(string spec)
        {
            Assert.Null(RuleVocabulary.Check(spec, Ai));
        }

        [Theory]
        [InlineData("structure:link-text")]
        [InlineData("structure:Unknown")]
        [InlineData("company:plain-language")]          // a page, not a rule: matches no finding
        [InlineData("google:word-list")]
        [InlineData("google:dates-and-times/*")]
        [InlineData("google:exclamation-points")]
        [InlineData("ai:undefined-abbreviation")]
        [InlineData("localized-style:terminology/*")]
        [InlineData("word-list/221")]
        [InlineData("company:claims/x")]
        public void Generator_hints_are_rejected(string spec)
        {
            Assert.NotNull(RuleVocabulary.Check(spec, Ai));
        }

        [Theory]
        [InlineData("structure:link-text", "structure:LinkText")]
        [InlineData("structure:hard-coded-value", "structure:HardCoded")]
        [InlineData("structure:heading-order", "structure:HeadingOrder")]
        [InlineData("structure:alt-text", "structure:AltText")]
        [InlineData("company:plain-language", "company:plain-language/*")]
        [InlineData("company:claims", "company:claims/*")]
        [InlineData("localized-style:terminology/*", "company:terminology/*")]
        [InlineData("google:dates-and-times/*", "google:dates-times/*")]
        [InlineData("google:exclamation-points", "google:*")]
        [InlineData("google:latin-abbreviations", "google:*")]
        [InlineData("google:word-list", "google:word-list/*")]
        [InlineData("ai:undefined-abbreviation", "ai:undefined_abbr")]
        [InlineData("google:word-list/221", "google:word-list/221")]
        public void Hints_map_to_scorer_ids(string hint, string expected)
        {
            Assert.Equal(new[] { expected }, RuleVocabulary.Normalize(hint, Ai).ToArray());
        }

        [Fact]
        public void The_ui_references_hint_becomes_the_company_row_and_the_ai_kind()
        {
            Assert.Equal(new[] { "company:ui-references/*", "ai:ui_not_bold" }, RuleVocabulary.Normalize("ai:ui-references", Ai).ToArray());
        }

        [Fact]
        public void Every_normalised_id_passes_the_check()
        {
            foreach (var hint in new[] { "structure:alt-text", "company:claims", "google:future-tense", "ai:ui-references", "google:dates-and-times/*", "localized-style:terminology/*" })
                foreach (var n in RuleVocabulary.Normalize(hint, Ai)) Assert.Null(RuleVocabulary.Check(n, Ai));
        }

        [Fact]
        public void Overlap_of_rule_specs()
        {
            Assert.True(RuleVocabulary.Intersects("*", "google:word-list/221"));
            Assert.True(RuleVocabulary.Intersects("google:*", "google:word-list/221"));
            Assert.True(RuleVocabulary.Intersects("google:word-list/*", "google:word-list/221"));
            Assert.False(RuleVocabulary.Intersects("google:word-list/*", "google:tone/1"));
            Assert.False(RuleVocabulary.Intersects("google:*", "company:*"));
            Assert.True(RuleVocabulary.Intersects("lint", "structure:AltText"));
            Assert.False(RuleVocabulary.Intersects("lint", "ai:vague"));
            Assert.True(RuleVocabulary.Intersects("ai", "ai:vague"));
            Assert.False(RuleVocabulary.Intersects("ai:vague", "ai:passive"));
        }

        // ---- kept in step with the engine and the guides ------------------------------------------------------------------

        [Fact]
        public void Google_slugs_are_the_guide_pages()
        {
            string dir = CorpusProject.FindUp(AppDomain.CurrentDomain.BaseDirectory, "styleguide/Content/Resources/StyleGuide/Google");
            Assert.NotNull(dir);
            var pages = Directory.GetFiles(dir, "*.htm").Select(f => Path.GetFileNameWithoutExtension(f).ToLowerInvariant()).OrderBy(x => x).ToArray();
            Assert.Equal(pages, RuleVocabulary.GoogleSlugs.OrderBy(x => x).ToArray());
        }

        [Fact]
        public void Company_pages_are_the_guide_pages()
        {
            string dir = CorpusProject.FindUp(AppDomain.CurrentDomain.BaseDirectory, "styleguide/Content/Resources/StyleGuide/Company");
            var pages = Directory.GetFiles(dir, "*.htm").Select(f => Path.GetFileNameWithoutExtension(f).ToLowerInvariant()).Where(p => p != "about").OrderBy(x => x).ToArray();
            Assert.Equal(pages, RuleVocabulary.CompanyPages.OrderBy(x => x).ToArray());
        }

        [Fact]
        public void Structure_rules_are_the_engines()
        {
            string file = CorpusProject.FindUp(AppDomain.CurrentDomain.BaseDirectory, "src/FlareAI.Core/Style/StructureCheck.cs");
            Assert.NotNull(file);
            var names = Regex.Matches(File.ReadAllText(file), "\"Structure\\.([A-Za-z0-9]+)\"").Cast<Match>().Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x).ToArray();
            Assert.Equal(names, RuleVocabulary.StructureRules.OrderBy(x => x).ToArray());
        }
    }

    public class NormalizerTests
    {
        [Fact]
        public void Rewrites_rules_promotes_exact_generator_labels_and_is_idempotent()
        {
            using (var p = new TempProject())
            {
                p.Topic("Content/a.htm", "<p>Click here to utilize the app!</p>");
                var tax = TempProject.Taxonomy();
                var seeded = p.Label("violation", "Content/a.htm", "/html/body/p[1]", "utilize", "utilize", new[] { "company:plain-language" }, status: "proposed");
                var natural = p.Label("violation", "Content/a.htm", "/html/body/p[1]", "Click here", "phone", new[] { "google:word-list" }, status: "proposed", origin: "natural", by: new[] { "dom-check" });
                var labels = new List<Label> { seeded, natural };

                var res = Normalizer.Run(labels, tax, p.Open());

                Assert.Equal(new[] { "company:plain-language/*" }, seeded.Rules.ToArray());
                Assert.Equal("verified", seeded.Status);
                Assert.Equal(new[] { "google:word-list/*" }, natural.Rules.ToArray());
                Assert.Equal("proposed", natural.Status);   // only an exact generator earns verified
                Assert.Equal(2, res.RulesRewritten);
                Assert.Equal(1, res.Promoted);

                var again = Normalizer.Run(labels, tax, p.Open());
                Assert.Equal(0, again.RulesRewritten);
                Assert.Equal(0, again.Promoted);
            }
        }

        [Fact]
        public void A_seeded_label_whose_quote_does_not_anchor_stays_proposed()
        {
            using (var p = new TempProject())
            {
                p.Topic("Content/a.htm", "<p>Plain text.</p>");
                var l = p.Label("violation", "Content/a.htm", "/html/body/p[1]", "missing words", "exclaim", status: "proposed");
                var res = Normalizer.Run(new List<Label> { l }, TempProject.Taxonomy(), p.Open());
                Assert.Equal("proposed", l.Status);
                Assert.Equal(1, res.PromotionBlocked);
            }
        }

        [Fact]
        public void Block_complete_labels_are_never_promoted_by_a_generator()
        {
            using (var p = new TempProject())
            {
                var l = p.Label("block-complete", "Content/a.htm", "/html/body/p[1]", null, status: "proposed");
                Normalizer.Run(new List<Label> { l }, TempProject.Taxonomy(), null);
                Assert.Equal("proposed", l.Status);
            }
        }
    }
}
