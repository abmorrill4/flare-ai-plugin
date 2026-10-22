using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlareAI.Scoring
{
    /// <summary>Values of <see cref="Label.Kind"/>.</summary>
    public static class LabelKind
    {
        public const string Violation = "violation";
        public const string Clean = "clean";
        public const string Exempt = "exempt";
        public const string Ambiguous = "ambiguous";
        public const string BlockComplete = "block-complete";
        public static readonly string[] All = { Violation, Clean, Exempt, Ambiguous, BlockComplete };
    }

    /// <summary>Vocabulary shared with the engine's context extraction (issue #53) and the corpus tools.</summary>
    public static class ContextVocabulary
    {
        public static readonly string[] Line =
        {
            "heading:h1", "heading:h2", "heading:h3", "heading:h4", "heading:h5", "heading:h6",
            "step", "list", "table-header", "table-cell", "note", "warning", "caption", "quote", "code-block",
            "dropdown-head", "toggler", "glossary-def", "paragraph"
        };
        public static readonly string[] Span =
        {
            "ui-label", "link-text", "xref", "emphasis", "inline-code", "kbd", "variable-adjacent",
            "in-snippet-block", "in-snippet-text", "conditional", "boilerplate"
        };
        public static readonly string[] Status = { "proposed", "verified", "disputed" };
        public static readonly string[] Origin = { "seeded", "natural" };
        public static readonly string[] Split = { "dev", "test" };
    }

    public sealed class LabelContext
    {
        [JsonProperty("line")] public List<string> Line = new List<string>();
        [JsonProperty("span")] public List<string> Span = new List<string>();
    }

    /// <summary>One line of a <c>labels.jsonl</c> file. See <c>docs/testing/CORPUS.md</c> for the field meanings.</summary>
    public sealed class Label
    {
        [JsonProperty("id")] public string Id;
        /// <summary>Topic or snippet path relative to the project root, forward slashes.</summary>
        [JsonProperty("file")] public string File;
        /// <summary>XPath to the block element (names as written in the file, e.g. <c>/html/body/ol[1]/li[3]/p[1]</c>).</summary>
        [JsonProperty("block")] public string Block;
        /// <summary>Selected text inside the block's plain text. Absent: the label covers the whole element at <c>block</c>.</summary>
        [JsonProperty("quote", NullValueHandling = NullValueHandling.Ignore)] public string Quote;
        [JsonProperty("prefix", NullValueHandling = NullValueHandling.Ignore)] public string Prefix;
        [JsonProperty("suffix", NullValueHandling = NullValueHandling.Ignore)] public string Suffix;
        /// <summary>violation | clean | exempt | ambiguous | block-complete.</summary>
        [JsonProperty("label")] public string Kind;
        /// <summary>Defect taxonomy id (corpus/schema/defects.yaml); required for violations.</summary>
        [JsonProperty("kind", NullValueHandling = NullValueHandling.Ignore)] public string DefectKind;
        /// <summary>Acceptable rule ids or families; see <see cref="RuleSpec"/>.</summary>
        [JsonProperty("rules")] public List<string> Rules = new List<string>();
        [JsonProperty("fix", NullValueHandling = NullValueHandling.Ignore)] public string Fix;
        [JsonProperty("reason", NullValueHandling = NullValueHandling.Ignore)] public string Reason;
        [JsonProperty("context", NullValueHandling = NullValueHandling.Ignore)] public LabelContext Context;
        [JsonProperty("origin")] public string Origin;
        [JsonProperty("seed", NullValueHandling = NullValueHandling.Ignore)] public JToken Seed;
        [JsonProperty("status")] public string Status;
        [JsonProperty("by")] public List<string> By = new List<string>();
        [JsonProperty("split")] public string Split;

        /// <summary>Project name: the part of <see cref="Id"/> before the first slash.</summary>
        [JsonIgnore]
        public string Project
        {
            get { int i = Id == null ? -1 : Id.IndexOf('/'); return i > 0 ? Id.Substring(0, i) : ""; }
        }

        [JsonIgnore] public bool IsVerified { get { return string.Equals(Status, "verified", StringComparison.Ordinal); } }
    }
}
