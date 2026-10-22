using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using FlareAI.Core.Editing;

namespace FlareAI.Core.Style
{
    /// <summary>
    /// What a unit of text contains, as the Content condition tags guide rules are scoped with: a rule tagged
    /// Content.Steps is sent to the AI only for sections that have numbered steps. Untagged rules always apply.
    /// </summary>
    public static class ContentTags
    {
        public static readonly string[] All = { "Code", "Steps", "Lists", "Numbers", "Tables", "Links", "Headings", "Images", "UI", "Notes", "Examples", "Math", "API", "Footnotes", "Emphasis",
                                                "Quotes", "Ellipses", "Slashes", "Semicolons", "Parentheses", "Dashes", "Colons", "Apostrophes", "Hyphens", "Abbreviations", WebMarkup };

        /// <summary>
        /// Rules about hand-writing HTML or Markdown (entities, attributes, which element to use): Flare writes the markup, so
        /// these never reach the reviewer unless a scope asks for them (#63). <see cref="Of"/> never reports this tag.
        /// </summary>
        public const string WebMarkup = "WebMarkup";

        // What the text itself shows: punctuation rules matter only where the punctuation is.
        private static readonly KeyValuePair<string, Regex>[] TextTags =
        {
            new KeyValuePair<string, Regex>("Quotes", new Regex("[\"“”‘’]|(?<!\\w)'|'(?!\\w)", RegexOptions.Compiled)),
            new KeyValuePair<string, Regex>("Ellipses", new Regex(@"\.\.\.|…", RegexOptions.Compiled)),
            new KeyValuePair<string, Regex>("Slashes", new Regex(@"/|\\", RegexOptions.Compiled)),
            new KeyValuePair<string, Regex>("Semicolons", new Regex(";", RegexOptions.Compiled)),
            new KeyValuePair<string, Regex>("Parentheses", new Regex(@"[()]", RegexOptions.Compiled)),
            new KeyValuePair<string, Regex>("Dashes", new Regex(@"[—–]| - | -- ", RegexOptions.Compiled)),
            new KeyValuePair<string, Regex>("Colons", new Regex(":", RegexOptions.Compiled)),
            new KeyValuePair<string, Regex>("Apostrophes", new Regex(@"\w['’]\w|s['’](?!\w)", RegexOptions.Compiled)),
            new KeyValuePair<string, Regex>("Hyphens", new Regex(@"\w-\w", RegexOptions.Compiled)),
            new KeyValuePair<string, Regex>("Abbreviations", new Regex(@"\b[A-Z][A-Z0-9]+s?\b|\b(?:e\.g|i\.e|etc|vs)\b\.?", RegexOptions.Compiled)),
        };

        private static readonly Regex Digit = new Regex(@"\d", RegexOptions.Compiled);
        private static readonly Regex Domainish = new Regex(@"\b[\w.-]+\.(?:com|org|net|io|dev|example|test)\b|[\w.+-]+@[\w-]+\.\w+|\bhttps?://", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex Mathish = new Regex(@"\d\s*[×÷=^≤≥±]\s*\d|\b(?:sqrt|log|sin|cos)\(", RegexOptions.Compiled);
        private static readonly Regex Apiish = new Regex(@"\b(?:API|parameters?|methods?|endpoints?|returns?|fields?|request|response)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex NoteStart = new Regex(@"^\s*(?:Note|Tip|Important|Caution|Warning|Key point)\s*:", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static HashSet<string> Of(IList<XmlNode> blocks)
        {
            var tags = new HashSet<string>();
            foreach (var b in blocks)
            {
                if (b.NodeType != XmlNodeType.Element) continue;
                var els = new List<XmlElement> { (XmlElement)b };
                els.AddRange(((XmlElement)b).SelectNodes(".//*").Cast<XmlElement>());
                foreach (var e in els)
                {
                    var n = e.LocalName;
                    var madcap = e.NamespaceURI == BlockSelector.MadCapNs;
                    if (!madcap)
                    {
                        if (n == "code" || n == "pre" || n == "kbd" || n == "samp" || n == "var") tags.Add("Code");
                        else if (n == "ol") { tags.Add("Steps"); tags.Add("Lists"); }
                        else if (n == "ul" || n == "dl") tags.Add("Lists");
                        else if (n == "table") tags.Add("Tables");
                        else if (n == "a") tags.Add("Links");
                        else if (n.Length == 2 && n[0] == 'h' && char.IsDigit(n[1])) tags.Add("Headings");
                        else if (n == "img") tags.Add("Images");
                        else if (n == "b" || n == "strong") tags.Add("UI");
                        else if (n == "i" || n == "em") tags.Add("Emphasis");
                        else if (n == "sup") tags.Add("Footnotes");
                        else if (n == "math") tags.Add("Math");
                        var cls = e.GetAttribute("class").ToLowerInvariant();
                        if (cls.Contains("note") || cls.Contains("tip") || cls.Contains("warning") || cls.Contains("caution")) tags.Add("Notes");
                    }
                    else if (n == "xref") tags.Add("Links");
                    else if (n == "codeSnippet") tags.Add("Code");
                    else if (n == "footnote") tags.Add("Footnotes");
                    else if (n == "equation") tags.Add("Math");
                }
                var text = b.InnerText;
                if (Digit.IsMatch(text)) tags.Add("Numbers");
                if (Domainish.IsMatch(text)) tags.Add("Examples");
                if (Mathish.IsMatch(text)) tags.Add("Math");
                if (NoteStart.IsMatch(text)) tags.Add("Notes");
                if (tags.Contains("Code") && Apiish.IsMatch(text)) tags.Add("API");
                foreach (var t in TextTags) if (t.Value.IsMatch(text)) tags.Add(t.Key);
            }
            return tags;
        }
    }
}
