using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace FlareAI.Core.Actions
{
    public sealed class StyleFinding
    {
        /// <summary>"lint" (deterministic, docstyle) or "ai" (model judgment).</summary>
        public string Source { get; set; }
        public string Rule { get; set; }
        public string Severity { get; set; }
        /// <summary>Verbatim text the finding is about; always inside one editable text run.</summary>
        public string Excerpt { get; set; }
        /// <summary>Why, in one short sentence.</summary>
        public string Suggestion { get; set; }
        /// <summary>Exact text to put in place of Excerpt; null when the fix needs a rewrite.</summary>
        public string Replacement { get; set; }
        /// <summary>Up to 40 chars of run text before the excerpt — disambiguates repeats when re-locating.</summary>
        public string Before { get; set; }
        /// <summary>The body-level block the finding was found in (may be replaced by later edits).</summary>
        public System.Xml.XmlNode Block { get; set; }
        /// <summary>"google", "company", or "ai".</summary>
        public string Layer { get; set; }
        /// <summary>Guide page for the rule, when known.</summary>
        public string Link { get; set; }
        public bool IsFixed { get; set; }
        public bool CanFix { get { return Replacement != null && !IsFixed; } }
    }

    /// <summary>Tolerant extraction of our two response contracts: a JSON findings array, or a &lt;revised&gt; block.</summary>
    public static class ResponseParser
    {
        public static List<StyleFinding> ParseFindings(string text)
        {
            var list = new List<StyleFinding>();
            if (string.IsNullOrEmpty(text)) return list;
            var s = StripFences(text).Trim();
            var start = s.IndexOf('[');
            var end = s.LastIndexOf(']');
            if (start < 0 || end <= start) throw new FormatException("Model did not return a JSON array.");
            var arr = JArray.Parse(s.Substring(start, end - start + 1));
            foreach (var item in arr)
            {
                var replacement = item["replacement"];
                list.Add(new StyleFinding
                {
                    Source = "ai", Layer = "ai",
                    Rule = (string)item["rule"] ?? "",
                    Severity = ((string)item["severity"] ?? "suggestion").ToLowerInvariant(),
                    Excerpt = (string)item["excerpt"] ?? "",
                    Suggestion = (string)item["why"] ?? (string)item["suggestion"] ?? "",
                    Replacement = replacement == null || replacement.Type == JTokenType.Null ? null : (string)replacement
                });
            }
            return list;
        }

        public static string ParseRevised(string text)
        {
            if (string.IsNullOrEmpty(text)) throw new FormatException("Empty response.");
            var s = StripFences(text);
            var open = s.IndexOf("<revised>", StringComparison.OrdinalIgnoreCase);
            var close = s.LastIndexOf("</revised>", StringComparison.OrdinalIgnoreCase);
            if (open < 0 || close <= open) throw new FormatException("Model did not return a <revised> block.");
            return s.Substring(open + "<revised>".Length, close - open - "<revised>".Length).Trim();
        }

        private static string StripFences(string s)
        {
            // models sometimes wrap output in ```xml ... ``` despite instructions
            var t = s.Trim();
            if (t.StartsWith("```"))
            {
                var nl = t.IndexOf('\n');
                if (nl > 0) t = t.Substring(nl + 1);
                if (t.EndsWith("```")) t = t.Substring(0, t.Length - 3);
            }
            return t;
        }
    }
}
