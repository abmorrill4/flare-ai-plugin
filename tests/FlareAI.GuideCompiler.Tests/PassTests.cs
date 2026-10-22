using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]   // the compiler keeps the source units in statics

namespace FlareAI.GuideCompiler.Tests
{
    /// <summary>The deterministic compiler passes on small made-up guides: term scope (#58), products (#57), canonical owner (#56), fix check (#59).</summary>
    public class PassTests
    {
        // ---- a made-up source and compiled guide ---------------------------------------------------------------------

        private static JObject Unit(string id, string kind, string text) { return new JObject { ["id"] = id, ["hash"] = id.GetHashCode().ToString("x"), ["kind"] = kind, ["heading"] = "", ["text"] = text }; }

        private static void Source()
        {
            Topic.LoadUnits(new JObject
            {
                ["word-list"] = new JArray(
                    Unit("word-list#1", "dt", "persistent disk link: Not PD . Lowercase except at the start of a sentence. please link Don't use please in the normal course."),
                    Unit("word-list#2", "dt", "proper, properly link: Avoid proper and properly."),
                    Unit("word-list#3", "dt", "tap link: In Android documentation, use for on-screen buttons. Use instead of touch."),
                    Unit("word-list#4", "dt", "e.g. link: Don't use. Instead, use for example."),
                    Unit("word-list#5", "dt", "i.e. link: Don't use. Instead, use that is."),
                    Unit("word-list#6", "dt", "for example link: Follow it with a comma."),
                    Unit("word-list#7", "dt", "man-hours link: Don't use. Instead, use person-hours."),
                    Unit("word-list#8", "dt", "admin link: Write out administrator. It's OK to use admin in Android documentation.")),
                ["abbreviations"] = new JArray(Unit("abbreviations#1", "p", "Don't use i.e. or e.g. Instead, write that is and for example.")),
                ["inclusive"] = new JArray(Unit("inclusive#1", "p", "Avoid gendered words such as man-hours and mankind. Build AI that benefits mankind.")),
            });
        }

        private static JObject Rule(string key, string kind, string heading, string units, string avoid = "", string use = "", string pattern = "", string example = "", string text = "", string severity = "warning")
        {
            return new JObject
            {
                ["heading"] = heading, ["kind"] = kind, ["text"] = text, ["bad"] = "", ["good"] = "", ["avoid"] = avoid, ["use"] = use, ["pattern"] = pattern,
                ["severity"] = severity, ["why"] = "", ["example"] = example, ["applies"] = new JArray(), ["products"] = new JArray(),
                ["units"] = new JArray(units.Split(',').Cast<object>().ToArray()), ["key"] = key
            };
        }

        private static JObject Page(string title, string scope, params JObject[] rules)
        {
            return new JObject { ["title"] = title, ["url"] = "https://example.com/" + title, ["scope"] = scope, ["rules"] = new JArray(rules.Cast<object>().ToArray()),
                                 ["not_applicable"] = new JArray(), ["uncovered"] = new JArray(), ["dropped"] = new JArray(), ["errors"] = new JArray(), ["retired"] = new JArray() };
        }

        private static JObject Coverage(JObject compiled)
        {
            var c = new JObject();
            foreach (var p in compiled.Properties()) c[p.Name] = new JObject { ["scope"] = (string)p.Value["scope"] };
            return c;
        }

        private static JObject RuleOf(JObject compiled, string page, string key) { return ((JArray)compiled[page]["rules"]).OfType<JObject>().FirstOrDefault(r => (string)r["key"] == key); }

        private static PassConfig Config(string owners = "{}", string products = "{}", string fixes = "{}")
        {
            return new PassConfig { Owners = JObject.Parse(owners), Products = JObject.Parse(products), Fixes = JObject.Parse(fixes) };
        }

        // ---- #58 term scope ---------------------------------------------------------------------------------------------

