using System;
using System.Collections.Generic;
using System.Linq;

namespace FlareAI.Labeler
{
    /// <summary>
    /// The rule ids a label's <c>rules</c> may name, and the mapping from the hints the corpus generators wrote
    /// (<c>structure:alt-text</c>, <c>company:plain-language</c>, <c>ai:ui-references</c>) to them. Kept in step with
    /// the engine by tests: Google slugs must match the compiled guide pages, structure names the engine's
    /// <c>Structure.*</c> rules, company pages the Company guide.
    /// </summary>
    public static class RuleVocabulary
    {
        /// <summary>Google guide page slugs (<c>google:&lt;slug&gt;/&lt;n&gt;</c>), from <c>tools/google-style/compiled.json</c>.</summary>
        public static readonly string[] GoogleSlugs =
        {
            "abbreviations", "accessibility", "anthropomorphism", "api-reference-comments", "articles", "capitalization", "code-in-text",
            "code-samples", "code-syntax", "colons", "commas", "contractions", "cross-references", "dashes", "dates-times", "ellipses",
            "examples", "excessive-claims", "filenames", "footnotes", "format-examples", "future", "headings", "headings-targets", "hyphens",
            "images", "inclusive-documentation", "italics-terms", "jargon", "lists", "mathematical-notation", "notices", "numbers",
            "other-sources", "paragraph-structure", "parentheses", "periods", "person", "phone-numbers", "placeholders", "pluralization",
            "possessives", "prepositions", "prescriptive-documentation", "procedures", "product-names", "pronouns", "quotation-marks",
            "reference-verbs", "semantic-tagging", "semicolons", "sentence-structure", "slashes", "tables", "tense",
            "timeless-documentation", "tone", "trademarks", "translation", "ui-elements", "units-of-measure", "voice", "word-list"
        };

        /// <summary>The engine's structure checks (<c>Structure.*</c>), written <c>structure:Name</c>.</summary>
        public static readonly string[] StructureRules = { "AltText", "OneH1", "HeadingOrder", "TableHeader", "LinkText", "HardCoded" };

        /// <summary>Pages of the Company guide, written <c>company:&lt;page&gt;/&lt;n&gt;</c> or as a family <c>company:&lt;page&gt;/*</c>.</summary>
        public static readonly string[] CompanyPages = { "claims", "plain-language", "terminology", "ui-references", "google-overrides" };

        public static readonly string[] Layers = { "google", "company", "structure", "project", "ai" };

