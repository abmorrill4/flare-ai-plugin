using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace FlareAI.CorpusKit
{
    // SCHEMA STUB. These types mirror section 1.3 of dist/audit/TEST-STRATEGY.md (labels.jsonl, one object per line) so
    // CorpusKit can write labels before the label schema and scoring library (#67) land. When #67 merges, replace
    // this file with the shared type (or keep it: the JSON shape is the contract, not the C# class) and run
    // `corpuskit check` against corpus/schema/labels.schema.json. Property names are the schema's field names.

    internal sealed class LabelContext
    {
        [JsonProperty("line", Order = 1)] public List<string> Line = new List<string>();
        [JsonProperty("span", Order = 2)] public List<string> Span = new List<string>();
    }

    internal sealed class LabelSeed
    {
        [JsonProperty("generator", Order = 1)] public string Generator;
        [JsonProperty("variant", Order = 2)] public string Variant;
    }

    internal sealed class Label
    {
        [JsonProperty("id", Order = 1)] public string Id;
        [JsonProperty("file", Order = 2)] public string File;
        [JsonProperty("block", Order = 3)] public string Block;
        [JsonProperty("quote", Order = 4, NullValueHandling = NullValueHandling.Ignore)] public string Quote;
        [JsonProperty("prefix", Order = 5, NullValueHandling = NullValueHandling.Ignore)] public string Prefix;
        [JsonProperty("suffix", Order = 6, NullValueHandling = NullValueHandling.Ignore)] public string Suffix;
        [JsonProperty("label", Order = 7)] public string Verdict;                // violation | clean | exempt | ambiguous | block-complete
        [JsonProperty("kind", Order = 8, NullValueHandling = NullValueHandling.Ignore)] public string Kind;
        [JsonProperty("rules", Order = 9, NullValueHandling = NullValueHandling.Ignore)] public List<string> Rules;
        [JsonProperty("fix", Order = 10, NullValueHandling = NullValueHandling.Ignore)] public string Fix;
        [JsonProperty("reason", Order = 11, NullValueHandling = NullValueHandling.Ignore)] public string Reason;
        [JsonProperty("context", Order = 12, NullValueHandling = NullValueHandling.Ignore)] public LabelContext Context;
        [JsonProperty("origin", Order = 13, NullValueHandling = NullValueHandling.Ignore)] public string Origin;
        [JsonProperty("seed", Order = 14, NullValueHandling = NullValueHandling.Ignore)] public LabelSeed Seed;
        [JsonProperty("status", Order = 15, NullValueHandling = NullValueHandling.Ignore)] public string Status;
        [JsonProperty("by", Order = 16, NullValueHandling = NullValueHandling.Ignore)] public List<string> By;
        [JsonProperty("split", Order = 17, NullValueHandling = NullValueHandling.Ignore)] public string Split;
    }

    internal static class LabelIO
    {
        private static readonly JsonSerializerSettings S = new JsonSerializerSettings { Formatting = Formatting.None, NullValueHandling = NullValueHandling.Ignore };

        public static List<Label> Read(string path)
        {
            var list = new List<Label>();
            if (!File.Exists(path)) return list;
            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
                if (line.Trim().Length > 0) list.Add(JsonConvert.DeserializeObject<Label>(line));
            return list;
        }

        public static void Write(string path, IEnumerable<Label> labels)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            foreach (var l in labels) sb.Append(JsonConvert.SerializeObject(l, S)).Append('\n');
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }
    }
}
