using System.Text.RegularExpressions;

namespace FlareAI.Core.Style
{
    /// <summary>
    /// How a rule is named for writers. A compiled pattern rule's text starts with its regular expression
    /// ("Avoid the pattern \b\d+\b… (rewrite)."): that stays in the details under "Pattern", never in a name.
    /// </summary>
    public static class RuleWording
    {
        /// <summary>The generic name of a pattern rule that has neither a reason nor an example.</summary>
        public const string Generic = "Avoid this wording pattern (see example)";

        private static readonly Regex PatternRule = new Regex(@"^Avoid the pattern .+? (?:\((?:rewrite|delete it)\)|→ use '[^']*')\.\s*", RegexOptions.Singleline);

        /// <summary>True when <paramref name="text"/> is the compiled form of a pattern row.</summary>
        public static bool IsPatternText(string text) { return text != null && text.StartsWith("Avoid the pattern ", System.StringComparison.Ordinal); }

        /// <summary>
        /// The name to show for a rule: its text as written, except for a pattern rule, which shows its own reason, else
        /// "Avoid this wording pattern (for example: …)" from its example (or its Bad text), else a generic name.
        /// </summary>
        public static string Display(string text, string example, string bad)
        {
            if (!IsPatternText(text)) return text ?? "";
            var m = PatternRule.Match(text);
            var rest = m.Success ? text.Substring(m.Length).Trim() : "";
            if (rest.Length > 0) return rest;
            var sample = Clean(example);
            if (sample.Length == 0) sample = Clean(bad);
            return sample.Length == 0 ? Generic : "Avoid this wording pattern (for example: " + sample + ")";
        }

        /// <summary>
        /// A readable stand-in for an internal rule name when the rule itself isn't at hand:
        /// "Company.PlainLanguage2" → "Plain language 2"; "google:word-list/221" → "Word list 221".
        /// </summary>
        public static string FromName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            var s = name.Trim();
            var colon = s.IndexOf(':');
            if (colon >= 0) s = s.Substring(colon + 1).Replace('-', ' ').Replace('/', ' ');
            else if (s.IndexOf('.') >= 0) s = s.Substring(s.LastIndexOf('.') + 1);
            s = Regex.Replace(s, @"(?<=[a-z])(?=[A-Z0-9])|(?<=[0-9])(?=[A-Za-z])", " ").Trim();
            s = Regex.Replace(s, @"\s+", " ");
            return s.Length == 0 ? name : char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant();
        }

        private static string Clean(string s)
        {
            s = Regex.Replace(s ?? "", @"\s+", " ").Trim();
            return s.Length > 100 ? s.Substring(0, 99) + "…" : s;
        }
    }
}