        private static readonly HashSet<string> GoogleSet = new HashSet<string>(GoogleSlugs, StringComparer.Ordinal);
        private static readonly Dictionary<string, string> StructureByKey = BuildStructureKeys();
        private static readonly Dictionary<string, string> AiKindAlias = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "undefined_abbreviation", "undefined_abbr" },
            { "undefined_abbreviations", "undefined_abbr" },
            { "ui_references", "ui_not_bold" },
            { "ui_reference", "ui_not_bold" },
            { "ui_bold", "ui_not_bold" },
        };

        private static Dictionary<string, string> BuildStructureKeys()
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var s in StructureRules) d[Key(s)] = s;
            d["hardcodedvalue"] = "HardCoded";
            d["onehone"] = "OneH1";
            d["multipleh1"] = "OneH1";
            d["secondh1"] = "OneH1";
            d["headingskip"] = "HeadingOrder";
            d["alttext"] = "AltText";
            d["missingalt"] = "AltText";
            return d;
        }

        private static string Key(string s) { return s.Replace("-", "").Replace("_", "").ToLowerInvariant(); }

        /// <summary>
        /// Null when <paramref name="spec"/> is a rule id, family, layer or keyword the scorer understands; otherwise why not.
        /// </summary>
        public static string Check(string spec, ISet<string> aiKinds)
        {
            if (string.IsNullOrWhiteSpace(spec)) return "empty rule";
            if (spec == "*" || spec == "lint" || spec == "ai") return null;
            int c = spec.IndexOf(':');
            if (c <= 0) return "'" + spec + "' is not a rule id";
            string layer = spec.Substring(0, c), rest = spec.Substring(c + 1);
            if (Array.IndexOf(Layers, layer) < 0) return "unknown layer '" + layer + "'";
            if (layer == "ai")
            {
                if (aiKinds != null && !aiKinds.Contains(rest)) return "ai kind '" + rest + "' is not in defects.yaml";
                return null;
            }
            if (rest == "*") return null;
            if (layer == "structure")
                return Array.IndexOf(StructureRules, rest) >= 0 ? null : "unknown structure rule '" + rest + "'";
            if (layer == "project")
                return rest == "variables" || rest == "variables/*" ? null : "unknown project rule '" + rest + "'";
            string page = rest.Split('/')[0];
            string tail = rest.Length > page.Length ? rest.Substring(page.Length + 1) : "";
            if (layer == "google" && !GoogleSet.Contains(page)) return "unknown Google page '" + page + "'";
            if (layer == "company" && Array.IndexOf(CompanyPages, page) < 0) return "unknown Company page '" + page + "'";
            // google:word-list alone matches no finding (ids are google:word-list/221): a page needs a row number or *
            if (rest.IndexOf('/') < 0) return "'" + spec + "' names a page, not a rule: use " + spec + "/* or " + spec + "/<row>";
            if (tail != "*" && !IsNumber(tail)) return "'" + tail + "' is not a row number or *";
            return null;
        }

        private static bool IsNumber(string s) { return s.Length > 0 && s.All(ch => ch >= '0' && ch <= '9'); }

        /// <summary>
        /// Maps one generator hint to scorer vocabulary (possibly several entries). A valid spec comes back unchanged.
        /// Google hints that name no guide page become the layer <c>google:*</c>: which page carries a rule changes when
        /// the guide is recompiled (#65), and DigitalBanking labels the same way.
        /// </summary>
        public static IEnumerable<string> Normalize(string spec, ISet<string> aiKinds)
        {
            spec = (spec ?? "").Trim();
            if (spec.Length == 0) return new string[0];
            if (Check(spec, aiKinds) == null) return new[] { spec };
            int c = spec.IndexOf(':');
            if (c <= 0) return new[] { spec };
            string layer = spec.Substring(0, c).ToLowerInvariant(), rest = spec.Substring(c + 1);
            switch (layer)
            {
                case "structure":
                    string s;
                    return StructureByKey.TryGetValue(Key(rest), out s) ? new[] { "structure:" + s } : new[] { spec };
                case "company":
                    return CompanyFamily(rest, spec);
                case "localized-style":
                    // The project's own override guide is the Company layer; the entry duplicates company:terminology/*.
                    return CompanyFamily(rest, spec);
                case "google":
                    {
                        string fixedRest = rest.Replace("dates-and-times", "dates-times");
                        string probe = "google:" + fixedRest;
                        if (fixedRest.IndexOf('/') < 0) probe += "/*";
                        if (Check(probe, aiKinds) == null) return new[] { probe };
                        return new[] { "google:*" };
                    }
                case "ai":
                    {
                        string kind = rest.Replace('-', '_');
                        string alias;
                        if (AiKindAlias.TryGetValue(kind, out alias)) kind = alias;
                        if (kind == "ui_not_bold" && rest.Replace('-', '_').StartsWith("ui_ref", StringComparison.Ordinal))
                            return new[] { "company:ui-references/*", "ai:ui_not_bold" };
                        string probe = "ai:" + kind;
                        return Check(probe, aiKinds) == null ? new[] { probe } : new[] { spec };
                    }
            }
            return new[] { spec };
        }

        private static IEnumerable<string> CompanyFamily(string rest, string original)
        {
            string page = rest.Split('/')[0].ToLowerInvariant();
            if (Array.IndexOf(CompanyPages, page) < 0) return new[] { original };
            string tail = rest.Length > page.Length ? rest.Substring(page.Length + 1) : "*";
            return new[] { "company:" + page + "/" + (tail.Length == 0 ? "*" : tail) };
        }

        // ---- intersection of two rule specs ----------------------------------------------------------------------------

        /// <summary>
        /// True when some finding could satisfy both specs, so a violation label and a clean/exempt label that overlap a span
        /// with such specs contradict each other.
        /// </summary>
        public static bool Intersects(string a, string b)
        {
            if (a == b) return true;
            if (a == "*" || b == "*") return true;
            if (a == "lint") return b != "ai" && !b.StartsWith("ai:", StringComparison.Ordinal);
            if (b == "lint") return Intersects(b, a);
            if (a == "ai") return b == "ai" || b.StartsWith("ai:", StringComparison.Ordinal);
            if (b == "ai") return Intersects(b, a);
            // prefix forms: "layer:*" and "layer:page/*" cover anything that starts with the part before the star
            string pa = Prefix(a), pb = Prefix(b);
            if (pa != null && pb != null) return pa.StartsWith(pb, StringComparison.OrdinalIgnoreCase) || pb.StartsWith(pa, StringComparison.OrdinalIgnoreCase);
            if (pa != null) return b.StartsWith(pa, StringComparison.OrdinalIgnoreCase);
            if (pb != null) return a.StartsWith(pb, StringComparison.OrdinalIgnoreCase);
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static string Prefix(string spec)
        {
            return spec.EndsWith("*", StringComparison.Ordinal) && spec.Length > 1 ? spec.Substring(0, spec.Length - 1) : null;
        }

        /// <summary>Layer name of a spec for kind/rule consistency: google, company, structure, project, ai, or "any" for <c>*</c> and <c>lint</c>.</summary>
        public static string LayerOf(string spec)
        {
            if (spec == "*" || spec == "lint") return "any";
            if (spec == "ai") return "ai";
            int c = spec.IndexOf(':');
            return c > 0 ? spec.Substring(0, c).ToLowerInvariant() : "other";
        }
    }
}
