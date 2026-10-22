using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;

namespace FlareAI.Core.Actions
{
    /// <summary>
    /// Changes a style edit must never make on its own: a modal verb (must, should, may…: changes an obligation or a
    /// normative keyword) or a code or API term (an identifier, a field or method name, a part number). Measured: 4 of 20
    /// hand-read AI findings did one of these ("should have reported" → "must report", "Clients should" → "Clients must",
    /// dropping the API's own term). The prompts forbid it; this is the check after the model answers.
    /// </summary>
    public static class MeaningGuard
    {
        // Only the modal verbs: "required" or "optional" as adjectives change too often in honest rewording to hold.
        private static readonly string[] Modals = { "must", "shall", "should", "may", "might" };
        private static readonly Regex Word = new Regex(@"[A-Za-z]+(?:'[a-z]+)?", RegexOptions.Compiled);
        // camelCase / PascalCase with an inner capital, snake_case, dotted.names, call(), and codes with digits (GP1, R11, HDMI-2).
        private static readonly Regex CodeTerm = new Regex(@"(?<![\w.])(?:[A-Za-z_][A-Za-z0-9_]+(?:\.[A-Za-z_][A-Za-z0-9_]+)+|[a-z]+[A-Z][A-Za-z0-9]*|[A-Z][a-z0-9]+[A-Z][A-Za-z0-9]*|[A-Za-z0-9]*_[A-Za-z0-9_]+|[A-Za-z_][A-Za-z0-9_]*\(\)|[A-Z]+[0-9][A-Z0-9-]*|[A-Z]{2,}-?[0-9]+)(?![\w])", RegexOptions.Compiled);
        private static readonly HashSet<string> CodeElements = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "code", "kbd", "samp", "var", "pre", "codeSnippet", "codeSnippetBody" };

        /// <summary>
        /// Why replacing <paramref name="before"/> with <paramref name="after"/> needs the author, or null when it doesn't.
        /// </summary>
        /// <param name="inCode">The text is inside code markup: any change at all needs the author.</param>
        /// <param name="lead">How the reason starts ("Not applied automatically: it", or "This edit" for one the author reviews whole).</param>
        public static string Check(string before, string after, bool inCode = false, string lead = "Not applied automatically: it")
        {
            before = before ?? ""; after = after ?? "";
            if (before == after) return null;
            if (inCode) return "This text is code; changing it is up to the author.";
            var was = Count(before);
            var now = Count(after);
            // A modal gained is the danger ("should" → "must", or a new "must"). One only dropped is usually the style fix
            // itself ("You should click Save" → "Click Save"), so that passes.
            var lost = Modals.Where(m => Get(now, m) < Get(was, m)).ToList();
            var gained = Modals.Where(m => Get(now, m) > Get(was, m)).ToList();
            if (gained.Count > 0)
            {
                var what = lost.Count > 0 ? "changes " + Quote(lost) + " to " + Quote(gained) : "adds " + Quote(gained);
                return lead + " " + what + ", which changes what the text requires or allows. Edit it yourself if that is meant.";
            }
            var terms = CodeTerm.Matches(before).Cast<Match>().Select(m => m.Value).Distinct().Where(t => after.IndexOf(t, StringComparison.Ordinal) < 0).ToList();
            if (terms.Count > 0)
                return lead + " drops the term " + Quote(terms) + ", which looks like a code or API name. Edit it yourself if that is meant.";
            return null;
        }

        /// <summary>True when the run sits inside code markup (code, kbd, samp, var, pre, a code snippet).</summary>
        public static bool InCode(XmlNode run)
        {
            for (var n = run == null ? null : run.ParentNode; n != null && n.NodeType == XmlNodeType.Element; n = n.ParentNode)
                if (CodeElements.Contains(n.LocalName)) return true;
            return false;
        }

        private static Dictionary<string, int> Count(string text)
        {
            var d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in Word.Matches(text))
            {
                var w = m.Value.ToLowerInvariant();
                if (w == "mustn't") w = "must";
                else if (w == "shouldn't") w = "should";
                else if (w == "mightn't") w = "might";
                if (Array.IndexOf(Modals, w) < 0) continue;
                d[w] = Get(d, w) + 1;
            }
            return d;
        }

        private static int Get(Dictionary<string, int> d, string k) { int n; return d.TryGetValue(k, out n) ? n : 0; }

        private static string Quote(IEnumerable<string> words) { return string.Join(", ", words.Select(w => "\"" + w + "\"").ToArray()); }
    }
}