        [Fact]
        public void A_term_rule_that_claims_a_neighbouring_entry_keeps_only_its_own_term()
        {
            Source();
            var rule = Rule("583", "term", "proper, properly", "word-list#2,word-list#1", text: "Avoid proper and properly, which are vague.");
            rule["term"] = "proper, properly, persistent disk, please";
            var compiled = new JObject { ["word-list"] = Page("Word list", "lint", rule) };
            Passes.Run(compiled, Coverage(compiled), Config());
            Assert.Equal("proper, properly", (string)RuleOf(compiled, "word-list", "583")["term"]);
            Assert.Contains(compiled["word-list"]["passes"], n => ((string)n).Contains("claims units of several entries"));
            var job = Passes.JobOf("word-list", (JObject)compiled["word-list"], null);
            Assert.DoesNotContain("please", Topic.Write(job, Topic.Prepare(job, (JArray)compiled["word-list"]["rules"])));
        }

        [Fact]
        public void A_glued_entry_takes_its_term_from_the_heading()
        {
            Source();
            // "please" has no unit of its own: it is glued inside the persistent disk unit.
            var rule = Rule("551", "prose", "please", "word-list#1", text: "Don't use please in the normal course of explaining how to use a product.");
            Assert.Equal("please", Topic.EntryTerm(rule));
        }

        // ---- #57 products -----------------------------------------------------------------------------------------------

        [Fact]
        public void Rows_get_product_tags_from_their_entry_and_from_products_json_but_not_from_a_passing_mention()
        {
            Source();
            var compiled = new JObject
            {
                ["word-list"] = Page("Word list", "lint",
                    Rule("673", "row", "tap", "word-list#3", avoid: "touch", use: "tap", example: "Touch the button."),
                    Rule("546", "row", "persistent disk", "word-list#1", avoid: "PD", use: "persistent disk", example: "Store it on the PD."),
                    Rule("30", "row", "admin", "word-list#8", avoid: "admin", use: "administrator", example: "Ask your admin.", text: "Write out administrator. It's OK to use admin in Android documentation."))
            };
            var products = @"{ ""names"": { ""Android"": ""Android"", ""GoogleCloud"": ""Google Cloud|gcloud"" }, ""entries"": { ""GoogleCloud"": [ ""persistent disk"" ] } }";
            Passes.Run(compiled, Coverage(compiled), Config(products: products));
            Assert.Equal(new[] { "Android" }, RuleOf(compiled, "word-list", "673")["products"].Select(x => (string)x));
            Assert.Equal(new[] { "GoogleCloud" }, RuleOf(compiled, "word-list", "546")["products"].Select(x => (string)x));
            Assert.Empty(RuleOf(compiled, "word-list", "30")["products"]);
        }

        // ---- #56 canonical owner ----------------------------------------------------------------------------------------

        [Fact]
        public void A_row_whose_terms_the_word_list_owns_is_dropped_and_its_units_move_to_the_owners()
        {
            Source();
            var compiled = new JObject
            {
                ["word-list"] = Page("Word list", "lint",
                    Rule("244", "row", "e.g.", "word-list#4", avoid: "e.g., E.g.", use: "for example,", example: "Use a format, e.g. JSON."),
                    Rule("377", "row", "i.e.", "word-list#5", avoid: "i.e.", use: "that is", example: "Use the default, i.e., the closest one.")),
                ["abbreviations"] = Page("Abbreviations", "core",
                    Rule("18", "row", "Abbreviations not to use", "abbreviations#1", avoid: "i.e., e.g.", use: "that is, for example", example: "Use a short name, e.g. myapp.")),
            };
            var cfg = Config();
            Passes.Run(compiled, Coverage(compiled), cfg);
            Assert.Null(RuleOf(compiled, "abbreviations", "18"));
            Assert.Contains("abbreviations#1", RuleOf(compiled, "word-list", "244")["units"].Select(u => (string)u));
            Assert.Contains("abbreviations#1", RuleOf(compiled, "word-list", "377")["units"].Select(u => (string)u));
            Assert.Contains(compiled["abbreviations"]["retired"], r => (string)r["key"] == "18");
            Assert.Equal("google:word-list/244", (string)cfg.Resolved["e.g."]);
        }

