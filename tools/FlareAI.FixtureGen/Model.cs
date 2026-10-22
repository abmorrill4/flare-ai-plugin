using System.Collections.Generic;
using System.Linq;

namespace FlareAI.FixtureGen
{
    /// <summary>What a Bad case is expected to do in a context: "fires", "clean" or "any" (see expectations.yaml).</summary>
    public static class Outcome
    {
        public const string Fires = "fires", Clean = "clean", Any = "any";
    }

    /// <summary>One lint row of the guide and the cases that exercise it. Serialized to tests/fixtures/rules/&lt;layer&gt;/&lt;page&gt;.json.</summary>
    public sealed class RuleFixture
    {
        public string Id { get; set; }
        public string Layer { get; set; }
        public string Page { get; set; }
        public int Row { get; set; }
        public string Severity { get; set; }
        public string Action { get; set; }
        public List<string> Tags { get; set; }
        public List<string> Avoid { get; set; }
        public string Use { get; set; }
        public string Example { get; set; }
        public bool IgnoreCase { get; set; }
        /// <summary>The row's patterns can only match markup (tags, entities, attributes), which lint never sees (#55): no context cases.</summary>
        public bool MarkupOnly { get; set; }
        public List<BadCase> Bad { get; set; }
        public List<GoodCase> Good { get; set; }
    }

    /// <summary>Text the rule must flag: where, per context, comes from expectations.yaml.</summary>
    public sealed class BadCase
    {
        /// <summary>"example", "avoid" or "avoid-variant".</summary>
        public string Source { get; set; }
        public string Text { get; set; }
        /// <summary>Outcome (fires, clean, any) to the contexts, comma separated, where a Bad case should have it.</summary>
        public Dictionary<string, string> Expect { get; set; }

        /// <summary>Every context with its expected outcome.</summary>
        public IEnumerable<KeyValuePair<string, string>> ByContext()
        {
            foreach (var kv in Expect)
                foreach (var c in kv.Value.Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries))
                    yield return new KeyValuePair<string, string>(c.Trim(), kv.Key);
        }
    }

    /// <summary>Text no rule may flag: the rule's own Use wording (what the guide tells writers to write).</summary>
    public sealed class GoodCase
    {
        public string Source { get; set; }
        public string Text { get; set; }
        /// <summary>Comma separated context names.</summary>
        public string Contexts { get; set; }

        public IEnumerable<string> ContextList()
        {
            return Contexts.Split(new[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries).Select(c => c.Trim());
        }
    }

    public sealed class Manifest
    {
        public string GuideStamp { get; set; }
        public int Rules { get; set; }
        public int MarkupOnlyRules { get; set; }
        public int BadCases { get; set; }
        public int GoodCases { get; set; }
        public List<string> Contexts { get; set; }
        public List<string> Files { get; set; }
    }

    public sealed class StructureFile
    {
        public List<StructureRule> Rules { get; set; }
    }

    public sealed class StructureRule
    {
        public string Rule { get; set; }
        public List<StructureCase> Bad { get; set; }
        public List<StructureCase> Good { get; set; }
    }

    public sealed class StructureCase
    {
        public string Context { get; set; }
        public string Xhtml { get; set; }
    }
}
