using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FlareAI.Scoring
{
    /// <summary>One finding from the linter, structure checks or the AI review, as the scorer consumes it.</summary>
    public sealed class ScoreFinding
    {
        /// <summary>Caller's id (optional); echoed in per-finding results.</summary>
        public string Id;
        /// <summary>Topic or snippet path relative to the project root.</summary>
        public string File;
        /// <summary>Optional XPath of the block the finding is in. Unknown for v1 bench findings.</summary>
        public string Block;
        /// <summary>Optional offsets into the block plain text (see CORPUS.md). Both or neither.</summary>
        public int? Start, End;
        /// <summary>The flagged text; used when offsets are missing.</summary>
        public string Excerpt;
        /// <summary>Full rule id such as <c>google:tone/22</c>; structure checks use <c>structure:AltText</c>; AI without a rule use <c>ai:&lt;kind&gt;</c>.</summary>
        public string RuleId;
        /// <summary>lint | structure | ai.</summary>
        public string Origin = "lint";
        /// <summary>For AI findings: the judgment kind (matches <c>ai:&lt;kind&gt;</c> label rules).</summary>
        public string Kind;
        /// <summary>Optional project name; otherwise taken from the matched label.</summary>
        public string Project;

        /// <summary>google | company | project | structure | ai (anything before the first colon of the rule id; "ai" for AI findings).</summary>
        public string Layer
        {
            get
            {
                if (string.Equals(Origin, "ai", StringComparison.OrdinalIgnoreCase)) return "ai";
                int i = RuleId == null ? -1 : RuleId.IndexOf(':');
                return i > 0 ? RuleId.Substring(0, i).ToLowerInvariant() : (string.Equals(Origin, "structure", StringComparison.OrdinalIgnoreCase) ? "structure" : "other");
            }
        }

        public bool IsAi { get { return string.Equals(Origin, "ai", StringComparison.OrdinalIgnoreCase); } }
    }

    /// <summary>Reads findings in either the Bench v1 <c>findings.jsonl</c> shape (File, Origin, Rule, RuleId, Excerpt) or the v2 shape
    /// (file, block, start, end, excerpt, ruleId, origin, kind, project). Property names are case-insensitive.</summary>
    public static class FindingIO
    {
        public static List<ScoreFinding> Read(string path)
        {
            var list = new List<ScoreFinding>();
            int n = 0;
            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                n++;
                if (line.Trim().Length == 0) continue;
                JObject o;
                try { o = JObject.Parse(line); }
                catch (JsonException ex) { throw new InvalidDataException(path + " line " + n + ": " + ex.Message, ex); }
                list.Add(FromJson(o, n));
            }
            return list;
        }

        public static ScoreFinding FromJson(JObject o, int ordinal)
        {
            var f = new ScoreFinding();
            f.Id = Str(o, "id") ?? ("F" + ordinal.ToString("D5"));
            f.File = Str(o, "file");
            f.Block = Str(o, "block");
            f.Excerpt = Str(o, "excerpt");
            f.Origin = (Str(o, "origin") ?? "lint").ToLowerInvariant();
            f.Kind = Str(o, "kind");
            f.Project = Str(o, "project");
            JToken t;
            if ((t = Get(o, "start")) != null && t.Type == JTokenType.Integer) f.Start = (int)t;
            if ((t = Get(o, "end")) != null && t.Type == JTokenType.Integer) f.End = (int)t;
            string ruleId = Str(o, "ruleId");
            string rule = Str(o, "rule");
            if (string.IsNullOrEmpty(ruleId) && rule != null && rule.StartsWith("Structure.", StringComparison.Ordinal))
                ruleId = "structure:" + rule.Substring("Structure.".Length);
            else if (string.IsNullOrEmpty(ruleId) && rule != null && rule.StartsWith("Project.", StringComparison.Ordinal))
                ruleId = "project:" + rule.Substring("Project.".Length).ToLowerInvariant();
            f.RuleId = string.IsNullOrEmpty(ruleId) ? null : ruleId;
            return f;
        }

        private static JToken Get(JObject o, string name)
        {
            JProperty p = o.Property(name, StringComparison.OrdinalIgnoreCase);
            return p == null || p.Value.Type == JTokenType.Null ? null : p.Value;
        }

        private static string Str(JObject o, string name)
        {
            var t = Get(o, name);
            return t == null ? null : (string)t;
        }
    }
}