        [Fact]
        public void A_shared_term_is_removed_with_its_own_fix_and_the_example_is_repaired()
        {
            Source();
            var compiled = new JObject
            {
                ["word-list"] = Page("Word list", "lint",
                    Rule("450", "row", "man-hours", "word-list#7", avoid: "man-hours", use: "person-hours", example: "The migration took 40 man-hours.")),
                ["inclusive"] = Page("Inclusive", "core",
                    Rule("2", "row", "Gendered language", "inclusive#1", avoid: "man-hours, mankind", use: "person-hours, humanity", example: "It takes 16 man-hours to install.")),
            };
            Passes.Run(compiled, Coverage(compiled), Config());
            var r = RuleOf(compiled, "inclusive", "2");
            Assert.Equal("mankind", (string)r["avoid"]);
            Assert.Equal("humanity", (string)r["use"]);
            Assert.Equal("Build AI that benefits mankind.", (string)r["example"]);
        }

        [Fact]
        public void Page_rank_scope_and_decisions_pick_the_owner_of_a_conflict()
        {
            Source();
            var compiled = new JObject
            {
                ["word-list"] = Page("Word list", "lint",
                    Rule("368", "row", "hover", "word-list#5", avoid: "hover over", use: "hold the pointer over", example: "Hover over File.")),
                ["inclusive"] = Page("Inclusive", "core",
                    Rule("15", "row", "Metaphors", "inclusive#1", avoid: "hover over", use: "point to", example: "Then hover over File.")),
            };
            ((JArray)RuleOf(compiled, "inclusive", "15")["applies"]).Add("UI");

            var byRank = (JObject)compiled.DeepClone();   // the Word-List owns words
            Passes.Run(byRank, Coverage(byRank), Config());
            Assert.NotNull(RuleOf(byRank, "word-list", "368"));
            Assert.Null(RuleOf(byRank, "inclusive", "15"));

            var scoped = (JObject)compiled.DeepClone();   // a UI-only rule can't stand in for a general one, even ranked higher
            Passes.Run(scoped, Coverage(scoped), Config(owners: @"{ ""topical"": [ { ""page"": ""inclusive"" } ] }"));
            Assert.NotNull(RuleOf(scoped, "word-list", "368"));
            Assert.NotNull(RuleOf(scoped, "inclusive", "15"));

            Passes.Run(compiled, Coverage(compiled), Config(owners: @"{ ""decisions"": [ { ""term"": ""hover over"", ""owner"": ""google:inclusive/15"" } ] }"));
            Assert.Null(RuleOf(compiled, "word-list", "368"));
            Assert.NotNull(RuleOf(compiled, "inclusive", "15"));
        }

        // ---- #59 fixes that create findings -----------------------------------------------------------------------------

        [Fact]
        public void A_fix_that_another_rule_flags_gets_its_comma_or_becomes_flag_only()
        {
            Source();
            var compiled = new JObject
            {
                ["word-list"] = Page("Word list", "lint",
                    Rule("244", "row", "e.g.", "word-list#4", avoid: "e.g., E.g.", use: "for example, For example", example: "Use a supported format, e.g. JSON."),
                    Rule("296", "pattern", "for example", "word-list#6", pattern: @"\b[Ff]or example(?= )", example: "Enter a hex number (for example 228B22)."),
                    Rule("269", "pattern", "etc.", "word-list#5", pattern: @"\betc\b(?!\.)", use: "etc.", example: "Fonts, colors, etc follow."),
                    Rule("36", "pattern", "lists", "word-list#5", pattern: @",\s+etc\.", example: "Fonts, colors, etc. follow.")),
            };
            Passes.Run(compiled, Coverage(compiled), Config());
            Assert.Equal("for example,", (string)RuleOf(compiled, "word-list", "244")["use"]);
            Assert.Equal("", (string)RuleOf(compiled, "word-list", "269")["use"]);
        }

        [Fact]
        public void Fix_decisions_change_the_row_before_the_other_passes()
        {
            Source();
            var compiled = new JObject
            {
                ["word-list"] = Page("Word list", "lint",
                    Rule("19", "row", "Compound nouns", "word-list#7", avoid: "web page, host name", use: "webpage, hostname", example: "Open the web page."))
            };
            Passes.Run(compiled, Coverage(compiled), Config(fixes: @"{ ""rules"": [ { ""rule"": ""google:word-list/19"", ""remove"": [ ""web page"" ], ""reason"": ""inferred"" } ] }"));
            var r = RuleOf(compiled, "word-list", "19");
            Assert.Equal("host name", (string)r["avoid"]);
            Assert.Equal("hostname", (string)r["use"]);
            Assert.Contains("host name", (string)r["example"]);
        }
    }
}
